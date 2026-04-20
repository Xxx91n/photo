namespace PhotoPrivacy.Ui;

public sealed class BackgroundUiOptions
{
    public string RuntimeKind { get; set; } = "tray";
    public bool HideMainWindowOnStartup { get; set; } = true;
    public bool UseTrayIcon { get; set; } = true;
    public bool HideTrayIcon { get; set; }
    public string WorkerEndpointName { get; set; } = string.Empty;
    public string WorkerExecutablePath { get; set; } = string.Empty;

    public Func<CancellationToken, Task<bool>> IsWorkerAliveAsync { get; set; } = static _ => Task.FromResult(false);
    public Func<CancellationToken, Task<bool>> IsPausedAsync { get; set; } = static _ => Task.FromResult(false);
    public Func<CancellationToken, Task> PauseAsync { get; set; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ResumeAsync { get; set; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ShutdownWorkerAsync { get; set; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ExitApplicationAsync { get; set; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task<string>> GetExifToolVersionAsync { get; set; } = static _ => Task.FromResult("unknown");
    public Func<ServiceRuntimeState> GetServiceRuntimeState { get; set; } = static () => ServiceRuntimeState.NotInstalled;
    public Func<CancellationToken, Task<WorkerConnectionResult>> ConnectOrLaunchWorkerAsync { get; set; } = static _ =>
        Task.FromResult(new WorkerConnectionResult(
            RuntimeKind: "tray",
            EndpointName: string.Empty,
            ShouldShowTrayIcon: true,
            Status: null));

    public Action ShowMainWindow { get; set; } = static () => { };

    public string ConfigPath { get; set; } = AppContext.BaseDirectory;
    public string AuditDirectory { get; set; } = AppContext.BaseDirectory;

    public static BackgroundUiOptions CreateFallback()
    {
        return new BackgroundUiOptions();
    }
}
