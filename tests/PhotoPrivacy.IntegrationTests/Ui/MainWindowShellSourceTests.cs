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
}
