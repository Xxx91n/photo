using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;
using PhotoPrivacy.Worker;
using Serilog;
using Serilog.Extensions.Hosting;

ConsoleCancelEventHandler? cancelKeyHandler = null;
EventHandler? processExitHandler = null;
PosixSignalHooks? posixHooks = null;

var requestedMode = RuntimeModeResolver.ResolveFromArgs(args);
var hasModeOption = RuntimeModeResolver.HasModeOption(args);
if (WorkerEntryGuard.ShouldRejectDirectLaunch(args, Environment.UserInteractive, hasModeOption))
{
    Console.Error.WriteLine("[PhotoPrivacyWorker] 请通过 PhotoPrivacy.exe 启动，不支持直接双击 Worker。 ");
    Console.Error.WriteLine("[PhotoPrivacyWorker] launch via PhotoPrivacy.exe only.");
    return 2;
}

if (!hasModeOption && Environment.UserInteractive)
{
    requestedMode = RuntimeMode.Background;
}

// Single-instance guard via ISingleInstanceGuard (Mutex on Windows, POSIX lockfile on Linux/macOS).
// The previous direct-Mutex path is replaced by a guard abstraction without changing observable behavior.
var mutexName = requestedMode switch
    {
        RuntimeMode.Service => WorkerInstanceMutexNames.Service,
        _ => WorkerInstanceMutexNames.Background
    };

using var instanceGuard = SingleInstanceGuardFactory.Create(mutexName);
if (!instanceGuard.IsOwner)
{
    Console.Error.WriteLine("[PhotoPrivacyWorker] 另一个 Worker 实例已在运行，退出。");
    Console.Error.WriteLine("[PhotoPrivacyWorker] another worker instance is already in use.");
    await InstanceConflictAudit.TryWriteAsync(args, requestedMode, mutexName);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
var mode = RuntimeModeResolver.Resolve(builder.Configuration);

if (mode == RuntimeMode.Service)
{
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "PhotoPrivacyCleaner";
        });
    }
    else if (OperatingSystem.IsLinux())
    {
        builder.Services.AddSystemd();
    }
}

// AddWindowsService 会隐式登记 EventLog logging provider；服务会话下它写失败（Cannot open log
// for source，Win32 1722）时 MEL Logger.Log 直接抛 AggregateException，沿 ReloadConfig 的 catch 链
// 炸穿 IPC 响应路径（服务管道 ReloadConfig 挂起，#85）。ClearProviders 必须排在 AddWindowsService
// 之后，才能把该 provider 一并摘掉；服务诊断改由下游 Serilog 文件日志承担（落点见 ResolveLogFilePath）。
// Design(#85): 服务进程不得保留 EventLog provider——ClearProviders 紧随 AddWindowsService 之后。
builder.Logging.ClearProviders();

// Windows 服务进程的 CWD 是 %WINDIR%\System32：相对日志路径既不可发现，受限服务账户也写不进去。
// 摘掉 EventLog 后 Serilog 文件是服务模式的唯一诊断落点，故服务进程改用 ProgramData 绝对路径；
// 只认「真由 SCM 启动」（而非控制台 --mode service），避免与同机真实服务争同一文件。
// Design(#85): 服务进程日志落点固定为 %ProgramData%\PhotoPrivacy\logs，其余情况保持相对 logs/。
var logFilePath = ResolveLogFilePath(mode);

var logConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<PhotoPrivacy.Core.Audit.PathMaskingEnricher>()
    .Destructure.With<PhotoPrivacy.Core.Audit.PathMaskingDestructuringPolicy>()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}");

logConfig.WriteTo.Async(a => a.File(
    logFilePath,
    rollingInterval: RollingInterval.Day,
    rollOnFileSizeLimit: true,
    fileSizeLimitBytes: 104857600,
    retainedFileCountLimit: 31,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

Log.Logger = logConfig.CreateLogger();

builder.Logging.AddSerilog(Log.Logger, dispose: false);
if (mode != RuntimeMode.Cli)
{
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Warning);
}

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddSingleton<IRuntimeControl, RuntimeControl>();
builder.Services.AddSingleton<MetadataCleanerWorker>();
builder.Services.AddHostedService(static services => services.GetRequiredService<MetadataCleanerWorker>());

static string? ResolveAuditLogDirectory(string configPath, string? cliOverride)
{
    try
    {
        var config = File.Exists(configPath)
            ? PhotoPrivacy.Core.Configuration.AppConfigLoader.Load(configPath)
            : PhotoPrivacy.Core.Configuration.AppConfig.Default;
        var dir = !string.IsNullOrWhiteSpace(cliOverride)
            ? cliOverride
            : config.Audit.LogDirectory;
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }
    catch
    {
        return null;
    }
}

builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var configPath = configuration["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
    var watchDirectory = ResolveWatchDirectory(configPath, configuration["hot-folder"] ?? configuration["hot_folder"]);
    return new WorkerRuntimeContext(
        runtimeControl: services.GetRequiredService<IRuntimeControl>(),
        worker: services.GetRequiredService<MetadataCleanerWorker>(),
        watchDirectoryAccessor: () => watchDirectory,
        mode: mode,
        auditLogDirectoryAccessor: () => ResolveAuditLogDirectory(configPath, configuration["audit-folder"] ?? configuration["audit_folder"]));
});

builder.Services.AddHostedService<WorkerIpcServerHostedService>();

var app = builder.Build();

var shutdownCoordinator = new ShutdownCoordinator(
    stopToken => app.StopAsync(stopToken),
    stopTimeout: TimeSpan.FromSeconds(4));
// （T1 承接）：POSIX 信号回调禁 sync-over-async——
// onReload 改 async 委托，PosixSignalHooks 内部 Task.Run 编组后立即返回；
// 异常就地记录，不推给 UnobservedTaskException 兜底。
posixHooks = PosixSignalHooks.Register(
    () => shutdownCoordinator.RequestStop(),
    onReload: async () =>
    {
        try
        {
            await app.Services.GetRequiredService<MetadataCleanerWorker>()
                .ReloadConfigAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SIGHUP config reload failed");
        }
    });
// UnhandledException + UnobservedTaskException → Serilog (also in UI Program.cs)
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try { Log.Fatal((Exception)e.ExceptionObject, "AppDomain.UnhandledException"); } catch { }
    try { Log.CloseAndFlush(); } catch { }
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    try { Log.Error(e.Exception, "TaskScheduler.UnobservedTaskException"); } catch { }
    e.SetObserved();
};

RegisterBestEffortShutdown(shutdownCoordinator, ref cancelKeyHandler, ref processExitHandler);

await app.RunAsync();
UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler);
posixHooks.Dispose();
Log.CloseAndFlush();
return Environment.ExitCode;

static string ResolveWatchDirectory(string configPath, string? hotFolderArg)
{
    if (!string.IsNullOrWhiteSpace(hotFolderArg))
    {
        return hotFolderArg;
    }

    try
    {
        var cfg = File.Exists(configPath)
            ? AppConfigLoader.Load(configPath)
            : AppConfig.Default;
        return cfg.Watch.HotFolder;
    }
    catch
    {
        return AppConfig.Default.Watch.HotFolder;
    }
}

static void RegisterBestEffortShutdown(
    ShutdownCoordinator coordinator,
    ref ConsoleCancelEventHandler? cancelKeyHandler,
    ref EventHandler? processExitHandler)
{
    cancelKeyHandler = (_, e) =>
    {
        e.Cancel = true;
        coordinator.RequestStop();
    };
    Console.CancelKeyPress += cancelKeyHandler;

    processExitHandler = (_, _) => coordinator.RequestStop();
    AppDomain.CurrentDomain.ProcessExit += processExitHandler;
}

// 服务进程的 CWD 是 %WINDIR%\System32：相对日志路径不可发现、受限服务账户也写不进去。
// 仅「真由 SCM 启动」才改落点——控制台 --mode service 与真实服务并存时不争同一文件。
static string ResolveLogFilePath(RuntimeMode runtimeMode)
{
    if (runtimeMode == RuntimeMode.Service
        && OperatingSystem.IsWindows()
        && WindowsServiceHelpers.IsWindowsService())
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            var logDirectory = Path.Combine(programData, "PhotoPrivacy", "logs");
            try
            {
                Directory.CreateDirectory(logDirectory);
                return Path.Combine(logDirectory, "worker-.log");
            }
            catch
            {
                // ProgramData 不可写：回落到安装目录的绝对路径，绝不回退到 CWD 相对路径。
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "logs", "worker-.log");
    }

    return "logs/worker-.log";
}

static void UnregisterBestEffortShutdown(
    ConsoleCancelEventHandler? cancelKeyHandler,
    EventHandler? processExitHandler)
{
    if (cancelKeyHandler is not null)
    {
        Console.CancelKeyPress -= cancelKeyHandler;
    }

    if (processExitHandler is not null)
    {
        AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
    }
}
