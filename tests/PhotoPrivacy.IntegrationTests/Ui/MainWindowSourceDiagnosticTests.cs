using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowSourceDiagnosticTests
{
    [Fact]
    public void MainWindow_Source_Should_Expose_Safe_Startup_Show_Fallback()
    {
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("EnsureWindowVisibleFallback", source, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Normal", source, StringComparison.Ordinal);
        Assert.Contains("Show();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Axaml_Should_Use_Tailscale_Style_Navigation_Shell()
    {
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("Width=\"920\"", source, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"740\"", source, StringComparison.Ordinal);
        Assert.Contains("CurrentPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabControl", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"settings-card\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch", source, StringComparison.Ordinal);
    }
}
