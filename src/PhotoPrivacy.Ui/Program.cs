using Avalonia;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using PhotoPrivacy.Core.Configuration;

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
        using var single = new UiSingleInstance();
        if (!single.IsOwner)
        {
            _ = UiSingleInstance.NotifyExistingInstanceAsync();
            return 0;
        }

        var argsConfig = new ConfigurationBuilder().AddCommandLine(args).Build();
        var configPath = argsConfig["config"] ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        var config = LoadConfigOrDefault(configPath);

        var workerPath = ResolveWorkerExecutablePath();
        var workerManager = new WorkerProcessManager(new WorkerIpcClient());
        var connectResult = workerManager.ConnectOrLaunchAsync(workerPath, CancellationToken.None).GetAwaiter().GetResult();

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
            workerManager: workerManager);

        var showPipeTask = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => App.RuntimeOptions.ShowMainWindow(),
            cancellationToken: showPipeCts.Token);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
        WorkerProcessManager workerManager)
    {
        options.RuntimeKind = modeKind;
        options.WorkerEndpointName = endpointName;
        options.WorkerExecutablePath = workerPath ?? string.Empty;
        options.HideMainWindowOnStartup = config.Ui.HideMainWindowOnStartup;
        options.HideTrayIcon = config.Ui.HideTrayIcon;
        options.UseTrayIcon = modeKind == "tray" && !config.Ui.HideTrayIcon;
        options.ConfigPath = configPath;
        options.AuditDirectory = config.Audit.LogDirectory;
        options.GetServiceRuntimeState = () => new ServiceManager().GetRuntimeState();
        options.IsWorkerAliveAsync = token => new WorkerIpcClient().IsAliveAsync(options.WorkerEndpointName, token);
        options.IsPausedAsync = async token =>
        {
            var res = await workerManager.GetStatusAsync(options.WorkerEndpointName, token);
            return res?.Data?.IsPaused ?? false;
        };
        options.PauseAsync = async token => { await workerManager.PauseAsync(options.WorkerEndpointName, token); };
        options.ResumeAsync = async token => { await workerManager.ResumeAsync(options.WorkerEndpointName, token); };
        options.ShutdownWorkerAsync = async token =>
        {
            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
            {
                await workerManager.ShutdownAsync(options.WorkerEndpointName, token);
            }
        };
        options.ExitApplicationAsync = async token =>
        {
            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
            {
                await workerManager.ShutdownAsync(options.WorkerEndpointName, token);
            }
        };
        options.GetExifToolVersionAsync = async token =>
        {
            var res = await workerManager.GetStatusAsync(options.WorkerEndpointName, token);
            return res?.Data?.ExifToolVersion ?? "unknown";
        };
        options.ConnectOrLaunchWorkerAsync = token => workerManager.ConnectOrLaunchAsync(workerPath, token);
        return options;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
#if DEBUG
        builder = builder.LogToTrace();
#endif
        return builder;
    }
}
