# 票 06 收口报告 — page-rules（RulesPage 整页对齐 v2.0：页壳 + 页头操作区 + 过滤工具条 + E2 空态成因二分）

> 覆盖：D-005, D-006。分支 `ui-craft2/06-page-rules`（锚定 ui-craft2/05-page-logs 之上）。
> 日期：2026-09-15。本机 CI-only：零构建零测试；验证全部为静态门禁 + 断言级预演（§4/§6）。
> 阻塞核查：本票 Blocked by 票 05 —— 05-report.md 已双轨落盘（.scratch 主本 + docs/process/reports/05-page-logs.md），阻塞解除，准开工。

---

## 0. 结论

RulesPage 整页切到 v2.0 目标形态：**页壳**（页头 MinHeight 58=LayoutPageHeaderHeight + page-title=nav.rules 22/Normal + 副标题 rules.panel.subtitle 落 md-page-shell 副标题位 + **右侧操作区**=SaveStatus + 保存/重置 SharedSizeGroup 等宽组迁入）→ **专家横幅卡**（裸 settings-card 非行集合卡，规范 §3.1/§3.2 明许；infobar CornerRadius 6→RadiusMd token 化）→ **36px 过滤工具条**（LayoutToolbarHeight 裸条无底色无边框；仅承载数据作用域的搜索过滤——atomcode 调研裁决一：页面级操作放页头右侧操作区、绝不进过滤工具条）→ **规则矩阵**（DataGrid +RowHeight=LayoutResultRowHeight 44=md 结果行；外层 ScrollViewer 撤除，DataGrid 内部滚动接管）→ **E2 空态骨架成因二分**（52×52 无底色定位盒 + ShieldCheckOutline 36 主色字形 + 标题 20 + 说明 12/520 居中/下侧留白 48；真空 rules.empty_desc / 过滤无结果 rules.empty_filtered 随新增 VM 标志 `SearchFilterActive` 切换，E5 落地）。locale×10 各 +2 key（empty_desc/empty_filtered）、孤儿键 rules.panel.title 同步清除。守卫三档：**新增 2 / 保留 15 / 改造 0 / 杀 0**；另有 HEAD 既有红 1 项呈报（§9-a，票 04 领地不跨界处置）。规范升 v2.5。报告双轨逐字一致落盘。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| # | issue 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 页内离轨值清零；按钮组等宽守卫仍绿 | ✅ | §6 复算：RulesPage Margin/Padding 字面量 6 处全在 ramp、Spacing 字面量 0、行内 FontSize/hex 0；6 视图合计 27 处零离轨；SharedSizeGroup=RuleActions 两列保留（Button_Groups_Must_Use_Equal_Width_Mechanism 预演绿，§4） |
| 2 | v2.0 节号对照表 | ✅ | §5 逐节对照 |
| 3 | 报告双轨落盘 | ✅ | §7 SHA256 比对 |

D-001 承接注：本票承接「侧栏 4 按钮观感丑陋」主诉的页面层延展——RulesPage 原实态为：旧式标题卡（title+caption 自写 Padding 24,24,24,16 非页壳）、操作行混杂（搜索/等宽按钮组/状态文同挤一行）、单行 caption 空态、规则矩阵无基准行高、外层 ScrollViewer 全页滚（表格无自滚动域）。这些与用户判 nav 丑的同款「散值密度 / 无骨架层级 / 反馈缺位」在本票全部切换为基准骨架（页壳-横幅-工具条-矩阵-E2 空态五层），对该痛点的页面侧回应与票 04/05 同构；最终验收仍以用户目检为准（规范 §7 V1）。

## 2. 改动清单（先列后改；共享文件触碰面声明）

> 共享文件触碰面（WORKFLOW §4.3，本票独占施工窗口）：
> Localization/Locales/*.json ×10（键集对等守卫覆盖）、PagesVisualAlignmentSourceTests.cs
> （只增不改既有断言）、ui-visual-standard.md（活文档随票演化，§8 留行）。
> 均为串行票面既定触碰面；g0/co 并行车道与票面文件零交集，无共享文件冲突。

| # | 文件 | 性质 | 变更 |
|---|---|---|---|
| 1 | src/PhotoPrivacy.Ui/Views/Pages/RulesPage.axaml | 本票领地 | 整页重写（75→100 行）：页壳页头（58+page-title nav.rules+副标题 caption+右操作区 SaveStatus/SharedSizeGroup 等宽组）；专家横幅卡裸 settings-card 保留+infobar CornerRadius→RadiusMd；36px 过滤工具条（搜索左置）；DataGrid +RowHeight=LayoutResultRowHeight；E2 空态成因二分（empty-icon ShieldCheckOutline+empty-title+empty-desc×2）；外层 ScrollViewer 撤除 |
| 2 | src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs | 本票领地 | +`SearchFilterActive` 计算属性（!string.IsNullOrEmpty(_searchFilter)）；ApplyFilter 收口处 OnPropertyChanged 联动（E4 同构票 05 LogLevelFilterActive） |
| 3 | src/PhotoPrivacy.Ui/Localization/Locales/*.json ×10 | **共享** | 各 +`rules.empty_desc`（真空→指向恢复默认重生内置集）+`rules.empty_filtered`（过滤无结果→提示清筛）；−`rules.panel.title`（nav.rules 取代后的孤儿键，10 语言同步删保持扁平键集对等） |
| 4 | tests/PhotoPrivacy.IntegrationTests/Ui/PagesVisualAlignmentSourceTests.cs | **共享** | +2 Fact：RulesPage_PageShell_Toolbar_And_Rows_Must_Consume_V20_Layout_Tokens、RulesPage_EmptyState_Must_Follow_E2_Skeleton（R1 注释齐备） |
| 5 | docs/design/ui-visual-standard.md | **共享** | §1.2 RulesPage 行转符合态（等宽机制沿用、落点迁页头）；§4.2 规则页行转 E2 落地态；§8 +v2.5 行 |
| 6 | .scratch/ui-craft2/reports/06-report.md + docs/process/reports/06-page-rules.md | 本票产物 | 报告双轨逐字一致 |

**不触碰**：AppTheme.axaml / DesignTokens.axaml（全部消费既有 class 与 token——page-title/caption/ghost/settings-card/data-grid/empty-icon/empty-title/empty-desc/RadiusMd/Layout*/Space*，零新增）、ConfigPage/LogsPage/ServiceManagerPage/MainWindow.axaml(.cs)/NavButton.axaml、DesignSystemTests.cs、CONTEXT.md（无新词条诉求）。

## 3. 调研（动工前置要求，通用纪律第 1 条）

**atomcode 调研实跑**：`atomcode -p` 单发串行完成（ctx_batch_execute 承载），三引擎 11 次检索 + 11 次原文核验（NN/g、Carbon×2、PatternFly、Handsontable、BleachBit、ExifCleaner、AUI、WinUI CommandBar、Ant Design、setting.page、uxdworld 等），Official/Comparative/Criticism/Community/Currency 五类角度全用，无杀进程无重开。回顾范围：账本 current 全集（D-001~D-008/A-001~A-005）+ CONTEXT.md 词条心智模型 + 票 01-05 既有调研复核 + MangoDisk 附录 D 取证值。

| 裁决 | 结论（置信度） | 本票落位 |
|---|---|---|
| 裁决一：页面级操作按钮位置 | **作用域分层**（高置信）：过滤工具条只承载数据作用域操作（搜索/筛选/批量），页面级操作（保存/重置）放**页头右侧操作区**或表单底未保存更改条；显式保存模型下按钮**绝不进过滤工具条**。先行裁决项=即时生效范式（Fluent/WinUI 设置页即时生效不设保存钮）——本项目 rules.json 显式保存事务模型（票 26/ADR 0062 既定），显式保存成立故取页头操作区（§9-g 登记） | 保存/重置 SharedSizeGroup 组 + SaveStatus 迁入页头右侧操作区（md-page-shell 右操作位 min-h 36/gap 8=SpaceSm）；过滤工具条仅放搜索 |
| 裁决二：可空表格空态 | **成因键控二分**（高置信）：真空（数据源空）与过滤无结果（数据存在被查询隐藏）必须双文案+双 CTA（真空→如何添加/启用；过滤→调整/清除筛选）；工程标准=按 source 键控非单布尔（Handsontable EmptyDataState 范式）；Avalonia DataGrid 无内置空态→VM 双标志+覆盖层 | VM +`SearchFilterActive` 标志 + HasNoVisibleRules 收口 → 空态层双文案（rules.empty_desc/empty_filtered）随其切换；覆盖层含列头（Carbon 指引：空态整体替换表格含列头）；工具条在过滤无结果时保留（本布局工具条在覆盖层之外，天然满足） |
| 专家模式横幅 | BleachBit 范式=偏好开关+启用警告+受保护项 infobar——与现状结构同构 | 横幅卡结构保留（开关+infobar），仅 token 化（CornerRadius→RadiusMd） |
| 即时生效范式 | WinUI 官方指引「设置即时生效，不要确认按钮」 | **不采纳**——rules.json 显式保存+重置二次确认系既定交互契约（ADR 0053 M6e/票 26），非本票审美对齐范围（§9-g） |

**调研结论与账本冲突核查**：无冲突——裁决一与 D-005/D-006 整页对齐方向一致，与 md-page-shell 右操作区实物吻合；裁决二与规范 §4 E5「区分空因」条文一致（E5 原文即要求过滤无结果提示可清筛）；未静默改向。

## 4. 撞红预演与三档处置（D-006）

| 断言 | 预演 | 处置 | 说明 |
|---|---|---|---|
| Views_Spacing_Must_Use_DynamicResource_Not_Literal | 绿 | 保留 | 本页全部 Spacing/ColumnSpacing 走 DynamicResource（SpaceXxs/SpaceSm/SpaceMd） |
| Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp | 绿 | 保留 | 本页 6 处字面量（24,0,24,0 / 24,16,24,24 / 0,0,0,24 / 12,8 / 0,0,0,8 / 24,24,24,48）全在 ramp |
| Settings_Groups_Must_Be_Separated_By_Per_Page_Spacing | 绿 | 保留 | per-page map 不含 RulesPage——本页无设置分组容器（横幅卡非设置分组），不加映射项 |
| Views_Must_Not_Inline_ComboBox_Width | 绿 | 保留 | 本页无 ComboBox |
| Button_Groups_Must_Use_Equal_Width_Mechanism | 绿 | 保留 | SharedSizeGroup=RuleActions 双列机制沿用（落点迁页头操作区，机制不动）——issue 验收项 1 后半句 "等宽守卫仍绿" 即本条 |
| Button_Elements_Must_Carry_A_Variant_Class | 绿 | 保留 | 两枚 ghost 变体保留，零裸按钮 |
| Pages_Should_Exist_And_Not_Exceed_220_Lines | 绿（本页） | 保留 | RulesPage 100 行 ≤220；**ConfigPage 234 行 HEAD 既有红另案呈报（§9-a）** |
| Page_CodeBehind_Should_Have_No_LongLived_Event_Subscriptions | 绿 | 保留 | code-behind 未动（仅 2 枚按钮访问器） |
| Pages_Must_Not_Contain_H2_Class_Usages | 绿 | 保留 | 无 h2 |
| All_Xaml_FontSize_Must_Be_On_Token_Ladder | 绿 | 保留 | 本页行内 FontSize 0（全走 class） |
| Locale_Json_Must_Not_Contain_Unicode_Status_Symbols | 绿 | 保留 | 新 key 文案无 ✓✗⚠ 系字形 |
| All_Locales_Have_Identical_Flat_Key_Sets | 绿 | 保留 | 10 语言 +2 −1 同步，扁平键集核验 0 missing/0 extra |
| Views_Must_Not_Inline_Width280_Use_Shared_InlineInput_Class | 绿 | 保留 | 无 Width=280；搜索框 MinWidth=260 沿用实态（§9-d） |
| Row_Divider_Must_Be_Inset_To_Label_Column_Not_Full_Bleed | 绿 | 保留 | AppTheme 未触 |
| ContextMdTerminologyTests / NavFeedbackSourceTests | 绿 | 保留 | 无 rules 措辞钉、未触 nav 领地 |
| 新增 RulesPage_PageShell_Toolbar_And_Rows_Must_Consume_V20_Layout_Tokens | — | **新增** | 防页壳/工具条/矩阵行高回潮：LayoutPageHeaderHeight+LayoutToolbarHeight+LayoutResultRowHeight+page-title 四者同场（R1 注释齐备） |
| 新增 RulesPage_EmptyState_Must_Follow_E2_Skeleton | — | **新增** | 防 E2/E5 回潮：empty-icon/empty-title/empty-desc 三 class + rules.empty/empty_desc/empty_filtered 三 key + HasNoVisibleRules + SearchFilterActive 同场（R1 注释齐备） |

三档计数：**改造 0 / 保留 15 / 新增 2 / 杀 0**。
新增守卫失效即红反向预演：若回退旧页（无 Layout* token、单行 caption 空态、无 SearchFilterActive），两 Fact 均红——反向判定成立。

## 5. v2.0 节号对照表（issue 验收项 2）

| 规范节 | 要求 | RulesPage 落位 |
|---|---|---|
| §0.1 布局骨架 | 页头 58 / h1 22 / 右操作区 min-h 36 gap 8 / 副标题 muted | MinHeight=LayoutPageHeaderHeight + page-title(nav.rules 22/Normal)；右操作区=SaveStatus+等宽按钮组（Spacing=SpaceSm=8）；副标题=rules.panel.subtitle caption（md 副标题位，§9-b） |
| §0.1 交互范式 | 按钮无 transform / 过渡白名单 | 零新增按钮形态——ghost 变体既有 BrushTransition 机制，页内零行内 transform |
| §1.1 R1-1 | 同组等宽机制 | SharedSizeGroup=RuleActions 沿用（组内最长文案动态定宽，10 语言安全）；落点由操作行迁页头操作区，机制本体不动 |
| §1.1 R1-3 | 尺寸权威 | 零行内 Height/Padding/MinWidth 覆盖按钮——Button Size Ladder 权威不动 |
| §3.1 | 非行集合卡仍用裸 settings-card（Padding 16） | 专家横幅卡沿用裸 settings-card（规范明许的同名例）；padding 结构不动 |
| §3.3 B1–B3 | 分隔线 inset / 控件宽度共享 / 组间距 | B1/B2 不适用（无行集合卡/无 ComboBox）；B3 层级间距=横幅→工具条 Margin 0,0,0,24（SpaceXl 组间档） |
| §4 E1 | 空态适用对象 | 规则矩阵=可空表格（搜索过滤可致空），HasNoVisibleRules 收口不动 |
| §4 E2 | 空态骨架 | empty-icon 52×52 无底色定位盒 + ShieldCheckOutline 36 SemiColorPrimary（与 nav.rules 图标同源，E4 克制）+ empty-title 20 + empty-desc 12/MaxWidth 520/Wrap/Center + Padding 24,24,24,48 居中偏上 |
| §4 E3 | 本地化 | rules.empty（标题沿用）+ rules.empty_desc + rules.empty_filtered ×10 语言；孤儿键 rules.panel.title 清除 |
| §4 E4 | 收口联动 | HasNoVisibleRules + SearchFilterActive 均由 ApplyFilter 收口通知（无事件订阅长活） |
| §4 E5 | 区分空因、过滤无结果须提示可清筛 | SearchFilterActive 驱动双文案：真空=empty_desc（指向「恢复默认」重生内置规则集=该语境唯一添加路径）；过滤无结果=empty_filtered（提示清空搜索框） |
| §5.1 | Layout* 定值消费 | LayoutPageHeaderHeight(58)+LayoutToolbarHeight(36)+LayoutResultRowHeight(44) 三枚消费 |
| §5.2 P1/P4/P7 | 间距分层 | 横幅→工具条 24（组间档）/工具条→矩阵 8/页边距 24,16——层级分明非等距（P7） |
| §5.4 A-008 | 字面量 ramp | 本页 6 处全在 ramp、Spacing 字面量 0（§6 复算） |
| §7 V1/V2 | before/after 同机位截图 | 登记待用户目检（§9-i；CI-only 本机零构建零运行，不虚构截图） |
| §7 V3 对照清单 | 胶囊 nav/卡片骨架/密度节奏/按钮纪律/空态构图/克制气场 | 页壳-横幅-工具条-矩阵-E2 空态五层骨架 ✅；行 44 结果行节奏 ✅；按钮组等宽+ghost 变体无 transform ✅；空态 E2 构图+成因二分 ✅；无装饰无 emoji 图标语义色 ✅ |

## 6. 离轨值复算（issue 验收项 1）

对受审 6 视图（MainWindow + 4 页 + NavButton）rg 级复算（注释剥离后计）：

- `Spacing|ItemSpacing|ColumnSpacing|RowSpacing` 字面量（非 DynamicResource）：**0**
- `Margin|Padding` 字面量合计 **27 处**（MainWindow 7 / ConfigPage 2 / LogsPage 7 / RulesPage 6 / ServiceManagerPage 4 / NavButton 1），逐值核对全部落在 ramp {0,2,4,8,12,16,24,32,48}——**离轨 0**
- RulesPage 本页 6 处明细：`Padding=24,0,24,0`（页头）/ `Margin=24,16,24,24`（内容区）/ `Margin=0,0,0,24`（横幅→工具条组距）/ `Padding=12,8`（infobar）/ `Margin=0,0,0,8`（工具条→矩阵）/ `Padding=24,24,24,48`（空态）
- RulesPage 行内 FontSize 字面量 0、hex 色值 0；Width 字面量 13 处=DataGrid 列宽 10（90/140/60×6/70×2/* 不含）+图标字形 3（20/16/36）——列宽与字形尺寸属组件度量非间距 token，兄弟页同例不列离轨
- 等宽守卫仍绿：SharedSizeGroup=RuleActions 两列 + Grid.IsSharedSizeScope 在场（§4 预演）

## 7. 双轨一致性核验

- 主本：.scratch/ui-craft2/reports/06-report.md
- 副本：docs/process/reports/06-page-rules.md
- 核验方式：同一字符串双写后逐字节 SHA256 比对 + BOM/CRLF 检查（见文末 §11 附记，写后即时执行）

## 8. 不动项核验

| 项 | 核验 |
|---|---|
| SharedSizeGroup 等宽机制 | RuleActions 两列 + ColumnSpacing=SpaceSm + HorizontalAlignment=Stretch 原样沿用，仅容器位置迁移（操作行→页头操作区） |
| 按钮接线 | x:Name=SaveRulesButton/ResetRulesButton 不变 → RulesPage.axaml.cs 访问器与 MainWindow.OnSaveRulesClick/OnResetRulesClick 接线零改动 |
| 专家模式语义 | ExpertModeToggle 绑定 RulesPanel.IsExpertMode TwoWay 不变；ExpertModeInfobar IsVisible=!IsExpertMode 不变；内容仅 CornerRadius token 化 |
| DataGrid 列与绑定 | 11 列 Header/Binding/Width/IsReadOnly 原样；ItemsSource/IsReadOnly/GridLinesVisibility/FrozenColumnCount/SelectionMode/HeadersVisibility/CanUserSortColumns 原样；唯一新增 RowHeight token |
| VM 语义 | SaveCustomRules/ResetToDefaults/LoadRules/BackfillFromStore 零改动；HasNoVisibleRules 收口语义不变；SearchFilterActive 为纯计算只读属性 |
| 搜索过滤语义 | RulesSearchBox→RulesPanel.SearchFilter TwoWay 绑定不变，ApplyFilter 匹配逻辑零改动 |
| 导航/图标族 | nav.rules/ShieldCheckOutline（nav 图标）/ShieldAccountOutline/AlertOutline 沿用既有族，无新 icon 引入 |
| 共享样式 | AppTheme.axaml/DesignTokens.axaml 零改动（全消费既有 class/token） |
| 其他页与壳 | ConfigPage/LogsPage/ServiceManagerPage/MainWindow.axaml(.cs)/NavButton 零改动 |

## 9. 张力与呈报

- **a. 既有红呈报**：`Pages_Should_Exist_And_Not_Exceed_220_Lines` 对 ConfigPage（234 行）在 HEAD 即红——系票 04 整页重写引入、属票 04 领地（共享文件一次一票，本票不跨界处置）。建议去向：ConfigPage 拆分组子件或上限复核，呈大脑/用户裁定。
- **b. 副标题保留**：rules.panel.subtitle 落 md-page-shell「副标题 muted」位（附录 D.5 明载 h1 22+副标题 14 muted）；用 caption(11) 承载——与 md 14 的差值落在已登记张力 T-5（字号阶梯：基准 6+1 档 vs 本项目 6-role）范围内，不静默改向。
- **c. rules.panel.title 孤儿键清除**：页头标题取 nav.rules（兄弟页 page-title=nav.* 口径一致），rules.panel.title×10 语言同步删除（键集对等核验通过）；rules.panel.subtitle 保留在用。
- **d. 搜索框 MinWidth=260 沿用实态**：无 search 共享 class；TextBox.inline-input(280) 语义=路径输入不套用；MinWidth 非等宽守卫/宽度守卫管辖面，如实登记。
- **e. DataGrid 裸放不进卡片**：对齐票 05 LogsPage 数据流裸放先例（工作区透出页面画布，附录 D.1 workspace transparent）；md 表格容器未取证，保守取兄弟页已 ACCEPT 先例。
- **f. 横幅卡无 group-label**：专家横幅系模式横幅非设置分组（md banner/infobar 无组标签）；规范 §3.1 明许非行集合卡裸 settings-card。
- **g. atomcode「即时生效无保存按钮」范式不采纳**：rules.json 显式保存+重置确认系票 26/ADR 0053 M6e 既定事务交互（防抖持久化语义不同），审美对齐不改交互契约——登记为裁量项。
- **h. T-4/T-5 张力继承**：行 40px 图标列未引入（T-4）、字号阶梯 13/15 档缺（T-5）均为已登记未裁定张力，本票不静默改向。
- **i. before/after 同机位截图**：登记待用户目检（CI-only 本机零构建零运行，不虚构截图资产）。

## 10. 完成定义自证

- issue 验收 checkbox 3/3：离轨清零+等宽守卫绿（§1-1）/节号对照表（§5）/报告双轨（§7）✅
- 通用纪律 6 条逐项：①atomcode 调研实跑完成（§3，一次一路串行、未杀进程未重开）②CI-only 零构建零测试，静态门禁全绿（§4/§6，唯一红=HEAD 既有红已呈报）③版本控制 but 独立分支（§4.2）+共享文件声明（§2）④中文交互+报告双轨（§7）⑤新断言 R1 注释齐备、三档计数留痕（§4）⑥过程违规呈报（§9-a 既有红、§9-b/c/d/e 裁量项）✅

## 11. 附记（写后即时执行的双轨核验结果回填位）

双轨核验 PASS：双侧同一字符串写入——SHA256 逐字一致、字节数一致、无 BOM、纯 LF（0 CRLF）。写后即时执行，非事后补录。
