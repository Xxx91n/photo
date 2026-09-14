using System.Text.RegularExpressions;
using Xunit;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 02 / ui-craft — 侧栏 nav hover 反馈体系防回潮守卫（对应 decision-ledger D-004 三根因）。
/// 票 03 / ui-craft2 — 视觉语言换胶囊三态（D-002 / D-003 / D-008），本文件随之改写：
/// 定值依据：docs/design/ui-visual-standard.md §2.1「nav 胶囊三态」。
/// 约束依据：§2.2 N1（纯色 / 透明度叙事，禁 scale 与弹性缓动）、N2（过渡须覆盖 Background 与 Foreground）、
/// N3（三态层次分明：idle 透明 / hover 半透明中性胶囊 / active 主色实心胶囊）。
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
        // 剥 XAML 注释是“失效即红”自检的硬要求：nav 段写有设计依据注释，
        // 注释里出现了 Foreground / SemiColorPrimary / 4,0,0,0 等字面量，不断言注释会导致守卫恒绿。
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
        // 防：上轮 D-004 根因残留现场 —— 全局 Button 过渡只含 Background / BorderBrush，
        // nav hover 的前景（文字 + 20px 图标）0ms 硬切，是“塑料感”第一现场（规范 §2.2 N2）。
        // 回潮形态：删掉 nav 段 Transitions 里的 Foreground 一路，或新增 nav 变体时只抄 Background。
        // 票 02 / ui-craft 立；票 03 / ui-craft2 复核保留（胶囊语言下两路仍必备）。
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var block = StyleBlock(source, selector);
            Assert.Contains("BrushTransition Property=\"Foreground\"", block, StringComparison.Ordinal);
            Assert.Contains("BrushTransition Property=\"Background\"", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nav_Hover_Must_Paint_Neutral_Capsule_And_Not_Retint_Foreground()
    {
        // 防（改造自票 02 的 Nav_Hover_Foreground_Must_Narrate_Primary_Not_Neutral_White）：
        // 票 03 / ui-craft2 D-008 裁定 hover 前景不变色（MangoDisk/VS Code/WinUI/Discord 实物均中性），
        // 上轮 D-004「向 primary 叙事」revised —— pointerover 不得再写任何 Foreground setter，
        // 否则 hover 上文字/图标又被染成主色；同时必须有实体胶囊底色块（D-002 否决纯变色 hover），
        // 即 Background 必须是 SemiColorNavItemHover 中性半透明刷而非 Transparent / 主色系。
        // 票 03 / ui-craft2
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var hover = StyleBlock(source, selector + ":pointerover");
            Assert.Contains("SemiColorNavItemHover", hover, StringComparison.Ordinal);
            Assert.DoesNotContain("Foreground", hover, StringComparison.Ordinal);
            Assert.DoesNotContain("SemiColorPrimary", hover, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nav_Must_Not_Have_Retired_4px_Accent_Slot()
    {
        // 防（改造自票 02 的 Nav_Hover_Must_Preview_Active_Accent_Bar）：
        // 4px 左缘 accent 槽位体系（BorderThickness 4,0,0,0 + Transparent 占位 + BorderBrush 预示）
        // 随 D-002 整体退役 —— 回潮形态：把槽位加回来、或给胶囊态重新引入 BorderBrush/BorderThickness。
        // 同时锁胶囊正向结构：base 段必须有 CornerRadius=RadiusLg（规范 §2.1 圆角 8）。
        // 票 03 / ui-craft2
        var source = AppTheme();
        foreach (var selector in NavSelectors)
        {
            var navBase = StyleBlock(source, selector);
            Assert.DoesNotContain("BorderThickness", navBase, StringComparison.Ordinal);
            Assert.DoesNotContain("BorderBrush", navBase, StringComparison.Ordinal);
            Assert.Contains("CornerRadius", navBase, StringComparison.Ordinal);
            Assert.Contains("RadiusLg", navBase, StringComparison.Ordinal);
            var hover = StyleBlock(source, selector + ":pointerover");
            Assert.DoesNotContain("BorderBrush", hover, StringComparison.Ordinal);
        }
        // active 段同样不得回潮槽位
        var active = StyleBlock(source, "Button.nav.active");
        Assert.DoesNotContain("BorderThickness", active, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush", active, StringComparison.Ordinal);
    }

    [Fact]
    public void Nav_Active_Must_Be_Solid_Primary_Capsule_With_OnPrimary_Foreground()
    {
        // 防：D-003 / D-008 定值回潮 —— active 必须是主色实心胶囊（SemiColorPrimary）+
        // on-primary 深字（SemiColorNavActiveForeground）+ SemiBold（MangoDisk 字重 600 信号）。
        // 回潮形态一：active 退回浅色底（SemiColorPrimaryLight）与 hover 层次不分明（规范 §2.2 N3 禁同浅）；
        // 回潮形态二：前景退回白字/Text0 —— 五主题 pastel 主色上白字仅 2.0–2.5:1，不达 WCAG AA（票 03 实测表）。
        // 票 03 / ui-craft2
        var source = AppTheme();
        var active = StyleBlock(source, "Button.nav.active");
        Assert.Contains("SemiColorPrimary", active, StringComparison.Ordinal);
        Assert.Contains("SemiColorNavActiveForeground", active, StringComparison.Ordinal);
        Assert.Contains("SemiBold", active, StringComparison.Ordinal);
        Assert.DoesNotContain("SemiColorPrimaryLight", active, StringComparison.Ordinal);
        Assert.DoesNotContain("SemiColorPrimaryPointerover", active, StringComparison.Ordinal);
        Assert.DoesNotContain("SemiColorText0", active, StringComparison.Ordinal);
    }

    [Fact]
    public void Nav_Feedback_Must_Not_Use_Scale_Or_Elastic_Easing()
    {
        // 防：规范 §2.2 N1 硬性约束（ADR 0051 A2 + ADR 0054 定稿延续）——
        // 引入 scale / 位移 / 弹性缓动即“花哨感”回潮，且 scale 属 WCAG 2.2 SC 2.3.3 前庭障碍触发源。
        // 回潮形态：给 nav 加 TransformOperationsTransition 或 BackEaseInOut / ElasticEaseInOut。
        // 票 02 / ui-craft 立；票 03 / ui-craft2 复核保留。
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
