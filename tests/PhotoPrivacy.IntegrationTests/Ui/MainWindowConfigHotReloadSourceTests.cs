using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowConfigHotReloadSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Apply_Config_Immediately_After_Save()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("ApplyRuntimeConfigToUiState();", source, StringComparison.Ordinal);
        Assert.Contains("_ = SwitchToDefaultModeAsync(CancellationToken.None", source, StringComparison.Ordinal);
    }
}
