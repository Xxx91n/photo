using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class DesignSystemTests
{
    [Fact]
    public void DesignTokens_File_Should_Exist_And_Define_Spacing_Ramp()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        Assert.True(File.Exists(path), "DesignTokens.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("SpaceXs", source, StringComparison.Ordinal);
        Assert.Contains("SpaceXxl", source, StringComparison.Ordinal);
        Assert.Contains("RadiusMd", source, StringComparison.Ordinal);
        Assert.Contains("DurationNormal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignTokens_Should_Define_Font_Scheme()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("fonts:Inter#Inter", source, StringComparison.Ordinal);
        Assert.Contains("fonts:CascadiaCode#Cascadia Code", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NordDark")]
    [InlineData("Catppuccin")]
    [InlineData("Dracula")]
    [InlineData("TokyoNight")]
    [InlineData("OneDarkPro")]
    public void Community_Theme_File_Should_Exist_And_Define_Semi_Tokens(string themeName)
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Themes", themeName + ".axaml");
        Assert.True(File.Exists(path), themeName + ".axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("SemiColorBackground0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorText0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorPrimary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Should_Not_Reference_Legacy_Token_Names()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("AppBgBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SidebarBgBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextPrimaryBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentRedBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Should_Define_Transitions()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("BrushTransition", source, StringComparison.Ordinal);
        Assert.Contains("0:0:0.150", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Should_Register_SemiTheme_Style()
    {
        var appPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml");
        Assert.True(File.Exists(appPath), "App.axaml should exist");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("semi:SemiTheme", source, StringComparison.Ordinal);
        Assert.Contains("semi:UrsaSemiTheme", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentTheme", source, StringComparison.Ordinal);
    }

}
