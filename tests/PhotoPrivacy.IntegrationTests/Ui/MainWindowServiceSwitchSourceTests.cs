
namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: 服务编排抽取到 Services/ServiceModeController.cs 后，源断言随之迁移指向新类。
/// MainWindow 只保留转发器；编排行为语义由 ServiceModeControllerTests 状态机单测覆盖。
/// </summary>
public sealed class MainWindowServiceSwitchSourceTests
{
    [Fact]
    public void ServiceModeController_Source_Should_Switch_To_Service_After_Service_Install_And_Start()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("options.RuntimeKind = \"service\";", source, StringComparison.Ordinal);
        Assert.Contains("_view.SetCurrentMode(MapModeLabel(options.RuntimeKind));", source, StringComparison.Ordinal);
        Assert.Contains("public async Task InstallAsync()", source, StringComparison.Ordinal);
        Assert.Contains("public async Task StartAsync()", source, StringComparison.Ordinal);
        Assert.Contains("SwitchToServiceModeAfterInstallAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Shutdown_Tray_Worker_Before_Start_Service()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("await ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken.None).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("msg.tray_worker_running", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Only_Require_Tray_Shutdown_When_Tray_Endpoint_Is_Active()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("if (requiresTrayShutdown)", source, StringComparison.Ordinal);
        Assert.Contains("PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Not_Allow_Window_Close_During_Service_Mode_Switch()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.DoesNotContain("_trayHost?.AllowWindowClose();\n            var next = await _options.ConnectOrLaunchWorkerAsync(token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Not_Switch_To_Tray_When_Service_Is_Stopped_But_Installed()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("public async Task StopAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (result.Status == ServiceCommandStatus.Success)\n        {\n            _ = EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken.None);\n        }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Only_Forward_Service_Clicks_To_Controller()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");

        Assert.Contains("private void OnInstallServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.InstallAsync();", source, StringComparison.Ordinal);
        Assert.Contains("private void OnStartServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.StartAsync();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void OnInstallServiceClick", source, StringComparison.Ordinal);
    }

}