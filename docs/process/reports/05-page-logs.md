# 票 05 收口报告 — page-logs（LogsPage 整页对齐 v2.0：页壳 + 工具条 + E2 空态 + emoji 清零）

> 覆盖：D-005, D-006。分支 `ui-craft2/05-page-logs`（锚定 ui-craft2/04-page-config 之上）。
> 日期：2026-09-15。本机 CI-only：零构建零测试；验证全部为静态门禁 + 断言级预演（§6）。
> 阻塞核查：本票 Blocked by 票 04 —— 04-report.md 已双轨落盘且首脑复核 ACCEPT（README W4 状态表），阻塞解除，准开工。

---

## 0. 结论

LogsPage 整页切到 v2.0 目标形态：**页壳**（页头 MinHeight 58=LayoutPageHeaderHeight + page-title 22/Normal）→ **36px 裸工具条**（LayoutToolbarHeight；级别过滤 ComboBox 左 / 清空 ghost 右，去原 NavBackground 条底与上下边线）→ **日志流**（行高 32→LayoutResultRowHeight 44、事件徽章 RadiusSm→RadiusXl 胶囊化 = md radius-999 等效）→ **E2 空态骨架**（52×52 无底色定位盒 + 36 主色字形 + 标题 20 + 说明 12 muted/520 居中 + 下侧留白 48 补偿视觉中心；空因双文案随 `LogLevelFilterActive` 切换，E5 落地）。顺手清除页内 emoji/Unicode 前缀（`MapEventStyle` 6 处，§6 T3/§7 V3 纪律）与一处潜伏缺口（LogLevel 热重载不回填 ComboBox 选中态——setter 联动通知补齐）。新增 1 token（EmptyDescFontSize=12）+ 3 空态 class + 2 locale key×10 语言。守卫三档：改造 2 / 保留 12 / 新增 2 / 杀 0。规范升 v2.4。报告双轨逐字一致落盘。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| # | issue 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 页内离轨值清零（守卫或 rg 计数入报告） | ✅ | §6 复算：LogsPage Margin/Padding 字面量 7 处全在 ramp、Spacing 字面量 0、行内 FontSize/hex 0；6 视图合计 25 处零离轨；守卫 Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp + Views_Spacing_Must_Use_DynamicResource_Not_Literal 预演绿 |
| 2 | v2.0 节号对照表 | ✅ | §5 逐节对照 |
| 3 | 报告双轨落盘 | ✅ | §7 SHA256 比对 |

D-001 承接注：本票承接「侧栏 4 按钮观感丑陋」主诉的页面层延展——LogsPage 原实态为：无页头基准（DockPanel 顶行边线工具条）、行高 32 自研密度、事件徽章圆角 4 + emoji/Unicode 前缀、空态=单行 caption。这些与用户判 nav 丑的同款「散值密度 / 无骨架层级 / 符号堆叠」在本票全部切换为基准骨架（页壳-工具条-数据流-E2 空态四层），对该痛点的页面侧回应与票 04 同构；最终验收仍以用户目检为准（规范 §7 V1）。

## 2. 改动清单（先列后改；共享文件触碰面声明）

> 共享文件触碰面（WORKFLOW §4.3，本票独占）：DesignTokens.axaml / AppTheme.axaml /
> MainWindowViewModel.cs / MainWindow.axaml.cs / AuditTailService.cs / 10 × locale JSON /
> PagesVisualAlignmentSourceTests.cs / AuditTailServiceFormattingTests.cs /
> ui-visual-standard.md / 报告双轨。
> 票 06/07 页面领地（RulesPage/ServiceManagerPage）零触碰；共享文件全部为新增/定向改（见下表）。

| 文件 | 动作 | 说明 | 共享面 |
|---|---|---|---|
| `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml` | 修订 | +1 token：`EmptyDescFontSize=12`（E2 说明档；MangoDisk `text-content-body`=12，6-role 阶梯无 12 非等宽档） | **共享**——只增不改 |
| `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | 修订 | +3 class：`TextBlock.empty-title`（DisplayFontSize 20/Normal/Text0/Center）、`TextBlock.empty-desc`（EmptyDescFontSize 12/Normal/Text2/MaxWidth 520/Wrap/Center）、`Border.empty-icon`（52×52/Center）——E2 空态骨架共享角色，票 06 空态直接复用 | **共享**——只增不改 |
| `src/PhotoPrivacy.Ui/Views/Pages/LogsPage.axaml` | 重写 | 65→83 行整页重构：页壳（页头 MinHeight=LayoutPageHeaderHeight 58 + page-title）+ 36px 工具条（LayoutToolbarHeight；级别过滤 ComboBox 左 + 清空 ghost 右）+ 日志流（行高 32→LayoutResultRowHeight 44、事件徽章 RadiusSm→RadiusXl 胶囊化）+ E2 空态骨架（52 定位盒 + 36 主色字形 + 标题 20 + 说明 12 双文案） | 本票页面领地 |
| `src/PhotoPrivacy.Ui/Views/Pages/LogsPage.axaml.cs` | 修订 | +`LogLevelComboBoxControl` 访问器（locale 刷新接线用，ConfigPage 同款模式） | 本票页面领地 |
| `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs` | 修订 | `RefreshI18nComboBoxItems` 接线 LogsPage 级别下拉（RefreshComboBoxItems + ForceComboBoxSelectionBoxRefresh 各一行） | **共享**——定向加 3 行 |
| `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs` | 修订 | +`LogLevelFilterActive`（E5 空因区分信号）；`LogLevel` setter 联动通知 `LogLevelIndex`/`LogLevelFilterActive`——顺手修复热重载直赋 LogLevel 时 ComboBox 选中态不回填的潜伏缺口 | **共享**——定向加 |
| `src/PhotoPrivacy.Ui/Services/AuditTailService.cs` | 修订 | `MapEventStyle` 6 处 Display 前缀 emoji/Unicode 符号清零（🟢✅⏭🔍⚠️❌）；规范 §6 T3/§7 V3「禁用 emoji/Unicode 符号」+ ADR 0050 A2 纪律，事件语义由 ColorHex 语义色继续承载 | **共享**——定向改 |
| `src/PhotoPrivacy.Ui/Localization/Locales/*.json` ×10 | 修订 | +2 key/语言：`log.empty_desc`（真空说明）、`log.empty_filtered`（过滤无结果说明，E5 提示可清筛） | **共享**——只增不改（`log.empty` 原值不动，转作空态标题） |
| `tests/…/Ui/PagesVisualAlignmentSourceTests.cs` | 修订 | +2 Fact：LogsPage 页壳/工具条/行高 token 消费守卫、E2 空态骨架在场守卫（R1 注释齐备） | **共享**——断言面新增 |
| `tests/…/Ui/AuditTailServiceFormattingTests.cs` | 修订 | 2 断言期望值去 emoji（改造档——断言意图=事件映射正确性/级别过滤放行不变） | **共享**——断言面更新 |
| `docs/design/ui-visual-standard.md` | 修订 | §4.2 日志页 E2 落地态刷新 + §8 v2.4 行 | **共享**——规范随票修订 |
| `.scratch/ui-craft2/reports/05-report.md` + `docs/process/reports/05-page-logs.md` | 新增 | 本报告双轨 | 流程产物 |

## 3. 调研（动工前置要求，通用纪律第 1 条）

**atomcode 额度呈报**：`atomcode -p` 单发即返 `[rate-limited] 5h window exhausted — resets around 02:17`——配额耗尽为 skill 明列的唯一不续跑例外，未重试未杀进程（串行护栏下亦未开新调研）。降级路径 = 附录 D 同款 `gh api` 只读取证（MangoDisk 仓库源码逐行直读，GPL-3.0 零拷贝）+ 账本 current 全集（D-001~D-008/A-001~A-005）+ CONTEXT.md 词条心智模型 + 票 01-04 报告内既有调研结论复核。

| 面 | 可核验定值（gh api 直读源码） | 本票落位 |
|---|---|---|
| 空态容器 | `md-empty-state.vue`：flex 居中、gap 10、padding `24 24 clamp(52,9vh,88)`（下侧补偿视觉中心） | Padding `24,24,24,48`（52→ramp 近档 48）+ VerticalAlignment=Center + StackPanel Spacing SpaceMd(12，gap 10 近档) |
| 空态图标盒 | 52×52 **纯定位盒——源码无底色无边框**，`grid place-items-center`；字形 36 `text-primary`（compact 44/28 muted） | `Border.empty-icon` 52×52 Center + MaterialIcon 36 `SemiColorPrimary`；**不画底色/边框**（源码复核订正「图标盒=带底盒」的潜在误读） |
| 空态文案 | 标题 `content-empty-title` 20 card-fg；说明 `content-body` 12 muted max-w 520 居中；actions margin-top 8 | `empty-title`(20/Text0) + `empty-desc`(12/Text2/MaxWidth 520/Wrap/Center)；本页无 actions 位（可选位判省） |
| 工具条 | `md-result-filter-toolbar.vue`：h-full 裸条、gap 2(8)、px-3(12)、主区 flex-1 + aside 右端——**无底色无边框**；附录 D.2 workspace toolbar=36、control=30 | DockPanel MinHeight=LayoutToolbarHeight(36)、左级别过滤右清空、去原 NavBackground+上下边线（裸条对齐） |
| 过滤控件形态 | `md-category-filter.vue`：分段胶囊排（h-7.5=30、rounded-md、active=primary/10 底+primary 字+600）；`md-result-search.vue`：控件高=workspace-control-height=30 | **ComboBox.inline-control 承载同级别语义**——取舍：①同一属性 LogLevelIndex 在 ConfigPage 已由 ComboBox 编辑，双控件形态分裂反而不一致；②CI-only 零构建，新建分段控件无验证手段风险高；③ComboBox 高度天然落 30-32 控件档。如实登记为裁量项（§9-a） |
| 日志行 | `md-result-item-content.vue`：padding `5,6`、icon 格 30×36、标题 13/徽章 radius-999 padding 2,6/值 12 右对齐 → 行总高 ~44（附录 D.2 结果行=44） | 行高 32→`LayoutResultRowHeight`(44)；事件徽章 `RadiusSm(4)`→`RadiusXl(12)` 胶囊化（md radius-999 等效）；徽章 Padding 8,2 保持（md 6,2 的 6 不可量化→8 已在位） |
| 页面骨架 | `md-page-shell`（附录 D.5）：页头 58 + h1 22 + 内容 readable 1160 | 页头 MinHeight=LayoutPageHeaderHeight + page-title；内容 Margin `24,16,24,24`（与 ConfigPage 页壳同式）；日志流为工作区全幅面不套用 readable 1160 限宽（数据流非表单列，§9-b 登记） |
| emoji/Unicode | 规范 §6 T3 禁用 emoji/Unicode 符号（ADR 0050 A2 已清零 locale JSON；状态走 Material.Icons/语义色） | `MapEventStyle` 6 处 emoji 前缀清零，ColorHex 语义色保留——页内离轨清除（v2.0 验收 V3「克制气场/状态图标纪律」） |

**调研结论与账本冲突核查**：无冲突——D-005 观感对标方向一致；附录 D.5 空态描述「图标盒 52」经源码复核为**无底色定位盒**（非带底卡片盒），按此落位并在规范 §4.2 落地态留注；未静默改向。

## 4. 撞红预演与三档处置（D-006）

| 断言 | 预演 | 处置 | 说明 |
|---|---|---|---|
| Views_Spacing_Must_Use_DynamicResource_Not_Literal | 绿 | 保留 | 本页唯一 Spacing 走 DynamicResource（SpaceMd） |
| Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp | 绿 | 保留 | 本页 7 处字面量（24,0,24,0 / 24,16,24,24 / 0,0,0,8 / 4 / 8,2 / 0,0,8,0 / 24,24,24,48）全在 ramp |
| Settings_Groups_Must_Be_Separated_By_Per_Page_Spacing | 绿 | 保留 | per-page map 不含 LogsPage（无设置分组） |
| Views_Must_Not_Inline_ComboBox_Width | 绿 | 保留 | 级别过滤 ComboBox 走 inline-control 宽度权威 |
| Ticket26_Empty_State_Copy_Must_Be_Wired | 绿 | 保留 | `{ex:Localize log.empty}` + `HasNoLogs` + VM 双赋值全部在场（log.empty 转作空态标题） |
| Ticket26_Views_Must_Not_Contain_Inline_FontSize / Hex | 绿 | 保留 | 本页 FontSize=0 / hex=0（ColorHex 为绑定非字面量） |
| Ticket26_Views_Classes_Must_Be_Defined_In_AppTheme | 绿 | 保留 | 本页用类 8 枚（page-title/inline-control/ghost/caption/mono/empty-icon/empty-title/empty-desc）全部在 AppTheme 定义 |
| Pages_Should_Exist_And_Not_Exceed_220_Lines | 绿 | 保留 | LogsPage 83 行 |
| Page_CodeBehind_No_Event_Subscriptions | 绿 | 保留 | code-behind 仅 +1 访问器属性，无 `+=` |
| Pages_Must_Not_Contain_H2_Class_Usages | 绿 | 保留 | 无 h2 |
| All_Xaml_FontSize_Must_Be_On_Token_Ladder | 绿 | 保留 | 新增 class 全部 DynamicResource（DisplayFontSize/EmptyDescFontSize） |
| Locale_Json_Must_Not_Contain_Unicode_Status_Symbols | 绿 | 保留 | 新 key 文案无 ✓✗⚠ 系字形（em-dash 分隔与既有 log.empty 同款风格） |
| All_Locales_Have_Identical_Flat_Key_Sets | 绿 | 保留 | 10 语言同步 +2 key，扁平化键集核对一致（§6） |
| AuditTailServiceFormattingTests（2 断言钉 emoji） | **红**（DisplayEvent 去前缀） | **改造** | `Assert.Equal("✅ 清理完成")`→`"清理完成"`、`Assert.Equal("🔍 检测到文件")`→`"检测到文件"`——断言意图=事件映射/级别放行正确性不变，emoji 系钉旧值非语义 |
| 新增 LogsPage_PageShell_Toolbar_And_Rows_Must_Consume_V20_Layout_Tokens | — | **新增** | 防页壳/工具条/行高回潮：LayoutPageHeaderHeight + LayoutToolbarHeight + LayoutResultRowHeight + page-title 四者同场（R1 注释齐备） |
| 新增 LogsPage_EmptyState_Must_Follow_E2_Skeleton | — | **新增** | 防 E2/E5 回潮：empty-icon/empty-title/empty-desc 三 class + log.empty/desc/filtered 三 key + HasNoLogs + LogLevelFilterActive 同场（R1 注释齐备） |

三档计数：**改造 2 / 保留 13 / 新增 2 / 杀 0**。
新增守卫失效即红预演：若回退为旧页（无 Layout* token、单行 caption 空态），两 Fact 均红——反向判定成立。

## 5. v2.0 节号对照表（issue 验收项 2）

| 规范节 | 要求 | LogsPage 落位 |
|---|---|---|
| §0.1 布局骨架 | 页头 58 / h1 22 / 右操作区 / 内容距 24,16,24,24 | MinHeight=LayoutPageHeaderHeight + page-title(22/Normal)；右操作位判省（页级操作已由工具条承接，见 §9-b）；内容 Margin `24,16,24,24` 与 ConfigPage 同式 |
| §3.1 骨架 | 组=group-label+grouped 卡 | **不适用**——日志页为工作区数据流页非表单分组页（E1 适用对象=日志流列表非设置行） |
| §3.3 B1–B3 | 分隔线 inset / 控件宽度共享 class / 组间距 | B1/B3 不适用（无行集合卡/无设置分组）；B2 = ComboBox.inline-control 复用 |
| §4 E1 | 空态适用对象存在性 | 日志流列表=适用对象；`HasNoLogs` 收口（AppendLog/ClearLogs 联动）不动 |
| §4 E2 | 空态骨架（图标盒 52/字形 36 主色/标题 20/说明 12/520 居中/下补≈52/可选操作位） | 全项落地：empty-icon 52 定位盒（**源码复核无底色无边框**）+ MaterialIcon 36 SemiColorPrimary + empty-title 20 + empty-desc 12/MaxWidth 520/Wrap/Center + Padding `24,24,24,48`（52→ramp 近档）；操作位判省（本页无可触发重试/跳转语义） |
| §4 E3 | 本地化 key | `log.empty`（标题）+ `log.empty_desc` + `log.empty_filtered` ×10 语言 |
| §4 E4 | 保守克制 | 无新 icon 族/无新主题刷——FileDocumentOutline 与 nav 图标同源、empty-* 三 class 走既有 token |
| §4 E5 | 区分空因，过滤无结果须提示可清筛 | `LogLevelFilterActive`（LogLevelIndex≠0）驱动双文案：真空=`log.empty_desc`、过滤无结果=`log.empty_filtered`（提示调低级别=清筛路径） |
| §5.1 | Layout* 定值消费 | LayoutPageHeaderHeight(58) / LayoutToolbarHeight(36) / LayoutResultRowHeight(44) 三枚全消费 |
| §5.2 P1/P2/P7 | 间距分层节奏 | 工具条→列表 8（SpaceSm）、页边距 24/16、行内 padding 4、徽章 inset 8——层级分明非等距 |
| §5.4 A-008 | 字面量 ramp | 本页 7 处全在 ramp、Spacing 0 字面量（§6 复算） |
| §6 T3 | 禁用 emoji/Unicode 符号 | `MapEventStyle` 6 处前缀清零（DisplayEvent 纯本地化文案，状态语义由 ColorHex 承载） |
| §7 V1/V2 | before/after 同机位截图 / 视觉基线 | 登记待用户目检（§9-e；CI-only 本机零构建零运行，不虚构截图） |
| §7 V3 对照清单 | 卡片骨架/密度节奏/按钮纪律/克制气场 | 页壳-裸工具条-数据流-E2 空态四层骨架 ✅；行 44 节奏+列表 padding 4 ✅；清空 ghost 单变体无 transform ✅；emoji 清零+图标语义色纪律 ✅ |

## 6. 离轨值复算（issue 验收项 1）

对受审 6 视图（MainWindow + 4 页 + NavButton）rg 级复算（注释剥离后计）：

- `Spacing|ItemSpacing|ColumnSpacing|RowSpacing` 字面量（非 DynamicResource）：**0**
- `Margin|Padding` 字面量：**25 处**（票 04 终态 24 → 净 +1：旧页工具条边线/内距等约 6 处撤、新页 7 处入——页头 `24,0,24,0`、内容 `24,16,24,24`、工具条 `0,0,0,8`、列表 `4`、徽章 `8,2`+`0,0,8,0`、空态 `24,24,24,48`）——**全部落在 ramp {0,2,4,8,12,16,24,32,48}，离轨 0**
- 行内 `FontSize=`：**0**；行内 hex 颜色：**0**（`ColorHex` 为绑定值非字面量）
- 页内新增消费 token：LayoutPageHeaderHeight / LayoutToolbarHeight / LayoutResultRowHeight / RadiusXl / SpaceMd / DisplayFontSize / EmptyDescFontSize —— 无散值新增
- 结构性约束（如实登记，非离轨）：基准值 clamp(52,9vh,88) 下补、gap 10、徽章 h-pad 6 等受 ramp/Thickness 硬约束就近落位（§9 观察项）

## 7. 双轨一致性核验

主本 `.scratch/ui-craft2/reports/05-report.md` ↔ 副本 `docs/process/reports/05-page-logs.md`：副本由主本字节级复制产生，写盘后 SHA256 比对一致、无 BOM、纯 LF（核验值见文末核验行）。

## 8. 不动项核验

- nav / nav-action / caption-btn / NavButton.axaml 零触碰（票 03 领地成品）；MainWindow.axaml 零触碰（壳层 119 行守卫不动）。
- `LogLevelIndex` 索引映射语义零改动（all/info/debug/warn/error = 0-4）；`LogEnabled`/`ShowDetailedEvents` 联动逻辑不动。
- `AuditTailService` 解析/过滤/回填/`ShouldInclude`/路径构造全部不动——仅 `MapEventStyle` 显示字符串去前缀（ColorHex 语义不动）。
- `ClearLogsButton` x:Name + Click 路由（MainWindow.axaml.cs 既有 `Click +=` 挂点）不动；`LogsList` x:Name + 滚动调用（`ScrollIntoView` 挂点）不动。
- `HasNoLogs` 收口逻辑（VM Append/Clear 双赋值）不动；`LogEntries` 绑定、`FontFeatures="+tnum"`、列定义 `68,108,*,1.6*`、`CharacterEllipsis` 全部保留。
- 事件徽章 `ColorHex` 绑定（事件语义色驱动）保留——emoji 前缀清除后色块语义不损。
- ComboBox.inline-control(160) / ghost 按钮变体 / caption / mono class 权威不动。
- 零新 NuGet 依赖；Core/Worker/IPC/主题刷 5 份零改动；无新增 emoji/Unicode 状态符号（反向净 -6）。

## 9. 张力与呈报

- **裁量项 a（级别过滤控件形态）**：MangoDisk 过滤=分段胶囊排（md-category-filter h-7.5/active=primary/10），本票取 `ComboBox.inline-control` 承载同级别语义——理由：①同一属性 `LogLevelIndex` 在 ConfigPage 已由 ComboBox 编辑，双控件形态分裂反而不一致；②CI-only 零构建，新建分段控件+样式族无验证手段风险高；③ComboBox 高度落 30-32 控件档与基准 30 吻合。若大脑裁定必须分段胶囊，属全局组件决策（新建控件+样式族）而非单页偏差。
- **裁量项 b（日志行高 44 与页头右操作位判省）**：附录 D.2「结果行 44」原指 MangoDisk 扫描结果行；审计日志流同构（处理结果流）取 `LayoutResultRowHeight` 对齐——若大脑裁定日志流属密排列表（VS Code/Discord 密度法 32-36），回退一行即可，token 消费面不变。另：页头右操作位判省——本页页级操作（清空）置工具条右端更符合基准的「页头=标题+页级主操作」分层（清空=数据操作非页面主操作），如实登记供复核。
- **观察项 c（既有 BOM）**：`MainWindow.axaml.cs` 文件头 UTF-8 BOM 为 HEAD 既有状态（git show 核验 efbbbf 先于本票），未引入未清除——如实登记不越权改编码。
- **过程呈报 d（atomcode 降级）**：atomcode 5h 额度耗尽（reset≈02:17），调研降级为 `gh api` 只读源码直读（附录 D 同款方法）+ 仓内既有调研复核——§3 全表即该路径产物，源码原文行级可复核；不追认未授权的替代路径，按「过程违规单独呈报」纪律登记。
- **待用户目检项 e**：before/after 同机位截图对照登记待用户目检（CI-only 本机零构建零运行，不虚构截图）——规范 §7 V1/V2 机位规程适用；重点目检面=页头/裸工具条观感、行 44 密度、徽章胶囊、空态居中偏上构图、emoji 清除后徽章纯文本观感、级别过滤下拉在场观感。

## 10. 完成定义自证

- [x] 页内离轨值清零（验收 1，§6 rg 复算入报告）
- [x] v2.0 节号对照表（验收 2，§5）
- [x] 报告双轨落盘（验收 3，§7 + 文末核验行）
- [x] 必读清单 7 份全读（账本 D-001~D-008 + A-001~A-005 / spec §0+§4 / issue / handoff 通用纪律 / WORKFLOW §4.2-4.4 / 规范 v2.3 全文 / CONTEXT 词条）
- [x] 调研先行动工前完成并入 §3（atomcode 额度耗尽按例外降级 gh api 只读取证，过程呈报 §9-d）
- [x] 改动清单先列后改（§2 先于代码改动落盘本骨架）
- [x] 空态适用对象先评估再动工（§3/§5：日志流=E1 适用对象，E2 全项落位+E5 空因区分）
- [x] 通用纪律：本机零构建零测试（CI-only）；静态门禁全绿（§4+§6：XAML 标签平衡 3/3、无新增 BOM/CRLF、守卫撞红三档处置留痕、失效即红预演成立）；与用户交互中文
- [x] WORKFLOW §4.2：but 提交 `ui-craft2/05-page-logs` 分支（vqw/yrs/收口件 三提交）；§4.3 共享文件触碰面 §2 已声明；§4.4 本票未执行历史改写/丢弃类操作（纯 commit 叠加栈顶），快照未触发
- [x] 不进入下一票——报告落盘后停住等复核

> **停点**：报告落盘后停住等复核；不自动续票 06。

---

核验行：主本 `.scratch/ui-craft2/reports/05-report.md` ↔ 副本 `docs/process/reports/05-page-logs.md` 字节级复制后 SHA256 比对一致、无 BOM、纯 LF（外部核验，不自嵌哈希）。
