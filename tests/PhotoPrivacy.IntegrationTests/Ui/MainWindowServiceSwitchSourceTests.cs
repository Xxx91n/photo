using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowServiceSwitchSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Switch_To_Service_After_Service_Install_And_Start()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("_options.RuntimeKind = \"service\";", source, StringComparison.Ordinal);
        Assert.Contains("vm.CurrentMode = MapModeLabel(_options.RuntimeKind);", source, StringComparison.Ordinal);
        Assert.Contains("OnInstallServiceClick(object? sender, RoutedEventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("OnStartServiceClick(object? sender, RoutedEventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("_ = SwitchToServiceModeAfterInstallAsync(CancellationToken.None);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Shutdown_Tray_Worker_Before_Start_Service()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("await ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken.None);", source, StringComparison.Ordinal);
        Assert.Contains("托盘 Worker 仍在运行，已取消服务启动，请稍后重试", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Only_Require_Tray_Shutdown_When_Tray_Endpoint_Is_Active()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("if (requiresTrayShutdown)", source, StringComparison.Ordinal);
        Assert.Contains("PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Not_Allow_Window_Close_During_Service_Mode_Switch()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("_trayHost?.AllowWindowClose();\n            var next = await _options.ConnectOrLaunchWorkerAsync(token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Not_Switch_To_Tray_When_Service_Is_Stopped_But_Installed()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("OnStopServiceClick(object? sender, RoutedEventArgs e)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (result.Status == ServiceCommandStatus.Success)\n        {\n            _ = EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken.None);\n        }", source, StringComparison.Ordinal);
    }
}
