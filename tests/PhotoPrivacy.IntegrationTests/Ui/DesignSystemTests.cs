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

    // ponytail: regression guard — Inter font scheme requires .WithInterFont() AppBuilder
    // extension to register the InterFontCollection EmbedFontCollection. Hand-writing
    // DefaultFontFamily=fonts:Inter#Inter in XAML WITHOUT calling .WithInterFont() leaves the
    // fonts:Inter scheme unregistered, so any FontWeight the theme requests (e.g. SemiBold=600,
    // logged as DemiBold) throws InvalidOperationException: Could not create glyphTypeface at
    // Window.Show() initial measure (regression from commit 300d6bf, root-caused via exa
    // search of official AppBuilderExtension.cs + InterFontCollection.cs + FontManagerTests).
    // Lock BOTH halves: the XAML scheme must be present AND Program.cs must register the collection.
    [Fact]
    public void Inter_Font_Scheme_Must_Be_Registered_Via_WithInterFont()
    {
        var tokensPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        Assert.True(File.Exists(tokensPath), "DesignTokens.axaml should exist");
        var tokens = File.ReadAllText(tokensPath, Encoding.UTF8);
        Assert.Contains("fonts:Inter#Inter", tokens, StringComparison.Ordinal);
        Assert.Contains("fonts:CascadiaCode#Cascadia Code", tokens, StringComparison.Ordinal);

        var programPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Program.cs");
        Assert.True(File.Exists(programPath), "Program.cs should exist");
        var program = File.ReadAllText(programPath, Encoding.UTF8);
        Assert.Contains(".WithInterFont()", program, StringComparison.Ordinal);
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


    [Fact]
    public void AppTheme_Should_Not_Contain_Dead_Legacy_Alias_Brushes()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("x:Key=\"AppBgBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"SidebarBgBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"CardBgBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"DividerBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TextPrimaryBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TextSecondaryBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TextTertiaryBrush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"AccentRedBrush\"", source, StringComparison.Ordinal);
    }

}
