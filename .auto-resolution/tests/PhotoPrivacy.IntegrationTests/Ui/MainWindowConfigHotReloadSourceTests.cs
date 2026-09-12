namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 30（架构恢复第七轮）迁移：OnApplyConfigClick 为 ADR 0037 遗留死代码，已删除。
/// 守卫改为锁定防抖即时应用链（ADR 0037 语义不回潮）+ 防抖写盘后的 Worker reload。
/// 断言读取口径（30-fix 返修）：.cs 源码断言统一走 SourceLint.ReadStripped（剥 // 行注释，
/// CompositionRootSourceTests 先例）——「票 30：XXX 已删除」考古保留注释不参与断言，
/// 代码回潮仍命中（拦截语义不放宽）。
/// </summary>
public sealed class MainWindowConfigHotReloadSourceTests
{
    [Fact]
    public void MainWindow_Source_Should_Expose_Explicit_Apply_Config_Action_Chain()
    {
        var source = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");

        // ADR 0037 防抖即时应用链唯一入口（手动按钮已死，不存在 OnApplyConfigClick）
        Assert.Contains("ScheduleDebouncedConfigApply", source, StringComparison.Ordinal);
        Assert.Contains("ApplyConfigImmediatelyAsync", source, StringComparison.Ordinal);
        Assert.Contains("ConfigEditor.UpdateConfig(", source, StringComparison.Ordinal);
        Assert.Contains("ReloadConfigAsync", source, StringComparison.Ordinal);
        // 防抖写盘成功后的 UI 状态再断言（自写盘 → ConfigFileWatcher 抑制 → 主动重放）语义不回潮
        Assert.Contains("ApplyRuntimeConfigToUiState();", source, StringComparison.Ordinal);
        Assert.Contains("LocalizationService.Instance.Get(\"msg.auto_saved\")", source, StringComparison.Ordinal);
        // 死代码不得回潮
        Assert.DoesNotContain("private async void OnApplyConfigClick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyConfigForCurrentModeAsync", source, StringComparison.Ordinal);
    }
}
