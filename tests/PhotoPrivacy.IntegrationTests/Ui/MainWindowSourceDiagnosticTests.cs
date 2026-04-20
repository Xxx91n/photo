using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowSourceDiagnosticTests
{
    [Fact]
    public void MainWindow_Source_Should_Expose_Safe_Startup_Show_Fallback()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("EnsureWindowVisibleFallback", source, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Normal", source, StringComparison.Ordinal);
        Assert.Contains("Show();", source, StringComparison.Ordinal);
    }
}
