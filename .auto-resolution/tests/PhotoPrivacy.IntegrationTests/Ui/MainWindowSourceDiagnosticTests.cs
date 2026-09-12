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
        // 票 24（ADR 0061）：窗口尺寸/导航 shell 断言仍在 MainWindow.axaml；
        // settings-card / ToggleSwitch 已随页面拆分迁入 Views/Pages/，改扫整个 Views 目录（语义不变）。
        var shellPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var shell = File.ReadAllText(shellPath, Encoding.UTF8);
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var combined = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));

        Assert.Contains("Width=\"920\"", shell, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"740\"", shell, StringComparison.Ordinal);
        Assert.Contains("CurrentPage", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabControl", combined, StringComparison.Ordinal);
        Assert.Contains("Classes=\"settings-card\"", combined, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch", combined, StringComparison.Ordinal);
    }
}
