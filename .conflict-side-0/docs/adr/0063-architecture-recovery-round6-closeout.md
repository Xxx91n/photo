# ADR 0063 — 架构恢复第六轮收口（GUI 心智模型落地）

日期：2026-09-04　状态：已合入 main（未 push）

## 1. 来源与决策链

第五轮（ADR 0060）收口后，用户痛点集中在 GUI 层心智模型混乱：按钮尺寸/反馈不统一、无审美设计、权威配置是否生效不明。本轮先做宏观调查（3 份仓库实物勘察 + atomcode 三引擎联网调研 12 项目对照，star ≥500 经 GitHub API 核验），判定 GUI「选型正确、消费端未拆分」，落成四票：

- 票 24 Shell + Pages/ 拆分（含顺带修复 5 处 Service→View 直写）
- 票 25 高频组件抽取（PathPicker / NavButton / ThemeSwatch）
- 票 26 token 消费纪律 gate + 按钮反馈标准定稿 + 反馈补缺
- 票 27 Config Editor Round-Trip Completion（ConfigFileWatcher 磁盘→UI 回环 + 3 处双 DTO 漂移收敛）

决策链：ADR 0061（页面文件化 + shell 最小化）、ADR 0062（token 纪律 + 按钮反馈标准 + 空态）。行业范式结论：Shell（只留 chrome）+ 按页拆 UserControl + VM-first；现役 Semi + Ursa.Themes.Semi 组合与官方 README 逐字同构，方向冻结，不换道。

## 2. 四票结果（首脑复核后）

| 票 | commit | 复核 | 关键结论 |
|---|---|---|---|
| 24 | 8cdc769 | 10 项 8✅/2⚠️/0❌ | MainWindow 139 行 shell + Pages 4 文件；直写改 VM 中转属实；commit 面清洁 |
| 25 | 02cf49c | 12 项 10✅/2⚠️/0❌ | Controls 4 文件入 commit；5×PathPicker/4×NavButton/Catalog 数据化 + 3 守卫属实 |
| 26 | 9fb5438 | 12 项 10✅/1⚠️/1❌ | 三断言 7 Fact + 13 状态样式 + 空态×2 + 10 语言 198 键 + ADR 0062 属实；**哈希口径错误** |
| 27 | 07dc512 | 10 项 8✅/1❌/1⚠️ | 初轮未提交 → 修复窗补提交闭环；ConfigFileWatcher/DTO 收敛/测试门属实 |

修复票：票 25 工作树 D+?? 混合态收口（raw index 残留，`git restore --staged` index-only，快照 20260904-213126 ZERO-LOSS 63/63）；票 27 补提交（07dc512）。

## 3. 合入拓扑（main）

base=0a8fd4b（ADR 0060）→ 栈序逐支 `--no-ff` 合入：

- f474e65 merge: round6/24-shell-pages-split
- c254d7e merge: round6/25-component-extraction
- aa31d30 merge: round6/26-token-button-gate
- 0062538 merge: round6/27-config-roundtrip-completion

零冲突。合入前工作树预检：raw index 干净（票 25 修复窗已收口），仅 4 个未跟踪 docs 副本（22/23/24-config/27-fix）随本收口提交。

## 4. 终门禁（CI-only 政策下的收口留证）

- 本机禁跑 dotnet（CI-only 用户政策），运行类门禁以各票 commit 内门禁自述 + 守卫代码存在性 + CI 为准：
  - 守卫存在性实测：MainWindowShellSourceTests 11 Fact/Theory、DesignSystemTests 42（Ticket26 前缀 7）、ConfigFileWatcherTests 3、AppConfigRoundTripTests 8。
  - ADR 0061（37 行）/ 0062（82 行，D1-D8）在 main。
- 脚本级 verify 实测：workflow-verify 20260904-213126 → ZERO-LOSS 63/63；git diff --check 空。
- **CI 待验**：build 0 错 0 SCS / semgrep 双配置 0 发现 / 单槽 Core 191/191 + Integration 321/321（24/25/26/27 各自 commit 自述）——推送后由 CI 云端复验，红灯即按教训流程开返修窗。

## 5. 过程违规（单独呈报，不追认）

| 票 | 违规 |
|---|---|
| 26 | 哈希口径错误（票 21 同类复发）：报告 3de0c32 / docs 副本 3ec6837，实物 9fb5438 |
| 26 | 双轨同步违约：.scratch 主本与 docs 副本 hash 行不一致且皆非实物 |
| 26 | 小口径偏差：「20 文件 519+/27-」实为 520+/27- |
| 25 | §4.3 共享文件突破：票 26 的 caption/ToolTip 行实入票 25 commit（双方 commit 信息书面披露） |
| 25 | 裸 git stash 误用（§4.2，自报，已恢复）+ 多窗口并发 test（§5，自报） |
| 25 | 行数口径与 git blob 不符（1699→1548 vs 实测 1679→1530） |
| 27 | 初轮未提交（已修复闭环）；修复窗动用 git restore --staged（启动器 fallback 授权 + §4.4 快照，首脑认可例外） |

## 6. 事故与处置

D+?? 混合态（票 25 修复窗收口）：raw index 残留 staged-delete 曾使「按 index 提交会把 Controls 4 文件从 HEAD 剔除」。处置：修复窗先快照（213126）后 index-only 复原，工作树零改动；首脑独立复验 index 干净 + Controls 4 blob 哈希不变。教训：并行窗口禁用裸 git stash，栈操作残态必须收口后再合入。

## 7. 决策

- 修复报告 27-fix-commit.md 与勘察报告 22/23/24-config 的 docs 副本随本收口提交（§4.4 轨 1）。
- 四分支本地合入 main 完成；**push 暂停**，待用户明确指令。
- 遗留事项列 backlog 呈报（见 docs/process/round6-README.md 与收口报告），由用户决定是否立票。

## 8. 下一动作

1. 用户指令 push（origin/main 0a8fd4b → 0062538）。
2. CI 云端复验（push 触发）；红灯开返修窗。
3. 遗留 backlog 立票与否待用户裁定。
