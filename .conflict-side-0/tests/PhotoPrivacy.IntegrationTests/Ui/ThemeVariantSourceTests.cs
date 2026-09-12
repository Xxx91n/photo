using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ThemeVariantSourceTests
{
    [Fact]
    public void App_Source_Should_Include_AppTheme_ResourceDictionary()
    {
        var appPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "App.axaml");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("Styling/AppTheme.axaml", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Source_Should_Include_DesignTokens_ResourceDictionary()
    {
        var appPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "App.axaml");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("Styling/DesignTokens.axaml", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Source_Should_Include_SemiTheme_And_UrsaSemiTheme()
    {
        var appPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "App.axaml");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("SemiTheme", source, StringComparison.Ordinal);
        Assert.Contains("UrsaSemiTheme", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Source_Should_Use_Semi_Semantic_Tokens()
    {
        var themePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(themePath, Encoding.UTF8);
        Assert.Contains("SemiColorBackground0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorText0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorPrimary", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorBorder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Source_Should_Not_Contain_Hardcoded_Color_Tokens()
    {
        var themePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(themePath, Encoding.UTF8);
        Assert.DoesNotContain("<Color x:Key=\"AppBg\">", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Color x:Key=\"AccentBlue\">", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Color x:Key=\"BtnPrimaryBg\">", source, StringComparison.Ordinal);
    }
}
