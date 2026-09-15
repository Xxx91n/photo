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
    // 受审文件：四页 + MainWindow + 侧栏控件 + ConfigPage 分组子件（票 04 返工轮拆分）
    private static readonly string[][] ViewFiles =
    {
        new[] { "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigPage.axaml" },
        // 票 04 返工轮：4 分组子件纳入受审面 —— ramp/Spacing/inline-Width 纪律对全部视图源生效。
        // （行数守卫 Pages<=220 不纳入：其为 ADR 0061「页面文件」复杂度预算口径，子件是组合单元非页面文件。）
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigPathGroup.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigBehaviorGroup.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigLogsGroup.axaml" },
        new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigExcludedGroup.axaml" },
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
        // 定值（票 04 / ui-craft2 复核）：分隔线 inset = 行水平 padding —— settings-row
        // Padding 16,8 的水平分量 16（MangoDisk md-settings-group ::before left/right=14px =
        // 其行 padding 14 同规则）。行集合卡（settings-card.grouped Padding 0）文本自卡边起 16；
        // 通用卡（Padding 16）文本起 32 —— 两种模式分隔线均与文本左缘对齐。
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
    public void Settings_Groups_Must_Be_Separated_By_Per_Page_Spacing()
    {
        // 防：规范 §3.3 B3 / §5.2 P1 回潮 —— 分组容器回到 Spacing="0" 或漏写 Spacing，
        // 分组直接相邻形成「均匀网格」，是 AI 感第一根因（ADR 0051 A1 / 规范 §5.2 P7）。
        // 票 04 / ui-craft2 复核上档：MangoDisk 基准组间约 24（附录 D.5），ConfigPage 取
        // SpaceXl(24)；ServiceManagerPage 票 07 / ui-craft2 已跟进，统一收紧为 SpaceXl。
        // per-page map 逐页钉死当前裁定值，防止施工顺序造成静默回退。
        var expected = new Dictionary<string, string>
        {
            ["ConfigPage.axaml"] = "SpaceXl",
            ["ServiceManagerPage.axaml"] = "SpaceXl",
        };
        foreach (var kv in expected)
        {
            var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", kv.Key });
            // '"' 字符字面量代替 \" 转义 —— 杜绝写盘链路再次吞掉反斜杠（票 04 返工轮实测教训）；
            // 变量名 needle 避开外层 expected（Dictionary 映射）重名。
            var needle = "Spacing=" + '"' + "{DynamicResource " + kv.Value + "}" + '"';
            Assert.True(
                source.Contains(needle),
                kv.Key + " 的分组容器须设 Spacing={DynamicResource " + kv.Value + "}（规范 §5.2 P1 / §3.3 B3，票 04/ui-craft2 复核）");
        }
    }

    [Fact]
    public void ConfigPage_Group_Label_Must_Sit_Above_Card_Not_Inside()
    {
        // 防：票 04 / ui-craft2 组标签骨架回潮 —— 分组标题退回 settings-card 内部
        // （卡内标题吃掉卡片顶部节奏且与行 padding 规则耦合）。MangoDisk md-settings-group
        // 与 WinUI SettingsCard 节的组标题均在卡片外上方（muted 小标签）。
        // 返工轮：4 分组迁移为 Config*Group 子件 —— 断言升级为逐文件：每组恰 1 个 group-label
        // + 恰 1 张 settings-card grouped + 无 section-header；页壳本体不得再含分组形态。
        var dq = '"';
        var groupLabel = "Classes=" + dq + "group-label" + dq;
        foreach (var file in SourceLint.ConfigPageCompositionFiles.Skip(1))
        {
            var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", file });
            Assert.False(
                source.Contains("section-header"),
                file + " 不得使用 section-header（组标签须在卡外上方走 .group-label，规范 §3.3 B4）");
            Assert.True(
                Regex.Matches(source, groupLabel).Count == 1,
                file + " 应恰有 1 个 .group-label（组标签外置规范 §3.3 B4）");
            Assert.True(
                source.Contains("settings-card grouped"),
                file + " 行集合卡须用 settings-card grouped（卡零内边距，padding 下沉到行）");
        }
        var shell = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigPage.axaml" });
        Assert.False(
            shell.Contains("section-header") || shell.Contains("settings-card"),
            "ConfigPage 页壳不得再含 section-header/settings-card（4 分组已迁入 Config*Group 子件）");
    }

    [Fact]
    public void ConfigPage_PageShell_Must_Consume_V20_Layout_Tokens()
    {
        // 防：票 04 / ui-craft2 页壳回潮 —— 页头高度 / 页标题 / 可读宽度档回退为散值或旧 class。
        // 页头 MinHeight=LayoutPageHeaderHeight(58)、标题 .page-title(22/Normal)、
        // 内容 MaxWidth=LayoutContentWidthReadable(1160) 居中 —— 均为 v2.0 附录 D.2/D.5 基准落位，
        // 三个消费点同时在场才构成 md-page-shell 骨架。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ConfigPage.axaml" });
        Assert.True(
            source.Contains("LayoutPageHeaderHeight") && source.Contains("LayoutContentWidthReadable") && source.Contains("page-title"),
            "ConfigPage 页壳须消费 LayoutPageHeaderHeight / LayoutContentWidthReadable / page-title（规范 §7 页壳基准）");
    }

    [Fact]
    public void LogsPage_PageShell_Toolbar_And_Rows_Must_Consume_V20_Layout_Tokens()
    {
        // 防：票 05 / ui-craft2 页壳+工具条+行高回潮 —— 页头高度/页标题/工具条高度/日志行高
        // 回退为散值或旧 class。页头 MinHeight=LayoutPageHeaderHeight(58)+page-title、
        // 工具条 MinHeight=LayoutToolbarHeight(36)、日志行 MinHeight=LayoutResultRowHeight(44)
        // —— 均为 v2.0 附录 D.2/D.5 基准落位，四者同场才构成 md-page-shell+workspace 骨架。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "LogsPage.axaml" });
        Assert.True(
            source.Contains("LayoutPageHeaderHeight")
                && source.Contains("LayoutToolbarHeight")
                && source.Contains("LayoutResultRowHeight")
                && source.Contains("page-title"),
            "LogsPage 须消费 LayoutPageHeaderHeight / LayoutToolbarHeight / LayoutResultRowHeight / page-title（规范 §5.1/§7 页壳与工具条基准）");
    }

    [Fact]
    public void LogsPage_EmptyState_Must_Follow_E2_Skeleton()
    {
        // 防：规范 §4 E2 回潮 —— 空态退回单行 caption 或缺图标盒/标题/说明三级骨架。
        // E2 落位 = empty-icon 定位盒 + empty-title(20) + empty-desc(12 muted) 三 class 同场；
        // E5 落位 = 真空 log.empty_desc 与过滤无结果 log.empty_filtered 双文案随
        // LogLevelFilterActive 切换（过滤无结果须提示可清筛）；HasNoLogs 收口开关不动。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "LogsPage.axaml" });
        Assert.True(
            source.Contains("empty-icon") && source.Contains("empty-title") && source.Contains("empty-desc"),
            "LogsPage 空态须用 E2 骨架 class（empty-icon / empty-title / empty-desc，规范 §4 E2）");
        Assert.Contains("{ex:Localize log.empty}", source, StringComparison.Ordinal);
        Assert.Contains("log.empty_desc", source, StringComparison.Ordinal);
        Assert.Contains("log.empty_filtered", source, StringComparison.Ordinal);
        Assert.Contains("HasNoLogs", source, StringComparison.Ordinal);
        Assert.Contains("LogLevelFilterActive", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesPage_PageShell_Toolbar_And_Rows_Must_Consume_V20_Layout_Tokens()
    {
        // 防：票 06 / ui-craft2 页壳+工具条+矩阵行高回潮 —— 页头高度/页标题/工具条高度/
        // 规则矩阵行高回退为散值或旧 class。页头 MinHeight=LayoutPageHeaderHeight(58)+page-title、
        // 过滤工具条 MinHeight=LayoutToolbarHeight(36)、矩阵行 RowHeight=LayoutResultRowHeight(44)
        // —— 均为 v2.0 附录 D.2/D.5 基准落位（atomcode 票 06 调研裁决一：页面级操作放页头
        // 右侧操作区，过滤工具条只承载数据作用域操作）。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "RulesPage.axaml" });
        Assert.True(
            source.Contains("LayoutPageHeaderHeight")
                && source.Contains("LayoutToolbarHeight")
                && source.Contains("LayoutResultRowHeight")
                && source.Contains("page-title"),
            "RulesPage 须消费 LayoutPageHeaderHeight / LayoutToolbarHeight / LayoutResultRowHeight / page-title（规范 §5.1/§7 页壳与工具条基准）");
    }

    [Fact]
    public void RulesPage_EmptyState_Must_Follow_E2_Skeleton()
    {
        // 防：规范 §4 E2+E5 回潮 —— 空态退回单行 caption 或缺图标盒/标题/说明三级骨架，
        // 或丢失成因二分文案。E2 落位 = empty-icon + empty-title(20) + empty-desc(12 muted)；
        // E5 落位 = 真空 rules.empty_desc 与过滤无结果 rules.empty_filtered 双文案随
        // RulesPanel.SearchFilterActive 切换（atomcode 票 06 调研裁决二：空态按成因键控，
        // 过滤无结果须提示可清筛）；HasNoVisibleRules 收口开关不动。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "RulesPage.axaml" });
        Assert.True(
            source.Contains("empty-icon") && source.Contains("empty-title") && source.Contains("empty-desc"),
            "RulesPage 空态须用 E2 骨架 class（empty-icon / empty-title / empty-desc，规范 §4 E2）");
        Assert.Contains("{ex:Localize rules.empty}", source, StringComparison.Ordinal);
        Assert.Contains("rules.empty_desc", source, StringComparison.Ordinal);
        Assert.Contains("rules.empty_filtered", source, StringComparison.Ordinal);
        Assert.Contains("HasNoVisibleRules", source, StringComparison.Ordinal);
        Assert.Contains("SearchFilterActive", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceManagerPage_PageShell_And_ServiceCard_Must_Consume_V20_Layout_Tokens()
    {
        // 防：票 07 / ui-craft2 页壳 + 分组卡骨架回潮 —— 页头高度 / 页标题 / 可读宽度档回退为
        // 散值或旧 class，或组标签退回卡内 section-header。页头 MinHeight=LayoutPageHeaderHeight(58)
        // + page-title、内容 MaxWidth=LayoutContentWidthReadable(1160) 居中、组标签 .group-label
        // 在卡外上方、行集合卡 settings-card.grouped —— 均为 v2.0 附录 D.2/D.5 + §3.1 基准落位。
        // 状态展示语义（规范 §0.2 / §4.2 评估口径：状态展示非空态）：状态点 + 文案双要素
        //（ServiceStatusDotColor + ServiceStatus）必须在场，禁 empty-* 骨架（无空态对象）与
        // 硬编码段头回潮（原 "ACTIONS" 字面量未走 Localize）。
        var source = ReadXaml(new[] { "src", "PhotoPrivacy.Ui", "Views", "Pages", "ServiceManagerPage.axaml" });
        Assert.True(
            source.Contains("LayoutPageHeaderHeight")
                && source.Contains("LayoutContentWidthReadable")
                && source.Contains("page-title")
                && source.Contains("group-label")
                && source.Contains("settings-card grouped"),
            "ServiceManagerPage 须消费 LayoutPageHeaderHeight / LayoutContentWidthReadable / page-title / group-label / settings-card grouped（规范 §3.1/§5.1/§7）");
        Assert.False(
            source.Contains("section-header"),
            "ServiceManagerPage 组标签须在卡外走 .group-label，不得回退卡内 section-header（规范 §3.3 B4）");
        Assert.False(
            source.Contains("empty-"),
            "ServiceManagerPage 状态展示非空态，不得引入 empty-* 骨架（规范 §4.2 评估口径：无可空列表）");
        Assert.False(
            source.Contains("Text=\"ACTIONS\""),
            "ServiceManagerPage 不得出现未走 Localize 的硬编码段头（i18n 缺口回潮防钉）");
        Assert.True(
            source.Contains("ServiceStatusDotColor") && source.Contains("{Binding ServiceStatus}"),
            "ServiceManagerPage 状态行须为状态点+文案双要素（ServiceStatusDotColor + ServiceStatus，规范 §0.2）");
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
