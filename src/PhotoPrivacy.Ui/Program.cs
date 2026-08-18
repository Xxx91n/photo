using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Core.Configuration;
using Serilog;

namespace PhotoPrivacy.Ui;

public static class UiProgram
{
    public static BackgroundUiOptions Options { get; set; } = BackgroundUiOptions.CreateFallback();

    public static void Main(string[] args)
    {
        Start(args);
    }

    // ADR 0053 M1: Two-phase async startup via Start(AppMain) Manual lifetime.
    // Phase 1 (sync): log init, single-instance guard, config load, initial RuntimeOptions with Connecting state.
    // Phase 2 (async): Avalonia starts, MainWindow shows immediately, Worker connects fire-and-forget in AppMain.
    public static int Start(string[] args)
    {
        var uiLogDir = Path.Combine(AppContext.BaseDirectory, "logs");
        UiDiagnosticLog.Initialize(uiLogDir);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            UiDiagnosticLog.Write($"[FATAL] UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            UiDiagnosticLog.Write($"[FATAL] UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

        UiDiagnosticLog.Write("UiProgram.Start entered");
        using var single = new UiSingleInstance();
        if (!single.IsOwner)
        {
            UiDiagnosticLog.Write("UiProgram.Start detected non-owner instance; notifying existing instance");
            _ = UiSingleInstance.NotifyExistingInstanceAsync();
            Thread.Sleep(120);
            return 0;
        }

        var argsConfig = new ConfigurationBuilder().AddCommandLine(args).Build();
        var configPath = argsConfig["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        var config = LoadConfigOrDefault(configPath);
        UiDiagnosticLog.Write($"Config resolved. path={configPath}, hide_main_window_on_startup={config.Ui.HideMainWindowOnStartup}, hide_tray_icon={config.Ui.HideTrayIcon}");

        var workerPath = ResolveWorkerExecutablePath();
        var workerManager = new WorkerProcessManager(new WorkerIpcClient());
        var serviceManager = new ServiceManager();

        // ADR 0053 M1: Set initial RuntimeOptions with placeholder endpoint (Connecting state).
        // App.OnFrameworkInitializationCompleted will create MainWindow with Connecting UI,
        // then AppMain fires ConnectWorkerAsync in background to replace the placeholder.
        var showPipeCts = new CancellationTokenSource();
        App.RuntimeOptions = BuildRuntimeOptions(
            options: Options,
            modeKind: "tray",
            endpointName: WorkerIpcEndpointNames.BackgroundPipe,
            workerPath: workerPath,
            configPath: configPath,
            config: config,
            workerManager: workerManager,
            serviceManager: serviceManager);

        // ConnectionState starts in Connecting — heartbeat timer will promote to Connected/Reconnecting.
        var connectionState = new ConnectionStateService(new WorkerIpcClient(), WorkerIpcEndpointNames.BackgroundPipe);
        connectionState.State = ConnectionState.Connecting;
        App.RuntimeOptions.ConnectionState = connectionState;

        var showPipeTask = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => App.RuntimeOptions.ShowMainWindow(),
            cancellationToken: showPipeCts.Token);
        UiDiagnosticLog.Write("Show-window IPC server started");

        // ADR 0053 M1: Start(AppMain) Manual lifetime — Avalonia starts, MainWindow shows immediately.
        // AppMain callback fires after OnFrameworkInitializationCompleted, connecting Worker in background.
        UiDiagnosticLog.Write("Avalonia Start(AppMain) starting");
        BuildAvaloniaApp().Start(AppMain, args);
        UiDiagnosticLog.Write("Avalonia lifetime exited");

        showPipeCts.Cancel();
        try
        {
            showPipeTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        finally
        {
            showPipeCts.Dispose();
        }

        connectionState.Dispose();
        UiDiagnosticLog.Shutdown();
        Log.CloseAndFlush();
        return 0;
    }

    // ADR 0053 M1: AppMain is called by Avalonia after OnFrameworkInitializationCompleted.
    // MainWindow is already visible with Connecting state — now connect Worker in background.
    private static void AppMain(Application app, string[] args)
    {
        UiDiagnosticLog.Write("AppMain entered — firing background Worker connect");

        // Fire-and-forget: connect Worker on thread pool, post result to UI thread when done.
        _ = Task.Run(async () =>
        {
            try
            {
                var connectResult = await App.RuntimeOptions.ConnectOrLaunchWorkerAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                UiDiagnosticLog.Write($"Worker connect result. runtime={connectResult.RuntimeKind}, endpoint={connectResult.EndpointName}, showTray={connectResult.ShouldShowTrayIcon}");

                // Update RuntimeOptions with actual connection result on UI thread.
                Dispatcher.UIThread.Post(() =>
                {
                    App.RuntimeOptions.RuntimeKind = connectResult.RuntimeKind;
                    App.RuntimeOptions.WorkerEndpointName = connectResult.EndpointName;
                    App.RuntimeOptions.UseTrayIcon = connectResult.ShouldShowTrayIcon &&
                        !App.RuntimeOptions.HideTrayIcon;
                    // ConnectionState will be promoted by heartbeat timer within ~1s.
                    UiDiagnosticLog.Write($"RuntimeOptions updated on UI thread. RuntimeKind={App.RuntimeOptions.RuntimeKind}, Endpoint={App.RuntimeOptions.WorkerEndpointName}");
                });
            }
            catch (Exception ex)
            {
                UiDiagnosticLog.Write($"[FATAL] Background Worker connect failed: {ex}");
                Dispatcher.UIThread.Post(() =>
                {
                    // ConnectionState stays Reconnecting — heartbeat will retry.
                });
            }
        });

        // ADR 0053 M1: Start(AppMain) uses classic desktop lifetime by default.
        // ShutdownMode.OnMainWindowClose is the default — no explicit set needed.
    }

    private static AppConfig LoadConfigOrDefault(string configPath)
    {
        try
        {
            return File.Exists(configPath)
                ? AppConfigLoader.Load(configPath)
                : AppConfig.Default;
        }
        catch
        {
            return AppConfig.Default;
        }
    }

    private static string? ResolveWorkerExecutablePath()
    {
        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var workerName = OperatingSystem.IsWindows() ? "PhotoPrivacyWorker.exe" : "PhotoPrivacyWorker";
        var workerPath = Path.Combine(dir, workerName);
        return File.Exists(workerPath) ? workerPath : null;
    }

    private static BackgroundUiOptions BuildRuntimeOptions(
        BackgroundUiOptions options,
        string modeKind,
        string endpointName,
        string? workerPath,
        string configPath,
        AppConfig config,
        WorkerProcessManager workerManager,
        ServiceManager serviceManager)
    {
        options.RuntimeKind = modeKind;
        options.WorkerEndpointName = endpointName;
        options.WorkerExecutablePath = workerPath ?? string.Empty;
        options.HideMainWindowOnStartup = config.Ui.HideMainWindowOnStartup;
        options.HideTrayIcon = config.Ui.HideTrayIcon;
        options.UseTrayIcon = modeKind == "tray" && !config.Ui.HideTrayIcon;
        options.ConfigPath = configPath;
        options.AuditDirectory = config.Audit.LogDirectory;
        options.GetServiceRuntimeState = serviceManager.GetRuntimeState;
        options.IsWorkerAliveAsync = token => new WorkerIpcClient().IsAliveAsync(options.WorkerEndpointName, token);
        options.IsPausedAsync = async token =>
        {
            var res = await workerManager.GetStatusAsync(options.WorkerEndpointName, token).ConfigureAwait(false);
            return res?.Data?.IsPaused ?? false;
        };
        options.PauseAsync = async token => { await workerManager.PauseAsync(options.WorkerEndpointName, token).ConfigureAwait(false); };
        options.ResumeAsync = async token => { await workerManager.ResumeAsync(options.WorkerEndpointName, token).ConfigureAwait(false); };
        options.ShutdownWorkerAsync = async token =>
        {
            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
            {
                await workerManager.ShutdownAsync(options.WorkerEndpointName, token).ConfigureAwait(false);
            }
        };
        options.ExitApplicationAsync = async token =>
        {
            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
            {
                await workerManager.ShutdownAsync(options.WorkerEndpointName, token).ConfigureAwait(false);
            }
        };
        options.GetExifToolVersionAsync = async token =>
        {
            var res = await workerManager.GetStatusAsync(options.WorkerEndpointName, token).ConfigureAwait(false);
            return res?.Data?.ExifToolVersion ?? "unknown";
        };
        options.ConnectOrLaunchWorkerAsync = token => workerManager.ConnectOrLaunchAsync(
            workerPath,
            token,
            getServiceRuntimeState: serviceManager.GetRuntimeState,
            configPath: options.ConfigPath);
        return options;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "fonts:Inter#Inter",
                FontFallbacks = new[]
                {
                    new FontFallback
                    {
                        FontFamily = new FontFamily("fonts:Inter#Inter")
                    },
                    new FontFallback
                    {
                        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter")
                    }
                }
            });
#if DEBUG
        builder = builder.LogToTrace();
#endif
        return builder;
    }
}
