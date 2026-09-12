using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoPrivacy.Core.Configuration;
using Serilog;
using PhotoPrivacy.Ui.Composition;
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

        // 票 29（架构恢复第七轮）：组合根装配 —— 核心服务在 DI 容器注册为单例，运行时选项构建收口
        // AppComposition（spec 研究输入 Q1/Q2：组合根 + VM 构造注入）；Program 不再手写 new 服务链。
        var services = AppComposition.Build(config, configPath, ResolveWorkerExecutablePath());
        App.RuntimeOptions = services.GetRequiredService<BackgroundUiOptions>();

        // ADR 0053 M1: Set initial RuntimeOptions with placeholder endpoint (Connecting state).
        // App.OnFrameworkInitializationCompleted will create MainWindow with Connecting UI,
        // then AppMain fires ConnectWorkerAsync in background to replace the placeholder.
        var showPipeCts = new CancellationTokenSource();

        // ConnectionState starts in Connecting — heartbeat timer will promote to Connected/Reconnecting.
        var connectionState = services.GetRequiredService<ConnectionStateService>();
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
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
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

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        // 票 29：App 经工厂重载拿到容器（AppBuilder.Configure(Func<TApp>)），OnFrameworkInitializationCompleted 内解析 MainWindow。
        var builder = AppBuilder.Configure(() => new App(services))
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
