# 票 04 收口报告 — page-config（ConfigPage 整页对齐 v2.0：页壳 + 组标签外置 + 行集合卡）

> 覆盖：D-005, D-006。分支 `ui-craft2/04-page-config`（锚定 ui-craft2/03-nav-capsule-tri-state 之上）。
> 日期：2026-09-14。本机 CI-only：零构建零测试；验证全部为静态门禁 + 断言级预演（§6）。
> 阻塞核查：本票 Blocked by 票 03 —— 03-report.md 已双轨落盘（.scratch ≡ docs/process/reports/03-nav-capsule-tri-state.md，diff 逐字一致），阻塞解除，准开工。

---

## 0. 结论

ConfigPage 整页切到 v2.0 目标骨架：**页壳**（页头 MinHeight 58=LayoutPageHeaderHeight + page-title 22/Normal + 右侧操作位 SaveStatus 迁入）→ **内容列**（MaxWidth=LayoutContentWidthReadable 1160 居中）→ **4 个分组单元**（组标签 group-label 12/600/muted 出卡上方 + settings-card.grouped 零内边距行集合卡）→ **行**（Padding 16,8 + MinHeight 60，分隔线 inset=行水平 padding）。通用卡 settings-card 保 Padding 16 不动（RulesPage 专家横幅卡、ServiceManagerPage 过渡态不受影响）；新增 3 token（PageTitleFontSize=22 / GroupLabelFontSize=12 / LayoutSettingsRowHeight=60）。顺手修复两处真缺陷：备份目录行隐藏时双分隔线相邻、隔离目录行与下行之间缺分隔线。守卫三档：改造 1 / 保留 5 / 新增 2 / 杀 0。规范升 v2.3。报告双轨逐字一致落盘。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| # | issue 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 页内离轨值清零（守卫或 rg 计数入报告） | ✅ | §6 复算：Margin/Padding 字面量 24 处全在 ramp、Spacing 字面量 0、离轨 0；守卫 Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp + Views_Spacing_Must_Use_DynamicResource_Not_Literal 预演绿 |
| 2 | v2.0 节号对照表 | ✅ | §5 逐节对照 |
| 3 | 报告双轨落盘 | ✅ | §7 SHA256 比对 |

D-001 承接注：本票承接「侧栏 4 按钮观感丑陋」主诉的页面层延展——用户判 nav 丑的同款「散值间距 / 无骨架层级」在 ConfigPage 的实态为：卡内标题混排、卡片与行 padding 混承、页头无基准高度、内容通栏无宽度档。本票整页切基准骨架即对该痛点的页面侧回应；最终验收仍以用户目检为准（规范 §7 V1）。

## 2. 改动清单（先列后改；共享文件触碰面声明）

> 共享文件触碰面（WORKFLOW §4.3，本票独占）：DesignTokens.axaml / AppTheme.axaml / PagesVisualAlignmentSourceTests.cs / ui-visual-standard.md / 报告双轨。其它页面文件零触碰；票 05-07 的页面领地不受本票影响（AppTheme 全部为新增/定向改，见下）。

| 文件 | 动作 | 说明 | 共享面 |
|---|---|---|---|
| `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml` | 修订 | +3 token：PageTitleFontSize=22（附录 D.2 h1）、GroupLabelFontSize=12（md-settings-group 组标签）、LayoutSettingsRowHeight=60（md-settings-row min-height）——全部新增不改既有 | **共享**——只增不改 |
| `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | 修订 | ①新增 `Border.settings-card.grouped`（Padding 0）；②`Border.settings-row` Padding 16→16,8 + MinHeight→LayoutSettingsRowHeight；③新增 `TextBlock.group-label`（12/SemiBold/Text2/Margin 2,0,0,0）与 `TextBlock.page-title`（22/Normal/Text0）；④row-divider 注释订正（inset 规则=行水平 padding，Margin 16,0,16,0 不动） | **共享**——新增 3 段 + 行定值改（他页行同步受益，卡外观不变） |
| `src/PhotoPrivacy.Ui/Views/Pages/ConfigPage.axaml` | 重写 | 216→234 行整页重构：页头 MinHeight=58 + page-title + SaveStatus 入右操作位；内容列 SpaceXl 组间 + MaxWidth 1160 居中；4 组 = group-label + settings-card.grouped；15 行统一左标签+描述丨右控件且 VerticalAlignment=Center 归一；exiftool 行孤图标删除；条件行尾随分隔线 IsVisible 联动 ×2 | 本票页面领地 |
| `tests/…/PagesVisualAlignmentSourceTests.cs` | 修订 | P1 守卫改造为 per-page map（ConfigPage=SpaceXl / ServiceManagerPage=SpaceLg 过渡）+ 新增 2 Fact（组标签外置、页壳 token 消费）；row-divider 守卫注释订正 | **共享**——断言面值按本票裁定更新 |
| `docs/design/ui-visual-standard.md` | 修订 | §3.1 骨架升版 / §3.2 实态刷新 / §3.3 新增 B4–B7 / §3.4 卡片 padding 取证误差订正 / §5.2 P1·P2 复核改判 / §5.4 复算 / §8 v2.3 行 | **共享**——规范随票修订 |
| `.scratch/ui-craft2/reports/04-report.md` + `docs/process/reports/04-page-config.md` | 新增 | 本报告双轨 | 流程产物 |

## 3. 调研（动工前置要求，通用纪律第 1 条）

atomcode 单发串行调研（5 检索任务 / 多源交叉 / 13+ 原文核验含 MangoDisk 仓库源码逐行提取、WinUI SettingsCard.xaml 92KB 模板、Ursa Form.axaml、shadcn Field 文档）。关键结论：

| 面 | 可核验数值 | 本票落位 |
|---|---|---|
| 分组卡片内边距 | WinUI SettingsCard=16；shadcn=p-4(16)；**MangoDisk=卡 0 + 行 7px/14px**（md-settings-group.vue 源码：卡片 gap:0/overflow:hidden/radius:10） | 取 MangoDisk 模型：settings-card.grouped Padding 0 + settings-row 16,8 |
| 组标题 | MangoDisk 12px/600/muted margin 1px 0 6px 2px（**卡片外上方**）；WinUI BodyStrong 14/600 margin 1,30,0,6 | group-label = 12/SemiBold/Text2 + 左 inset 2 + 组内 SpaceXs(4)；组间 SpaceXl(24) 承担 WinUI 上 30 语义 |
| 表单行 | MangoDisk min-height 60 / padding 7,14 / 三列 40px+1fr+auto / gap 10 / 标题 13px/650 / 描述 11px/1.5 / 控件区右对齐 gap 12 | 行 16,8 + MinHeight 60；标签描述结构沿用；**40px 图标列未引入 → 张力 T-4** |
| 分隔线 inset | MangoDisk ::before left/right=14px = 行水平 padding；macOS 惯例 ≥20pt | inset=行水平 padding（16），规则不变量化 |
| 页壳 | md-page-shell：页头 58 / h1 22 font-normal / 副标题 14 muted / 操作区 min-h 36 gap 8 / 内容 readable 1160、wide 1280 居中 / 页 padding 20,14 | 页头 58+page-title 22+SaveStatus 右位；内容 1160 居中；页 padding 20/14 不可消费于 Thickness → 维持 ramp 24/16（结构性约束登记 §9） |
| 可复用组件 | WinUI 有 SettingsCard（非 Avalonia）；Ursa 有 Form/FormGroup 无 SettingsCard；Semi 无 | 维持自建 class 体系，零新依赖（A-003 纪律） |

**取证误差订正（如实呈报）**：附录 D.5 原记「卡片 p-6=24」与 md-settings-group.vue 源码实态（卡 0 + 行 7/14）冲突——本票按源码实证改判并在规范 §3.4/§5.3 留订正注记；旧值不回收改写历史，按「标 revised 呈报」处理。

## 4. 撞红预演与三档处置（D-006）

| 断言 | 预演 | 处置 | 说明 |
|---|---|---|---|
| Views_Spacing_Must_Use_DynamicResource_Not_Literal | 绿 | 保留 | 本页全部 Spacing 走 DynamicResource（SpaceXl/SpaceXs/SpaceSm/SpaceXxs） |
| Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp | 绿 | 保留 | 新增字面量 24,0 / 2,0,0,0 均在 ramp |
| Row_Divider_Must_Be_Inset_To_Label_Column_Not_Full_Bleed | 绿 | 保留 | Margin 16,0,16,0 不动；注释内 32 数学订正为「inset=行水平 padding」（两种卡模式均成立） |
| Sidebar_Panel_Must_Follow_Column_Width_Not_Fixed_200 | 绿 | 保留 | 未触碰 |
| Views_Must_Not_Inline_ComboBox_Width | 绿 | 保留 | 三枚 ComboBox 仍走 inline-control |
| Settings_Cards_Must_Be_Separated_By_SpaceLg | **红（ConfigPage 上档 SpaceXl）** | **改造** → Settings_Groups_Must_Be_Separated_By_Per_Page_Spacing | per-page map：ConfigPage=SpaceXl（基准组间 24 复核上档）、ServiceManagerPage=SpaceLg（过渡态，票 07 跟进收紧）；钉死每页当前裁定值防静默回退 |
| 新增 ConfigPage_Group_Label_Must_Sit_Above_Card_Not_Inside | — | **新增** | 防组标签退回卡内：ConfigPage 不得含 section-header + group-label≥4 + settings-card grouped 在场（R1 注释齐备） |
| 新增 ConfigPage_PageShell_Must_Consume_V20_Layout_Tokens | — | **新增** | 防页壳回潮：LayoutPageHeaderHeight + LayoutContentWidthReadable + page-title 三者同场（R1 注释齐备） |

三档计数：**改造 1 / 保留 5 / 新增 2 / 杀 0**。

## 5. v2.0 节号对照表（issue 验收项 2）

| 规范节 | 要求 | ConfigPage 落位 |
|---|---|---|
| §0.1 布局骨架 | 页头 58 / h1 22 / 右操作区 / 内容 readable 1160 居中 / 卡片 bg+border+radius | MinHeight=LayoutPageHeaderHeight、page-title(22/Normal)、SaveStatus 右位、MaxWidth=LayoutContentWidthReadable+Center、grouped 卡四张 |
| §3.1 骨架 | 组=group-label+grouped 卡；行=左标签描述丨右控件 | 4 组 / 15 行全中；主题预设行（label+swatch 堆叠行）与排除目录行（label+ListBox 堆叠行）为骨架内合法变体 |
| §3.3 B1 | 分隔线 inset 自标签列起点 | row-divider Margin 16,0,16,0 = 行水平 padding 16 对齐（卡 0 时文本 16=线 16） |
| §3.3 B2 | 控件宽度共享 class | ComboBox.inline-control ×3 / PathPicker.inline-input ×4 保持 |
| §3.3 B3/P1 | 分组间距 | 组间 SpaceXl(24) —— 复核上档对齐基准 |
| §4 空态 | 本页无可空列表（路径行 Watermark 承载） | 不适用（§4.2 已评） |
| §5.2 P2 | 卡内边距 / 组标签节奏 | grouped 卡 0 + 行承载；组标签→卡 SpaceXs(4)、左 inset 2 |
| §5.2 P7 | 禁等距均匀 | 层级档：组内 4 < 行间 16 < 组间 24 < 页边距 24 —— 四档分明 |
| §5.4 A-008 | 字面量 ramp | 24 处全在 ramp、Spacing 全 DynamicResource（§6） |
| §7 V3 对照清单 | 卡片骨架 / 密度节奏 / 按钮纪律 / 克制气场 | 行集合卡+组标签+inset 分隔线 ✅；行 60/min-height 统一节奏、组间 24 ✅；两枚 icon 按钮归队、无 transform ✅；孤图标清除、点缀色仅控件语义色 ✅；**待用户目检项登记 §9** |

## 6. 离轨值复算（issue 验收项 1）

对受审 6 视图（MainWindow + 4 页 + NavButton）rg 级复算：

- `Spacing|ItemSpacing|ColumnSpacing|RowSpacing` 字面量（非 DynamicResource）：**0**
- `Margin|Padding` 字面量：**24 处**（较票 04/ui-craft 终态 25 → 净 -1：页头 24,24,24,16→24,0、SaveStatus 底行撤除、组标签左 inset 2 入 class）——**全部落在 ramp {0,2,4,8,12,16,24,32,48}，离轨 0**
- 页内新增消费 token：LayoutPageHeaderHeight / LayoutContentWidthReadable / LayoutSettingsRowHeight / PageTitleFontSize / GroupLabelFontSize / SpaceXl / SpaceXs —— 无散值新增
- 结构性约束（如实登记，非离轨）：基准页 padding 20/14、组标签 margin-bottom 6 等 Layout*/非 ramp 值受 Thickness 字面量硬约束不可直接消费，按 ramp 就近落位（§9 观察项）

## 7. 双轨一致性核验

主本 `.scratch/ui-craft2/reports/04-report.md` ↔ 副本 `docs/process/reports/04-page-config.md`：副本由主本字节级复制产生，写盘后 SHA256 比对一致、无 BOM、纯 LF。

## 8. 不动项核验

- 通用卡 `settings-card`（Padding 16）不动——RulesPage 专家横幅卡 / ServiceManagerPage 过渡态继续成立；`MainWindowSourceDiagnosticTests` 的 `Classes="settings-card"` 断言仍由该两页供给（ConfigPage 改 grouped 组合类后不供应该断言，存量 3 处足够）。
- nav / nav-action / caption-btn / NavButton.axaml 零触碰（票 03 领地成品）。
- ComboBox.inline-control(160) / PathPicker.inline-input(280) / TextBox.inline-input 宽度权威不动。
- 既有绑定与 x:Name 全保留（ThemeVariantComboBox / LocaleVariantComboBox / LogLevelComboBox / ThemeSwatchList / UserExcludedDirectoriesListBox / AddExcludedDirectoryButton / RemoveExcludedDirectoryButton / SaveStatus）；Click 路由与 VM 属性零改动。
- ToggleSwitch OnContent/OffContent 空串、ListBox Height=80（控件尺寸非间距，见 §9 观察项）、IsVisible 条件行逻辑语义不变。
- 零新 NuGet 依赖；Core/Worker/IPC 零改动；无 emoji/Unicode 状态符号引入。

## 9. 张力与呈报

- **T-4（新增登记，待大脑裁定）**：MangoDisk 设置行公式含 40px 图标列（34×34 容器）——本票反向操作删了唯一孤图标使 15 行统一；全行引入图标列属另行设计变更（图标语义逐行设计 + Material.Icons 选型），规范 §3.3 B7 与 §8 已登记。若裁定引入，由后续票统一实施而非单页孤行。
- **T-5（新增登记，待大脑裁定）**：字号阶梯——基准内容档 10/11/12/13/15/20 + h1 22 vs 本项目 6-role 11/12/14/16/18/20：行标题 13（row-label 现 14）、节标题 15 无对应 token。本票仅补 page-title 22 / group-label 12 两档（页面壳与组标签急需），行/节字号全面对齐属全局 token 决策，不动 6-role 体系。
- **观察项 a（结构性约束）**：Layout* 族为 x:Double，Margin/Padding 属 Thickness 不可 DynamicResource（Padding Literal Quantization）→ 页 padding 20/14 基准值无法按 token 落位，维持 ramp 24/16；如未来要强对齐，须改规范允许页级 padding 例外或立 Thickness token 族（资源类型变更，大脑决策面）。
- **观察项 b**：ListBox Height=80 ×2（排除目录列表）为控件尺寸定值非间距节奏，不在 ramp 口径内；未动，如实登记。
- **观察项 c（过渡态）**：settings-row 16,8+MinHeight60 为共享 class，ServiceManagerPage 行即时受益变紧凑——其卡片 Padding 16 保留至票 07 迁移 grouped/组标签外置；过渡期内该页「卡 16 + 行 16,8」仍自洽（文本 inset 32、分隔线 16+16=32 对齐），非半新半旧混排而是共享 class 的统一演进。
- **观察项 d**：SaveStatus 迁入页头右操作位后，页底状态行撤除；票 08 toast 落地时该位即 sonner 触发文案的现行承载，位置与基准操作区一致。

## 10. 完成定义自证

- [x] 页内离轨值清零（验收 1，§6 rg 复算入报告）
- [x] v2.0 节号对照表（验收 2，§5）
- [x] 报告双轨落盘（验收 3，§7 + 文末核验行）
- [x] 必读清单 7 份全读（账本 D-001~D-008 + A-001~A-005 / spec §0+§4 / issue / handoff 通用纪律 / WORKFLOW §4.2-4.4 / 规范 v2.2 全文 / CONTEXT 词条）
- [x] atomcode 调研先行动工前完成并入 §3（调研结论与附录 D 冲突处按纪律标订正呈报，未静默改向）
- [x] 改动清单先列后改（§2 先于代码改动落盘本骨架）
- [x] 通用纪律：本机零构建零测试（CI-only）；静态门禁全绿（§4+§6：XAML 标签平衡、无 BOM、纯 LF、守卫失效即红预演——新增守卫对新结构为红→绿判定成立）；措辞钉/守卫撞红三档处置留痕；与用户交互中文
- [x] WORKFLOW §4.2：but 提交 ui-craft2/04-page-config 分支（本票三提交）；§4.3 共享文件触碰面 §2 已声明；§4.4 本票未执行历史改写/丢弃类操作（纯 commit 叠加栈顶），快照未触发

> **停点**：报告落盘后停住等复核；不自动续票 05。
---

# 返工轮次 2026-09-15（prompts/04-fix-page-config-rerun.md）

## R-0. 结论

触发源=票 06 复核 §9-a 既有红升格：`Pages_Should_Exist_And_Not_Exceed_220_Lines` 对 ConfigPage 实测 234>220
（计数口径=ReadAllText 后 Replace(\r\n→\n).Split('\n').Length，ADR 0061 页面文件规约）。
处置=首选拆分路径落地：4 分组抽为 Config*Group 子件，ConfigPage 只留页壳组合（234→24 行），
视觉/行为零变化（控件访问器经 code-behind 转发，MainWindow 接线零改动，D-001 目检口径不受扰）。
**同轮另发现第二处本票领地缺陷**：原轮 P1 守卫改造写入的字符串 \ 转义在写盘链路丢失
（`Contains("Spacing="{DynamicResource ...` 编译级缺损，与收口硬验收 f1e4f13 登记的
「node 沙箱复演测不出 Roslyn 语法错」CI 盲区同款故障模式），本轮修复+登记。
守卫三档：改造 4 / 保留 10 / 新增 0 / 杀 0（另有 1 项 HEAD 侧异常事件呈报，见 R-5）。

## R-1. 缺陷复述与成因

| 缺陷 | 口径 | 来源 |
|---|---|---|
| D-fix-1 行数守卫红 | Split('\n').Length 实测 234>220 | 票 04 整页重写把 4 组 15 行表单全量内联入单文件，行数超 ADR 0061 页面预算 |
| D-fix-2 测试文件编译级缺损 | PagesVisualAlignmentSourceTests.cs 原 153 行 `"Spacing="{DynamicResource` 未转义引号 | 票 04 原轮 ctx 写盘时 \ 转义丢失（与 f1e4f13 收口登记的票02同款写盘链路缺陷） |

## R-2. 改动清单（先列后改；共享文件触碰面 §4.3 申报）

> 共享面：SourceLint.cs（+组合件 helper，零改既有成员）、MainWindowShellSourceTests.cs（2 处断言换读组合件）、
> SettingsVmSyncSourceTests.cs（1 处断言换读组合件）、PagesVisualAlignmentSourceTests.cs（缺损修复+守卫升级+ViewFiles 扩容）。
> 未触：AppTheme/DesignTokens（拆分纯结构迁移）、locale×10（键零增删）、其它页面、守卫阈值 220（ADR 0061 硬约束）。

| # | 文件 | 性质 | 变更 |
|---|---|---|---|
| 1 | Views/Pages/ConfigPathGroup.axaml(.cs) | 新增 | 路径组子件（37/11 行）：group-label + settings-card grouped（2 行） |
| 2 | Views/Pages/ConfigBehaviorGroup.axaml(.cs) | 新增 | 行为组子件（130/14 行）：9 行含条件分隔线联动×2 + 主题色板；访问器 ThemeVariantComboBoxControl/ThemeSwatchListControl |
| 3 | Views/Pages/ConfigLogsGroup.axaml(.cs) | 新增 | 日志组子件（35/13 行）：访问器 LogLevelComboBoxControl |
| 4 | Views/Pages/ConfigExcludedGroup.axaml(.cs) | 新增 | 排除组子件（32/15 行）：访问器×3（ListBox+Add/Remove） |
| 5 | Views/Pages/ConfigPage.axaml | 改写 | 234→24 行纯页壳：页头 58+page-title+SaveStatus 操作位 + ScrollViewer + StackPanel(SpaceXl, 1160 居中) + 4 子件引用（x:Name 供转发） |
| 6 | Views/Pages/ConfigPage.axaml.cs | 改写 | 6 访问器签名原样保留、转发到子件实例（MainWindow.axaml.cs 6 消费点零改动） |
| 7 | SourceLint.cs | **共享** | +ConfigPageCompositionFiles[] + ReadConfigPageComposition()（ConfigPage=页壳+4子件合并文本的唯一出处） |
| 8 | MainWindowShellSourceTests.cs | **共享** | PathPicker==5 与 ThemeSwatch 数据化两断言改读组合件（语义不变） |
| 9 | SettingsVmSyncSourceTests.cs | **共享** | ComboBox SelectedIndex×3+ThemeSwatchSelectionConverter 断言改读组合件（绑定契约不变） |
| 10 | PagesVisualAlignmentSourceTests.cs | **共享** | 153 行转义缺损修复（'"' 字符字面量拼接，零反斜杠防链路吞转义）；group-label 守卫升级逐文件断言；ViewFiles +4 子件 |

## R-3. 设计决策

- **子件形态**：UserControl + x:DataType=vm:MainWindowViewModel（编译绑定不变）+ 极简 code-behind
  （仅 InitializeComponent+访问器，无 +=，Pages 目录规约延续）。平铺 Views/Pages/ 同命名空间，
  ConfigPage 经 xmlns:pages 引用——不引子目录，Pages 目录非递归扫描守卫（h2）仍覆盖新文件。
- **访问器转发**：原 6 个 internal 访问器签名逐字保留在 ConfigPage.axaml.cs，实体转发到子件
  x:Name 实例——MainWindow.axaml.cs 全部消费点（Click 接线/RefreshI18nComboBoxItems/排除列表增删）零改动。
- **行数守卫不纳入子件（评估结论）**：Pages<=220 是 ADR 0061「页面文件」复杂度预算口径，
  页壳复杂度已由 ConfigPage 本体守卫表达；子件是组合单元而非页面文件，纳入会稀释守卫语义。
  但 ramp/Spacing/inline-Width 纪律守卫是「全部视图源」口径——ViewFiles 增补 4 子件纳入受审面。
- **组标签守卫升级而非平移**：由「整页计数>=4」升级为「每子件恰 1 group-label + 恰 1 grouped 卡
  + 无 section-header」+「页壳本体零分组形态」——拆分后逐文件断言比合并断言更严。

## R-4. 撞红预演与三档处置（D-006）

| 断言 | 预演 | 处置 | 说明 |
|---|---|---|---|
| Pages_Should_Exist_And_Not_Exceed_220_Lines | **转绿** | 保留 | ConfigPage 24 行；4 子件 32-130 行各自远低于上限且按 R-3 评估不入清单 |
| Views_Must_Not_Contain_Inline_Browse_Path_Rows | 绿 | **改造** | PathPicker==5 原钉 ConfigPage.axaml 单文件——拆分后归零必红；改读组合件合并文本，断言值 5 不变 |
| Theme_Swatches_Must_Be_DataDriven_From_Catalog | 绿 | **改造** | ThemeSwatchCatalog.Presets 随行为组迁出 ConfigPage；改读组合件，contain/排除双断言不变 |
| SettingsVmSync.ConfigPage_ComboBoxes_* | 绿 | **改造** | SelectedIndex×3+converter 断言改读组合件；方法名保留（组合件即页面语义） |
| ConfigPage_Group_Label_Must_Sit_Above_Card_Not_Inside | 绿 | **改造** | 升级逐文件断言（见 R-3），比原断言更严 |
| ConfigPage_PageShell_Must_Consume_V20_Layout_Tokens | 绿 | 保留 | 页壳三 token 留在 ConfigPage 本体 |
| Settings_Groups_*_Per_Page_Spacing | 绿 | 保留 | 外层 StackPanel Spacing=SpaceXl 留在页壳 |
| Views_Spacing/Margin_Padding/Inline_ComboBox_Width | 绿 | 保留+扩面 | ViewFiles 10 文件全绿：Spacing 字面量 0、Margin/Padding 字面量 2 处全在 ramp（24,0,24,0 / 24,16,24,24）、ComboBox 无行内 Width |
| Pages_Must_Not_Contain_H2 / Width280 / Browse 名 | 绿 | 保留 | Pages 目录扫描自然覆盖新文件；零违例 |
| MainWindowSourceDiagnosticTests（combined 扫描） | 绿 | 保留 | settings-card 裸形态由 Rules/SM 供给；ToggleSwitch/PathPicker 在组合件内计数不变 |
| HardcodedChineseScan | 绿 | 保留 | 语言下拉豁免按内容模式非文件名——迁移后仍命中豁免 |
| Page_CodeBehind 无 += 订阅 | 绿 | 保留 | 4 页+4 子件 code-behind 零事件订阅 |
| locale 键集对等 / 措辞钉 / nav 守卫 | 未触 | 保留 | locale×10 零增删；CONTEXT.md 未触 |
| **新增**：C# 词法骨架门禁（本轮加测） | 全绿 | — | 迷你词法器剥字符串/字符/注释后计括号：4 测试文件 0 未终结字符串、花括号/圆括号全平衡——针对 D-fix-2 同款链路缺陷的可复演检查 |

## R-5. 过程异常呈报（如实登记，不隐瞒）

- **PagesVisualAlignmentSourceTests.cs 改动被 absorb 进票 07 车道 zmx**：本窗写入该文件后，
  GitButler 依赖归属把落在 zmx 行域上的 hunk 卷进了 07 车道提交（磁盘 blob 1e494302 与 zmx 树内 blob
  逐字节一致、but status 不再列为未提交）。后果=zmx 提交面超出其 message 描述（含本返工的守卫改造），
  且栈顶曾短暂处于「引用 ConfigPageCompositionFiles/子件文件但二者未提交」的悬空态——本轮其余 13 文件
  提交落地即恢复一致性（helper 与子件位于 04 车道、在 zmx 祖先序内）。属并行/引擎行为非本窗主动，如实登记。
- **D-fix-2 为复发型缺陷**：f1e4f13（收口硬验收）已登记「node 写盘吞 C# \ 转义、node 复演测不出」盲区，
  本轮仍踩同款——佐证该盲区需工具层而非流程层修复；本轮以零反斜杠写法（'"' 字符字面量）规避。

## R-6. 静态门禁与验收复演（CI-only 零构建零测试，口径同原轮）

- 行数守卫：ConfigPage 24 行（守卫口径 Split('\n').Length）✅ 转绿；子件 37/130/35/32 行。
- 组合件断言全量复演：PathPicker=5、ThemeSwatchCatalog.Presets 在场、GroupName=ThemePreset 缺席、
  SelectedIndex×3+ThemeSwatchSelectionConverter 在场、group-label 逐文件恰 1、页壳三 token+SpaceXl 在场 ✅。
- 访问器↔x:Name 交叉核验：6 访问器在对应子件 .axaml 有同名 x:Name、子件 .cs 有访问器、页壳 .cs 有转发 ✅。
- XAML 平衡 5/5、无 BOM、纯 LF；code-behind 零 +=；测试文件词法骨架平衡（R-4 末行）。
- 离轨值复算（含新子件）：10 视图 Spacing 字面量 0、Margin/Padding 字面量 2 处全在 ramp、离轨 0。
- 视觉/行为零变化声明：全部 XAML 结构逐行搬移（行/分隔线/绑定/IsVisible 联动/注释一并迁移），
  样式类名、locale key、绑定路径、x:Name 语义全部保留；MainWindow.axaml(.cs) 零改动。

## R-7. 版本控制（§4.2）

- 分支：改动提交至 `ui-craft2/04-page-config`（票 04 车道中栈插入；05/06/07 车道随栈自动 rebase——
  纯 commit 叠加非 §4.4 列管的历史改写类操作，无快照触发）。
- 提交物：4 子件×2 + ConfigPage.axaml/.cs + SourceLint + MainWindowShell + SettingsVmSync +
  本报告 docs 副本。PagesVisualAlignmentSourceTests.cs 已在 zmx 内（见 R-5 呈报）。
- 未 push、未建 PR（§4.2 默认）。
