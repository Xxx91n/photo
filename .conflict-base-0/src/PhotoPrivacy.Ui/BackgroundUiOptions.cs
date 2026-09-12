using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.Ui;

/// <summary>
/// UI 运行时选项不可变快照（票20 检查点 C）：公共面全部只读。
/// 构建期一次性写入的属性用 init；运行期需变更的状态（RuntimeKind/WorkerEndpointName/UseTrayIcon/
/// HideMainWindowOnStartup/HideTrayIcon/ShowMainWindow/ConnectionState）为 private set，
/// 外部一律经由类内具名变更方法收口，去除可变字段外写（issue 20）。
/// </summary>
public sealed class BackgroundUiOptions
{
    public string RuntimeKind { get; private set; } = "tray";
    public bool HideMainWindowOnStartup { get; private set; } = true;
    public bool UseTrayIcon { get; private set; } = true;
    public bool HideTrayIcon { get; private set; }
    public string WorkerEndpointName { get; private set; } = string.Empty;
    public string WorkerExecutablePath { get; init; } = string.Empty;

    public Func<CancellationToken, Task<bool>> IsWorkerAliveAsync { get; init; } = static _ => Task.FromResult(false);
    public ConnectionStateService? ConnectionState { get; private set; }
    public Func<CancellationToken, Task<bool>> IsPausedAsync { get; init; } = static _ => Task.FromResult(false);
    public Func<CancellationToken, Task> PauseAsync { get; init; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ResumeAsync { get; init; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ShutdownWorkerAsync { get; init; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task> ExitApplicationAsync { get; init; } = static _ => Task.CompletedTask;
    public Func<CancellationToken, Task<string>> GetExifToolVersionAsync { get; init; } = static _ => Task.FromResult("unknown");
    public Func<ServiceRuntimeState> GetServiceRuntimeState { get; init; } = static () => ServiceRuntimeState.NotInstalled;
    public Func<CancellationToken, Task<WorkerConnectionResult>> ConnectOrLaunchWorkerAsync { get; init; } = static _ =>
        Task.FromResult(new WorkerConnectionResult(
            RuntimeKind: "tray",
            EndpointName: string.Empty,
            ShouldShowTrayIcon: true,
            Status: null));

    public Action ShowMainWindow { get; private set; } = static () => { };

    public string ConfigPath { get; init; } = AppContext.BaseDirectory;
    public string AuditDirectory { get; init; } = AppContext.BaseDirectory;

    /// <summary>运行期状态变更收口：RuntimeKind/WorkerEndpointName/UseTrayIcon 三元组一次更新（票20）。</summary>
    public void UpdateRuntimeState(string runtimeKind, string endpointName, bool useTrayIcon)
    {
        RuntimeKind = runtimeKind;
        WorkerEndpointName = endpointName;
        UseTrayIcon = useTrayIcon;
    }

    /// <summary>配置热重载收口：HideMainWindowOnStartup/HideTrayIcon 落值；托盘模式下 UseTrayIcon 联动
    /// （票20，逐字镜像原 MainWindow.ApplyRuntimeConfigToUiState 的条件语义）。</summary>
    public void UpdateHideFlags(bool hideMainWindowOnStartup, bool hideTrayIcon)
    {
        HideMainWindowOnStartup = hideMainWindowOnStartup;
        HideTrayIcon = hideTrayIcon;
        if (string.Equals(RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
        {
            UseTrayIcon = !HideTrayIcon;
        }
    }

    public void SetShowMainWindow(Action showMainWindow) => ShowMainWindow = showMainWindow;

    public void SetConnectionState(ConnectionStateService? connectionState) => ConnectionState = connectionState;

    public static BackgroundUiOptions CreateFallback()
    {
        return new BackgroundUiOptions();
    }
}
