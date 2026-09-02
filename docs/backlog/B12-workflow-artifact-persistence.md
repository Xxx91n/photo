# B12 — 流程产物持久化策略

- **优先级**: 高　**来源**: ADR 0058 事故 1

## 问题与验收

.scratch 在 GitButler move/undo 中全树蒸发一次（实爆）。方案候选：关键流程文件沉 docs/；或动栈前强制快照（本轮已手工快照到仓库外）。验收：策略成文 + 至少一轮流程实测无丢失。

## 完成记录（2026-09-02，票12）

- 双轨策略已成文：.scratch/architecture-recovery/WORKFLOW.md §4.4（轨1 docs/process/ 沉淀 + 轨2 动栈前仓库外快照，触发时机逐条列明）；AGENTS.md §6/§7 同步。
- 实测：6 快照 + 6 哈希校验全 ZERO-LOSS（含 move 真实冲突 + resolve 处置链）；演练仅动自建演练分支，他票在途改动原样健在。报告：.scratch/architecture-recovery/report-12-workflow-artifact-persistence.md（受控副本 docs/process/reports/）。
- 门禁：build 0 错误 0 SCS（纯文档票）。
