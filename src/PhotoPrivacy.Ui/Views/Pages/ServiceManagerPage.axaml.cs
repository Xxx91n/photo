using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线（无订阅）。
public partial class ServiceManagerPage : UserControl
{
    public ServiceManagerPage() => InitializeComponent();

    internal Button InstallServiceButtonControl => InstallServiceButton;
    internal Button UninstallServiceButtonControl => UninstallServiceButton;
    internal Button StartServiceButtonControl => StartServiceButton;
    internal Button StopServiceButtonControl => StopServiceButton;
    internal Button RefreshServiceStatusButtonControl => RefreshServiceStatusButton;
}
