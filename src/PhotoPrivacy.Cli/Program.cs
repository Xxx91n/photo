using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Cli;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;
using UiHostProgram = PhotoPrivacy.Ui.UiProgram;
using BackgroundUiOptions = PhotoPrivacy.Ui.BackgroundUiOptions;

ConsoleCancelEventHandler? cancelKeyHandler = null;
EventHandler? processExitHandler = null;
PosixSignalHooks? posixHooks = null;

var requestedMode = RuntimeModeResolver.ResolveFromArgs(args);
var hasModeOption = RuntimeModeResolver.HasModeOption(args);

var argsConfig = new ConfigurationBuilder().AddCommandLine(args).Build();
var probeConfigPath = argsConfig["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
var probeConfig = TryLoadConfig(probeConfigPath);
var serviceManager = new PhotoPrivacy.Ui.ServiceManager();
var serviceInstalledProbe = OperatingSystem.IsWindows() && serviceManager.IsInstalled();
var bootstrapDecision = RuntimeBootstrapPolicy.Decide(
    requestedMode: requestedMode,
    hasModeOption: hasModeOption,
    isUserInteractive: Environment.UserInteractive,
    isServiceInstalled: serviceInstalledProbe);
requestedMode = bootstrapDecision.EffectiveMode;

var mutexName = requestedMode == RuntimeMode.Service
    ? AppInstanceMutexNames.ServiceHost
    : AppInstanceMutexNames.Unified;

using var mutex = new Mutex(initiallyOwned: true, mutexName, out var isNewInstance);
if (!isNewInstance)
{
    if (requestedMode == RuntimeMode.Service)
    {
        return 1;
    }

    var sent = await SingleInstanceIpc.TryNotifyRunningInstanceToShowWindowAsync();
    if (sent)
    {
        return 0;
    }

    Console.Error.WriteLine("[PhotoPrivacy] 另一个实例已在运行，退出。");
    Console.Error.WriteLine("[PhotoPrivacy] another instance is already in use.");

    await InstanceConflictAudit.TryWriteAsync(args, requestedMode, mutexName);
    return 1;
}

if (bootstrapDecision.RunUiControlShell)
{
    using var showWindowPipeCts = new CancellationTokenSource();

    UiHostProgram.Options = new BackgroundUiOptions
    {
        RuntimeKind = "service",
        IsBackgroundMode = false,
        HideMainWindowOnStartup = false,
        IsServiceInstalled = true,
        UseTrayIcon = false,
        IsPaused = static () => false,
        Pause = static () => { },
        Resume = static () => { },
        ExitAsync = static () =>
        {
            Environment.Exit(0);
            return Task.CompletedTask;
        },
        GetExifToolVersion = static () => "service_mode",
        GetServiceRuntimeState = serviceManager.GetRuntimeState,
        RestartToDefaultModeAsync = _ => RestartToDefaultModeAsync(Environment.ProcessPath),
        ConfigPath = probeConfigPath,
        AuditDirectory = probeConfig?.Audit.LogDirectory ?? AppContext.BaseDirectory,
        SelfExecutablePath = Environment.ProcessPath
    };

    UiHostProgram.Options.ShowMainWindow = () => { };

    var pipeTask = SingleInstanceIpc.RunShowWindowServerAsync(
        onShowWindowRequested: () => UiHostProgram.Options.ShowMainWindow(),
        cancellationToken: showWindowPipeCts.Token);

    try
    {
        return UiHostProgram.Start(args);
    }
    finally
    {
        await showWindowPipeCts.CancelAsync();
        try
        {
            await pipeTask;
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }
}

var builder = Host.CreateApplicationBuilder(args);
var mode = RuntimeModeResolver.Resolve(builder.Configuration);
mode = requestedMode;

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
if (mode != RuntimeMode.Cli)
{
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Warning);
}

if (mode == RuntimeMode.Service)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("[PhotoPrivacy] service 模式仅支持 Windows。请改用 --mode cli。");
        return 1;
    }

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "PhotoPrivacyCleaner";
    });
}

// Extend shutdown timeout so StopAsync has time to flush the audit log
// and drain the ExifTool bridge cleanly before the host force-kills the app.
// The default 5 s was too short when StopAsync received an already-cancelled
// token and the bridge needed a moment to tear down the stay_open process.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddSingleton<IRuntimeControl, RuntimeControl>();
builder.Services.AddSingleton<MetadataCleanerWorker>();
builder.Services.AddHostedService(static services => services.GetRequiredService<MetadataCleanerWorker>());

var app = builder.Build();

var shutdownCoordinator = new ShutdownCoordinator(
    stopToken => app.StopAsync(stopToken),
    stopTimeout: TimeSpan.FromSeconds(4));
posixHooks = PosixSignalHooks.Register(() => shutdownCoordinator.RequestStop());
RegisterBestEffortShutdown(shutdownCoordinator, ref cancelKeyHandler, ref processExitHandler);

if (mode == RuntimeMode.Background)
{
    await app.StartAsync();
    using var showWindowPipeCts = new CancellationTokenSource();

    var runtimeControl = app.Services.GetRequiredService<IRuntimeControl>();
    var configPath = builder.Configuration["config"]
        ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
    var auditDirectory = builder.Configuration["audit-folder"]
        ?? builder.Configuration["audit:log_directory"]
        ?? AppContext.BaseDirectory;

    var loadedConfig = TryLoadConfig(configPath);
    var hideMainWindowOnStartup = loadedConfig?.Ui.HideMainWindowOnStartup ?? true;
    var serviceInstalled = serviceManager.IsInstalled();

    UiHostProgram.Options = new BackgroundUiOptions
    {
        RuntimeKind = bootstrapDecision.RuntimeKind,
        IsBackgroundMode = true,
        HideMainWindowOnStartup = hideMainWindowOnStartup,
        IsServiceInstalled = serviceInstalled,
        UseTrayIcon = bootstrapDecision.UseTrayIcon,
        IsPaused = () => runtimeControl.IsPaused,
        Pause = runtimeControl.Pause,
        Resume = runtimeControl.Resume,
        ExitAsync = () => ExitBackgroundAsync(app),
        GetExifToolVersion = () => app.Services.GetRequiredService<MetadataCleanerWorker>().CurrentExifToolVersion,
        GetServiceRuntimeState = serviceManager.GetRuntimeState,
        RestartToDefaultModeAsync = token => RestartToDefaultModeAsync(Environment.ProcessPath, () => app.StopAsync(token), token),
        ConfigPath = configPath,
        AuditDirectory = auditDirectory,
        SelfExecutablePath = Environment.ProcessPath
    };

    UiHostProgram.Options.ShowMainWindow = () => { };

    var pipeTask = SingleInstanceIpc.RunShowWindowServerAsync(
        onShowWindowRequested: () => UiHostProgram.Options.ShowMainWindow(),
        cancellationToken: showWindowPipeCts.Token);

    UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler);
    posixHooks.Dispose();

    try
    {
        return UiHostProgram.Start(args);
    }
    finally
    {
        await showWindowPipeCts.CancelAsync();
        try
        {
            await pipeTask;
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        await app.StopAsync(CancellationToken.None);
    }
}

await app.RunAsync();
UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler);
posixHooks.Dispose();
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

static AppConfig? TryLoadConfig(string configPath)
{
    try
    {
        return File.Exists(configPath)
            ? AppConfigLoader.Load(configPath)
            : AppConfig.Default;
    }
    catch
    {
        return null;
    }
}

static async Task ExitBackgroundAsync(IHost app)
{
    await app.StopAsync(CancellationToken.None);
    Environment.Exit(0);
}

static async Task RestartToDefaultModeAsync(
    string? executablePath,
    Func<Task>? stopBeforeRestart = null,
    CancellationToken cancellationToken = default)
{
    try
    {
        if (stopBeforeRestart is not null)
        {
            await stopBeforeRestart();
        }
    }
    catch
    {
        // best effort
    }

    try
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
        }
    }
    catch
    {
        // best effort
    }

    Environment.Exit(0);
}
