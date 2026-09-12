using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PhotoPrivacy.Ui.Localization;
using PhotoPrivacy.Ui.Services;
using PhotoPrivacy.Ui.ViewModels;
using PhotoPrivacy.Ui.Views;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// MainWindow 对 IUiHost 的实现：托盘/窗口/暂停按钮可见性行为。
/// 抽到独立文件保持 MainWindow.axaml.cs 行数预算（issue 06: ≤1600）。
/// </summary>
internal sealed class MainWindowUiHost : IUiHost
{
    private readonly MainWindow _window;

    public MainWindowUiHost(MainWindow window)
    {
        _window = window;
    }

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void SetPauseResumeAvailability(bool enabled)
    {
        // 票 24（ADR 0061）：Service→View 直写清零 — 可用性/文案改经 VM 中转（PauseResumeContent 派生文案由 XAML 绑定消费）。
        if (_window.DataContext is MainWindowViewModel vm)
        {
            vm.PauseResumeAvailable = enabled;
        }
    }

    public void EnsureTrayVisible()
    {
        if (_window._options is null)
        {
            return;
        }

        _window._trayHost ??= new TrayHost(_window, _window._options, _window.ExitApplicationAsync);
        _window._trayHost.IsVisible = true;
    }

    public void DisposeTray()
    {
        _window._trayHost?.Dispose();
        _window._trayHost = null;
    }

    public void ShowAndActivateWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}

/// <summary>
/// MainWindow 对 IViewModelView 的实现：控制器产生的状态应用到窗口控件与 ViewModel。
/// </summary>
internal sealed class MainWindowViewModelView : IViewModelView
{
    private readonly MainWindow _window;

    public MainWindowViewModelView(MainWindow window)
    {
        _window = window;
    }

    private MainWindowViewModel? Vm => _window.DataContext as MainWindowViewModel;

    public void SetServiceButtons(ServiceButtonState state)
    {
        // 票 24（ADR 0061）：Service→View 直写清零 — 按钮可用性改经 VM ServiceButtons 中转，ServiceManagerPage 绑定消费。
        if (Vm is { } vm)
        {
            vm.ServiceButtons = state;
        }
    }

    public void SetServiceStatusText(string text)
    {
        if (Vm is { } vm)
        {
            vm.ServiceStatus = text;
        }
    }

    public void SetRuntimeStatus(string text)
    {
        if (Vm is { } vm)
        {
            vm.RuntimeStatus = text;
        }
    }

    public void SetCurrentMode(string modeLabel)
    {
        if (Vm is { } vm)
        {
            vm.CurrentMode = modeLabel;
        }
    }

    public void SetUninstallingStatus(string statusText, string uninstallingLabel)
    {
        if (Vm is { } vm)
        {
            vm.ServiceStatus = statusText + " | " + uninstallingLabel;
        }
    }

    public void SetModeAndRuntimeStatus(string modeLabel, string runtimeStatus, string? exifToolVersion)
    {
        if (Vm is { } vm)
        {
            vm.CurrentMode = modeLabel;
            vm.RuntimeStatus = runtimeStatus;
            if (exifToolVersion is not null)
            {
                vm.ExifToolVersion = exifToolVersion;
            }
        }
    }

    public void UpdateServiceButtons() => _window._serviceModeController.UpdateServiceButtons();
}