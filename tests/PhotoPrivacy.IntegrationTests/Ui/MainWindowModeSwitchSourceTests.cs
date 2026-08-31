using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowModeSwitchSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Not_Shutdown_Tray_Worker_During_Mode_Switch()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("await _workerIpc.ShutdownAsync(previousEndpoint, token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Source_Should_Use_Explicit_Tray_Shutdown_Helper_For_Service_Switch()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("private async Task<bool> ShutdownTrayWorkerForServiceSwitchAsync", source, StringComparison.Ordinal);
        Assert.Contains("await _workerIpc.ShutdownAsync(endpoint, token);", source, StringComparison.Ordinal);
    }
}
