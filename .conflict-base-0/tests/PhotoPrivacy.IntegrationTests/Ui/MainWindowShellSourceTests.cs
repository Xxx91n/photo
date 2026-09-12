using System.Text;
using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 24（架构恢复第六轮 / ADR 0061）source-lint：
/// - MainWindow.axaml 降为 shell（≤180 行）
/// - Views/Pages/{Config,Logs,Rules,ServiceManager}Page.axaml 存在且各 ≤220 行
/// - Page code-behind 无长活事件订阅（+= 只允许出现在注释外的无/或显式禁止）
/// - h2 违例清零（report-22 B 节）
/// - Width=280 收敛为 AppTheme TextBox.inline-input 共享 class
/// - Service→View 直写清零：adapter 不得再写 _window.*Button.IsEnabled / .Content
/// 行数口径：文件字节文本按 \n 切分后的总行数（含空行/注释行），与文件物理行数一致。
/// </summary>
public sealed class MainWindowShellSourceTests
{
    private static string UiPath(params string[] segs) => Path.Combine(new[] { SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui" }.Concat(segs).ToArray());

    private static string ReadAll(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static int LineCount(string source) => source.Replace("\r\n", "\n").Split('\n').Length;

    [Fact]
    public void MainWindow_Shell_Should_Not_Exceed_180_Lines()
    {
        var source = ReadAll(UiPath("Views", "MainWindow.axaml"));
        var lines = LineCount(source);
        Assert.True(lines <= 180, $"MainWindow.axaml must stay a ≤180-line shell, got {lines}");
    }

    [Theory]
    [InlineData("ConfigPage")]
    [InlineData("LogsPage")]
    [InlineData("RulesPage")]
    [InlineData("ServiceManagerPage")]
    public void Pages_Should_Exist_And_Not_Exceed_220_Lines(string page)
    {
        var path = UiPath("Views", "Pages", page + ".axaml");
        Assert.True(File.Exists(path), $"Views/Pages/{page}.axaml must exist (ADR 0061 page files)");
        var lines = LineCount(ReadAll(path));
        Assert.True(lines <= 220, $"Views/Pages/{page}.axaml must stay ≤220 lines, got {lines}");
    }

    [Theory]
    [InlineData("ConfigPage")]
    [InlineData("LogsPage")]
    [InlineData("RulesPage")]
    [InlineData("ServiceManagerPage")]
    public void Page_CodeBehind_Should_Have_No_LongLived_Event_Subscriptions(string page)
    {
        var path = UiPath("Views", "Pages", page + ".axaml.cs");
        Assert.True(File.Exists(path), $"Views/Pages/{page}.axaml.cs must exist");
        var source = ReadAll(path);
        // Pages/ 目录规约（ADR 0061）：code-behind 只允许控件访问器；事件订阅（+=）一律收口 MainWindow/服务层/VM，
        // 防止页面生命周期短于订阅目标导致长活订阅泄漏。
        var violations = Regex.Matches(source, @"(?m)^\s*[^/]*\+=");
        Assert.True(violations.Count == 0,
            $"{page}.axaml.cs must not contain event subscriptions (+=); route them through MainWindow.axaml.cs");
    }

    [Fact]
    public void Pages_Must_Not_Contain_H2_Class_Usages()
    {
        var dir = UiPath("Views", "Pages");
        foreach (var file in Directory.GetFiles(dir, "*.axaml"))
        {
            var source = ReadAll(file);
            Assert.DoesNotContain("Classes=\"h2\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Views_Must_Not_Inline_Width280_Use_Shared_InlineInput_Class()
    {
        var viewsDir = UiPath("Views");
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories))
        {
            var source = ReadAll(file);
            Assert.DoesNotContain("Width=\"280\"", source, StringComparison.Ordinal);
        }
        // 单一权威在 AppTheme 的 TextBox.inline-input。
        var appTheme = ReadAll(UiPath("Styling", "AppTheme.axaml"));
        var block = appTheme[appTheme.IndexOf("TextBox.inline-input", StringComparison.Ordinal)..];
        Assert.Contains("Property=\"Width\" Value=\"280\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceAdapters_Must_Not_DirectWrite_View_Controls()
    {
        // 票 24：Service→View 直写清零 — 按钮可用性/文案必须经 VM 中转（ServiceButtons / PauseResumeAvailable）。
        var source = ReadAll(UiPath("Services", "MainWindowServiceAdapters.cs"));
        Assert.DoesNotContain("_window.PauseResumeButton.IsEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_window.PauseResumeButton.Content", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".InstallServiceButton.IsEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".UninstallServiceButton.IsEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".StartServiceButton.IsEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".StopServiceButton.IsEnabled", source, StringComparison.Ordinal);
        // 中转证据：VM 属性存在且被 adapter 使用
        Assert.Contains("vm.ServiceButtons = state", source, StringComparison.Ordinal);
        Assert.Contains("vm.PauseResumeAvailable = enabled", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_Should_Expose_ServiceButton_And_PauseResume_Mediation_State()
    {
        var source = ReadAll(UiPath("ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("public ServiceButtonState ServiceButtons", source, StringComparison.Ordinal);
        Assert.Contains("public bool PauseResumeAvailable", source, StringComparison.Ordinal);
        Assert.Contains("public string PauseResumeContent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_Should_Reference_All_Four_Pages()
    {
        var source = ReadAll(UiPath("Views", "MainWindow.axaml"));
        foreach (var page in new[] { "views:ConfigPage", "views:LogsPage", "views:RulesPage", "views:ServiceManagerPage" })
        {
            Assert.Contains(page, source, StringComparison.Ordinal);
        }
    }

    // === 票 25（架构恢复第六轮）：高频组件抽取守卫 ===

    [Fact]
    public void Views_Must_Not_Contain_Inline_Browse_Path_Rows()
    {
        // 检查点 A 验收：Browse 行零残留 inline — TextBox + Browse icon 按钮的复制粘贴行结构不得回潮，
        // 路径选择统一走 Ursa u:PathPicker（ButtonContent=btn.browse 文案 + FolderOpen 由 Ursa 语义承接）。
        var viewsDir = UiPath("Views");
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.AllDirectories))
        {
            var source = ReadAll(file);
            Assert.DoesNotContain("x:Name=\"Browse", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Classes=\"icon\" ToolTip.Tip=\"{ex:Localize dialog.select", source, StringComparison.Ordinal);
        }
        // PathPicker 弹窗标题权威仍在 localization key（dialog.select_*）— 5 处经 u:PathPicker 声明
        var configPage = ReadAll(UiPath("Views", "Pages", "ConfigPage.axaml"));
        var pickerCount = Regex.Matches(configPage, "<u:PathPicker").Count;
        Assert.True(pickerCount == 5, $"expected 5 u:PathPicker rows in ConfigPage, got {pickerCount}");
    }

    [Fact]
    public void Shell_Nav_Buttons_Must_Use_NavButton_Component()
    {
        // 检查点 B 验收：4 个侧栏导航按钮组件化 — Icon+Label+PageTag 声明式，active 态经 NavButton.IsActive。
        var shell = ReadAll(UiPath("Views", "MainWindow.axaml"));
        var navCount = Regex.Matches(shell, "<controls:NavButton").Count;
        Assert.True(navCount == 4, $"expected 4 NavButton in shell, got {navCount}");
        // inline 导航按钮结构不得回潮（Button Classes="nav" + 内嵌 Grid MaterialIcon）
        Assert.DoesNotContain("Classes=\"nav active\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_Swatches_Must_Be_DataDriven_From_Catalog()
    {
        // 检查点 B 验收：5 色板数据化 — 唯一权威 ThemeSwatchCatalog.Presets，inline RadioButton 色板块不得回潮。
        var configPage = ReadAll(UiPath("Views", "Pages", "ConfigPage.axaml"));
        Assert.Contains("ThemeSwatchCatalog.Presets", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupName=\"ThemePreset\"", configPage, StringComparison.Ordinal);
        var controls = UiPath("Views", "Controls", "ThemeSwatch.axaml.cs");
        var catalog = ReadAll(controls);
        foreach (var theme in new[] { "catppuccin", "dracula", "nord", "onedarkpro", "tokyonight" })
        {
            Assert.Contains("\"" + theme + "\"", catalog, StringComparison.Ordinal);
        }
        // GroupName/Tag 语义迁移进 ThemeSwatch 控件本体
        var swatchAxaml = ReadAll(UiPath("Views", "Controls", "ThemeSwatch.axaml"));
        Assert.Contains("GroupName=", swatchAxaml, StringComparison.Ordinal);
        Assert.Contains("Tag=", swatchAxaml, StringComparison.Ordinal);
    }
}
