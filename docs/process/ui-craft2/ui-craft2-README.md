# README — ui-craft2 轮（MangoDisk 观感级复刻）

> 决策数据源 decision-ledger.md（D-001~D-007 + A-001~A-005）；spec.md §8 三段覆盖核验通过、无去向清单=空（2026-09-13）。

## 波次表（全串行，从 issue Blocked by 推导，无新造顺序）

| 波次 | 票 | slug | Blocked by | 状态 |
|---|---|---|---|---|
| W1 | 01 | visual-standard-v2 | — | **ACCEPT**（首脑复核 2026-09-14，双轨一致/首件 8a9dea9/4 词条 verbatim/ADR0067+v2.0 实物验证；T-3 张力登记待裁定，不阻塞 W2） |
| W2 | 02 | rounded-window-shell-tokens | 01 | **ACCEPT**（首脑复核 2026-09-14：双轨 13603B 逐字一致；ead97bb 实物验证——显式 Round+守卫 R1 注释+禁读回断言、不动项零 diff、tokens 对照表逐项吻合（Radius 4/6/8/12、Elevation #12/#2E 两档、Layout 17 枚、Dracula/Nord 深档纠偏）、静态门禁复演 5/5 平衡+无 BOM 纯 LF+FullScreen 判省依据成立+既有断言无钉旧值；R3 侧栏默认宽 200vs240 呈报待大脑裁定） |
| W3 | 03 | nav-capsule-tri-state | 02 | **ACCEPT**（首脑复核 2026-09-14：双轨 18265B 逐字一致；uur+lwm 实物验证——槽位零残留、hover 中性胶囊无前景 setter、active 实心+深字+SemiBold+pointerover 锁、五主题两新刷、对比度七行独立复算全数一致（5.00–7.79 过 AA，nord/dracula 实解）、守卫 5Fact 健全+旧版必红逻辑成立、8/8 XAML 平衡无 BOM 纯 LF、D-008 含用户原话；账本偏差 1 项已补落地注记：NavItemHover@0.7 vs 草拟 NavHover@0.55；01 分支因 pull 呈 conflicted 移交大脑） |
| W4 | 04 | page-config | 03 | **可开工（frontier）** |
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
- 票 03（2026-09-14）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS（issue 验收 5/5+D-001 承接注）；T-3 已裁定入 D-008（含用户原话：T-3a=C 折中/T-3b=hover 中性），流程合法；WCAG 对比度独立复算七行零偏差；守卫断言体健全非恒绿。过程呈报 3 项：①账本-实现落地偏差（D-008 草拟 NavHover@0.55 vs 实现 NavItemHover@0.7）报告未登记，大脑已补落地注记；②uur 提交信息记报告 17372B vs 实际入库 18265B（定稿后增量，提交内容正确，仅信息数字漂移）；③报告记 nav-action 裸 Button 行号 89/94 vs 实物 90/96（微漂不追责）。
- **移交大脑**：票 03 窗口 but pull（§4.4 快照 20260914-094037 ZERO-LOSS 100/100 先行）后 `ui-craft2/01-visual-standard-v2` 车道呈 conflicted（上游 b77a970/cbcc852 + 大脑收尾件 kzx）——票 03 按纪律不代解析；属票 01 领地+大脑件，处置（rebase 重放或 land 时解析）待大脑/用户裁定，不阻塞 W4（03 分支自身干净）。
- frontier：**W4 = 票 04**（page-config）。T-3 已裁定，无开工前置阻塞。
