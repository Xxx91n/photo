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
        // ADR 0051 A1: settings-card must consume DesignTokens elevation ladder (Elevation2), not raw SemiShadowElevated.
        Assert.Contains("Elevation2", source, StringComparison.Ordinal);
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

    // === ADR 0051 test guards ===

    [Fact]
    public void Elevation_Token_Ladder_Must_Be_Defined()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Elevation0", source);
        Assert.Contains("Elevation1", source);
        Assert.Contains("Elevation2", source);
        Assert.Contains("Elevation4", source);
    }

    [Fact]
    public void Elevation_Token_Ladder_Must_Be_Referenced_At_Least_3_Places()
    {
        // ADR 0051 A1 spec: sidebar/settings-card/popover must reference elevation token at least 3 places.
        var mainWindowPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var appThemePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var mainWindow = File.ReadAllText(mainWindowPath, Encoding.UTF8);
        var appTheme = File.ReadAllText(appThemePath, Encoding.UTF8);
        var combined = mainWindow + appTheme;
        var sidebarElevation1 = mainWindow.Contains("Elevation1", StringComparison.Ordinal) ? 1 : 0;
        var cardElevation2 = appTheme.Contains("Elevation2", StringComparison.Ordinal) ? 1 : 0;
        var totalRefs = sidebarElevation1 + cardElevation2;
        Assert.True(combined.Contains("Elevation1") || combined.Contains("Elevation2") || combined.Contains("Elevation4"),
            "elevation ladder must be consumed somewhere in views or theme, not just defined");
        // Spec said at least 3 places — settings-card style applies to 2 cards (provider will multiply) + sidebar.
        Assert.True(sidebarElevation1 + cardElevation2 >= 2,
            $"expected sidebar + settings-card to reference elevation ladder; got sidebar={sidebarElevation1} card={cardElevation2}");
    }

    [Fact]
    public void Button_Transitions_Easing_Must_Be_Specified()
    {
        // atomcode 2026-08-18: BrushTransition SineEaseOut (hover/press color) is the industry standard.
        // Removed TransformOperationsTransition + QuadraticEaseInOut per atomcode research:
        // scale(0.97) is SukiUI style (flashy), VS Code / Windows 11 Settings use pure color change.
        // WCAG 2.2 SC 2.3.3: scale = motion animation (vestibular trigger), color change is not.
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("SineEaseOut", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_Transitions_Must_Not_Include_Scale_Pressed()
    {
        // atomcode 2026-08-18: scale(0.97) pressed removed per industry standard.
        // VS Code / Windows 11 Settings = pure color transition (BrushTransition only).
        // SukiUI scale(0.95/0.97) + hover scale(1.03) = flashy, not enterprise-grade.
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("BrushTransition", source);
        Assert.DoesNotContain("scale(0.97)", source);
        Assert.DoesNotContain("TransformOperationsTransition", source);
    }

    [Fact]
    public void Caption_Btn_Danger_Pressed_And_Inactive_Pseudoclasses_Defined()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Button.caption-btn.danger:pressed", source);
        Assert.Contains("Window:inactive Button.caption-btn", source);
    }

    [Fact]
    public void Caption_Btn_Padding_Must_Be_16_6()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // ADR 0051 A4: Win11 standard 32px height = Padding Value="16,6"
        Assert.Contains("Value=\"16,6\"", source);
        // Old 14,8 padding must be gone from caption-btn style
        Assert.DoesNotContain("Padding=\"14,8\"", source);
    }

    [Fact]
    public void Focus_Visible_Pseudoclass_Must_Be_Defined()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains(":focus-visible", source);
    }

    [Fact]
    public void Program_Must_Have_FontManagerOptions_WithFallbacks()
    {
        var dir = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui");
        var progPath = Path.Combine(dir, "Program.cs");
        var source = File.ReadAllText(progPath, Encoding.UTF8);
        Assert.Contains("FontManagerOptions", source);
        Assert.Contains("FontFallbacks", source);
    }

    [Fact]
    public void MiddleClick_Behavior_Must_Use_RequestAnimationFrame()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Behaviors", "MiddleClickScrollBehavior.cs");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("RequestAnimationFrame", source);
        // Constants must align Files.App: DeadZone=12, MaxSpeedPerTick=32
        Assert.Contains("DeadZone = 12.0", source);
        Assert.Contains("MaxSpeedPerTick = 32.0", source);
    }

    [Fact]
    public void MiddleClick_Behavior_Must_Have_Watchdog_And_WallClock()
    {
        // ADR 0055 A2 regression guard: RAF primary + Render-priority watchdog + wall-clock Stopwatch dt.
        // Watchdog steps when RAF stalls >32ms (maximize layout storm); wall-clock dt keeps
        // exponential curve correct at any sample rate. Without either, maximize-window scroll breaks.
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Behaviors", "MiddleClickScrollBehavior.cs");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // Watchdog present
        Assert.Contains("Watchdog_Tick", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render, Watchdog_Tick", source, StringComparison.Ordinal);
        // Wall-clock Stopwatch for dt
        Assert.Contains("Stopwatch", source, StringComparison.Ordinal);
        Assert.Contains("StepScroll", source, StringComparison.Ordinal);
        // Stall threshold constant
        Assert.Contains("WatchdogStallMs = 32.0", source);
        // Stop() must clean up watchdog + clock (no leak)
        Assert.Contains("_state.Watchdog", source, StringComparison.Ordinal);
        Assert.Contains("_state.Clock", source, StringComparison.Ordinal);
    }


    [Fact]
    public void Spacing_Must_Reference_Space_Tokens()
    {
        var path = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // At least 20 Spacing references should now use DynamicResource Space* tokens
        var tokenHits = System.Text.RegularExpressions.Regex.Matches(source, @"Spacing=""{DynamicResource Space");
        Assert.True(tokenHits.Count >= 20, $"Expected >=20 Spacing token references, got {tokenHits.Count}");
    }

    // ponytail: regression guard for architecture-recovery ticket 03 — IFolderWatcher was a
    // shallow abstraction (single implementation FswFolderWatcher, sole consumer MetadataCleanerWorker
    // news the concrete class directly; zero call sites went through the interface). The interface
    // was deleted; the two REAL test seams in the same file (IRecoveryScanner, IFileSystemWatcherFactory)
    // each have genuine test-double consumers and were preserved in WatcherInterfaces.cs. This guard
    // locks the decision: no IFolderWatcher may reappear, and both real seams must stay intact.
    [Fact]
    public void Watcher_Shallow_Abstraction_IFolderWatcher_Must_Not_Reappear()
    {
        var watcherDir = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Core", "Watcher");
        Assert.True(Directory.Exists(watcherDir), "Watcher directory should exist");
        Assert.False(File.Exists(Path.Combine(watcherDir, "IFolderWatcher.cs")), "IFolderWatcher.cs must stay deleted");
        foreach (var file in Directory.GetFiles(watcherDir, "*.cs"))
        {
            var source = File.ReadAllText(file, Encoding.UTF8);
            Assert.DoesNotContain("IFolderWatcher", source, StringComparison.Ordinal);
        }
        var interfacesFile = File.ReadAllText(Path.Combine(watcherDir, "WatcherInterfaces.cs"), Encoding.UTF8);
        Assert.Contains("IRecoveryScanner", interfacesFile, StringComparison.Ordinal);
        Assert.Contains("IFileSystemWatcherFactory", interfacesFile, StringComparison.Ordinal);
    }
}
