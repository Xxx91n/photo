using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;
using PhotoPrivacy.Worker;

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

var mutexName = WorkerInstanceMutexNames.Unified;

using var mutex = new Mutex(initiallyOwned: true, mutexName, out var isNewInstance);
if (!isNewInstance)
{
    Console.Error.WriteLine("[PhotoPrivacyWorker] 另一个 Worker 实例已在运行，退出。");
    Console.Error.WriteLine("[PhotoPrivacyWorker] another worker instance is already in use.");
    await InstanceConflictAudit.TryWriteAsync(args, requestedMode, mutexName);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
var mode = RuntimeModeResolver.Resolve(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
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
posixHooks = PosixSignalHooks.Register(() => shutdownCoordinator.RequestStop());
RegisterBestEffortShutdown(shutdownCoordinator, ref cancelKeyHandler, ref processExitHandler);

await app.RunAsync();
UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler);
posixHooks.Dispose();
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
