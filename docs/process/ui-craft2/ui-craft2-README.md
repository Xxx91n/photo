# README — ui-craft2 轮（MangoDisk 观感级复刻）

> 决策数据源 decision-ledger.md（D-001~D-007 + A-001~A-005）；spec.md §8 三段覆盖核验通过、无去向清单=空（2026-09-13）。

## 波次表（全串行，从 issue Blocked by 推导，无新造顺序）

| 波次 | 票 | slug | Blocked by | 状态 |
|---|---|---|---|---|
| W1 | 01 | visual-standard-v2 | — | **ACCEPT**（首脑复核 2026-09-14，双轨一致/首件 8a9dea9/4 词条 verbatim/ADR0067+v2.0 实物验证；T-3 张力登记待裁定，不阻塞 W2） |
| W2 | 02 | rounded-window-shell-tokens | 01 | **可开工（frontier）** |
| W3 | 03 | nav-capsule-tri-state | 02 | 待开工 |
| W4 | 04 | page-config | 03 | 待开工 |
| W5 | 05 | page-logs | 04 | 待开工 |
| W6 | 06 | page-rules | 05 | 待开工 |
| W7 | 07 | page-service-manager | 06 | 待开工 |
| W8 | 08 | toast | 07 | 待开工 |

每波恰好一票（共享文件串行纪律 + spec §4 全串行决策 D-006）。

## 工件索引

- spec: spec.md ｜ issues/ 8 份 ｜ handoffs/ 8 份（含通用纪律段）｜ prompts/ 8 份启动器 ｜ reports/（施工时生成）
- 上一轮归档: .scratch/_archive/ui-craft-2026-09-13/ ｜ 本轮 docs 副本: docs/process/ui-craft2/

## 路径偏差声明

- goal 模板写 {architecture-recovery}，本轮实际落 .scratch/ui-craft2/（grill 轮 slug 既定，A 系列登记在 ui-craft2/decision-ledger.md）；WORKFLOW 仍在 architecture-recovery/ 原位引用。
- handoff 任务书（next-round.md）T3 页面清单与实物差异按 A-001 收敛：无"主页/设置弹层"票。

## 复核登记

- 票 01（2026-09-14）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS；过程呈报 2 项（--allow-merged flag 使用、分支锚定旧栈顶）待用户追认与否，不阻塞；T-3（nav active/hover 定值：D-003 vs MangoDisk 实物 accent 胶囊+3px pill+600 字重）票 03 施工前须裁定。
- frontier：**W2 = 票 02**（rounded-window-shell-tokens）。
