using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Core.Configuration;
using Serilog;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.Ui;

public static class UiProgram
{
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
        var workerIpc = new WorkerIpcClient();
        var serviceManager = new ServiceManager();

        // ADR 0053 M1: Set initial RuntimeOptions with placeholder endpoint (Connecting state).
        // App.OnFrameworkInitializationCompleted will create MainWindow with Connecting UI,
        // then AppMain fires ConnectWorkerAsync in background to replace the placeholder.
        var showPipeCts = new CancellationTokenSource();
        App.RuntimeOptions = BuildRuntimeOptions(
            modeKind: "tray",
            endpointName: WorkerIpcEndpointNames.BackgroundPipe,
            workerPath: workerPath,
            configPath: configPath,
            config: config,
            workerManager: workerManager,
            workerIpc: workerIpc,
            serviceManager: serviceManager);

        // ConnectionState starts in Connecting — heartbeat timer will promote to Connected/Reconnecting.
        var connectionState = new ConnectionStateService(new WorkerIpcClient(), WorkerIpcEndpointNames.BackgroundPipe);
        connectionState.State = ConnectionState.Connecting;
        App.RuntimeOptions.SetConnectionState(connectionState);

        var showPipeTask = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => App.RuntimeOptions.ShowMainWindow(),
            cancellationToken: showPipeCts.Token);
        UiDiagnosticLog.Write("Show-window IPC server started");

        // ADR 0053 M1: Fire-and-forget Worker connect BEFORE entering Avalonia lifetime.
        // StartWithClassicDesktopLifetime runs the message loop (doesn't block UI thread—
        // it IS the message loop). Worker connects in background, posts result to UI thread
        // when ready via Dispatcher.UIThread.Post.
        UiDiagnosticLog.Write("Firing background Worker connect (fire-and-forget)");
        _ = Task.Run(async () =>
        {
            try
            {
                var connectResult = await App.RuntimeOptions.ConnectOrLaunchWorkerAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                UiDiagnosticLog.Write($"Worker connect result. runtime={connectResult.RuntimeKind}, endpoint={connectResult.EndpointName}, showTray={connectResult.ShouldShowTrayIcon}");

                Dispatcher.UIThread.Post(() =>
                {
                    App.RuntimeOptions.UpdateRuntimeState(
                        connectResult.RuntimeKind,
                        connectResult.EndpointName,
                        connectResult.ShouldShowTrayIcon && !App.RuntimeOptions.HideTrayIcon);
                    UiDiagnosticLog.Write($"RuntimeOptions updated. RuntimeKind={App.RuntimeOptions.RuntimeKind}, Endpoint={App.RuntimeOptions.WorkerEndpointName}");
                });
            }
            catch (Exception ex)
            {
                UiDiagnosticLog.Write($"[FATAL] Background Worker connect failed: {ex}");
            }
        });

        UiDiagnosticLog.Write("Avalonia StartWithClassicDesktopLifetime starting");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
        string modeKind,
        string endpointName,
        string? workerPath,
        string configPath,
        AppConfig config,
        WorkerProcessManager workerManager,
        WorkerIpcClient workerIpc,
        ServiceManager serviceManager)
    {
        // 票20 检查点 C：一次性构建不可变快照；委托在调用时读取 App.RuntimeOptions 的当前值
        //（与原实现闭包读取同一共享实例语义一致）。
        var options = new BackgroundUiOptions
        {
            WorkerExecutablePath = workerPath ?? string.Empty,
            ConfigPath = configPath,
            AuditDirectory = config.Audit.LogDirectory,
            GetServiceRuntimeState = serviceManager.GetRuntimeState,
            IsWorkerAliveAsync = token => workerIpc.IsAliveAsync(App.RuntimeOptions.WorkerEndpointName, token),
            IsPausedAsync = async token =>
            {
                var res = await workerIpc.GetStatusAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                return res?.Data?.IsPaused ?? false;
            },
            PauseAsync = async token =>
            {
                await workerIpc.PauseAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
            },
            ResumeAsync = async token =>
            {
                await workerIpc.ResumeAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
            },
            ShutdownWorkerAsync = async token =>
            {
                if (string.Equals(App.RuntimeOptions.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
                {
                    await workerIpc.ShutdownAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                }
            },
            ExitApplicationAsync = async token =>
            {
                if (string.Equals(App.RuntimeOptions.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
                {
                    await workerIpc.ShutdownAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                }
            },
            GetExifToolVersionAsync = async token =>
            {
                var res = await workerIpc.GetStatusAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                return res?.Data?.ExifToolVersion ?? "unknown";
            },
            ConnectOrLaunchWorkerAsync = token => workerManager.ConnectOrLaunchAsync(
                workerPath,
                token,
                getServiceRuntimeState: serviceManager.GetRuntimeState,
                configPath: configPath)
        };
        options.UpdateRuntimeState(modeKind, endpointName, modeKind == "tray" && !config.Ui.HideTrayIcon);
        options.UpdateHideFlags(config.Ui.HideMainWindowOnStartup, config.Ui.HideTrayIcon);
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
