using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class AppStartupPolicyTests
{
    [Fact]
    public void App_Source_Should_Not_Always_Hide_Window_Based_On_HideMainWindowOnStartup_Alone()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("if (RuntimeOptions.HideMainWindowOnStartup)", source, StringComparison.Ordinal);
    }
}
