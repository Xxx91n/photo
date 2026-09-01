using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class UiThreadSafetyRegressionTests
{
    [Fact]
    public void MainWindow_Source_Should_Not_Access_DataContext_Inside_Audit_Callbacks()
    {
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("includeDetailedEvents: () => (DataContext as MainWindowViewModel)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (DataContext is MainWindowViewModel innerVm)", source, StringComparison.Ordinal);
    }
}
