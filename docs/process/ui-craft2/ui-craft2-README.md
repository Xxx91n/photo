# README — ui-craft2 轮（MangoDisk 观感级复刻）

> 决策数据源 decision-ledger.md（D-001~D-007 + A-001~A-005）；spec.md §8 三段覆盖核验通过、无去向清单=空（2026-09-13）。

## 波次表（全串行，从 issue Blocked by 推导，无新造顺序）

| 波次 | 票 | slug | Blocked by | 状态 |
|---|---|---|---|---|
| W1 | 01 | visual-standard-v2 | — | **ACCEPT**（首脑复核 2026-09-14，双轨一致/首件 8a9dea9/4 词条 verbatim/ADR0067+v2.0 实物验证；T-3 张力登记待裁定，不阻塞 W2） |
| W2 | 02 | rounded-window-shell-tokens | 01 | **ACCEPT**（首脑复核 2026-09-14：双轨 13603B 逐字一致；ead97bb 实物验证——显式 Round+守卫 R1 注释+禁读回断言、不动项零 diff、tokens 对照表逐项吻合（Radius 4/6/8/12、Elevation #12/#2E 两档、Layout 17 枚、Dracula/Nord 深档纠偏）、静态门禁复演 5/5 平衡+无 BOM 纯 LF+FullScreen 判省依据成立+既有断言无钉旧值；R3 侧栏默认宽 200vs240 呈报待大脑裁定） |
| W3 | 03 | nav-capsule-tri-state | 02 | **ACCEPT**（首脑复核 2026-09-14：双轨 18265B 逐字一致；uur+lwm 实物验证——槽位零残留、hover 中性胶囊无前景 setter、active 实心+深字+SemiBold+pointerover 锁、五主题两新刷、对比度七行独立复算全数一致（5.00–7.79 过 AA，nord/dracula 实解）、守卫 5Fact 健全+旧版必红逻辑成立、8/8 XAML 平衡无 BOM 纯 LF、D-008 含用户原话；账本偏差 1 项已补落地注记：NavItemHover@0.7 vs 草拟 NavHover@0.55；01 分支因 pull 呈 conflicted 移交大脑） |
| W4 | 04 | page-config | 03 | **ACCEPT**（首脑复核 2026-09-14：双轨 15104B 逐字一致；xzs/mkm/yox 三提交实物验证——页壳 58/22/右操作位、内容 1160 居中、4 组 group-label 出卡+grouped 卡零内边距、离轨值独立复算 0/24 与报告一致、缺陷修复×2 IsVisible 联动在位、守卫改造1/新增2/保留5 断言体健全非恒绿、settings-card 存量供给 3 处不失效、3/3 XAML 平衡无 BOM 纯 LF、规范 v2.3+取证误差订正留痕；新增张力 T-4/T-5 登记待裁定，不阻塞 W5） |
| W5 | 05 | page-logs | 04 | **ACCEPT**（首脑复核 2026-09-15：双轨 21220B 逐字一致；vqw/yrs/qps 三提交实物验证——LogsPage 四层骨架（页壳58/36裸工具条去NavBackground/行高44/徽章RadiusXl胶囊）、E2 空态（52无底色定位盒+空因双文案随 LogLevelFilterActive 切换 E5）、MapEventStyle emoji 前缀 6 处清零、AppTheme +empty-title/desc/icon 三 class、+EmptyDescFontSize=12、locale ×10 双 key 齐；离轨值独立复算 Spacing0/MP25处全ramp离轨0 与报告一致、XAML 3/3 平衡无BOM纯LF、守卫 10Fact（改造2去emoji/新增2页壳+E2/保留13）断言体健全非恒绿、规范 v2.4+取证订正留痕；报告§0「保留12」与§4表实数/结论/提交信息「13」自相矛盾1处——登记不追责；裁量项a/b（级别过滤ComboBox非分段胶囊/行高44/右操作位判省）+atomcode额度降级gh api 登记待裁定，不阻塞 W6） |
| W6 | 06 | page-rules | 05 | **ACCEPT**（首脑复核 2026-09-15：双轨 19689B 逐字一致；ont/qtt/wnl 三提交实物验证——RulesPage 五层骨架（页壳58+右操作区等宽组迁入/横幅 RadiusMd/36裸工具条/矩阵 RowHeight44+外层 ScrollViewer 撤除/E2 成因二分随 SearchFilterActive）、locale 10/10 增二删一且扁平键集 201 键零分歧独立复算通过、离轨值逐视图明细 27 处与报告逐项一致零离轨、守卫 12 Fact（新增2断言体健全R1齐备/保留15/改造0）、AppTheme/DesignTokens 零触碰声明属实、XAML平衡无BOM纯LF、规范 v2.5；§9-a 既有红呈报属实——见 W4-fix；裁量项 b/e/f/g 登记待裁不阻塞） |
| W4-fix | 04 | page-config（返工轮） | 06 触发 | **可开工（frontier）**——票 04 引入 `Pages≤220` 守卫对 ConfigPage 234 行 HEAD 即红（云端 CI 必红），票 06 报告 §9-a 呈报、票 06 复核确认；票 04 复核存在检测遗漏（本大脑未复演该守卫，已登记）。修复启动器 prompts/04-fix-page-config-rerun.md（拆分 4 分组子件，禁降标）。全串行纪律下先于 W7 派发 |
| W7 | 07 | page-service-manager | 06 | 待开工（04-fix 收口后放行） |
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
- 票 04（2026-09-14）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS（issue 验收 3/3+D-001 承接注）。独立复算离轨值与报告完全一致（Spacing 字面量 0 / Margin·Padding 24 处全在 ramp / 离轨 0）。取证误差订正按纪律留痕（附录 D 原「卡片 p-6=24」经 md-settings-group.vue 源码复核改判「卡 0 + 行 7/14」，旧值标 revised 未静默改向）。过程呈报 3 项：①ConfigPage 行数报告记 234 实测 233（尾行口径）②报告 §10 记「必读清单 7 份」而启动器模板为 8 份（第 7/8 项 atomcode-verdict/adr0050 系票 02 专属前提，对本票非必需，口径不追责）③阻塞核查自述属实（03-report 双轨我上轮已独立验证一致）。**新增张力 T-4**（MangoDisk 设置行 40px 图标列未引入——本票反向删唯一孤图标使 15 行统一，全行引入属跨页设计变更待裁）与 **T-5**（字号阶梯：基准行标题 13/节标题 15 无对应 token，本票仅先补 page-title22/group-label12，全面对齐属全局决策）登记待裁定，二者均不阻塞 W5。
- 票 05（2026-09-15）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS（issue 验收 3/3+D-001 承接注）。独立复算离轨值与报告完全一致（Spacing 字面量 0 / Margin·Padding 25 处全在 ramp / 离轨 0 / LogsPage 内 7 处）；emoji 清零全仓 grep 零残留、MapEventStyle 8 分支纯本地化 key、VM LogLevelFilterActive 联动+热重载回填缺口修复、locale 10/10 双 key、守卫 10 Fact（改造 2 去 emoji/新增 2/保留 13）断言体健全非恒绿、MainWindow.axaml.cs BOM 既有性独立核实（HEAD~3 即 efbbbf，未越权改）。过程呈报 4 项：①报告 §0/§1 记「保留 12」与 §4 表实数/结论/提交信息「保留 13」自相矛盾（尾数簿记偏差，守卫实物无缺失，不追责）②LogsPage 行数报告记 83 实测 82（尾行口径）③atomcode 5h 额度耗尽降级 gh api 只读源码直读（skill 明列唯一例外路径，串行护栏未开新调研，§9-d 如实呈报）④裁量项 a/b（级别过滤 ComboBox 承载 vs MangoDisk 分段胶囊、日志行高 44、页头右操作位判省）登记待大脑裁定，不阻塞 W6。
- 票 06（2026-09-15）：首脑复核 **ACCEPT**——声明→证据→结论对照全 PASS（issue 验收 3/3+D-001 承接注）；离轨值 27 处逐视图明细（MainWindow7/Config2/Logs7/Rules6/SM4/NavButton1）独立复算与报告逐项一致；locale 扁平键集 201 键 10 语言零分歧+rules.panel.title 清除核证；守卫 12 Fact 计数与报告一致（改造0/保留15/新增2/杀0）。**§9-a 既有红呈报属实并升格为返工触发**：`Pages_Should_Exist_And_Not_Exceed_220_Lines`（MainWindowShellSourceTests.cs:37，LineCount=Split('\n').Length 口径故 233 行文件计 234）对 ConfigPage HEAD 即红——票 04 整页重写引入。**本大脑票 04 复核检测遗漏登记**：当时未复演该守卫（报告行数口径疑问仅按「微漂不追责」处理，未追到守卫阈值语义），致红漏检两波；已按返工条款重发修复启动器 prompts/04-fix-page-config-rerun.md。过程呈报 2 项：①RulesPage 行数报告记 100 实测 99（守卫口径 100，与报告自洽）②en/zh-CN locale diff 行数与他语言不同（JSON 嵌套结构差异，键集对等已独立复算通过）。
- frontier：**W4-fix（票 04 返工轮）= 当前可开工**（全串行，先于 W7）；04-fix 收口复核后放行 **W7 = 票 07**（page-service-manager）。T-4/T-5/票 05 裁量项 a·b/票 06 裁量项 b·e·f·g 均为全局裁定面，不阻塞施工，可攒至收口统一裁。
