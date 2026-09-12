# Report — 票 03 button-system-cleanup（ui-craft 轮）

**Date**: 2026-09-13
**Status**: 阻断停手呈报 —— 必读清单第 7 项缺失；C1 检查点达成（全仓裸按钮零命中，票面前提失准）；C2/C3 定线完成；施工冻结待规范文档落盘
**Branch**: ui-craft/03-button-system-cleanup（GitButler 虚拟分支，零代码改动）
**来源**: prompts/03 + handoffs/03 + issues/03 + spec.md + decision-ledger.md + WORKFLOW.md §4.2 + atomcode 深度调研（会话 7983c24e，37 条来源）

---

## 0. 开工复述（启动器强制）

- **Blocked by 现状**：票 02（`Styling/AppTheme.axaml` 共享文件，串行纪律）**未开工**；票 01 **并行窗口施工中**——`docs/design/` 目录已于 00:25 建立但为空，`CONTEXT.md` 与 `tests/TEST-CONVENTIONS.md` 为在途改动（`git status --short`：`M CONTEXT.md`、`?? tests/TEST-CONVENTIONS.md`、`?? docs/design/`）。
- **必读清单 12 份**：11 份已逐份读全；**第 7 项 `docs/design/ui-visual-standard.md` 缺失（ENOENT）**。按启动器「缺一份即停下呈报」，本窗口**停止施工动作**，转入勘察 + 呈报（与票 04 窗口同一处置，见 `docs/process/reports/04-pages-visual-alignment.md`）。

## 1. 必读清单对账表（启动器 12 项逐份核验）

| # | 文件 | 状态 | 核验方式 |
|---|---|---|---|
| 1 | `.scratch/ui-craft/handoffs/03-button-system-cleanup.md` | ✅ 读全 | 2677 B |
| 2 | `.scratch/ui-craft/issues/03-button-system-cleanup.md` | ✅ 读全 | 928 B |
| 3 | `.scratch/ui-craft/spec.md` | ✅ 读全 | 8555 B |
| 4 | `.scratch/ui-craft/decision-ledger.md` | ✅ 读全 | 18257 B，D-001~D-009 + A-001~A-010 全覆盖 |
| 5 | `.scratch/architecture-recovery/WORKFLOW.md` | ✅ 读全 | 4052 B，§4.2 版本控制逐句引用 |
| 6 | `CONTEXT.md` | ✅ 读全 | 397 行全文检索，UI 相关词条 8 条逐条对齐（见 §6） |
| 7 | `docs/design/ui-visual-standard.md`（票 01 产物） | ❌ **缺失** | `ls docs/design/` 为空目录；全仓 `grep -rn ui-visual-standard` 仅命中 CONTEXT.md 的指针引用 |
| 8 | `docs/adr/0054-...button-feedback-and-sidebar-nav-layout...` | ✅ 读全 | 80 行 |
| 9 | `docs/adr/0056-architecture-recovery.md` | ✅ 读全 | 51 行 |
| 10 | `docs/adr/0062-...token-discipline-button-feedback...` | ✅ 读全 | 82 行 |
| 11 | `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | ✅ 读全 | 365 行 |
| 12 | `src/PhotoPrivacy.Ui/Views/MainWindow.axaml` | ✅ 读全 | 116 行 |

**开工前 delta 核验**：票 02 在途改动核查 —— `AppTheme.axaml` 的 nav 段（`Button.nav:pointerover` L88、`Button.nav-action:pointerover` L110）**无 `Transitions` setter**，即票 02 未动过该文件；三处独立核验（全文检索 `Transitions` 命中仅 L340/349/357 的全局 Button/ListBoxItem/Border）→ delta 第 1 条满足，但亦证票 02 未开工。

## 2. C1 检查点：裸按钮实态 —— 票面前提偏差（重要发现）

**票面声明**（issues/03 + handoffs/03 + A-001）：`MainWindow.axaml:85 PauseResumeButton`、`:90 OpenConfigDirButton` 两枚裸 Button 吃默认样式，须归队登记变体。

**实态核验（三步）**：

1. **单行 grep 复现假阳性**：按「`<Button` 所在行是否含 `Classes=`」判定，命中 MainWindow.axaml:85 / :90 两处「裸」——与票面描述完全一致。
2. **多行感知普查修正**：剥离 XAML 注释后以 `<Button ... >` 跨行匹配，全仓 15 个 axaml 文件共 **16 枚 Button，16 枚均带 `Classes`**，**裸按钮零命中**。
3. **git blame 实锤**：`git blame -L 84,94` 显示两枚按钮的 `Classes="nav-action"` 与 `HorizontalAlignment="Stretch"` 均出自 **2026-04-27 首版提交 `542c5059`**，从未裸奔；`nav-action` 变体本身由架构恢复票 01（`4e71898`，ADR 0056 票 01 按钮 Size Ladder）在 AppTheme 中定型。

**结论**：票面「两枚裸按钮」出自单行 grep 假阳性（本窗口先复现、后修正）。A-001 的「裸按钮吃默认样式」子项**不成立**；A-001 的「同组按钮宽度不一」子项**成立**（见 §4）。该项在本票中应登记为**已归队（无需代码改动）**，但「零裸 Button 守卫」仍应落一条——防的是「将来新增按钮遗漏 Classes」，而非修正现存缺陷（D-006 注释须写明此区别）。

## 3. 按钮变体普查（C3 对账表素材 · 全仓 16 枚）

| # | 文件:行 | x:Name | Classes | 行内尺寸 |
|---|---|---|---|---|
| 1 | Views/Controls/NavButton.axaml:10 | PART_Button | nav | 无 |
| 2 | Views/MainWindow.axaml:28 | — | caption-btn | 无 |
| 3 | Views/MainWindow.axaml:31 | — | caption-btn | 无 |
| 4 | Views/MainWindow.axaml:34 | — | caption-btn danger | 无 |
| 5 | Views/MainWindow.axaml:85 | PauseResumeButton | nav-action | 无 |
| 6 | Views/MainWindow.axaml:90 | OpenConfigDirButton | nav-action | 无 |
| 7 | Views/Pages/ConfigPage.axaml:205 | AddExcludedDirectoryButton | icon | 无（icon glyph 16x16 属图标尺寸非按钮尺寸） |
| 8 | Views/Pages/ConfigPage.axaml:206 | RemoveExcludedDirectoryButton | icon | 无 |
| 9 | Views/Pages/LogsPage.axaml:15 | ClearLogsButton | ghost | 无 |
| 10 | Views/Pages/RulesPage.axaml:29 | SaveRulesButton | ghost | 无 |
| 11 | Views/Pages/RulesPage.axaml:30 | ResetRulesButton | ghost | 无 |
| 12 | Views/Pages/ServiceManagerPage.axaml:30 | InstallServiceButton | primary | 无 |
| 13 | Views/Pages/ServiceManagerPage.axaml:43 | StartServiceButton | ghost | 无 |
| 14 | Views/Pages/ServiceManagerPage.axaml:44 | StopServiceButton | ghost | 无 |
| 15 | Views/Pages/ServiceManagerPage.axaml:45 | RefreshServiceStatusButton | ghost | 无 |
| 16 | Views/Pages/ServiceManagerPage.axaml:58 | UninstallServiceButton | danger | 无 |

**变体计数**：ghost 6 / caption-btn 2（其中 1 枚叠加 danger）/ nav-action 2 / icon 2 / nav 1 / primary 1 / danger 1。七变体（primary/ghost/danger/icon/nav/nav-action/caption-btn）全部有实例消费，无孤儿变体、无未登记变体。**行内尺寸覆盖：0 处**（16 枚按钮均无 Width/MinWidth/MaxWidth/Height/Padding 覆盖，ADR 0056 票 01 的清零成果保持）。

## 4. 按钮组清单与等宽现状（C2 素材）

| 组 | 位置 | 成员 | 现状宽度行为 | 等宽 |
|---|---|---|---|---|
| G1 侧栏导航组 | MainWindow.axaml:75-94 | 3×NavButton(config/log/rules) + 服务管理器 + PauseResume + OpenConfigDir | `Button.nav`/`nav-action` 均设 HorizontalContentAlignment=Stretch，视图 HorizontalAlignment=Stretch，侧栏 Border 定宽 200 | ✅ 已等宽 |
| G2 服务操作组 | ServiceManagerPage.axaml:42-46 | Start / Stop / Refresh（ghost×3） | `StackPanel Orientation=Horizontal`，宽度=文案长度 | ❌ 不等宽 |
| G3 规则操作组 | RulesPage.axaml:27-32 | Save / Reset（ghost×2）+ 搜索框 | 同上，宽度=文案长度 | ❌ 不等宽 |
| G4 排除目录增减 | ConfigPage.axaml:204-206 | Add / Remove（icon×2） | `Button.icon` 32×32 固定 | ✅ 天然等宽 |
| G5 单枚行内动作 | LogsPage.axaml:15；ServiceManagerPage:30/58 | ClearLogs / Install / Uninstall | 单枚，无组内对比；跨行之间宽度不一致（Apple 骨架下是否统一待规范定线） | ⚠️ 待定线 |
| G6 标题栏组 | MainWindow.axaml:27-37 | 3×caption-btn | `caption-btn` 定尺（Padding 16,6，guard `Caption_Btn_Padding_Must_Be_16_6` 锁定） | ✅ 等宽 |

**施工落点（待规范定线后执行）**：G2、G3 为唯一两处实质不等宽组；G5 视规范「行内动作统一 MinWidth」条款而定。实现方式候选见 §5.4，均不引入视图行内尺寸。

## 5. atomcode 深度调研（通用调研要求 #1 · 串行护栏内单发，会话 7983c24e）

### 5.1 等宽 / 内容宽度 / 混合：三策略适用条件

| 策略 | 语义 | 官方背书 | 典型场景 | 边界 |
|---|---|---|---|---|
| 等宽（最长标签定宽） | 这些选项地位相等 | Carbon、GNOME、USWDS、Apple（同级时）【官方明文】 | 对话框 OK/Cancel、2–3 个同级操作、分栏工具条 | ghost 按钮（Carbon 明文不等宽）、带 primary 的强调场景 |
| 内容宽度（hug-content） | 每个按钮自带边界 | MD3 标准组默认、Fluent 2 未规定即默认、GNOME header bar【官方明文】 | 页面独立操作、工具条、扁平/ghost 按钮 | 相邻同组时违背 GNOME「相邻同宽」 |
| 混合（fluid 主 + 固定次） | 用宽度强调层级 | Carbon fluid 50% 规则、MD3「change width」、Apple watchOS 全宽主按钮【官方明文】 | 表单提交、移动端全宽 | Carbon 与 MD3 在「同组可否不同宽」上直接冲突 |

**对本项目的收敛**：G2/G3 属「2–3 个同级操作」→ 走**等宽（最长标签定宽）**；这与 D-005 三锚中的 Apple 系统设置骨架一致（行内右置的同级动作组等宽），亦满足 A-001 用户原话「同组按钮长短不一」。

### 5.2 最小点击目标尺寸（对 CONTEXT 现存口径的修正候选）

| 规范 | 数值 | 性质 | 例外 |
|---|---|---|---|
| WCAG 2.2 SC 2.5.8 | 24×24 CSS px | AA 合规底线 | 间距/等效/句内/UA 默认/必需，共 5 类 |
| WCAG 2.2 SC 2.5.5 | 44×44 CSS px | AAA | — |
| Apple HIG | 44×44 pt（macOS 默认 28×28、最小 20×20） | 平台规范（桌面指针精度降级） | 平台分档 |
| Material 3 | 48×48 dp（间距 ≥8dp） | 平台规范 should 级 | 40dp 密度档 |
| Microsoft Fluent | 40×40 epx（7.5mm）；宽 ≥120epx 可矮至 32epx | 平台规范 | 触屏/鼠标分档 |

**修正候选（题设纠偏）**：Fluent 的 **32px 是按钮默认高度而非最小目标尺寸**（Fluent UI 源码 useButtonStyles.styles.ts：medium minWidth 96、small 64、icon-only 32×32；PR #26522：默认高度 32/small 24/large 40）。现有 ADR 0054/0062 引用「Fluent 32px」时指按钮高度口径，无误用，但规范文档落地时建议明确区分「目标尺寸」与「控件高度」两个量纲。

**本项目定位**：Button 变体高度 32（primary/ghost/danger/icon）/ 40（nav/nav-action），宽度受 Padding 12,6 与文案决定 → 高度 32 已超 WCAG AA 24 底线，nav 40 达 Fluent 桌面建议值；无需为目标尺寸改动现有 Size Ladder。

### 5.3 XAML/Avalonia 等宽实现：五法适用边界（★=官方机制）

| 做法 | 官方支持 | 适用 | 坑 |
|---|---|---|---|
| A. SharedSizeGroup + IsSharedSizeScope | ★ WPF How-to: Share Sizing Properties Between Grids；★ Avalonia Grid how-to §SharedSizeGroup + API SetIsSharedSizeScope | **首选**：最长标签定宽；跨 Grid 同步；动态模板可用 | ① 同名组在 scope 内全部同步（组名=作用域）；② IsSharedSizeScope 只在共同祖先设一次；③ 列需 Auto，与 star 混用失效；④ Avalonia 早期 shrink 缺陷（issue #15612，修复 PR #21837）；⑤ scope 依赖逻辑树非视觉树；⑥ 需配 MaxWidth/对齐防过度扩张 |
| B. UniformGrid | WPF/Avalonia 内置 | 静态固定数量、要绝对等分 | ① 图标/ghost 也被强制拉宽；② 无权重；③ 无「最长标签定宽」中间态；④ 动态数量需重建 |
| C. ColumnDefinitions="*,*,*"（star 等分） | ★ Avalonia Grid how-to §Star sizing | 组内等分容器宽度（Carbon fluid） | ① 窗口拉伸时无限变宽，需 MaxWidth/固定对齐；② 是「容器均分」非「最长标签定宽」 |
| D. 样式类统一 MinWidth | 无官方条款，社区惯例 | 兜底：保底宽度 | 与「零行内尺寸」兼容（值在 AppTheme），但需定值依据，否则又成魔法数 |
| E. 绑定组内最长 ActualWidth | 社区惯例 | 动态文案 | 需 master 元素 + 绑定回环风险；可维护性差 |

**推荐（待规范采纳）**：G2/G3 用 **A（SharedSizeGroup）** 或 **D（AppTheme 内新增 `Button.group` 类统一 MinWidth）**。二者均在样式层定值、视图零行内尺寸，符合 ADR 0056 票 01「AppTheme 是唯一权威」与 ADR 0061「Views 禁止内联宽度」。D 更简单但需规范给出 MinWidth 定值依据（建议取组内最长文案宽度 + Padding 的量化值，或对齐既有 `TextBox.inline-input` Width=280 的同等手法）；A 语义更准（真正「最长标签定宽」）但需处理 Avalonia shrink 与逻辑树 scope 两个坑。

### 5.4 来源清单（37 条，节选 15 条；完整清单随 atomcode 会话 7983c24e 留存）

1. ★ W3C — Understanding SC 2.5.8 Target Size (Minimum) — https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html
2. ★ W3C — WCAG 2.2 TR #target-size-minimum — https://www.w3.org/TR/WCAG22/#target-size-minimum
3. ★ Microsoft — Guidelines for touch targets — https://learn.microsoft.com/en-us/windows/apps/design/input/guidelines-for-touch-targets
4. ★ Microsoft — touch-interactions.md（40×40 epx，宽 ≥120epx 可矮至 32epx）— https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/develop/input/touch-interactions.md
5. ★ Microsoft — Guidelines for app settings（SettingsCard 行内 action 控件）— https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings
6. ★ Google — Touch target size（48dp）— https://support.google.com/accessibility/android/answer/7101858
7. ★ Android — minimumInteractiveComponentSize — https://developer.android.com/reference/kotlin/androidx/compose/material3/minimumInteractiveComponentSize.modifier
8. ★ USWDS — Button group（组内不混尺寸）— https://designsystem.digital.gov/components/button-group/
9. ★ Ant Design — v5→v6 迁移（Button.Group → Space.Compact）— https://ant.design/docs/react/migration-v6
10. ★ Fluent UI 源码 — useButtonStyles.styles.ts（minWidth 96/64/32）— https://github.com/microsoft/fluentui/blob/master/packages/react-components/react-button/library/src/components/Button/useButtonStyles.styles.ts
11. ★ Fluent UI PR #26522（按钮默认高度 32/24/40）— https://github.com/microsoft/fluentui/pull/26522
12. ★ WPF 官方 — How to: Share Sizing Properties Between Grids — https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-share-sizing-properties-between-grids
13. ★ Avalonia 官方 — Grid how-to §SharedSizeGroup — https://docs.avaloniaui.net/docs/how-to/grid-how-to
14. ★ Avalonia — PR #21837（allow shared size groups to shrink）— https://github.com/AvaloniaUI/Avalonia/pull/21837
15. ★ Deque — Touch Target Spacing vs Size（24dp AA 与 48dp 建议并存）— https://docs.dequelabs.com/devtools-mobile/2025.7.2/en/android-touch-target-spacing/

### 5.5 与 D-005 三锚对照（通用调研要求 #3 · 禁空泛审美词）

| 锚 | 本票落点 | 可核验设计点 |
|---|---|---|
| Wasabi 气场（克制/留白/暗基调） | G2/G3 等宽后组宽收敛为「最长标签 + Padding」的单一值，消除三档参差宽度带来的杂讯 | 组内按钮数 3 与 2，宽度档位由 3 档降为 1 档 |
| Apple 系统设置骨架（分组卡片/行内左右分置） | G2/G3 位于 settings-row 的 DockPanel 右置动作区，等宽后行内右边界整齐 | 与 ServiceManagerPage:30/58 单行按钮的右边界对齐关系 |
| VS Code / Discord 密度法 | 不因等宽而增高行高；保持 Height=32 与 SpaceSm 间距 | 组内 Spacing 沿用 `SpaceSm` DynamicResource，未新增局部值 |

## 6. ADR / CONTEXT 对齐与冲突声明（通用调研要求 #2）

| 来源 | 与本票关系 | 判定 |
|---|---|---|
| ADR 0054（去 scale + 侧栏等宽 Grid Auto,*） | G1 等宽现状即本 ADR 产物（StackPanel→Grid Auto,*） | ✅ 对齐，本票不改 |
| ADR 0056 票 01（Button Size Ladder，AppTheme 唯一权威，视图零行内尺寸） | 本票实现等宽须走样式层，不得在视图写尺寸 | ✅ 对齐（硬约束） |
| ADR 0061（Views 禁止内联宽度，`TextBox.inline-input` Width=280 单一权威） | 等宽定值若走「样式类 MinWidth」须同此手法 | ✅ 对齐 |
| ADR 0062（七变体×五态矩阵、纯色 150ms SineEaseOut、disabled 语义） | 本票不动反馈态——属票 02 领地（AppTheme nav 段） | ✅ 对齐，主动避让（WORKFLOW §4.3） |
| ADR 0050 A2 / 0051 A1 / 0052 A1 / 0055 A1（变体基线、Space token、nav 规格、Nav Group Split） | 变体登记与间距节奏的依据 | ✅ 对齐 |
| CONTEXT `Button Size Ladder`（L378） | 尺寸单一权威 + 五个 source-lint guard | ✅ 一致（本票再加一条零裸 Button guard，不冲突） |
| CONTEXT `Sidebar Nav Item (40px)`（L297） | 已由票 01 修正 44→40（A-006） | ✅ 一致 |
| CONTEXT `Nav Group Split`（L362） | 底组 utility 划分，G1 现状符合 | ✅ 一致 |
| CONTEXT `Space Token Spend Cleanup`（L281） | 本票 Margin 清理依据 | ✅ 一致，但定值待规范（见 §7） |
| CONTEXT `Source-lint Test`（L248） | Avoid 含 Avalonia.Headless | ⚠️ 张力：D-006 修订方向为「必要时小步引入」，本票只需静态文本断言，**不触发**该张力，交由后续票处置 |
| CONTEXT `UI Visual Standard`（L398）/ `Visual Baseline`（L402） | 硬引用 `docs/design/ui-visual-standard.md` 与「见 ADR 0065」，**两者均未落盘** | ❌ **词条悬空**——票 01 在途改动中，本窗口**不修订 CONTEXT.md**（避免与在途 `no` 变更冲突） |

## 7. C4：本票触碰文件的 Margin 残留（A-008 部分 · 定线，施工冻结）

| 文件 | 行 | 现值 | 类型 |
|---|---|---|---|
| MainWindow.axaml | 27 | Margin="0,0,4,0" | Thickness |
| MainWindow.axaml | 61 | Margin="10,10,10,0" | Thickness |
| MainWindow.axaml | 75 | Margin="8,16,8,0" + Spacing="4" | Thickness + Double |
| MainWindow.axaml | 82 | Margin="8,0,8,12" | Thickness |
| MainWindow.axaml | 83 | Margin="0,0,0,4" | Thickness |

**与票 04 普查交叉核对**：票 04 报告 §3 给出「6 文件 22 处字面量 / 11 处离轨（值 20×7、10×3、6×1）」，本窗口独立复算一致（22 处）。**勘误**：A-008 原始登记「14 处」为旧口径，实际 22 处（票 04 已同口径勘误）。

**技术张力（引用并确认票 04 结论）**：Margin 属 Thickness，受 CONTEXT `Padding Literal Quantization` 词条约束——**不可 DynamicResource**（跳过 ThicknessTypeConverter → InvalidCastException），故「token 化」只能是**量化到 ramp 字面量**；Spacing 属 Double，**可**改 DynamicResource。离轨值（如 20）映射到 ramp 的**上档 24 还是下档 16** 属页面级节奏决策 → 须由 `ui-visual-standard.md` 间距节奏节定值。**故本票 Margin 清理与票 04 同步冻结。**

## 8. 阻断判定与处置选项

**判定**：票 03 存在**双层阻断**

1. **名义层**：Blocked by 票 02（`AppTheme.axaml` 共享文件串行纪律，WORKFLOW §4.3）——票 02 未开工。
2. **实质层**：必读清单第 7 项 `docs/design/ui-visual-standard.md`（票 01 主交付物）未落盘——`docs/design/` 为空目录。本票的「按钮组宽度策略按规范文档落地」与「变体对账表入规范附录」两项**直接依赖该文档的 §1 按钮组宽度策略节与附录**，缺之则无法定值、无法入附录。

**与票 04 的对称性**：票 04 窗口已因同一缺失停下呈报（`docs/process/reports/04-pages-visual-alignment.md`），本窗口处置一致——**零代码改动、冻结施工、交付勘察与调研资产**。

**三档处置选项（待大脑/用户裁定）**

| 选项 | 内容 | 代价 |
|---|---|---|
| **A. 维持串行，本票转 blocked** | 等票 01 落盘规范 §1 + 附录 → 票 02 落 AppTheme nav 段 → 票 03 施工 | 零冲突、符合 WORKFLOW §4.3；本票在规范落盘后可一次性闭环（落点已定线，预计改动 2 个页文件 + 1 条 guard） |
| **B. 授权本窗口代建规范 §1 与附录** | 由本窗口写 `ui-visual-standard.md` 的按钮组宽度策略节 + 变体对账附录，再施工 | 侵入票 01 领地（同文件同区），需票 01 窗口确认不并行写该文件；与 README 波次表冲突 |
| **C. 授权改写票面（按实态收缩）** | 承认「裸按钮」前提失准，票 03 收缩为「G2/G3 等宽 + 零裸 Button guard + 变体对账表先落 .scratch」，对账表待规范落盘后由票 01/04 搬运入附录 | 需修订 issues/03 与 decision-ledger A-001 表述；仍需规范给出等宽定值依据（否则凭空定值） |

**本窗口建议**：选项 **A**。理由——(1) 落点已全部定线，等待成本低于并行冲突成本；(2) §3/§4/§5 的普查与调研可直接作为票 01「按钮组宽度策略」节的输入素材，避免重复勘察；(3) 票 02 未开工，即便跳过文档阻断也仍卡在共享文件纪律上。

## 9. 声明 → 证据 → 结论 对照表

| # | 声明 | 证据 | 结论 |
|---|---|---|---|
| 1 | 必读清单第 7 项缺失 | `ls docs/design/` 空目录；`grep -rn ui-visual-standard` 全仓仅 CONTEXT.md 指针命中 | 启动器「缺一份即停下呈报」成立 → 停止施工 |
| 2 | 票 02 未开工 | `AppTheme.axaml` nav 段无 Transitions setter（L88/L110），全文 Transitions 仅 L340/349/357 | 名义阻断成立 |
| 3 | 全仓零裸按钮 | 多行感知普查 16/16 带 Classes（单行 grep 假阳性已复现并修正） | C1 ✅ 达成（但性质为「无需改动」而非「已修复」） |
| 4 | 两枚点名按钮从未裸奔 | `git blame -L 84,94` → `542c5059` 2026-04-27 | 票面 A-001「裸按钮吃默认样式」子项**不成立**，建议随票修订 |
| 5 | 同组按钮宽度不一仍成立 | ServiceManagerPage:42-46、RulesPage:27-32 内容宽度 | A-001 该子项成立，落点 G2/G3 已定线 |
| 6 | 无行内尺寸覆盖按钮 | 16 枚按钮逐枚核验 Width/MinWidth/MaxWidth/Height/Padding | ADR 0056 票 01 清零成果保持 |
| 7 | 等宽策略有工业依据 | atomcode 37 源（Carbon/GNOME/USWDS/Apple 官方明文支持同级等宽） | G2/G3 走「最长标签定宽」等宽，非审美偏好 |
| 8 | 目标尺寸无需改动 | 高度 32/40 均超 WCAG AA 24；Fluent 40epx 桌面建议值 | Size Ladder 不动 |
| 9 | Margin 清理须冻结 | Thickness 不可 DynamicResource（Padding Literal Quantization 词条）；离轨映射属页面级节奏决策 | C4 定线完成，施工冻结待规范间距节奏节 |
| 10 | 与票 04 处置一致 | 票 04 报告同因缺失停下呈报 | 本窗口零代码改动 |

---

## 附：本窗口产物与纪律声明

- **产物**：本报告（`.scratch/ui-craft/reports/03-button-system-cleanup.md` 主本 + `docs/process/reports/03-button-system-cleanup.md` 双轨副本，逐字一致）；atomcode 调研会话 `7983c24e-134d-401a-95a5-655c282e809a`（可 `--resume` 续跑）。
- **零代码改动**：未修改任何 `.axaml`/`.cs`/`CONTEXT.md`/`TEST-CONVENTIONS.md`——后两者为票 01 在途领地，主动避让（WORKFLOW §4.3）。
- **版本控制**：WORKFLOW §4.2 —— GitButler 虚拟分支 `ui-craft/03-button-system-cleanup`，仅圈入本窗口产物，不 push、不 PR。
- **CI**：CI-only 政策，本机零构建/测试；本票无代码改动，无 CI 面影响。
