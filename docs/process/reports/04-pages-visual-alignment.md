# Report — 票 04 pages-visual-alignment（ui-craft 轮）

**Date**: 2026-09-13
**Status**: 阻断停手呈报（必读清单第 7 项缺失；A-007 核验达成、处置冻结；Margin 普查达成、清零冻结；探活/截图未执行如实登记）
**Branch**: ui-craft/04-pages-visual-alignment（GitButler 虚拟分支，零代码改动）
**来源**: prompts/04 + handoffs/04 + issues/04 + spec.md + decision-ledger.md + WORKFLOW.md

---

## 0. 开工复述（启动器强制）

- **Blocked by 现状**：票 03（MainWindow.axaml 共享文件 + 体系先行）**未开工**。波次表（README 权威）：01 → 02 → 03 → 04 全串行。实态核验：`.scratch/ui-craft/reports/` 仅 `.gitkeep`，票 01/02/03 报告零落盘；状态表 01 = `ready-for-agent`（待派发），02/03/04 = `blocked`。
- **必读清单 14 份读全确认**：13 份已逐份读全；**第 7 项 `docs/design/ui-visual-standard.md` 缺失（ENOENT）**，且 `docs/design/` 目录整体不存在。按启动器「缺一份即停下呈报」，本窗口停止施工动作，转入勘察 + 呈报。

## 1. 阻断裁定（核心结论）

| 项 | 实态 | 证据 | 裁定 |
|---|---|---|---|
| 票 01 产物 | `docs/design/ui-visual-standard.md` 不存在，`docs/design/` 目录不存在 | `find . -iname "*ui-visual-standard*"` 零命中；`ls docs/design/` ENOENT；`docs/` 下无 design 子目录 | 票 04 的对照基准物理缺失 |
| 票 01 状态 | **并行窗口施工中（00:24 实锤）**，主交付物未落盘 | `but diff`：`no M CONTEXT.md`（UI Visual Standard / Visual Baseline / Sidebar Nav Item 44→40 三词条）+ `rr A tests/TEST-CONVENTIONS.md`（D-006 两条规则）；`stat CONTEXT.md` mtime = 2026-09-13 00:24:29 | 波次 1 **进行中**，非未派发 |
| 票 01 主交付物 | `docs/design/ui-visual-standard.md` 与 ADR 0065 **均尚未落盘** | `ls docs/design/` ENOENT；`ls docs/adr/` 最新仅 0063；CONTEXT.md 已先行引用「见 docs/design/ui-visual-standard.md §2」 | 规范 §2 = nav 反馈三态（节号语义已可推断），全文仍缺 |
| 对票 04 影响 | C1（四页 diff 对照**规范节号**）无法成立 | handoffs/04 完成定义第 1 条；issues/04 勾选项 1 | **❌ 阻断** |
| 代码在途冲突 | `git status --short` 空；`but status` = `zz (no changes)`；`git diff -- src/.../MainWindow.axaml` 空 | 三处独立核验 | ✅ 开工前无在途改动，delta 第 1 条满足 |

**结论（2026-09-13 00:24 修订）**：票 04 存在**双层阻断**——(1) 名义 blocked by 票 03；(2) 实质 blocked by 票 01 的规范基准。初判「票 01 从未派发」经复核**不准确**：票 01 正由并行窗口施工（CONTEXT.md 与 TEST-CONVENTIONS.md 在途改动实锤），但主交付物 `ui-visual-standard.md` 与 ADR 0065 尚未落盘。故票 04 仍受阻，但性质为**等待依赖落盘**，而非**依赖未启动**。

## 2. A-007 侧栏 Border Width=200 实态核验（检查点 C2）

### 2.1 证据链（源码实物行号）

| 锚点 | 内容 | 语义 |
|---|---|---|
| `MainWindow.axaml:40` | `<Grid Grid.Row="1" ColumnDefinitions="200,4,*" x:Name="MainRootGrid">` | 侧栏列 = Column[0]，可被改写 |
| `MainWindow.axaml:41-42` | `<Border Grid.Column="0" Width="200" ...>` | **内部面板硬钉 200** |
| `MainWindow.axaml.cs:798-805` | `RestoreSidebarWidth(double width)` → `grid.ColumnDefinitions[0].Width = new GridLength(clamped)` | 启动时按 `ui.sidebar_width` 改列宽 |
| `MainWindow.axaml.cs:815-822` | `OnSidebarSplitterDragCompleted` → `Math.Clamp(grid.ColumnDefinitions[0].ActualWidth, 170, 400)` → `vm.SidebarWidth = width` | 拖宽改列宽 + 防抖持久化 |

### 2.2 判定

- **列宽侧**：可在 170–400 间变化（代码 Clamp，非 XAML MinWidth/MaxWidth）。
- **面板侧**：`Width="200"` 为硬钉定值，不随列宽变化。
- **后果**：列宽 > 200 → 面板右侧留空，露出 `SemiColorBackground0`，侧栏视觉宽度与内容区断裂；列宽 < 200 → 面板溢出被裁。
- **A-007 成立**：确为缺陷，**不是**「内部定宽 = 设计意图」——理由：同一 Grid 的列宽既已开放可调（ADR 0052 A5 显式交付「可拖拽侧栏」），内部定宽与之直接冲突，且无任何注释或 ADR 声明此定宽为设计意图。

### 2.3 修复形态（已定线，未执行）

删除 `MainWindow.axaml:42` 的 `Width="200"`。Border 位于 Grid cell 内，默认 `HorizontalAlignment=Stretch`，移除后自动跟随 Column[0] 实际宽度。改动量 1 行，无副作用（内部 `DockPanel` 子内容按 Stretch 重排）。

### 2.4 处置冻结理由

A-007 显式约束（decision-ledger.md:201）：**「修复与否须与 ui-visual-standard.md 侧栏节口径一致」**。该规范节不存在 → 本窗口不执行修复，仅定线。

## 3. Margin/Spacing 离轨普查（检查点 C3）

### 3.1 token ramp（DesignTokens.axaml:4-11，实物）

`SpaceXxs=2 / SpaceXs=4 / SpaceSm=8 / SpaceMd=12 / SpaceLg=16 / SpaceXl=24 / SpaceXxl=32 / SpaceXxxl=48`

### 3.2 普查结果（6 axaml 全量，脚本逐行提取）

**合计 22 处间距字面量，其中 11 处离轨**（`0` 为合法零值，不计离轨）：

| 文件 | 行 | 属性 | 现值 | 离轨值 |
|---|---|---|---|---|
| MainWindow.axaml | 61 | Margin | `10,10,10,0` | 10 |
| MainWindow.axaml | 62 | Padding | `12,10` | 10 |
| ConfigPage.axaml | 4 | Padding | `24,20,24,16` | 20 |
| ConfigPage.axaml | 7 | Margin | `24,16,24,20` | 20 |
| ConfigPage.axaml | 212 | Spacing | `10` | 10 |
| LogsPage.axaml | 4 | Padding | `24,20,24,16` | 20 |
| LogsPage.axaml | 8 | Margin | `24,12,24,20` | 20 |
| LogsPage.axaml | 34 | Padding | `6,2` | 6 |
| RulesPage.axaml | 4 | Padding | `24,20,24,16` | 20 |
| ServiceManagerPage.axaml | 4 | Padding | `24,20,24,16` | 20 |
| ServiceManagerPage.axaml | 8 | Margin | `24,16,24,20` | 20 |

**分布**：值 `20` × 7 处、值 `10` × 3 处、值 `6` × 1 处。`Views/Controls/NavButton.axaml` 全量在轨（0 处离轨）。

### 3.3 与账本 A-008 的数字勘误

A-008 登记「6 个 axaml 文件共 **14 处** Margin/Spacing 字面量残留」。实态：**22 处**间距字面量，其中**真正离轨 11 处**。差异来源：A-008 计数口径未区分「在轨字面量」（如 `0,0,4,0`、`8,16,8,0`，已符合 ramp）与「离轨字面量」。**提请票 01 建规范时以本报告 11 处为准，并勘误账本。**

### 3.4 技术张力（须规范或 ADR 裁定，窗口不代裁）

CONTEXT.md「Padding Literal Quantization」词条：XAML **Thickness 必须用字面量字符串**触发 `ThicknessTypeConverter`；`DynamicResource` Double 赋 Thickness 会跳过 TypeConverter → `InvalidCastException` 布局测量期崩溃。

推论（本窗口勘察结论）：

- **Margin / Padding（Thickness）**：**不可**改 `{DynamicResource SpaceXxx}`。「token 化」的可执行语义 = **量化到 ramp 字面量**（20→24 或 16；10→8 或 12；6→4 或 8）。
- **Spacing（Double）**：**可**改 `{DynamicResource SpaceXxx}`。既有代码已广泛采用（ConfigPage L14/L18/L29/L45/L55/L67 等 `Spacing="{DynamicResource SpaceXxs}"`），路径验证成立。

因此 A-008「清至 SpaceXxx token」对 Thickness 与 Double 是**两种不同动作**，且离轨值映射到 ramp 的**上档还是下档**属页面级节奏决策（如 `20` → 取 `24` 还是 `16`）。此即 A-003「页面级规范缺位」的具体表现，须由 `ui-visual-standard.md` 间距节奏节定值。

### 3.5 处置冻结

离轨值归档方向无权威口径 → **不清零**，仅交付计数证据（§3.2 表）。

## 4. 四页结构清点（C1 前置勘察，未施工）

| 页 | 页头节奏 | 主体骨架 | Apple 设置骨架（D-005 锚 2）对照 | 空态 |
|---|---|---|---|---|
| ConfigPage | `Padding="24,20,24,16"` + `Classes="title"` | `settings-card` × 4 + `settings-row` + `row-divider`，行内 `DockPanel LastChildFill=False` 左标签右控件 | **已符合**（分组卡片 + 左标签/描述 + 右控件） | 无（表单页，不需） |
| ServiceManagerPage | 同左（逐字重复） | `settings-card` × 2 + `settings-row` | **已符合** | 无（状态页，不需） |
| RulesPage | 同左 + `StackPanel Spacing="8"` 副标题 | `settings-card`（expert 卡）+ `StackPanel Spacing="16"` + `DataGrid` | **异质**（数据网格为主，无 settings-row 骨架） | ✅ `rules.empty` / `HasNoVisibleRules`（RulesPage:59-63） |
| LogsPage | 同左（无副标题） | `DockPanel` + `ListBox` + 空态 TextBlock | **异质**（日志流，无卡片骨架） | ✅ `log.empty` / `HasNoLogs`（LogsPage:58-62） |

**发现（勘察级，非施工）**：

1. **页头四份逐字重复**：四页 `Padding="24,20,24,16"` + `BorderThickness="0,0,0,1"` + `Classes="title"` 完全一致，应抽为共享 style class。属「页面级节奏」节裁定范围。
2. **空态已在位**：Logs / Rules 两处空态由票 26（ADR 0062 D5）落地，本票**无新增空态动作**。
3. **ConfigPage:212 空壳**：`StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10"` 内仅剩单个 `SaveStatus` TextBlock（ADR 0037 移除「应用配置」按钮后的遗留），横向布局与 `Spacing="10"` 已无第二个对象。可收敛，属 A-008 同族处置。
4. **RulesPage:27-32 搜索行**：`Spacing="8"` + `RulesSaveStatus Margin="8,0,0,0" Opacity="0.6"`，按钮宽度按文案自适应（`MinWidth="260"` 仅给 TextBox）——**同组按钮不等宽**属 A-001（票 03 领地），本票不触碰。

## 5. 声明 → 证据 → 结论对照表

| # | 检查点（启动器 C1-C4） | 声明 | 证据 | 结论 |
|---|---|---|---|---|
| C1 | 四页修正 diff 与规范节号对照表 | 无修正，故无对照表 | §1 规范缺失；§4 仅结构清点 | ❌ **阻断**（依赖票 01） |
| C2 | A-007 处置结论 | 核验完成：缺陷成立，非设计意图；修复形态已定线（删 1 行） | §2.1-2.3 源码行号证据链 | ⚠️ **核验 ✅ / 处置 ⛔ 冻结**（待规范侧栏节口径） |
| C3 | Margin 清零计数证据 | 6 文件 22 处字面量 / 11 处离轨；Thickness 不可 DynamicResource | §3.2 逐处表 + §3.4 张力 | ⚠️ **计数 ✅ / 清零 ⛔ 冻结**（离轨值归档方向无权威） |
| C4 | 探活回填 + 截图落盘 | 28 项全「未探活」；`docs/design/screenshots/` 不存在；本窗口 CI-only 不自产 | report-32 §3/§6；`docs/design/` ENOENT | ❌ **未执行，如实登记** |

## 6. 完成定义逐条裁定（handoffs/04）

| # | 完成定义 | 裁定 | 说明 |
|---|---|---|---|
| 1 | 四页修正 diff 逐项对照规范节号 | ❌ | 规范不存在，无节号可对 |
| 2 | A-007 处置结论写入报告与规范附录 | ⚠️ | 报告侧 ✅（§2）；**规范附录不存在**，无处写入 |
| 3 | Margin 清零（守卫或报告计数证据） | ⚠️ | 报告计数证据 ✅（§3.2）；清零动作冻结（§3.5） |
| 4 | 探活回填与基线截图到位（未执行项如实标注） | ❌ | 如实标注：28/28 未探活，截图 0 张 |
| 5 | CI 云端绿 | ➖ | 本窗口零代码改动，无验证分支可推；无可判红绿对象 |
| 6 | 报告写入 reports/04-pages-visual-alignment.md | ✅ | 本文件（双轨，见 §8） |

## 7. 处置选项（交大脑 / 用户裁定，窗口不代裁）

### (a) 按波次表先派票 01 — **推荐**

派发票 01 建立 `docs/design/ui-visual-standard.md`（含侧栏节、间距节奏节、页头节奏节），随后 02 → 03 → 04 顺序解锁。**本报告的 §2 A-007 定线、§3 离轨普查、§4 结构清点可直接作为票 01 的输入素材**，避免重复勘察。票 04 维持 blocked。

### (b) 授权本窗口兼做票 01

需用户**明确批准**跨票施工。注意票 01 含 handoff 强制项：**atomcode 深度调研**（按钮组宽度策略 / nav 反馈 / 表单行骨架的工业界成熟实现）、新 ADR、CONTEXT.md 词条、测试约定区两条规则（D-006）。工作量与票 04 不同量级，且违反 WORKFLOW §1（一票一窗）。

### (c) 大脑直接裁定本票三项口径，本窗口立即施工

由大脑/用户直接定值以下三项，本窗口即可在本分支完成票 04 全部代码改动：

1. **A-007**：确认删 `MainWindow.axaml:42` `Width="200"`（跟随列宽）；
2. **Margin 离轨归档**：`20` → `24` 还是 `16`；`10` → `8` 还是 `12`；`6` → `4` 还是 `8`；
3. **页头节奏**：四页 `Padding="24,20,24,16"` 统一取何值、是否抽共享 class。

> 选项 (c) 的实质是把票 01 的「间距节奏节」以裁定形式前置。**副作用**：规范文档仍未建立，票 02/03 无基准；且后续规范若与本次裁定冲突需返工。

## 8. 双轨与版本控制

- 本文件主本：`.scratch/ui-craft/reports/04-pages-visual-alignment.md`
- 受控副本（WORKFLOW §4.4 轨 1）：`docs/process/reports/04-pages-visual-alignment.md`，与主本逐字一致（字节级核验）。
- 分支：`ui-craft/04-pages-visual-alignment`（GitButler 虚拟分支，§4.2）；commit 信息中文带票号；**不 push 不 PR**（§4.2）。
- 快照（§4.4 轨 2）：本窗口**未执行任何 GitButler 历史改写/丢弃类操作**（无 move/undo/squash/discard/uncommit/branch delete/pull），故轨 2 快照未触发。
- 代码：**零改动**（无 axaml / cs / 测试改动）。
## 9. 风险登记

1. **运行侧证据持续缺位**：report-32 的 28 项探活自 2026-09-07 起全部「未探活」，本轮 ui-craft 全部四票的验收条件（D-007：同机位 before/after 对照）均依赖此项。当前状态 = 零基线、零回填。**这是本轮最大的闭环风险，且非任一执行窗口可自行解除。**
2. **产物新鲜度**：本机产物时间戳停留 2026-09-04，早于票 29–31 源码。即便用户执行探活，也须先产出新鲜产物（`scripts/release-readiness.ps1`）。
3. **波次串行放大阻塞**：四票全串行（共享文件纪律推导），任一票未交付即连锁阻塞后续。当前票 01 未派发 → 实际阻塞 3 票。

## 10. 并行窗口观察与边界声明（2026-09-13 00:24 增补）

本窗口执行期间，`but diff` 与 `stat` 实锤存在**并行窗口正在施工票 01**：

| 观测 | 证据 |
|---|---|
| CONTEXT.md 在途改动（`no`） | mtime `2026-09-13 00:24:29`（本窗口开工后）；内容 = UI Visual Standard / Visual Baseline 新词条 + Sidebar Nav Item 44px→40px 修正（A-006），并已引用「见 docs/design/ui-visual-standard.md §2」 |
| `tests/TEST-CONVENTIONS.md` 新增（`rr`） | D-006 两条守卫规则载体，属票 01 交付项 |
| 主交付物仍未落盘 | `docs/design/` 不存在；`docs/adr/` 最新为 0063，ADR 0065 未建 |

**边界声明（WORKFLOW §4.3 + 多窗口纪律）**：本窗口**只提交自己的产物**（`tv` = `docs/process/reports/04-pages-visual-alignment.md` 及其 `.scratch` 主本）；`no`（CONTEXT.md）与 `rr`（TEST-CONVENTIONS.md）**属票 01 窗口，本窗口不碰、不提交、不改**。

**对票 04 的即时收益**：票 01 在途的 CONTEXT.md 已先行锁定规范节号语义（§2 = nav 反馈三态），为票 04 的 C1 对照表提供了节号锚点预期，待规范全文落盘即可直接引用。
