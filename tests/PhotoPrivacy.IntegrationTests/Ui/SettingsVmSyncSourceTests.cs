using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 30（架构恢复第七轮）SettingsService 单一真相源 + code-behind 镜像收敛 source-lint：
/// - 三套 SelectionChanged 手动镜像清零（主题/语言/日志级别）
/// - 三套 Sync*ComboSelection 手动回填清零
/// - 色板 _pending+Loaded 重放 hack 清零（MultiBinding 替代）
/// - VM 索引属性存在且承载联动（ThemeVariantIndex/CurrentLocaleIndex/LogLevelIndex）
/// - ComboBox SelectedIndex TwoWay 绑定落位（spec 研究输入 Q1：设置单一真相源在 VM）
/// 断言读取口径（30-fix 返修）：.cs 源码断言统一走 SourceLint.ReadStripped（剥 // 行注释，
/// CompositionRootSourceTests 先例）——「票 30：XXX 已删除」考古保留注释不参与断言，
/// 代码回潮仍命中（拦截语义不放宽）；axaml 断言与行数断言用原始全文。
/// </summary>
public sealed class SettingsVmSyncSourceTests
{
    private static string UiPath(params string[] segs) => Path.Combine(new[] { SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui" }.Concat(segs).ToArray());

    private static string Read(string path) => File.ReadAllText(path, System.Text.Encoding.UTF8);

    private static string ReadStripped(params string[] segments) => SourceLint.ReadStripped(segments);

    [Fact]
    public void MainWindow_Should_Not_Contain_SelectionChanged_Handlers()
    {
        var source = ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        foreach (var handler in new[]
                 {
                     "OnThemeVariantSelectionChanged",
                     "OnLogLevelSelectionChanged",
                     "OnLocaleSelectionChanged"
                 })
        {
            Assert.DoesNotContain(handler, source, StringComparison.Ordinal);
        }

        // 事件订阅形态不得回潮（SelectionChanged += 在窗口 code-behind 清零）
        Assert.DoesNotContain("ComboBoxControl.SelectionChanged +=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Should_Not_Contain_SyncCombo_Backfill_Methods()
    {
        var source = ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        foreach (var sync in new[]
                 {
                     "SyncThemeVariantComboSelection",
                     "SyncLogLevelComboSelection",
                     "SyncLocaleComboSelection",
                     "SyncThemeSwatchSelection",
                     "_pendingThemeSwatchSelection",
                     "TryApplyThemeSwatchSelection",
                     "OnThemeSwatchListLoaded"
                 })
        {
            Assert.DoesNotContain(sync, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConfigPage_ComboBoxes_Should_Bind_SelectedIndex_To_Vm()
    {
        // 票 04 返工轮：三枚 ComboBox 与色板 MultiBinding 随分组迁入 Config*Group 子件 ——
        // 按组合件合并文本断言（ConfigPage = 页壳 + 4 子件），断言语义与绑定契约不变。
        var source = SourceLint.ReadConfigPageComposition();
        Assert.Contains("SelectedIndex=\"{Binding ThemeVariantIndex, Mode=TwoWay}\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"{Binding CurrentLocaleIndex, Mode=TwoWay}\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"{Binding LogLevelIndex, Mode=TwoWay}\"", source, StringComparison.Ordinal);
        // 色板选中态经 MultiBinding 比较绑定（VM.ThemeId ↔ 色板 ThemeId）
        Assert.Contains("ThemeSwatchSelectionConverter.Instance", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_Should_Expose_Index_Properties_With_Side_Effects()
    {
        var source = ReadStripped("src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs");
        Assert.Contains("public int ThemeVariantIndex", source, StringComparison.Ordinal);
        Assert.Contains("public int CurrentLocaleIndex", source, StringComparison.Ordinal);
        Assert.Contains("public int LogLevelIndex", source, StringComparison.Ordinal);
        // 联动副作用在 VM setter（原窗口手动镜像迁入）
        Assert.Contains("LogEnabled = level is \"all\" or \"debug\";", source, StringComparison.Ordinal);
        Assert.Contains("ShowDetailedEvents = level is \"all\" or \"debug\";", source, StringComparison.Ordinal);
        Assert.Contains("_localization.SwitchLocale(value);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSwatch_Should_Bind_IsChecked_Via_StyledProperty()
    {
        var cs = ReadStripped("src", "PhotoPrivacy.Ui", "Views", "Controls", "ThemeSwatch.axaml.cs");
        Assert.Contains("IsCheckedProperty", cs, StringComparison.Ordinal);
        // 手动回填遍历视觉树的形态不得回潮（GetVisualDescendants + PART_Radio.IsChecked 赋值）
        Assert.DoesNotContain("ThemePresetRadioControl.IsChecked = true", cs, StringComparison.Ordinal);
        var axaml = Read(UiPath("Views", "Controls", "ThemeSwatch.axaml"));
        Assert.Contains("IsChecked=\"{Binding IsChecked", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CodeBehind_Should_Shink_Vs_Ticket29_Baseline()
    {
        // 检查点 B：code-behind 行数下降 —— 基线 1437 行（票 29 收口 blob a564f4e 实测），
        // 阈值放宽 5% 防 CI 平台行尾差异；实际降幅见 report-30（-279 行）。行数口径须用原始全文。
        var source = Read(UiPath("Views", "MainWindow.axaml.cs"));
        var lines = source.Replace("\r\n", "\n").Split('\n').Length;
        Assert.True(lines <= 1365, $"MainWindow.axaml.cs must shrink vs ticket-29 baseline 1437 (≤1365), got {lines}");
    }
}
