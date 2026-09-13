using System.Text.RegularExpressions;
using Xunit;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 02 / ui-craft — 侧栏 nav hover 反馈体系防回潮守卫（对应 decision-ledger D-004 三根因）。
/// 定值依据：docs/design/ui-visual-standard.md §2「nav 反馈三态」。
/// 约束依据：§2.2 N1（纯色 / 透明度叙事，禁 scale 与弹性缓动）、N2（过渡须覆盖 Background 与 Foreground）、
/// N3（hover 须对 active 建立预示联动）。
/// 边界：本文件只锁结构不变量；色彩 / 节奏 / 观感不交给文本断言单独背锅，
/// 走人工 before/after 同机位截图对照（规范 §7 V5）。
/// </summary>
public class NavFeedbackSourceTests
{
    private static readonly string[] NavSelectors = { "Button.nav", "Button.nav-action" };

    /// <summary>
    /// 读取 AppTheme.axaml 并剥除 XAML 注释。
    /// 注意：XAML 不能走 SourceLint.ReadStripped —— 它按“//”剥行注释，
    /// 而 XAML 的 xmlns 声明形如 https://github.com/avaloniaui，会被拦腰截断。
    /// </summary>
    private static string AppTheme()
    {
        var raw = SourceLint.Read("src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        // 剥 XAML 注释是“失效即红”自检的硬要求：本票在 nav 段写有设计依据注释，
        // 注释里出现了 Foreground / BorderBrush / Transitions 等字面量，不断言注释会导致守卫恒绿。
        return Regex.Replace(raw, @"<!--[\s\S]*?-->", string.Empty);
    }

    private static string StyleBlock(string source, string selector)
    {
        var start = source.IndexOf("<Style Selector=\"" + selector + "\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "nav style block missing: " + selector);
        var end = source.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, "nav style block unterminated: " + selector);
        return source.Substring(start, end - start);
    }

    [Fact]
    public void Nav_Transitions_Must_Cover_Foreground_Not_Only_Background()
    {
        // 防：D-004 根因①的残留现场 —— 全局 Button 过渡只含 Background / BorderBrush，
        // nav hover 的前景（文字 + 20px 图标）0ms 硬切，是“塑料感”第一现场（规范 §2.2 N2）。
        // 回潮形态：删掉 nav 段 Transitions 里的 Foreground 一路，或新增 nav 变体时只抄 Background。
        // 票 02 / ui-craft
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var block = StyleBlock(source, selector);
            Assert.Contains("BrushTransition Property=\"Foreground\"", block, StringComparison.Ordinal);
            Assert.Contains("BrushTransition Property=\"Background\"", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nav_Hover_Foreground_Must_Narrate_Primary_Not_Neutral_White()
    {
        // 防：D-004 根因② —— hover 前景退回中性近白 SemiColorText0 的“纯中性灰平移”，
        // 全程无品牌色参与、20px 图标不随 hover 染色（规范 §2.1 定值 / §2.5 反例）。
        // 回潮形态：为了“更亮”把 pointerover 的 Foreground 改回 SemiColorText0。
        // 票 02 / ui-craft
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var hover = StyleBlock(source, selector + ":pointerover");
            Assert.Contains("SemiColorPrimary", hover, StringComparison.Ordinal);
            Assert.DoesNotContain("SemiColorText0", hover, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nav_Hover_Must_Preview_Active_Accent_Bar()
    {
        // 防：D-004 根因③ —— active 的 4px accent bar 在 hover 上零预示、三态各自割裂（规范 §2.2 N3）。
        // 回潮形态：删掉 pointerover 的 BorderBrush 预示；或把 base 段 BorderThickness 改回 0 ——
        // 后者会同时带回 hover/active 切换时的 4px 内容位移与 active 项图标错位。
        // 票 02 / ui-craft
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var hover = StyleBlock(source, selector + ":pointerover");
            Assert.Contains("BorderBrush", hover, StringComparison.Ordinal);
            var navBase = StyleBlock(source, selector);
            Assert.Contains("BorderThickness\" Value=\"4,0,0,0\"", navBase, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nav_Feedback_Must_Not_Use_Scale_Or_Elastic_Easing()
    {
        // 防：规范 §2.2 N1 硬性约束（ADR 0051 A2 + ADR 0054 定稿延续）——
        // 引入 scale / 位移 / 弹性缓动即“花哨感”回潮，且 scale 属 WCAG 2.2 SC 2.3.3 前庭障碍触发源。
        // 回潮形态：给 nav 加 TransformOperationsTransition 或 BackEaseInOut / ElasticEaseInOut。
        // 票 02 / ui-craft
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var block = StyleBlock(source, selector);
            Assert.DoesNotContain("scale(", block, StringComparison.Ordinal);
            Assert.DoesNotContain("TransformOperationsTransition", block, StringComparison.Ordinal);
            Assert.DoesNotContain("BackEaseInOut", block, StringComparison.Ordinal);
            Assert.DoesNotContain("ElasticEaseInOut", block, StringComparison.Ordinal);
        }
    }
}
