using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
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
// Design(#82): 服务进程不得保留 EventLog provider——ClearProviders 紧随 AddWindowsService 之后。
builder.Logging.ClearProviders();

// Windows 服务进程的 CWD 是 %WINDIR%\System32：相对日志路径既不可发现，受限服务账户也写不进去。
// 摘掉 EventLog 后 Serilog 文件是服务模式的唯一诊断落点，故服务进程改用 ProgramData 绝对路径；
// 只认「真由 SCM 启动」（而非控制台 --mode service），避免与同机真实服务争同一文件。
// Design(#82): 服务进程日志落点固定为 %ProgramData%\PhotoPrivacy\logs，其余情况保持相对 logs/。
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
    // #188：运行期日志文件显式收 0640（含日滚/大小滚新文件）——读穿根因是继承的 default
    // ACL（安装脚本已 setfacl -b 剥离），hook 作纵深防御在每次打开时施加组只读口径。
    hooks: new LogFilePermissionHook(),
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

Log.Logger = logConfig.CreateLogger();

builder.Logging.AddSerilog(Log.Logger, dispose: false);
if (mode != RuntimeMode.Cli)
{
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Warning);
}

// 单写者准入门（#162）：实例守卫按模式分锁（Service/Background 各一把）是 UI 回落场景的有意设计，
// 代价是同 config 的服务与后台 Worker 可并存、争用同一 processed.ndjson——后启动者的 store 常驻句柄
// 打不开即 IOException，被 BackgroundServiceExceptionBehavior.StopHost 放大成服务自停。此门以
// 「有效审计目录」为身份补上跨模式互斥：拿不到锁 = 让位干净退出（日志 + 审计留痕），不触碰审计文件。
// 门自身异常（含跨账户打不开既有锁）同样让位——无法确证独占就不写审计；放行 = 带未知争用风险，
// 与本票要消灭的故障同类。
// Design(#82): 常驻 Worker 触碰审计文件前必须过准入门；拿不到锁或门异常一律让位，禁静默降级。
ISingleInstanceGuard? singleWriterGate = null;
if (requestedMode != RuntimeMode.Cli)
{
    // 配置解析失败不归门管：门只拿审计目录，走回退口径继续；配置错误由 Worker 既有校验链报。
    string? configuredAuditDirectory = null;
    try
    {
        configuredAuditDirectory = InstanceConflictAudit.LoadConfigFromArgs(args).Audit.LogDirectory;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "config could not be resolved for the single-writer gate; using the fallback audit directory (#162)");
    }

    var admission = SingleWriterGate.TryAcquire(configuredAuditDirectory);
    if (admission.Error is not null)
    {
        Log.Error(
            admission.Error,
            "single-writer admission gate could not be verified for {AuditDirectory}; yielding conservatively (single-writer gate #162)",
            admission.AuditDirectory);
    }
    else if (admission.Guard is null)
    {
        Log.Error(
            "audit directory {AuditDirectory} is held by another worker; yielding cleanly instead of crashing (single-writer gate #162)",
            admission.AuditDirectory);
    }

    if (admission.Guard is null)
    {
        Console.Error.WriteLine("[PhotoPrivacyWorker] 无法取得审计目录的单写者准入锁，本进程让位退出。 ");
        Console.Error.WriteLine("[PhotoPrivacyWorker] audit directory is owned (or cannot be verified) — yielding.");
        await InstanceConflictAudit.TryWriteSingleWriterYieldAsync(args, requestedMode, admission.AuditDirectory);
        Log.CloseAndFlush();
        return 1;
    }

    singleWriterGate = admission.Guard;
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
    catch (Exception ex)
    {
        // 语义保留（返回 null，调用方走回退口径），但不再静默（#37）：配置坏/读不了的原因必须留痕，
        // 否则「审计目录解析失败」与「配置本来就空」在下游无法区分。此时 Serilog 静态 Log 已可用。
        Log.Error(ex, "audit log directory could not be resolved from {ConfigPath}; the accessor returns null and callers fall back (#37)", configPath);
        return null;
    }
}

builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var configPath = configuration["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
    return new WorkerRuntimeContext(
        runtimeControl: services.GetRequiredService<IRuntimeControl>(),
        worker: services.GetRequiredService<MetadataCleanerWorker>(),
        watchDirectoryAccessor: () => services.GetRequiredService<MetadataCleanerWorker>().CurrentWatchDirectory,
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
// 日志兜底本身失败时走 stderr 留痕（#37）：Log 已不可用是终局故障，静默会让致命异常只留
// 退出码一个信号；stderr 是此刻唯一确定的去处（服务进程无控制台时写入静默成功，不抛）。
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try { Log.Fatal((Exception)e.ExceptionObject, "AppDomain.UnhandledException"); }
    catch (Exception logEx) { TraceToStderr(logEx, "fatal log after unhandled exception"); }
    try { Log.CloseAndFlush(); }
    catch (Exception logEx) { TraceToStderr(logEx, "log close/flush after unhandled exception"); }
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    try { Log.Error(e.Exception, "TaskScheduler.UnobservedTaskException"); }
    // 兜底输出保留原始任务异常（CodeRabbit #228）：SetObserved 之后再无他处记录，只留 logEx 会丢故障本体。
    catch (Exception logEx) { TraceToStderr(logEx, "unobserved task exception log", original: e.Exception); }
    e.SetObserved();
};

RegisterBestEffortShutdown(shutdownCoordinator, ref cancelKeyHandler, ref processExitHandler);

await app.RunAsync();
singleWriterGate?.Dispose();
UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler);
posixHooks.Dispose();
Log.CloseAndFlush();
return Environment.ExitCode;

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
// 判定用 WindowsServiceHelpers.IsWindowsService()（父进程为 services，即真由 SCM 拉起）：
// Environment.UserInteractive 会把「计划任务里跑 --mode service」也算进来，与真实服务争同一文件。
// 该类型在 Microsoft.Extensions.Hosting.WindowsServices 命名空间（不是 Microsoft.Extensions.Hosting）。
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
            if (TryPrepareWritableDirectory(logDirectory))
            {
                return Path.Combine(logDirectory, "worker-.log");
            }
        }

        // ProgramData 不可写：回落到安装目录的绝对路径，绝不回退到 CWD 相对路径。
        return Path.Combine(AppContext.BaseDirectory, "logs", "worker-.log");
    }

    return "logs/worker-.log";
}

// 「目录存在」不等于「当前账户可写」：建目录 + 真实写一个探针文件（DeleteOnClose）才算过。
static bool TryPrepareWritableDirectory(string directory)
{
    try
    {
        Directory.CreateDirectory(directory);
        using var probe = new FileStream(
            Path.Combine(directory, ".write-probe"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.DeleteOnClose);
        return true;
    }
    catch (Exception ex)
    {
        // 语义保留（返回 false，调用方回落安装目录），但留痕（#37）：本函数在日志器配置之前运行，
        // Log 不可用，stderr 是唯一去处——「日志目录不可写」回落这条线索排查服务日志缺失时必需。
        TraceToStderr(ex, $"log directory {directory} is not writable, falling back to the install directory");
        return false;
    }
}

// 日志兜底的兜底（#37）：Log 自身不可用时唯一确定的留痕去处。stderr 写入失败再无去处，
// 静默丢弃——这是登记过的「接受静默」终点，不是可再降级的路径。original 是被兜底操作
// 掩盖的原始异常（如 SetObserved 前的 UnobservedTaskException），一并留痕防丢失。
static void TraceToStderr(Exception failure, string context, Exception? original = null)
{
    try
    {
        Console.Error.WriteLine(
            $"[PhotoPrivacyWorker] {context}: {failure.GetType().Name}: {failure.Message}"
            + (original is null
                ? string.Empty
                : $" | original: {original.GetType().Name}: {original.Message}"));
    }
    catch
    {
        // nothing left to lose here
    }
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
