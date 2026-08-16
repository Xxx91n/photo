using Avalonia;
using Avalonia.Media;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
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
        var connectResult = workerManager.ConnectOrLaunchAsync(
            workerPath,
            CancellationToken.None,
            getServiceRuntimeState: serviceManager.GetRuntimeState,
            configPath: configPath).GetAwaiter().GetResult();
        UiDiagnosticLog.Write($"Worker connect result. runtime={connectResult.RuntimeKind}, endpoint={connectResult.EndpointName}, showTray={connectResult.ShouldShowTrayIcon}, workerPath={workerPath}");

        var endpointName = connectResult.EndpointName;
        var modeKind = connectResult.RuntimeKind;

        var showPipeCts = new CancellationTokenSource();
        App.RuntimeOptions = BuildRuntimeOptions(
            options: Options,
            modeKind: modeKind,
            endpointName: endpointName,
            workerPath: workerPath,
            configPath: configPath,
            config: config,
            workerManager: workerManager,
            serviceManager: serviceManager);

        var connectionState = new ConnectionStateService(new WorkerIpcClient(), endpointName);
        App.RuntimeOptions.ConnectionState = connectionState;

        var showPipeTask = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => App.RuntimeOptions.ShowMainWindow(),
            cancellationToken: showPipeCts.Token);
        UiDiagnosticLog.Write("Show-window IPC server started");

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
