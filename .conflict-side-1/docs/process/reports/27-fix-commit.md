# 修复报告 — 票 27 补提交到独立虚拟分支（架构恢复第六轮）

- 日期：2026-09-04　窗口：票 27 修复窗　分支：arc-recovery/27-config-roundtrip-completion
- 启动器：prompts/27-fix-commit.md；必读清单已逐份读全（review-27 复核报告 / report-27 / issue 27 / README / WORKFLOW / but skill）。
- 动栈前快照（WORKFLOW §4.4 轨2）：`node scripts/workflow-snapshot.js` → photo-snapshots/20260904-130713（57 文件 / 262326 字节 + manifest SHA256）。本窗动过栈（but branch new / but move / but pull），快照已先行。

## 1. 声明 → 证据 → 结论

| # | 声明（检查点） | 证据 | 结论 |
|---|---|---|---|
| A1 | but status --short + git status --short 核实「票 27 改动滞留 zz [uncommitted]、无 arc-recovery/27-* 分支」属实 | but status：8 文件在 zz（un/nv/yv/ss/pl/yr/kl/xz），分支列表仅 24/20/21/19/18/17/codex-session；git status：5 M + 6 ?? 印证 | ✅ |
| A2 | git show --stat HEAD 证实 workspace commit 不含票 27 文件 | HEAD=5e17be4 树中 git ls-tree 无 ConfigFileWatcher.cs、无 27 报告（0 命中）；grep 命中的同名文件为轮五票 17/20 历史改动非票 27 增量 | ✅ |
| B1 | 8 文件圈入 arc-recovery/27-config-roundtrip-completion 提交 | but commit 创建 change xup → 实物 e0b6acb39e75b4e47abb0a611bec4f8df185b6f2（短 e0b6acb），8 files changed, 443 insertions(+), 14 deletions(-) | ✅ |
| B2 | commit 中文带票号 | 消息以「票27:」开头，含三个检查点实现/门禁/依赖声明全文（git show 实物可查） | ✅ |
| C1 | 提交后 but status --short 分支落位 | arc-recovery/27-config-roundtrip-completion 现身栈顶（叠于 arc-recovery/24 之上），commit xup；zz 池仅剩他票 3 个勘察报告（ly/os/mm） | ✅ |
| C2 | git log --oneline 确认 commit 哈希 | 9904cb2 (workspace) → e0b6acb (票27) → 8cdc769 (票24) → 0a8fd4b (main)；哈希以 git log 物理实物为准（WORKFLOW §7.3） | ✅ |
| C3 | 提交面不含他人在途物品 | git show --name-only e0b6acb grep Views/Pages、MainWindow.axaml$、MainWindowServiceAdapters、reports/22-/23-/24-、results/ 均 0 命中 | ✅ |

## 2. 提交面清单（e0b6acb，正好 8 文件）

1. src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs（M, +23 行级变更）
2. src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs（M）
3. src/PhotoPrivacy.Ui/Services/ConfigEditor.cs（M, OnSelfWrite 抑制通道）
4. src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs（M, +18 行 watcher 接线）
5. src/PhotoPrivacy.Ui/Services/ConfigFileWatcher.cs（A, 166 行）
6. tests/PhotoPrivacy.IntegrationTests/Ui/ConfigFileWatcherTests.cs（A, 135 行）
7. tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigRoundTripTests.cs（M, +2 Theory 8 InlineData）
8. docs/process/reports/27-config-roundtrip-completion.md（A, 轨1 沉淀副本，42 行）

## 3. 处置过程记录（依赖冲突链，审计可复算）

1. 首次 but commit 直接落独立分支 → 依赖拒绝：AppConfigJson / AppConfigLoader / AppConfigRoundTripTests 3 文件 hunk depends on ticket-17（同文件轮五改动在栈内）。
2. 按 CLI hint but branch new --anchor ticket-17 → branch new/move 落位均报 merge-base 冲突（工作区 5 条并行分支 tip 互相合并冲突，多基无法收敛）。
3. but status 提示 17-21 均 (merged upstream)（main 0a8fd4b 已含）→ 按 CLI 官方语义执行 but pull：5 条 merged-upstream 分支集入 main 并移除，workspace rebase successful，票 24 与空分支 27 干净重放，未提交改动全程携带无冲突。
4. 重试 but commit（8 个文件 ID 未变）→ 成功创建 xup。
5. 处置合规性：无 move 撤销他人、无 uncommit/discard/squash；but pull 为 CLI 官方 hint 动作且快照先行，but undo 可回退。

## 4. 与并行窗口的隔离（WORKFLOW §4.3/§4.4）

- 提交面正好 8 个票 27 文件（§2 清单），grep 实证 0 命中他人物品（Views/Pages、MainWindow.axaml、MainWindowServiceAdapters.cs、勘察报告 22/23/24、results/**）。
- zz 未提交池现存 3 个勘察报告（reports/22/23/24-config，他票在途）原样保留未圈入。
- 本修复窗未触碰票 24 已提交内容（8cdc769 仅作栈基重放，内容零修改）。

## 5. 结论

票 27 补提交闭环达成：review-27 呈报的「未提交」违规已消除——改动现隔离于 arc-recovery/27-config-roundtrip-completion 独立虚拟分支（实物 e0b6acb），轨 1 双轨沉淀随 commit 进 git，zz 池不再含票 27 任何文件。README 状态表票 27 行可由「❌ 未提交（待修复）」改「✅ done（e0b6acb）」。遗留移交大脑：本修复报告自身为未提交新产物（.scratch + docs 副本双轨已落），是否随收口提交由大脑裁定；禁 push（WORKFLOW §4.2）。
