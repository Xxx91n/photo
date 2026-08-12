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

// ADR 0026 (Q7): Single-instance guard via ISingleInstanceGuard (Mutex on Windows, POSIX lockfile on Linux/macOS).
// The previous direct-Mutex path is replaced by a guard abstraction without changing observable behavior.
var mutexName = WorkerInstanceMutexNames.Unified;

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

builder.Logging.ClearProviders();

var logConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<PhotoPrivacy.Core.Audit.PathMaskingEnricher>()
    .Destructure.With<PhotoPrivacy.Core.Audit.PathMaskingDestructuringPolicy>()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}");

if (mode != RuntimeMode.Service)
{
    logConfig.WriteTo.Async(a => a.File(
        "logs/worker-.log",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 104857600,
        retainedFileCountLimit: 31,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
}

Log.Logger = logConfig.CreateLogger();

builder.Logging.AddSerilog(Log.Logger, dispose: false);
if (mode != RuntimeMode.Cli)
{
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Warning);
}

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

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddSingleton<IRuntimeControl, RuntimeControl>();
builder.Services.AddSingleton<MetadataCleanerWorker>();
builder.Services.AddHostedService(static services => services.GetRequiredService<MetadataCleanerWorker>());

builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var configPath = configuration["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
    var watchDirectory = ResolveWatchDirectory(configPath, configuration["hot-folder"] ?? configuration["hot_folder"]);
    return new WorkerRuntimeContext(
        runtimeControl: services.GetRequiredService<IRuntimeControl>(),
        worker: services.GetRequiredService<MetadataCleanerWorker>(),
        watchDirectoryAccessor: () => watchDirectory,
        mode: mode);
});

builder.Services.AddHostedService<WorkerIpcServerHostedService>();

var app = builder.Build();

var shutdownCoordinator = new ShutdownCoordinator(
    stopToken => app.StopAsync(stopToken),
    stopTimeout: TimeSpan.FromSeconds(4));
posixHooks = PosixSignalHooks.Register(() => shutdownCoordinator.RequestStop(), onReload: () => app.Services.GetRequiredService<MetadataCleanerWorker>().ReloadConfigAsync().GetAwaiter().GetResult());
// ADR 0034: UnhandledException + UnobservedTaskException → Serilog (also in UI Program.cs)
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
