using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowUninstallFlowSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Run_Uninstall_Off_Ui_Thread()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("private async void OnUninstallServiceClick", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => _serviceManager.Uninstall()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Pin_Background_Endpoint_After_Service_Uninstall()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("_options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Force_Background_Reconnect_After_Uninstall()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("getServiceRuntimeStateOverride: () => ServiceRuntimeState.NotInstalled", source, StringComparison.Ordinal);
    }
}
