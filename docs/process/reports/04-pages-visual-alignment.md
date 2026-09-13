# Report — 票 04 pages-visual-alignment（ui-craft 轮）

**Date**: 2026-09-13
**Status**: 施工完成，验收部分阻塞（代码侧全绿；用户侧探活 28 项与 before/after 截图未执行，如实登记）
**Branch**: ui-craft/04-pages-visual-alignment（GitButler 虚拟分支）
**来源**: prompts/04 + handoffs/04 + issues/04 + spec.md + decision-ledger.md + WORKFLOW.md + docs/design/ui-visual-standard.md v1.1（施工依据）/ v1.2（本票产出）

---

## 0. 开工复述与依赖解锁

- **Blocked by 票 03**：已解锁。票 01/02/03 三份报告齐备（01 = 26792 B、02 = 14191 B、03 = 35843 B）；`docs/design/ui-visual-standard.md` 26595 B / 308 行 v1.1 已落盘；ADR 0065 已落盘。
- **必读清单 14 份**：全部读全。第 7 项 `ui-visual-standard.md` 于本窗口首次开工时缺失 → 曾按启动器「缺一份即停下呈报」停下并交付勘察报告；票 01 落盘后本窗口复工并逐份补齐。
- **开工前在途冲突核验**：`git status --short src/` 空、`git diff --stat` 三处空、`but status` 无 MainWindow.axaml 在途改动 → 启动器 delta 第 1 条满足。
- **票 03 移交**：票 03 报告 §12.2 明确「离轨值 20 共 3 处移交票 04，不抢票 04 定值权」。本票据此行使定值权（见 §4.3）。

## 1. 声明 → 证据 → 结论总表（handoff 完成定义）

| # | 完成定义 | 声明 | 证据 | 结论 |
|---|---|---|---|---|
| 1 | 四页修正 diff 逐项对照规范节号 | 6 文件共 15 项修正，逐项锚定规范节号 | §2 对照表（含 before → after 与行号） | 达成 |
| 2 | A-007 处置结论写入报告与规范附录 | 判定为缺陷并修复（删硬钉 Width=200） | §3 证据链；规范**附录 C** + §8 v1.2 | 达成 |
| 3 | Margin 清零（守卫或报告计数证据） | 离轨 11 → **0**；Spacing 字面量 → **0** | §4 计数表 + 6 条 source-lint 守卫（§6） | 达成（双证） |
| 4 | 探活回填与基线截图到位（未执行项如实标注） | 28 项**全部未探活**；基线截图 **0 张** | §7；report-32 §3/§6；screenshots 目录仅 .gitkeep | 未执行，如实登记 |
| 5 | CI 云端绿 | 本机 CI-only 零构建零测试；守卫经 Node 逐条复演 6/6 PASS | §8 静态门禁 | 待推验证分支 |
| 6 | 报告写入 reports/04（含 before/after 对照清单） | 本文件；双轨逐字一致 | §9 / §10 | 达成 |

## 2. C1 — 四页修正 diff × 规范节号对照表（核心交付）

> 节号均指 `docs/design/ui-visual-standard.md`。行号为**修改前**行号（便于与票 03 报告对账）。

### 2.1 Views/MainWindow.axaml（票 04 领地）

| # | 修正项 | before → after | 规范节号 |
|---|---|---|---|
| M1 | 侧栏内部 Border 硬钉定宽 | `Width="200"` → **删除**（L42） | A-007 / **附录 C** / §0 Wasabi 锚「拖宽 170–400」 |
| M2 | 侧栏状态卡外边距离轨 10 | `Margin="10,10,10,0"` → `Margin="8,8,8,0"`（L61） | §5.4 / §5.2 P7 |
| M3 | 侧栏状态卡内边距离轨 10 | `Padding="12,10"` → `Padding="12,12"`（L62） | §5.4 |
| M4 | 导航组间距字面量 | `Spacing="4"` ×2 → `{DynamicResource SpaceXs}`（L75 / L82） | §5.2 **P5** |

### 2.2 Views/Pages/ConfigPage.axaml

| # | 修正项 | before → after | 规范节号 |
|---|---|---|---|
| C1 | 页头内边距离轨 20 | `Padding="24,20,24,16"` → `24,24,24,16`（L4） | §5.4 / §5.2 P7 |
| C2 | 内容区外边距离轨 20 | `Margin="24,16,24,20"` → `24,16,24,24`（L7） | §5.4 / §5.2 P7 |
| C3 | 分组卡片之间零间距 | `Spacing="0"` → `{DynamicResource SpaceLg}`（L8） | §3.3 **B3** / §5.2 **P1** |
| C4 | 控件列宽度散落 | `Width="160"` ×3 → `Classes="inline-control"`（L113/L149/L167） | §3.3 **B2** |
| C5 | ADR 0037 遗留空壳 | 单子 StackPanel（Spacing=10）→ TextBlock 直挂（L212-214） | §5.4（空壳消解，非新增空态） |

### 2.3 Views/Pages/LogsPage.axaml

| # | 修正项 | before → after | 规范节号 |
|---|---|---|---|
| L1 | 页头内边距离轨 20 | `24,20,24,16` → `24,24,24,16`（L4） | §5.4 / §5.2 P7 |
| L2 | 内容区外边距离轨 20 | `Margin="24,12,24,20"` → `24,12,24,24`（L8） | §5.4 / §5.2 P7 |
| L3 | 事件徽章内边距离轨 6 | `Padding="6,2"` → `Padding="8,2"`（L34） | §5.4 |

### 2.4 Views/Pages/RulesPage.axaml

| # | 修正项 | before → after | 规范节号 |
|---|---|---|---|
| R1 | 页头内边距离轨 20 | `24,20,24,16` → `24,24,24,16`（L4） | §5.4 / §5.2 P7 |
| R2 | 间距字面量 token 化 | Spacing=8 ×3 → SpaceSm；16 → SpaceLg；12 → SpaceMd | §5.2 P4 / P7 + §5.4 |

### 2.5 Views/Pages/ServiceManagerPage.axaml

| # | 修正项 | before → after | 规范节号 |
|---|---|---|---|
| S1 | 页头内边距离轨 20 | `24,20,24,16` → `24,24,24,16`（L4） | §5.4 / §5.2 P7 |
| S2 | 内容区外边距离轨 20 | `Margin="24,16,24,20"` → `24,16,24,24`（L8） | §5.4 / §5.2 P7 |
| S3 | 分组卡片之间零间距 | 无 Spacing → `{DynamicResource SpaceLg}`（L9） | §3.3 **B3** / §5.2 **P1** |

### 2.6 Styling/AppTheme.axaml（票 02 已闭环，文件空闲）

| # | 修正项 | 内容 | 规范节号 |
|---|---|---|---|
| A1 | 行分隔线通栏 | Border.row-divider 增 `Margin="16,0,16,0"` | §3.3 **B1** |
| A2 | 控件列宽度权威收归 | 新增 ComboBox.inline-control（Width=160） | §3.3 **B2** |

## 3. C2 — A-007 处置结论

**判定：缺陷，非设计意图。已修复。**

| 锚点 | 实态 |
|---|---|
| `MainWindow.axaml:40` | `ColumnDefinitions="200,4,*"`，Column[0] 可被改写 |
| `MainWindow.axaml:41-42`（改前） | `<Border Grid.Column="0" Width="200">` —— 内部面板**硬钉** 200 |
| `MainWindow.axaml.cs:798-805` | `RestoreSidebarWidth` → `grid.ColumnDefinitions[0].Width = new GridLength(clamped)` |
| `MainWindow.axaml.cs:815-822` | `OnSidebarSplitterDragCompleted` → `Math.Clamp(..., 170, 400)` → 持久化 |

- **冲突**：列宽开放 170–400（ADR 0052 A5 显式交付「可拖拽侧栏」）vs 面板定宽 200。
- **后果**：列宽 > 200 → 面板右侧留空露出 `SemiColorBackground0`；< 200 → 面板溢出被裁。
- **非设计意图的判定依据**：无任何注释或 ADR 声明该定宽为设计；§0 Wasabi 锚记「拖宽 170–400」，「拖宽」语义即要求面板跟随。
- **修复**：删除 `Width="200"`（1 行）。Border 位于 Grid cell 内默认 `HorizontalAlignment=Stretch`，自动跟随 Column[0]。
- **落盘**：报告 §3（本节）+ 规范**附录 C** + §8 **v1.2**。守卫 `Sidebar_Panel_Must_Follow_Column_Width_Not_Fixed_200`。

## 4. C3 — Margin 清零计数证据（A-008）

### 4.1 计数（脚本逐行提取，6 个 axaml 全量）

| 阶段 | 间距字面量 | 离轨 | Spacing 字面量 | Thickness 字面量 |
|---|---|---|---|---|
| 开工前（本票勘察） | 22 | **11**（20×7 / 10×3 / 6×1） | 3 | 19 |
| **施工后（终态）** | 27 | **0** | **0** | 25（全部在 ramp 上） |

> 终态字面量总数上升，是因为 `Spacing` 改为 DynamicResource 后不再计入字面量，且票 03 与本票在 RulesPage / ServiceManagerPage 引入的 token 化结构使受审项增加。**离轨与 Spacing 字面量双双为 0** 是「清零」的实质指标。

### 4.2 技术硬约束（本票勘察结论，已写入规范 §5.4）

`Margin` / `Padding` 属 **Thickness**，受 CONTEXT「Padding Literal Quantization」约束**必须保持字面量字符串**——`DynamicResource` Double 赋 Thickness 会跳过 `ThicknessTypeConverter`，导致布局测量期 `InvalidCastException`。

因此：

- **Thickness（Margin / Padding）**：「token 化」= **量化到 ramp 字面量**，不等于改 `DynamicResource`（那会引入崩溃，不是收紧）。
- **Spacing / *Spacing（Double）**：可且应改 `{DynamicResource SpaceXxx}`。

### 4.3 离轨值映射定值（票 03 显式移交，规范 P1–P7 未覆盖）

| 离轨值 | 取定 | 理由 |
|---|---|---|
| 20 | **24**（SpaceXl） | **决定性理由 = §5.2 P7 禁等距均匀**：取 16 会使页头 `24,16,24,16` 与内容区 `24,16,24,24` 两个层级取同一档位。取 24 后外框四边统一 24、分隔线上下各 16，层级清晰；同时满足 §5.3 Wasabi「≥16」与 Apple「分组间距 20–24」 |
| 10（侧栏外边距） | **8**（SpaceSm） | 同侧栏兄弟元素 `Margin="8,16,8,0"` / `Margin="8,0,8,12"` 水平内缩已用 8 —— 去重对齐兄弟，非猜测 |
| 10（卡内垂直） | **12**（SpaceMd） | 该卡水平已为 12（在轨）；上档 12 使卡内四边统一，避免制造新的不对称 |
| 6（事件徽章） | **8**（SpaceSm） | chip 通行密度；垂直 2（SpaceXxs）已在轨 |

## 5. 空态评估（规范 §4.2 移交票 04 的「服务管理器页缺口」）

**结论：不构成 E1 缺口，不新增空态占位。**（已回写规范 §4.2）

- E1 原文：「每个可空**列表 / 表格**必须有空态占位」。ServiceManagerPage 无 `ListBox` / `DataGrid` / 任何可空列表或表格 → **E1 适用对象不存在**。
- 「未安装服务」由 `ServiceStatus` 文案 + `ServiceStatusDotColor` 状态点承载（ServiceManagerPage 状态卡），属**状态展示**而非空态。
- Logs / Rules 两处空态已由票 26（ADR 0062 D5）落地，本票**无新增空态动作**。

## 6. 守卫（D-006 / TEST-CONVENTIONS R1）

新增 `tests/PhotoPrivacy.IntegrationTests/Ui/PagesVisualAlignmentSourceTests.cs`（8853 B / 166 行 / 6 `[Fact]`），每条含「防什么 bug」注释：

| 断言 | 防什么 |
|---|---|
| `Views_Spacing_Must_Use_DynamicResource_Not_Literal` | Spacing 写回字面量，脱离 Space ramp 单点权威（§5.4） |
| `Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp` | Margin/Padding 写出 ramp 外值（历史 20/10/6），页面节奏退回散值（§5.4 / P7） |
| `Sidebar_Panel_Must_Follow_Column_Width_Not_Fixed_200` | A-007 回潮：侧栏 Border 重新硬钉 `Width="200"` |
| `Row_Divider_Must_Be_Inset_To_Label_Column_Not_Full_Bleed` | §3.3 B1 回潮：分隔线退回通栏，行与行视觉粘连 |
| `Settings_Cards_Must_Be_Separated_By_SpaceLg` | §3.3 B3 / P1 回潮：卡片容器回到 `Spacing="0"`，形成「均匀网格」（AI 感第一根因） |
| `Views_Must_Not_Inline_ComboBox_Width` | §3.3 B2 回潮：ComboBox 重新行内写宽，与 ADR 0061「Views 禁止内联宽度」冲突 |

- **定性**：变更探测器而非契约（D-006 R2 保留档）——锁的是规范定值的落位，规范改值时须同步改本文件。
- **独立文件**：沿用票 02 惯例（票 02 用独立 `NavFeedbackSourceTests.cs` 规避 `DesignSystemTests.cs` 共享面），避免与票 03 在 `DesignSystemTests.cs` 的改动叠加冲突。
- **失效即红自检**：`ReadXaml` 剥除 XAML 注释后再断言——本票在 AppTheme 写入的设计依据注释含 `Margin` 等字面量，不剥注释守卫会恒绿。

## 7. 用户侧收口（如实登记，未执行）

| 项 | 状态 | 说明 |
|---|---|---|
| report-32 §2 探活 28 项 | 28/28 未探活 | 自 2026-09-07 票 32 交收口起即全部未执行（report-32 §6 检查点 B 未达成） |
| 票 01 新增 G 组（nav hover 六项） | 未执行 | 规范 §7 V4 |
| `docs/design/screenshots/` 基线截图 | 0 张 | 目录仅 `.gitkeep`（650 B，票 01 建） |
| before/after 同机位对照 | 无法产出 | 需窗口 920×600 / catppuccin / zh-CN / 侧栏 200（规范 §7 V2） |

**阻塞原因（非本窗口可解除）**：CI-only 政策禁 agent 本机构建与运行；且本机产物时间戳停留 2026-09-04，早于票 29–31 源码，须用户先跑 `scripts/release-readiness.ps1` 产出新鲜产物。

**本票新增的验收面（建议纳入用户探活）**：侧栏拖宽到 300 时面板是否跟随（A-007 修复验证，原 D3 观察项）、配置页卡片间是否出现 16px 间距、行分隔线是否自标签列起点对齐、三枚下拉是否仍等宽 160。

## 8. 静态门禁（本机 CI-only，零构建零测试）

| 项 | 结果 |
|---|---|
| XAML 标签平衡（6 个 axaml） | 0 偏差（开 − 闭 − 自闭合 = 0，逐文件） |
| 守卫 C# 词法感知括号平衡 | delta=0 / min=0；`()` 平衡 |
| 守卫 6 条断言 Node 复演 | 6/6 PASS（复演含 XAML 注释剥离，与 `ReadXaml` 同口径） |
| UTF-8 无 BOM / LF / 非空 | 8 个改动文件全通过 |
| 撞红三档分类 | 杀 0 / 改造 0 / 保留 0（本票未改既有断言，无撞红） |
| 新断言注释齐备 | 6/6 含「防什么 bug」+ 票号（D-006 R1） |

## 9. before / after 对照清单（供人工验收）

| 面 | before | after | 规范锚 |
|---|---|---|---|
| 侧栏拖宽 | 面板定宽 200，拖宽后右侧留背景断层 | 面板跟随 170–400 | 附录 C / §0 |
| 配置页卡片 | 4 张卡直接相邻 | 卡片间 16px | §3.3 B3 / §5.2 P1 |
| 行分隔线 | 通栏 | 自标签列起点 inset 16 | §3.3 B1 |
| 下拉控件宽度 | 3 处行内 `Width="160"` | 共享 class `ComboBox.inline-control` | §3.3 B2 |
| 页头外框 | 20（离轨） | 24（SpaceXl） | §5.4 / P7 |
| 事件徽章 | `6,2`（离轨） | `8,2`（SpaceSm / SpaceXxs） | §5.4 |
| 保存状态行 | 空壳 StackPanel（Spacing=10） | TextBlock 直挂 | §5.4 |

## 10. 双轨与版本控制

- 主本 `.scratch/ui-craft/reports/04-pages-visual-alignment.md`；受控副本 `docs/process/reports/04-pages-visual-alignment.md`（WORKFLOW §4.4 轨 1，逐字一致）。
- 分支 `ui-craft/04-pages-visual-alignment`；commit 信息中文带票号；**不 push 不 PR**（§4.2）。
- 轨 2 快照：本窗口**未执行**任何 GitButler 历史改写 / 丢弃类操作（无 move/undo/squash/discard/uncommit/branch delete/pull），故未触发。
- **跨窗口边界**：只提交本窗口产物；票 01 在途的 `CONTEXT.md`、`tests/TEST-CONVENTIONS.md` 未触碰（§4.3）。

## 11. 风险与遗留

1. **运行侧证据仍为零**（本轮最大闭环风险）：28 项探活 + 基线截图全部缺位，本票 15 项修正**无任何肉眼验证**。规范 §7 V1 要求「每票验收 = 同机位 before/after 人工对照」，当前无法满足。
2. **守卫未经真机编译**：本机 CI-only，6 条断言仅经 Node 复演自证 + 词法括号平衡；真实编译与 xunit 执行待云端 CI。
3. **`ComboBox.inline-control` 进入 class 白名单**：ADR 0062 D1.3 的「无未定义 Classes」断言从 AppTheme 选择器提取白名单，新增选择器会自动入列，预期不红灯；若云端报未定义，说明提取器未覆盖 `ComboBox.` 前缀，需按三档分类处置。
4. **规范 v1.2 已回写**：§3.3 B1/B2/B3、§4.2、§5.2 P1/P5、§5.4、新增附录 C、§8 v1.2。
