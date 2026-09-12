namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// ServiceModeController 对 ServiceManager 的操作缝。运行时由 ServiceManager 实现；
/// 单测用 fake 记录调用。方法面即服务编排实际消费的最小集合。
/// </summary>
public interface IServiceManagerOps
{
    ServiceCommandResult Install(string exePath, string configPath);
    ServiceCommandResult Uninstall();
    ServiceCommandResult Start(string exePath, string? configPath);
    ServiceCommandResult StopService();
    ServiceRuntimeState GetRuntimeState();
    string GetStatusText();
}

/// <summary>
/// ServiceManager 适配：Start 的 configPath 参数签名（string?）与 ServiceManager.Start(string, string) 对齐；
/// </summary>
public sealed class ServiceManagerOps : IServiceManagerOps
{
    private readonly ServiceManager _serviceManager;

    public ServiceManagerOps(ServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    public ServiceCommandResult Install(string exePath, string configPath) => _serviceManager.Install(exePath, configPath);
    public ServiceCommandResult Uninstall() => _serviceManager.Uninstall();
    public ServiceCommandResult Start(string exePath, string? configPath) => _serviceManager.Start(exePath, configPath ?? string.Empty);
    public ServiceCommandResult StopService() => _serviceManager.Stop();
    public ServiceRuntimeState GetRuntimeState() => _serviceManager.GetRuntimeState();
    public string GetStatusText() => _serviceManager.GetStatusText();
}

/// <summary>
/// 窗口宿主缝：托盘/窗口/暂停按钮的可见性行为。MainWindow 实现之；
/// 单测用 fake 验证编排触发顺序，不依赖 Avalonia 控件。
/// </summary>
public interface IUiHost
{
    void Post(Action action);
    void SetPauseResumeAvailability(bool enabled);
    void EnsureTrayVisible();
    void DisposeTray();
    void ShowAndActivateWindow();
}

/// <summary>
/// 视图状态缝：控制器产生状态，MainWindow 应用到自身控件与 ViewModel。单测可全 fake。
/// </summary>
public interface IViewModelView
{
    void SetServiceButtons(ServiceButtonState state);
    void SetServiceStatusText(string text);
    void SetRuntimeStatus(string text);
    void SetCurrentMode(string modeLabel);
    void SetUninstallingStatus(string statusText, string uninstallingLabel);
    void SetModeAndRuntimeStatus(string modeLabel, string runtimeStatus, string? exifToolVersion);
    void UpdateServiceButtons();
}

/// <summary>ServiceButtonState 的全禁用便捷值（Busy 状态与非 Windows 平台共用）。</summary>
public static class ServiceButtonStates
{
    public static readonly ServiceButtonState AllDisabled = new(InstallEnabled: false, UninstallEnabled: false, StartEnabled: false, StopEnabled: false);
}