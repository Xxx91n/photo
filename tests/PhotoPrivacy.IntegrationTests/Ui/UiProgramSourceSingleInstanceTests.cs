using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class UiProgramSourceSingleInstanceTests
{
    [Fact]
    public void UiProgram_Source_Should_Bring_Existing_Window_To_Front_On_Second_Launch()
    {
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Program.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("UiSingleInstance.NotifyExistingInstanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("Thread.Sleep(120)", source, StringComparison.Ordinal);
    }
}
