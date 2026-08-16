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

    // ADR 0050 source-lint regression guards — UI polish surface depth + typography +
    // button unification + Material.Icons caption buttons. These prevent silent drift
    // back to inline FontSize / scattered Padding / emoji buttons that caused the "AI
    // slop" visual regression. Each Fact checks one concrete invariant from the ADR.

    [Fact]
    public void Typography_6_Classes_Must_Exist()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("TextBlock.display", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.headline", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.title", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.body", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.caption", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.mono", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Typography_FontSize_Tokens_Must_Be_Defined()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        Assert.True(File.Exists(path), "DesignTokens.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("DisplayFontSize", source, StringComparison.Ordinal);
        Assert.Contains("HeadlineFontSize", source, StringComparison.Ordinal);
        Assert.Contains("TitleFontSize", source, StringComparison.Ordinal);
        Assert.Contains("BodyFontSize", source, StringComparison.Ordinal);
        Assert.Contains("CaptionFontSize", source, StringComparison.Ordinal);
        Assert.Contains("MonoFontSize", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_Elevation_Should_Be_Present()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Border.settings-card", source, StringComparison.Ordinal);
        Assert.Contains("BoxShadow", source, StringComparison.Ordinal);
        Assert.Contains("SemiShadowElevated", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_Variants_Should_Set_Padding()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // Each of the 4 variants must declare Padding so button heights stay uniform.
        Assert.Contains("Button.primary", source, StringComparison.Ordinal);
        Assert.Contains("Button.ghost", source, StringComparison.Ordinal);
        Assert.Contains("Button.danger", source, StringComparison.Ordinal);
        Assert.Contains("Button.nav", source, StringComparison.Ordinal);
        Assert.Contains("Button.nav-action", source, StringComparison.Ordinal);
        // Padding literal must appear at least 5 times (one per variant + caption-btn).
        var paddingHits = System.Text.RegularExpressions.Regex.Matches(source, "Padding");
        Assert.True(paddingHits.Count >= 5, $"Expected >=5 Padding setters, got {paddingHits.Count}");
    }

    [Fact]
    public void MainWindow_No_Inline_Button_Padding_Four_Zero()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // The scattered inline Padding="4,0" on Browse buttons was the root visual inconsistency;
        // they must now route through the variant style instead.
        Assert.DoesNotContain("Padding=\"4,0\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Browse_Buttons_Use_MaterialIcons()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Kind=\"FolderOpen\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Plus\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Minus\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Caption_Buttons_Use_ElementRole_And_MaterialIcons()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("ExtendClientAreaToDecorationsHint=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("WindowDecorations=\"None\"", source, StringComparison.Ordinal);
        Assert.Contains("WindowDecorationProperties.ElementRole=\"TitleBar\"", source, StringComparison.Ordinal);
        // ADR 0050 A4/plan-0050 4.3 — each caption button carries its semantic ElementRole.
        Assert.Contains("ElementRole=\"MinimizeButton\"", source, StringComparison.Ordinal);
        Assert.Contains("ElementRole=\"MaximizeButton\"", source, StringComparison.Ordinal);
        Assert.Contains("ElementRole=\"CloseButton\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"WindowMinimize\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"WindowMaximize\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"WindowClose\"", source, StringComparison.Ordinal);
        // Self-drawn caption buttons (client area, NOT WindowDrawnDecorations template parts)
        // still need click handlers to drive WindowState/Close — the old ADR line claiming
        // "no handler needed" was wrong; see ADR 0050 A4 followup note.
        Assert.Contains("OnMinimizeClick", source, StringComparison.Ordinal);
        Assert.Contains("OnMaximizeClick", source, StringComparison.Ordinal);
        Assert.Contains("OnCloseClick", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBar_Uses_Semi_Tokens()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // The self-drawn titlebar must theme through Semi Color tokens, not inline hex.
        Assert.Contains("{DynamicResource SemiColorBackground0}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SemiColorText2}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Titlebar_State_Pseudoclasses_Defined()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Window:maximized", source, StringComparison.Ordinal);
        Assert.Contains("Window:fullscreen", source, StringComparison.Ordinal);
        Assert.Contains("Border.titlebar-host", source, StringComparison.Ordinal);
        // fullscreen hides caption buttons; maximized drops titlebar padding — match ADR A4 prose.
        Assert.Contains("Button.caption-btn", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Behaviors_Namespace_Bound()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("xmlns:behaviors=\"using:PhotoPrivacy.Ui.Behaviors\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MiddleClick_Behavior_Attached_To_ScrollViewer()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // At least 2 ScrollViewers (config + service manager pages) must attach the behavior.
        var hits = System.Text.RegularExpressions.Regex.Matches(source, @"MiddleClickScrollBehavior\.IsEnabled=""True""");
        Assert.True(hits.Count >= 2, $"Expected >=2 MiddleClickScrollBehavior attachments, got {hits.Count}");
    }

}
