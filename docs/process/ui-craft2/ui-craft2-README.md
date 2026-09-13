# README — ui-craft2 轮（MangoDisk 观感级复刻）

> 决策数据源 decision-ledger.md（D-001~D-007 + A-001~A-005）；spec.md §8 三段覆盖核验通过、无去向清单=空（2026-09-13）。

## 波次表（全串行，从 issue Blocked by 推导，无新造顺序）

| 波次 | 票 | slug | Blocked by | 状态 |
|---|---|---|---|---|
| W1 | 01 | visual-standard-v2 | — | **ACCEPT**（首脑复核 2026-09-14，双轨一致/首件 8a9dea9/4 词条 verbatim/ADR0067+v2.0 实物验证；T-3 张力登记待裁定，不阻塞 W2） |
| W2 | 02 | rounded-window-shell-tokens | 01 | **ACCEPT**（首脑复核 2026-09-14：双轨 13603B 逐字一致；ead97bb 实物验证——显式 Round+守卫 R1 注释+禁读回断言、不动项零 diff、tokens 对照表逐项吻合（Radius 4/6/8/12、Elevation #12/#2E 两档、Layout 17 枚、Dracula/Nord 深档纠偏）、静态门禁复演 5/5 平衡+无 BOM 纯 LF+FullScreen 判省依据成立+既有断言无钉旧值；R3 侧栏默认宽 200vs240 呈报待大脑裁定） |
| W3 | 03 | nav-capsule-tri-state | 02 | **可开工（frontier；开工前置=T-3 张力裁定）** |
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
- 票 02（2026-09-14）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS（issue 验收 4/4+D-001 承接注）；上游权威独立复核（Avalonia 12.1.1 Win32Properties.cs 源码直读：附加属性/默认 Default/<22000 ignored 与票面一致）；过程呈报 3 项：R6 启动器必读清单第 3 项字面 `undefined`（大脑模板缺陷，已由大脑修复 8 份 prompts）、R7 同值 setter replace 误中自愈（终态核验无残留）、报告 §3.2 措辞"任务书"实为 prompts/（口径偏差不追责）；R3（侧栏默认宽 200 vs 基准 240）呈报待大脑裁定；T-3 维持待裁定（票 02 未碰 nav，零立场）。
- frontier：**W3 = 票 03**（nav-capsule-tri-state）。开工前置：**T-3 张力裁定待用户拍板**（D-003 主色实心反白 vs MangoDisk 实物 accent 胶囊+3px pill+600 字重；建议=维持 D-002/D-003 + 吸收几何细节）。
