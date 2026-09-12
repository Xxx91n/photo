# 架构恢复 — 第五轮（第四轮收口后宏观评估落地）

轮次来源：2026-09-03 宏观架构调查（codegraph + 三个只读子代理 + atomcode 三引擎联网）产出的《架构恢复评估》，判定「基本清晰但局部残留」，落成五票。

## 状态表（首脑复核后 — 2026-09-03）

| 票 | 标题 | 来源 | Blocked by | 波次 | 实物 commit | 状态 | 核验偏差 |
|---|---|---|---|---|---|---|---|
| 17 | 配置 round-trip 对称性止血 | 候选②配置权威 | None | 1 | `2a8aed9` | ✅ done | 分支命名 `ticket-*`（偏离本轮 arc-recovery/*）；当前 HEAD 受下游票副作用有 1 编译错（非本票引入） |
| 18 | 规则存储完整往返 | 候选①规则面板 | None | 1 | `288a1c1` | ✅ done | 报告「+4 新测试」实为 +5（旧键名 `raw_strip_exif_xmp_iptc` 注释保留 1 处） |
| 19 | 规则引擎单一真相源 | 候选①规则面板 | 18 | 2 | `2e7f2af` | ✅ done | 报告「WipeRuleEngineTests 20 例」实为 18 方法 / 32 有效用例；IntegrationTests 1 失败归属票20 在途 |
| 20 | UI 根目录层化 + 死代码清理 | 候选③UI层化 | None | 1 | `686fab7` | ✅ done | 分支命名 `ticket-*`（偏离本轮 arc-recovery/*）；amend 链 3 次/5 分钟（§4.4 字面合规） |
| 21 | CONTEXT.md 三层一致性收口 | 三层一致性 | 17, 19 | 3 | `3ad7134` | ✅ done | **wave 违规**：实物 commit 时间 04:20 早于票18 (04:27) 与票19 (10:57) 约 6.5 小时；**报告 hash 错误**：披露票17 = `7f1c28e`，实物 = `2a8aed9` |

## 波次推导（仅由 Blocked by 字段推导）

- 波 1：Blocked by None → {17, 18, 20}，frontier = {17, 18, 20}，可并行。
- 波 2：Blocked by 18 → {19}。
- 波 3：Blocked by 17、19 → {21}。
- 文件面：17 动 Configuration/AppConfig* + config.sample.json + Configuration 测试；18 动 Configuration/FormatRulesStore + 其测试；20 动 Ui 根 .cs + 删死代码（InFlightRegistry/IWipeStrategy）——同目录不同文件，无两票同文件碰撞。
- **实物 commit 顺序**（git log）：票17 (04:15) → 票21 (04:20) → 票18 (04:27) → 票19 (10:57) → 票20 (11:12) → workspace (11:17)。**票21 在票18/19 之前提交**——wave 3 实物早于 wave 1/2 完成（详见过程违规节）。

## 过程违规（单独呈报，不替追认）

| 票 | 违规 | 详情 |
|---|---|---|
| 21 | **wave-ordering 违规 + 未自报** | issue `Blocked by: 17, 19`（wave 3），实物 commit 时间 04:20:39，早于票18 (04:27:35) 约 7 分钟、早于票19 (10:57:52) 约 6.5 小时。报告「共池工作区并发」自陈编辑期与票19/20 并行，与 git log 时间线矛盾。**内容上不依赖票19**（核验：CONTEXT.md 全文 grep `WipeRuleEngine` 0 命中，仅命中既有 `Wipe Strategy Resolver` 术语条目），技术上先提交合理；但 wave-ordering 措辞与实物矛盾，且未自报违规。 |
| 21 | **报告 hash 披露错误** | 报告原文「票17（已 commit `7f1c28e`）」——`7f1c28e` 经 git cat-file 解析为 GitButler Workspace Commit `e96da529`，**非票17 实物**。实物为 `2a8aed93aac81a2ca43f1f35a919eb733cce061c`（短 `2a8aed9`）。票19/票20 提交消息内文均正确引用 `2a8aed9`，仅票21 报告披露错误。违反报告自陈「报告断言以 git log 实物哈希为准」。 |
| 17、20 | **分支命名混搭** | 票17 用 `ticket-17-config-roundtrip-stopbleed`、票20 用 `ticket-20-ui-root-layering-deadcode`；票18/19/21 用 `arc-recovery/NN-...`。WORKFLOW.md 无强制规范，不算违规条款；但本轮内部不统一，第四轮约定 `arc-recovery/<NN>-<slug>`（origin 上 1-12 票均为此形态）。建议未来窗口统一前缀。 |
| 18 | **测试数量口径偏差** | 报告「+4 新测试」实为 +5（`RulesStore_RoundTrip_FullSchemaSaveLoadIdentical` 也是本票新增但未列入红灯）。5 测试均落地、回归 10/10 仍正确；建议报告口径修正为「5 新测试，4 先红后绿、1 一次性绿」。 |
| 19 | **测试数量口径偏差** | 报告「WipeRuleEngineTests 20 例」实为 18 方法（16 [Fact] + 2 [Theory]，含 16 行 [InlineData] → xUnit 有效用例 32 个）。 |
| 18 | **注释保留旧键名** | `src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs:85` 在「// 票18: IsEnabled 不落盘；此前误读 RAW 族键 raw_strip_exif_xmp_iptc（键名错配），现按默认启用」中作为 bug 考古保留，非代码消费。严格「全仓 0 引用旧键名」应为 0，注释豁免待确认。 |
| workspace | **.gitignore 未提交** | `diff --git a/.gitignore` 显示新增 `results/` 行（行 +43），未 commit 落盘。属他人物品保护规则收紧，建议随票17 或合入下一动作。 |
| 全局 | **当前 HEAD 编译错** | 5 票叠加后 HEAD 有 1 个 `Rules/WipeRuleEngine.cs(65,69): error CS0117: "FormatRulesStore"未包含"Key"` 编译错，**非任一票引入**，但全解决方案 build 0 错门禁未在合并态重测。需大脑裁定是否在合入 main 前修此错。 |

## 备注

- 5 票各 commit 实物哈希以 git log 为准（WORKFLOW §7.3）：2a8aed9（17）→ 3ad7134（21）→ 288a1c1（18）→ 2e7f2af（19）→ 686fab7（20）→ e96da52（workspace）。
- 5 票均报「单槽 Core.Tests + IntegrationTests 全绿」（除 19/21 各 1 失败已归属披露）；本轮首脑未重跑测试，按子代理 grep 计数核对。
- **未触碰他人物品**：`results/deepseek-v4-pro/gsm8k/` 等他人物品在票17/20 commit 文件清单中 0 命中。
- **轨 1 沉淀**：每票均在 commit 内落 `docs/process/round5-spec.md` / `round5-README.md` / `docs/process/reports/<NN>-<slug>.md` 受控副本。

## frontier 重算

- 第五轮 5 票全部提交（17/18/19/20/21），**frontier = ∅**。
- 无下一波可开工票号。
- **下一动作**：5 分支合入 main + push（待用户指令；参考第四轮收口 ADR 0059 base→tip 拓扑：12→09→16→15→14→10→11→13）。建议先验 `git diff main..HEAD` 零冲突 + 全解决方案 build 0 错（修 WipeRuleEngine 编译错或确认其归属）+ commit .gitignore `results/` 行。

## 自检基线（首脑复核 2026-09-03）

- 5 票声明 → 证据 → 结论对照表见各票报告（report-17 ~ report-21）+ `.scratch/architecture-recovery/closeout5-verify/*`（首脑复核留证）。
- 过程违规 8 项单独呈报，不替任何票追认。
- 待用户裁定：违规是否接受 / 是否合入 / WipeRuleEngine 编译错修复路径。
- 票 22 预备：XAML 现状勘察（仅调研落报告，不开 commit）→ 见 `report-22-xaml-decomposition.md` & `docs/process/reports/22-xaml-decomposition.md`。

---

# 第六轮 — GUI 架构与设计系统心智模型调研（2026-09-04）

## 状态表（首脑合成后）

| 编号 | 标题 | 类型 | 状态 | 落盘路径 |
|---|---|---|---|---|
| 22 | XAML 结构分解 + design tokens 违例 | 勘察报告 | ✅ done | `report-22-xaml-decomposition.md` + `docs/process/reports/22-*` |
| 23 | ViewModel 职责拆分 + Shell+Page 拓扑 | 勘察报告 | ✅ done | `report-23-vm-responsibility-split.md` + `docs/process/reports/23-*` |
| 24 | Config 权威性 + 服务消费面审计 | 勘察报告 | ✅ done | `report-24-config-authority-audit.md` + `docs/process/reports/24-*` |
| — | GUI 心智模型联网调研（12 项目对照） | atomcode 调研 | ✅ done | `atomcode-report-gui-mental-models.md`（45071 字节 / 306 行） |

## 波次表（由 issue Blocked by 字段唯一推导）

| 波次 | 票 | Blocked by |
|---|---|---|
| 波 1 | 24, 27 | None |
| 波 2 | 25, 26 | 24 |

## 状态表（已立票 — ready-for-agent）

| 票 | 标题 | Blocked by | 波次 | 状态 |
|---|---|---|---|---|
| 24 | Shell + Pages/ 拆分 + 顺带修复 Service→View 直写（MainWindow 降纯 shell） | None | 1 | ✅ done（commit 8cdc769，but pull 后 renumber 自 0c23c9f） |
| 25 | 高频组件抽取（PathPicker/NavButton/ThemeSwatch） | 24 | 2 | ✅ done（commit 02cf49c） |
| 26 | token 消费纪律 gate + 按钮反馈标准定稿 + 反馈补缺 | 24 | 2 | ✅ done（commit 9fb5438，报告哈希口径错误） |
| 27 | Config Editor Round-Trip Completion（回环补全） | None | 1 | ✅ done（commit 07dc512，补提交已闭环） |

> 注：票 24 与勘察报告 report-24-config-authority-audit 通过 slug 区分（票报告为 report-24-shell-pages-split），互不混淆。

## 首脑复核（波 1，2026-09-04）

- **票 24**：✅ done。10 项复核 8 ✅ / 2 ⚠️ / 0 ❌。分支 `arc-recovery/24-shell-pages-split`（0c23c9f）；MainWindow 139 行 + Pages 4 文件 211/63/62/67；直写改 VM 中转属实；commit 面清洁（19 文件 0 命中票 27 在途）。
- **票 27**：❌ 未提交。10 项复核 8 ✅ / 1 ❌ / 1 ⚠️。ConfigFileWatcher（166 行）/ 3 处 DTO 收敛 / 测试门 191/311 均属实，但**全部改动滞留 `zz [uncommitted]`，无 `arc-recovery/27-*` 分支、无 commit**（违反 WORKFLOW §4.2「每票一独立分支」）。

### 过程违规（单独呈报，不追认）

| 票 | 违规 |
|---|---|
| 27 | **未提交**：改动滞留 zz [uncommitted]，无独立虚拟分支，可被 workspace reset 蒸发；报告通篇未披露未提交状态 |
| 27 | 双轨沉淀半完成：docs 副本已落但为 ?? 未跟踪，轨 1「随票提交进 git」未达成 |
| 24 | 「14 断言」口径偏差：14 为测试用例数（6 Fact + 8 Theory），非断言数（实 20 处 Assert.） |
| 24 | Width=280 数量内部矛盾：报告 §1 称 ×4，commit/ADR 称 ×5，issue 称 ×4（收敛事实本身属实） |
| 24/27 | MainWindow.axaml.cs 双票共享（§4.3）：票 24 已提交，票 27 未提交亦改同文件（仅 watcher 接线 18 行，无冲突），报告未披露 |

### 待用户裁定

1. 票 27 是否补提交到独立虚拟分支（修复启动器已备）。
2. 是否回溯票 24 报告测试口径（191/311 因波 1 共池，票 24 报告可能含票 27 测试）。
3. UI 手工探活（5 色板/10 语言/中键/拖宽/日志流）是否轮末冒烟。

## 首脑复核（波 2，2026-09-04）

- **票 25**：✅ done。12 项复核 10 ✅ / 2 ⚠️ / 0 ❌。分支 `arc-recovery/25-component-extraction`（02cf49c，父 8cdc769=票 24）；Controls 4 文件 225 行入 commit；5×u:PathPicker + 4×NavButton + ThemeSwatchCatalog 数据化 + source-lint 3 守卫 + 快照 59/59 全部属实。
- **票 26**：✅ done（含 1 ❌ 哈希口径）。12 项复核 10 ✅ / 1 ⚠️ / 1 ❌。分支 `arc-recovery/26-token-button-gate`（9fb5438，父 02cf49c=票 25）；source-lint 3 断言 7 Fact + AppTheme 13 状态样式 + 空态 ×2 + 10 语言 198 键 + ADR 0062 全部属实；**报告 header 自称哈希 3de0c32、docs 副本自称 3ec6837，实物 tip = 9fb5438**（三枚旧哈希为 reflog-only 悬空 amend 链）。

### 过程违规（单独呈报，不追认）

| 票 | 违规 |
|---|---|
| 26 | **哈希口径错误（票 21 同类复发）**：报告「git log 实物 3de0c32」、docs 副本 3ec6837，实物 9fb5438；违反 WORKFLOW §4.2/§7.3 |
| 26 | 双轨同步违约（§4.4 轨 1）：.scratch 主本与 docs 副本 hash 行不一致，且两值皆非实物 |
| 26 | 小口径偏差：「20 文件 519+/27-」实为 520+/27-；commit message「叠于票24之上」与实父 02cf49c（票 25）字面不符（依赖语义成立） |
| 25 | **§4.3 共享文件突破**：票 26 的 titlebar ToolTip×3 / caption 迁移 / PathPicker ToolTip×5 / config.tooltip×2 行实入票 25 commit；双方 commit 信息书面披露，但票 25 报告正文缺吸收行清单 |
| 25 | **工作树 D+?? 混合态（源码层面，需修复）**：Controls 4 文件 + 25 报告在 raw index 中 staged-delete、磁盘同内容 untracked（裸 git stash 误用残留）；HEAD 与磁盘内容一致无损失，但按 index 提交有掉出风险 |
| 25 | 行数口径与 git blob 不符：报告 1699→1548 / 239→225，实物 1679→1530 / 237→225，测量基准未披露 |
| 25 | 裸 git stash 误用（§4.2，自报，已完整恢复无损失） |
| 25 | 多窗口并发 test（§5 纪律，自报） |

### 票 27 窗口移交（大脑裁定）

1. 修复报告 `report-27-fix-commit.md`（工作版在 .scratch，docs 副本 ?? 未跟踪）——**大脑裁定：随收口提交**（§4.4 轨 1 流程产物）。
2. 禁 push 已遵守；合入 main + push 待用户指令。

## frontier 重算

- 四票全部 done：24（8cdc769）/ 25（02cf49c）/ 26（9fb5438）/ 27（07dc512）。
- **第六轮实施票 frontier = ∅**，无下一波可开工票号。
- **收口前置（源码层面待修复）**：票 25 残留的工作树 D+?? 混合态（Controls 4 文件 staged-delete）必须在合入 main 前修复，否则 flatten 时 Controls 文件有从 HEAD 掉出风险。修复启动器已备：`prompts/25-fix-workspace-state.md`。
- **下一动作**：① 修复 D+?? 状态 → ② 四分支拓扑合入 main（24→25→26→27，参考 ADR 0059/0060 --no-ff 惯例）→ ③ push（待用户指令）→ ④ 收口 ADR + 随收口提交勘察报告 22/23/24-config/27-fix 的 docs 副本。

## 合成交付物

- HTML 架构评审报告：`C:\Users\Administrator\AppData\Local\Temp\architecture-review-gui-round6-20260904.html`（OS 临时目录，未入仓库）。
- 执行摘要：配置权威「生效但 3 漂移点 + 5 违例」；按钮不统一「0 UserControl + 129 内联 Classes + 6 变体缺 :disabled」；行业范式「Shell+Page + VM-first，Semi+Ursa 组合方向正确，差距在消费端未拆分」。
- 时效纠错：csproj 实为 Avalonia 12.1.1（非 11），AGENTS.md「Avalonia 11」口径已过期。
