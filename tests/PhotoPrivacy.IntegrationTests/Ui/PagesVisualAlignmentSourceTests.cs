using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 04 / ui-craft — 四页视觉对齐防回潮守卫。
/// 定值依据：docs/design/ui-visual-standard.md
///   §3.3 B1（分隔线自标签列起点 inset）/ B2（控件列宽度收归共享 class）/ B3（分组卡片之间须有间距）
///   §4.2（空态）、§5.2 P1（同级分组卡片之间 SpaceLg）/ P5（侧栏导航项 SpaceXs）/ P7（禁等距均匀）
///   §5.4 A-008（间距字面量随票清至 SpaceXxx）
/// 边界：本文件只锁结构不变量；色彩 / 节奏 / 观感不交给文本断言单独背锅，
/// 走人工 before/after 同机位截图对照（规范 §7 V5）。
/// 每条断言的行内注释写明「防什么 bug」（tests/TEST-CONVENTIONS.md R1，D-006）。
/// 定性：变更探测器而非契约 —— 锁的是规范定值的落位，规范改值时须同步改本文件。
/// </summary>
public class PagesVisualAlignmentSourceTests
{
    // 受审文件：四页 + MainWindow（票 04 领地）+ 侧栏控件
    private static readonly string[][] ViewFiles =
    {
        new[] { "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigPage.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "LogsPage.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "RulesPage.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ServiceManagerPage.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Controls", "NavButton.axaml" },
    };

    // DesignTokens Space ramp（DesignTokens.axaml:4-11）加 0（合法零间距，不计离轨）
    private static readonly HashSet<int> SpaceRamp = new HashSet<int> { 0, 2, 4, 8, 12, 16, 24, 32, 48 };

    /// <summary>
    /// 读 XAML 并剥除注释。XAML 不能走 SourceLint.ReadStripped —— 它按“//”剥行注释，
    /// 而 XAML 的 xmlns 声明形如 https://github.com/avaloniaui，会被拦腰截断。
    /// 剥注释同时是“失效即红”自检的硬要求：本票在 AppTheme 写入设计依据注释，
    /// 不断言剥注释后的文本会让守卫恒绿。
    /// </summary>
    private static string ReadXaml(string[] segments)
        => Regex.Replace(SourceLint.Read(segments), "<!--[\\s\\S]*?-->", string.Empty);

    private static string Describe(string[] segments) => string.Join("/", segments);

    [Fact]
    public void Views_Spacing_Must_Use_DynamicResource_Not_Literal()
    {
        // 防：A-008 回潮 —— 改编 / 新增页面时把 Spacing 写回字面量（如 Spacing="10"），
        // 间距再次脱离 Space ramp 单点权威，改 token 时页面不跟随（规范 §5.4）。
        // Spacing 是 Double，可安全走 DynamicResource；本条不适用于 Thickness（见下一条断言）。
        // 票 04 / ui-craft
        foreach (var segments in ViewFiles)
        {
            var source = ReadXaml(segments);
            foreach (Match m in Regex.Matches(source, "\\b(Spacing|ItemSpacing|ColumnSpacing|RowSpacing)\\s*=\\s*\"([^\"]+)\""))
            {
                Assert.True(
                    m.Groups[2].Value.StartsWith("{"),
                    Describe(segments) + " 的 " + m.Groups[1].Value + " 必须走 {DynamicResource SpaceXxx}，实态为 " + m.Groups[2].Value);
            }
        }
    }

    [Fact]
    public void Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp()
    {
        // 防：A-008 回潮 —— Margin / Padding 写出 ramp 外的值（历史上为 20 / 10 / 6 三档），
        // 与 DesignTokens 的 Space ramp 脱钩，页面节奏退回散值（规范 §5.4、§5.2 P7）。
        // 注意：Margin / Padding 属 Thickness，受 CONTEXT「Padding Literal Quantization」约束
        // 必须保持字面量 —— DynamicResource Double 赋 Thickness 会跳过 ThicknessTypeConverter
        // 导致布局测量期 InvalidCastException。故本断言只校验数值落在 ramp 上，
        // 不要求改成 DynamicResource（那会引入崩溃，不是收紧）。
        // 票 04 / ui-craft
        foreach (var segments in ViewFiles)
        {
            var source = ReadXaml(segments);
            foreach (Match m in Regex.Matches(source, "\\b(Margin|Padding)\\s*=\\s*\"([^\"]+)\""))
            {
                var raw = m.Groups[2].Value;
                if (raw.StartsWith("{"))
                {
                    continue;
                }

                foreach (var part in raw.Split(','))
                {
                    if (!int.TryParse(part.Trim(), out var value))
                    {
                        continue;
                    }

                    Assert.True(
                        SpaceRamp.Contains(value),
                        Describe(segments) + " 的 " + m.Groups[1].Value + " 含 ramp 外数值 " + value + "（原文 \"" + raw + "\"）");
                }
            }
        }
    }

    [Fact]
    public void Sidebar_Panel_Must_Follow_Column_Width_Not_Fixed_200()
    {
        // 防：A-007 回潮 —— 侧栏内部 Border 重新硬钉 Width="200"。它与 GridSplitter 可拖宽
        // （Clamp 170-400：MainWindow.axaml.cs 的 RestoreSidebarWidth / OnSidebarSplitterDragCompleted）
        // 直接冲突：拖宽后面板右侧留背景断层，拖窄则面板溢出被裁。
        // 修复形态 = 删除 Width，Border 在 Grid cell 内默认 Stretch 自动跟随列宽。
        // 票 04 / ui-craft
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml" });
        Assert.False(
            source.Contains("Width=\"200\""),
            "侧栏内部 Border 不得硬钉 Width=200（A-007）：须跟随 Column[0] 实际列宽");
    }

    [Fact]
    public void Row_Divider_Must_Be_Inset_To_Label_Column_Not_Full_Bleed()
    {
        // 防：规范 §3.3 B1 回潮 —— 行分隔线退回通栏（只有 BorderThickness 0,0,0,1 而无 inset），
        // 行与行视觉粘连，失去 Apple 系统设置 / WinUI SettingsCard 的「分隔线自标签列起点对齐」。
        // 定值：settings-card Padding 16 + settings-row Padding 16 = 行文本自卡边起 32，
        // 分隔线加 Margin 16,0,16,0 后同为 32，与文本左缘对齐。
        // 票 04 / ui-craft
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml" });
        var start = source.IndexOf("<Style Selector=\"Border.row-divider\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "Border.row-divider 样式段缺失");
        var end = source.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Border.row-divider 样式段未闭合");
        var block = source.Substring(start, end - start);
        Assert.True(
            block.Contains("16,0,16,0"),
            "Border.row-divider 须设 Margin 16,0,16,0（规范 §3.3 B1：自标签列起点 inset）");
    }

    [Fact]
    public void Settings_Cards_Must_Be_Separated_By_SpaceLg()
    {
        // 防：规范 §3.3 B3 / §5.2 P1 回潮 —— 分组卡片的容器回到 Spacing="0" 或漏写 Spacing，
        // 卡片直接相邻形成「均匀网格」，是 AI 感第一根因（ADR 0051 A1 / 规范 §5.2 P7）。
        // 定值 SpaceLg(16)：§5.3 Wasabi「卡片外间距不得小于 16」，Apple 分组间距 20-24 明显大于行内 8-12。
        // 票 04 / ui-craft
        foreach (var page in new[] { "ConfigPage.axaml", "ServiceManagerPage.axaml" })
        {
            var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", page });
            Assert.True(
                source.Contains("Spacing=\"{DynamicResource SpaceLg}\""),
                page + " 的分组卡片容器须设 Spacing={DynamicResource SpaceLg}（规范 §5.2 P1 / §3.3 B3）");
        }
    }

    [Fact]
    public void Views_Must_Not_Inline_ComboBox_Width()
    {
        // 防：规范 §3.3 B2 回潮 —— 下拉控件重新行内写 Width（历史形态 Width="160" ×3），
        // 与票 24（ADR 0061）已立的「Views 禁止内联宽度」冲突，控件列宽度再次散落各页无法统一调整。
        // 收敛形态：Width 单一权威迁入 Styling 的 ComboBox.inline-control 共享 class。
        // 票 04 / ui-craft
        foreach (var segments in ViewFiles)
        {
            var source = ReadXaml(segments);
            Assert.False(
                Regex.IsMatch(source, "<ComboBox[^>]*\\bWidth=\""),
                Describe(segments) + " 不得行内写 ComboBox Width（规范 §3.3 B2：改走 ComboBox.inline-control 共享 class）");
        }
    }
}
