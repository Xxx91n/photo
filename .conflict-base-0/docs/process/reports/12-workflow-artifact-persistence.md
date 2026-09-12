# 报告 — 票12 流程产物持久化策略（B12）（架构恢复第三轮）

日期：2026-09-02　窗口：本票专用　分支：arc-recovery/12-workflow-artifact-persistence（哈希以 git log 实物为准）

## 完成定义对照（声明 → 证据 → 结论）

| 声明（handoff 完成定义） | 证据 | 结论 |
|---|---|---|
| WORKFLOW 新增持久化条款成文 | .scratch/architecture-recovery/WORKFLOW.md §4.4（轨1 docs/ 沉淀 + 轨2 仓库外快照，触发时机逐条列明）；docs/process/WORKFLOW.md 受控副本同文（逐字节一致） | ✅ |
| 一轮无破坏实测零丢失留证 | 6 次仓库外快照（D:/Aworker/photo-snapshots/20260902-*，各附 SHA256 manifest）+ 6 次全树哈希校验全部 ZERO-LOSS；演练覆盖 pull / move（真实冲突+resolve）/ squash / uncommit / discard 全序列，见「实测记录」 | ✅ |
| AGENTS.md / WORKFLOW 同步一致 | AGENTS.md §6 禁止事项表新增「动栈前不先快照 .scratch」行、§7 Git 规范新增「流程产物持久化」条，均反向引用 WORKFLOW §4.4；WORKFLOW §7 教训 5 升级引用 §4.4 | ✅ |
| 纯文档票 build 绿即可 | dotnet build PhotoPrivacy.sln：0 错误；--no-incremental 复核 SCS 警告 = 0（988 条既有 CA 警告与本票无关） | ✅ |
| 完成后写报告（声明→证据→结论对照） | 本文件；受控副本 docs/process/reports/12-workflow-artifact-persistence.md | ✅ |

## 实测记录（检查点 B 留证）

策略条款：WORKFLOW §4.4。本轮实测按该条款逐触发时机执行（每步前置快照、步后校验）：

| # | 动栈前快照 | 栈操作 | 校验 |
|---|---|---|---|
| 1 | 20260902-074101（15 文件/17,389B） | but pull（19 上游提交 rebase + 18 已合并本地分支清理） | ZERO-LOSS 15/15 哈希一致 |
| 2 | 20260902-080710（20 文件；票11 窗口并行新增 bench/ 已入保护圈） | 演练分支 arc-recovery/12-drill 两提交（pow/vpu，docs/.b12-drill-marker.md 为提交对象） | —（随 #3 校验） |
| 3 | 20260902-082411（24 文件） | but move vpu --below pow —— **真实复现事故同款冲突**（vpu/pow 相继 [conflict]，删除/重命名类） | ZERO-LOSS 24/24 |
| 4 | resolve 期间 | but resolve 两轮（edit mode 手工清除冲突标记 + finish×2）；每次 finish 后 workspace 完整恢复、未提交改动无损 | ZERO-LOSS 24/24 |
| 5 | 20260902-082633 | but squash vpu -t pow（合并为一提交） | ZERO-LOSS 24/24 |
| 6 | 20260902-082644 | but uncommit pow（提交退回未提交区） | ZERO-LOSS 24/24 |
| 7 | 20260902-082701 | but discard ww:4（清演练标记）+ but discard arc-recovery/12-drill（清空演练分支） | ZERO-LOSS 24/24；marker 确认不存在 |

- 校验方法：.codex-tmp/b12-snapshot.js（整树复制 + manifest.json 逐文件字节数/SHA256）与 b12-verify.js（现树 vs 基线逐文件比对，输出 missing/changed/added 与 ZERO-LOSS 判定）。
- 结论：**策略全序列执行下流程产物零丢失**；ADR 0058 事故 1 的触发动作类（move→冲突→resolve 族）在本轮受控复现中被双轨防护完整兜住。
- 不破坏现行工作区：全程仅动本票自建演练分支与标记文件；票 09/10 在途未提交改动（scripts/release-readiness.ps1、scripts/smoke.ps1、ServiceManager.cs、ProcessHygieneGuardTests.cs、SmokeScriptDiagnosticsTests.cs）与 arc-recovery/11 分支原样健在。

## 过程要点

1. 启动器 + 必读六件全部读全后开工；Blocked by: None，开工复述已交付。
2. 关键勘察：.scratch/ 被 .gitignore:40 忽略（git ls-files 计 0）——事故根因实锤：流程产物完全活在版本控制之外，WORKFLOW.md 本身即无法提交。故轨 1 必须以 docs/process/ 受控副本承载（.scratch 工作版 + docs 灾备权威副本，双向同步义务写入条款）。
3. 演练 move 真实触发删除/重命名类冲突两连（vpu→pow），按 GitButler 规程 edit-mode 解决；全程未用全局 but undo（多窗口并行安全）。
4. 6 个快照目录（074101/080710/082411/082633/082644/082701）全部在仓库外 D:/Aworker/photo-snapshots/。

## 偏差与披露

- 条款触发清单含 but pull（会 rebase 活跃分支），首轮实测即按此执行（快照 #0 先于 pull）。
- undo 未单独演练：多窗口并行下全局 but undo 可能回退他窗操作，改以 move→resolve→squash→uncommit→discard 覆盖事故同款动作类；undo 防护与同族一致（前置快照已强制）。
- 快照/校验脚本暂存 .codex-tmp/（gitignored），manifest 持久副本在各快照目录内。

## 提交物清单

- WORKFLOW 条款：.scratch/architecture-recovery/WORKFLOW.md §4.4 + §7-5 升级（gitignored，快照保护）
- AGENTS.md：§6 禁止事项新行 + §7「流程产物持久化」条
- docs/backlog/B12：文末追加完成记录
- docs/process/ 沉淀区（受版本控制）：README.md、WORKFLOW.md、round3-spec.md、round3-README.md、reports/12-workflow-artifact-persistence.md
- .scratch 产物：issues/12 勾选、README 状态表、本报告

## 遗留（交大脑）

- issues/handoffs/prompts 等单票工作文件按条款不逐份沉淀，依赖动栈前快照轨；如需全量沉淀可扩 docs/process/。
- 仓库外快照目录无版本控制，机器级灾难（盘损）不在本策略防护范围。
- 快照/校验脚本若需长期复用，建议大脑裁决是否转正为 scripts/ 维护脚本（本轮按临时脚本纪律用后清理）。
- 轮次收口时建议合并 round2 归档恢复注记：WORKFLOW.md 曾灭后重建，现已有 docs/process/ 永续副本。
