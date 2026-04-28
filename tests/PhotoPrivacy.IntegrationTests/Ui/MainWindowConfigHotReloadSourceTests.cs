using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowConfigHotReloadSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Expose_Explicit_Apply_Config_Action_Chain()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("private async void OnApplyConfigClick", source, StringComparison.Ordinal);
        Assert.Contains("ReloadConfigAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyRuntimeConfigToUiState();", source, StringComparison.Ordinal);
        Assert.Contains("\"✓ 已应用\"", source, StringComparison.Ordinal);
    }
}
