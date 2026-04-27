using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ThemeVariantSourceTests
{
    [Fact]
    public void App_Source_Should_Include_AppTheme_ResourceDictionary()
    {
        var appPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("Styling/AppTheme.axaml", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Source_Should_Define_Light_And_Dark_Color_Tokens()
    {
        var themePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(themePath, Encoding.UTF8);
        Assert.Contains("AccentBlue", source, StringComparison.Ordinal);
        Assert.Contains("BtnPrimaryBg", source, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", source, StringComparison.Ordinal);
    }
}
