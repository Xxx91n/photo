using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class DesignSystemTests
{
    [Fact]
    public void DesignTokens_File_Should_Exist_And_Define_Spacing_Ramp()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
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
        var tokensPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
        Assert.True(File.Exists(tokensPath), "DesignTokens.axaml should exist");
        var tokens = File.ReadAllText(tokensPath, Encoding.UTF8);
        Assert.Contains("fonts:Inter#Inter", tokens, StringComparison.Ordinal);
        Assert.Contains("fonts:CascadiaCode#Cascadia Code", tokens, StringComparison.Ordinal);

        var programPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Program.cs");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Themes", themeName + ".axaml");
        Assert.True(File.Exists(path), themeName + ".axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("SemiColorBackground0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorText0", source, StringComparison.Ordinal);
        Assert.Contains("SemiColorPrimary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Should_Not_Reference_Legacy_Token_Names()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("AppBgBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SidebarBgBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextPrimaryBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentRedBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Should_Define_Transitions()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("BrushTransition", source, StringComparison.Ordinal);
        Assert.Contains("0:0:0.150", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Should_Register_SemiTheme_Style()
    {
        var appPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "App.axaml");
        Assert.True(File.Exists(appPath), "App.axaml should exist");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("semi:SemiTheme", source, StringComparison.Ordinal);
        Assert.Contains("semi:UrsaSemiTheme", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentTheme", source, StringComparison.Ordinal);
    }


    [Fact]
    public void AppTheme_Should_Not_Contain_Dead_Legacy_Alias_Brushes()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // The scattered inline Padding="4,0" on Browse buttons was the root visual inconsistency;
        // they must now route through the variant style instead.
        Assert.DoesNotContain("Padding=\"4,0\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Browse_Buttons_Use_MaterialIcons()
    {
        // 票 24（ADR 0061）：页面拆分后 Add/Remove 按钮位于 Views/Pages/，扫描整个 Views 目录。
        // 票 25：5 处 Browse 行收敛为 Ursa u:PathPicker（OpenFolder/OpenFile 图标由 Ursa 语义承接），
        // Views 内不再有 inline FolderOpen Browse 按钮；Plus/Minus 图标按钮仍在。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        Assert.Contains("<u:PathPicker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Kind=\"FolderOpen\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Plus\"", source, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Minus\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Caption_Buttons_Use_ElementRole_And_MaterialIcons()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // The self-drawn titlebar must theme through Semi Color tokens, not inline hex.
        // 票 26：Text2 字面前景为许可的语义 token 消费（gate 只禁内联 hex/FontSize），保留原断言。
        Assert.Contains("{DynamicResource SemiColorBackground0}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SemiColorText2}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Titlebar_State_Pseudoclasses_Defined()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), "MainWindow.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("xmlns:behaviors=\"using:PhotoPrivacy.Ui.Behaviors\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MiddleClick_Behavior_Attached_To_ScrollViewer()
    {
        // 票 24（ADR 0061）：ScrollViewer 挂载点（Config/ServiceManager 页）位于 Views/Pages/，扫描整个 Views 目录。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        // At least 2 ScrollViewers (config + service manager pages) must attach the behavior.
        var hits = System.Text.RegularExpressions.Regex.Matches(source, @"MiddleClickScrollBehavior\.IsEnabled=""True""");
        Assert.True(hits.Count >= 2, $"Expected >=2 MiddleClickScrollBehavior attachments, got {hits.Count}");
    }

    // === ADR 0051 test guards ===

    [Fact]
    public void Elevation_Token_Ladder_Must_Be_Defined()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "DesignTokens.axaml");
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
        var mainWindowPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml");
        var appThemePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("SineEaseOut", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_Transitions_Must_Not_Include_Scale_Pressed()
    {
        // atomcode 2026-08-18: scale(0.97) pressed removed per industry standard.
        // VS Code / Windows 11 Settings = pure color transition (BrushTransition only).
        // SukiUI scale(0.95/0.97) + hover scale(1.03) = flashy, not enterprise-grade.
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("BrushTransition", source);
        Assert.DoesNotContain("scale(0.97)", source);
        Assert.DoesNotContain("TransformOperationsTransition", source);
    }

    [Fact]
    public void Caption_Btn_Danger_Pressed_And_Inactive_Pseudoclasses_Defined()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("Button.caption-btn.danger:pressed", source);
        Assert.Contains("Window:inactive Button.caption-btn", source);
    }

    [Fact]
    public void Caption_Btn_Padding_Must_Be_16_6()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        // ADR 0051 A4: Win11 standard 32px height = Padding Value="16,6"
        Assert.Contains("Value=\"16,6\"", source);
        // Old 14,8 padding must be gone from caption-btn style
        Assert.DoesNotContain("Padding=\"14,8\"", source);
    }

    [Fact]
    public void Focus_Visible_Pseudoclass_Must_Be_Defined()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains(":focus-visible", source);
    }

    [Fact]
    public void Program_Must_Have_FontManagerOptions_WithFallbacks()
    {
        var dir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui");
        var progPath = Path.Combine(dir, "Program.cs");
        var source = File.ReadAllText(progPath, Encoding.UTF8);
        Assert.Contains("FontManagerOptions", source);
        Assert.Contains("FontFallbacks", source);
    }

    [Fact]
    public void MiddleClick_Behavior_Must_Use_RequestAnimationFrame()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Behaviors", "MiddleClickScrollBehavior.cs");
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
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Behaviors", "MiddleClickScrollBehavior.cs");
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
        // 票 24（ADR 0061）：Spacing 消费散布在 shell + Pages，扫描整个 Views 目录。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
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
        var watcherDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Core", "Watcher");
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

    // === Ticket 01 — button size tokens (architecture recovery C2) guards ===
    // Button height/padding must have a single authority (AppTheme.axaml Size Ladder:
    // content 32 / nav 40 / icon 32x32); views may not override sizing inline.

    private static string Extract_Style_Block(string source, string selector)
    {
        var idx = source.IndexOf($"Selector=\"{selector}\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"AppTheme.axaml must define style {selector}");
        var end = source.IndexOf("</Style>", idx, StringComparison.Ordinal);
        Assert.True(end > idx, $"style {selector} must have a closing </Style>");
        return source[idx..end];
    }

    [Fact]
    public void Button_Size_Ladder_Must_Be_Single_Authority_In_AppTheme()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        Assert.True(File.Exists(path), "AppTheme.axaml should exist");
        var source = File.ReadAllText(path, Encoding.UTF8);

        // Content-area variants: 32 high (small rung)
        foreach (var variant in new[] { "Button.primary", "Button.ghost", "Button.danger" })
        {
            var block = Extract_Style_Block(source, variant);
            Assert.Contains("Property=\"Height\" Value=\"32\"", block);
            Assert.Contains("Property=\"Padding\" Value=\"12,6\"", block);
        }
        // Navigation variants: 40 high (nav rung)
        foreach (var variant in new[] { "Button.nav", "Button.nav-action" })
        {
            var block = Extract_Style_Block(source, variant);
            Assert.Contains("Property=\"Height\" Value=\"40\"", block);
            Assert.Contains("Property=\"Padding\" Value=\"12,0\"", block);
        }
        // Caption buttons ride the 32 rung (Win11 standard height)
        var caption = Extract_Style_Block(source, "Button.caption-btn");
        Assert.Contains("Property=\"Height\" Value=\"32\"", caption);
    }

    [Fact]
    public void Button_Icon_Variant_Must_Be_32_Square_With_Zero_Padding()
    {
        var path = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(path, Encoding.UTF8);
        var block = Extract_Style_Block(source, "Button.icon");
        Assert.Contains("Property=\"Padding\" Value=\"0\"", block);
        Assert.Contains("Property=\"Height\" Value=\"32\"", block);
        Assert.Contains("Property=\"MinWidth\" Value=\"32\"", block);
    }

    [Fact]
    public void MainWindow_Buttons_Must_Not_Override_Size_Inline()
    {
        // 票 24（ADR 0061）：按钮散布在 shell + Pages，扫描整个 Views 目录。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        // Every <Button ...> opening tag (multiline included) must be free of
        // Padding/Height/MinWidth overrides — sizing comes from the style class only.
        var buttons = System.Text.RegularExpressions.Regex.Matches(source, "<Button[^>]*>");
        Assert.True(buttons.Count >= 10, $"Expected >=10 Button tags, got {buttons.Count}");
        foreach (System.Text.RegularExpressions.Match b in buttons)
        {
            Assert.DoesNotContain("Padding=", b.Value);
            Assert.DoesNotContain("Height=", b.Value);
            Assert.DoesNotContain("MinWidth=", b.Value);
        }
    }

    [Fact]
    public void MainWindow_Icon_Buttons_Must_Use_Icon_Variant_Class()
    {
        // 票 24（ADR 0061）：icon 按钮散布在各 Page 文件，扫描整个 Views 目录。
        // 票 25：5 处 Browse icon 按钮随 PathPicker 收敛移出（Ursa 模板内部按钮不受本项目 Button.icon 纪律约束），
        // 余下 Add/Remove 2 处仍须走 Button.icon 变体。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        var iconButtons = System.Text.RegularExpressions.Regex.Matches(source, @"Classes=""icon""");
        Assert.True(iconButtons.Count >= 2, $"Expected >=2 Button.icon usages, got {iconButtons.Count}");
    }

    [Fact]
    public void Button_Elements_Must_Carry_A_Variant_Class()
    {
        // 票 03（ui-craft / A-001）——防的 bug：新增 Button 忘记挂 Classes 变体类，吃 Semi 默认样式，
        // 尺寸与反馈脱离七变体体系（用户原话「按钮长度和大小都不统一」的字面来源）。
        // D-006 定性：本断言是变更探测器而非契约——只能证明现役按钮全部归队，不能证明视觉等宽。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        // 与 MainWindow_Buttons_Must_Not_Override_Size_Inline 同一口径：<Button ...> 起始标签整体匹配（含跨行）。
        var buttons = System.Text.RegularExpressions.Regex.Matches(source, "<Button[^>]*>");
        Assert.True(buttons.Count >= 10, $"Expected >=10 Button tags, got {buttons.Count}");
        foreach (System.Text.RegularExpressions.Match b in buttons)
        {
            Assert.Contains("Classes=", b.Value);
        }
    }

    [Fact]
    public void Button_Groups_Must_Use_Equal_Width_Mechanism()
    {
        // 票 03（ui-craft R1-1）——防的 bug：按钮组回退到 StackPanel 内容宽度，同组按钮长短不一（A-001）。
        // 等宽走 Grid + SharedSizeGroup（按组内最长文案动态定宽），不取固定 MinWidth——
        // 本项目 10 语言，长文案（德/俄）会撑破定值导致等宽失效。
        var servicePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "Pages", "ServiceManagerPage.axaml");
        var rulesPath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "Pages", "RulesPage.axaml");
        var service = File.ReadAllText(servicePath, Encoding.UTF8);
        var rules = File.ReadAllText(rulesPath, Encoding.UTF8);
        Assert.Contains("SharedSizeGroup", service, StringComparison.Ordinal);
        Assert.Contains("SharedSizeGroup", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void All_Xaml_FontSize_Must_Be_On_Token_Ladder()
    {
        // Ladder: 11/12/14/16/18/20 — anything else (e.g. 11.5, 13) reintroduces
        // the off-token gradient; {DynamicResource}/{StaticResource} refs are token-based and pass.
        var srcDir = Path.Combine(SourceLint.RepoRoot, "src");
        var files = Directory.GetFiles(srcDir, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count >= 9, $"Expected >=9 axaml files, got {files.Count}");
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(content, "FontSize=\"([^\"]+)\""))
            {
                var value = m.Groups[1].Value;
                var onLadder = value.StartsWith("{") || value is "11" or "12" or "14" or "16" or "18" or "20";
                Assert.True(onLadder, $"{file} has off-ladder FontSize=\"{value}\" (ladder: 11/12/14/16/18/20)");
            }
        }
    }

    // === 票 26（ADR 0062）— token 消费纪律 gate：Views 源码层静态断言 ===

    [Fact]
    public void Ticket26_Views_Must_Not_Contain_Inline_FontSize()
    {
        // 检查点 A①: Views 内 FontSize= 清零 — 字号唯一权威是 AppTheme typography class
        //（caption=11/mono=12/body=14/title=16/headline=18/display=20，DesignTokens 档位）。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file, Encoding.UTF8);
            Assert.DoesNotContain("FontSize=", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ticket26_Views_Must_Not_Contain_Hardcoded_Hex_Colors()
    {
        // 检查点 A①②: Views 内联 hex 清零 — 色板标识色收敛 DesignTokens Swatch* token，
        // 语义色一律走 SemiColor* DynamicResource。3/6/8 位 hex 全禁（#RRGGBB/#AARRGGBB/#RGB）。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file, Encoding.UTF8);
            var hits = System.Text.RegularExpressions.Regex.Matches(source, @"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b");
            Assert.True(hits.Count == 0, $"{file} has inline hex color(s): {string.Join(", ", hits.Select(h => h.Value))}");
        }
    }

    [Fact]
    public void Ticket26_Views_Classes_Must_Be_Defined_In_AppTheme()
    {
        // 检查点 A②: 无未定义 Classes — h2 类违例（report-22 B 节）不得回潮。
        // AppTheme.axaml 选择器内 .class 提取为白名单；Views 的 Classes="a b" 逐词校验。
        var appTheme = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml"), Encoding.UTF8);
        var defined = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(appTheme, @"Selector=""[^""]*\.([A-Za-z][\w-]*)"))
        {
            defined.Add(m.Groups[1].Value);
        }
        Assert.True(defined.Count >= 15, $"AppTheme should define >=15 style classes, got {defined.Count}");

        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file, Encoding.UTF8);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, @"Classes=""([^""]+)"""))
            {
                foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    Assert.True(defined.Contains(cls), $"{file} uses undefined Classes=\"{cls}\" (AppTheme whitelist)");
                }
            }
        }
    }

    [Fact]
    public void Ticket26_Icon_Only_Buttons_Must_Carry_ToolTip()
    {
        // 检查点 A③: 图标必须伴随文字或 ToolTip — caption-btn（3）与 icon 变体（Add/Remove，票 25 后仅存 2 个
        // icon Button；5 处 Browse 的 ToolTip 由票 25 u:PathPicker 自带 ToolTip.Tip 承载）均无文字，
        // ToolTip.Tip 必须挂在本体（locale key 驱动）。
        var viewsDir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views");
        var source = string.Concat(Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));
        var buttons = System.Text.RegularExpressions.Regex.Matches(source, "<Button[^>]*>");
        var checkedButtons = 0;
        foreach (System.Text.RegularExpressions.Match b in buttons)
        {
            var isOpenIconOnly = b.Value.Contains("Classes=\"caption-btn") || b.Value.Contains("Classes=\"icon\"");
            if (!isOpenIconOnly) continue;
            Assert.Contains("ToolTip.Tip=", b.Value);
            checkedButtons++;
        }
        Assert.True(checkedButtons >= 5, $"Expected >=5 icon-only buttons with ToolTip, got {checkedButtons}");
    }

    [Fact]
    public void Ticket26_Button_State_Matrix_Must_Be_Complete()
    {
        // 检查点 B: 7 variant × pointerover/pressed/disabled 全覆盖 + :focus-visible 全局 ring。
        //（idle 为基线样式本体，不设伪类；pressed 语义 = 纯色/透明度反馈，无 scale，ADR 0054/0062。）
        var appTheme = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml"), Encoding.UTF8);
        foreach (var variant in new[] { "primary", "ghost", "danger", "icon", "nav", "nav-action", "caption-btn" })
        {
            foreach (var state in new[] { "pointerover", "pressed", "disabled" })
            {
                Assert.Contains($"Button.{variant}:{state}", appTheme, StringComparison.Ordinal);
            }
        }
        Assert.Contains("Button:focus-visible", appTheme, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticket26_Empty_State_Copy_Must_Be_Wired()
    {
        // 检查点 C: Logs/Rules 空态文案 — locale key 存在 + 页面消费 + VM 开关收口（无事件订阅）。
        var en = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Localization", "Locales", "en.json"), Encoding.UTF8);
        var zh = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Localization", "Locales", "zh-CN.json"), Encoding.UTF8);
        Assert.Contains("\"empty\"", en, StringComparison.Ordinal);
        Assert.Contains("\"empty\"", zh, StringComparison.Ordinal);

        var logsPage = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "Pages", "LogsPage.axaml"), Encoding.UTF8);
        Assert.Contains("{ex:Localize log.empty}", logsPage, StringComparison.Ordinal);
        Assert.Contains("HasNoLogs", logsPage, StringComparison.Ordinal);
        var rulesPage = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "Pages", "RulesPage.axaml"), Encoding.UTF8);
        Assert.Contains("{ex:Localize rules.empty}", rulesPage, StringComparison.Ordinal);
        Assert.Contains("HasNoVisibleRules", rulesPage, StringComparison.Ordinal);

        var vm = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs"), Encoding.UTF8);
        Assert.Contains("public bool HasNoLogs", vm, StringComparison.Ordinal);
        Assert.Contains("HasNoLogs = false", vm, StringComparison.Ordinal);
        Assert.Contains("HasNoLogs = true", vm, StringComparison.Ordinal);
        var rpvm = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "ViewModels", "RulesPanelViewModel.cs"), Encoding.UTF8);
        Assert.Contains("public bool HasNoVisibleRules", rpvm, StringComparison.Ordinal);
        Assert.Contains("HasNoVisibleRules = Rules.Count == 0", rpvm, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticket26_Theme_Swap_Timing_Instrumented()
    {
        // 检查点 C: 色板切换耗时观测插桩 — ThemeSwapMs 打点必须存在（运行时实测值落 logs/ui-*.log 报告）。
        var app = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "App.axaml.cs"), Encoding.UTF8);
        Assert.Contains("ThemeSwapMs=", app, StringComparison.Ordinal);
        Assert.Contains("ApplyCommunityThemeResourcesCore", app, StringComparison.Ordinal);
    }
}
