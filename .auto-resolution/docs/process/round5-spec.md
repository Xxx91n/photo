# Spec — 架构恢复第五轮（第四轮收口后宏观评估落地）

来源（架构报告）：2026-09-03 宏观架构调查（codegraph CLI + 三个只读子代理 + atomcode 三引擎联网调研）产出的《架构恢复评估》。评估判定架构「基本清晰但局部残留」，并定位权威配置两个死字段、规则面板双轨孤岛、UI 根目录未层化、CONTEXT.md 九处漂移。

## Problem Statement

第四轮收口后，宏观评估发现四处明确残留：(1) 权威配置读写往返不对称——`ui.sidebar_width` 读不进也存不回（用户拖拽宽度重启即丢）、`backup.retain_days` 读得到存不回（UI 保存即回落默认 30）、`audit` 段 sample 与代码默认取值相反；(2) 规则面板与真实擦除引擎双轨孤岛——专业模式勾选对 ExifTool 实际命令零影响；(3) UI 根目录 23 个 .cs 未层化、含死代码；(4) CONTEXT.md 作为权威术语表存在 Endpoint Ownership 缺失等九处漂移/内部矛盾。

## Solution

五张垂直切片票：配置 round-trip 对称性止血（票17）；规则存储完整往返（票18）→ 规则引擎单一真相源（票19）；UI 根目录层化 + 死代码清理（票20）；CONTEXT.md 三层一致性收口（票21，落于代码收敛之后）。

## User Stories

1. As a 用户, I want 拖拽侧边栏宽度后重启仍能恢复, so that UI 偏好不丢失。
2. As a 用户, I want 修改备份保留天数后保存不丢失, so that 清理策略按我的设置执行。
3. As a 维护者, I want 配置读写往返对称且由测试锁定, so that 未来新增字段不再悄悄变死字段。
4. As a 专业用户, I want 勾选「保留 ICC / 删 MakerNotes」真实改变清理命令, so that 规则面板不是摆设。
5. As a 维护者, I want 擦除命令只有一处真相源, so that 硬编码表与 UI 展示表不再漂移。
6. As a 开发者, I want UI 服务类归位到 Services/ 且死代码删除, so that 目录结构反映真实分层。
7. As a 新人/Agent, I want CONTEXT.md 术语表与代码/ADR 一致, so that 权威术语表可信可导航。

## Implementation Decisions

- 票17 采用最小止血：仅补 DTO 字段 + round-trip 测试，不做 double-DTO 架构重构（中远期，超出本轮）。
- 票18/19 为 expand-contract：先修存储往返（18），再让引擎由规则驱动并删复制表（19），每步可验证。
- 票20 为纯移动/删除：不动行为，只移文件 + 抽 contracts + 删死代码。
- 票21 是文档追平：修正 CONTEXT.md 与 ADR 措辞，反映代码现状；落于 17/19 之后以反映最终状态。
- 版本控制遵循 WORKFLOW §4.2；动栈前遵循 WORKFLOW §4.4 快照。

## 研究输入（atomcode 完整提示词）

子代理在做设计裁决（尤其票19 规则引擎心智模型、票17 配置 SSOT）时，用 atomcode-research 跑下面这条完整提示词（一次一条、串行，见 atomcode-research skill 的串行护栏）：

全景调研并给出架构决定：回顾本项目 docs/adr 全部架构决策记录与 CONTEXT.md 领域术语表里已确立的心智模型，结合学术背景与目前工业界成熟落地的桌面应用架构心智模型，判定：(1) 工业界成熟落地的桌面应用架构心智模型里哪个最适合本项目继续收敛；(2) 本项目现有心智模型里还有哪个关键缺口最需要补充；(3) 给出优先级与落地建议，重点标注工业级成熟模板而非重复造轮子。

（此提示词已由大脑用 atomcode 跑过一次，结论见 Further Notes。）

## Testing Decisions

- 单槽串行门禁不变（test.runsettings MaxCpuCount=1）。
- 票17：AppConfigJson/AppConfigLoader round-trip 测试覆盖全部字段（含 sidebar_width/retain_days），并断言 sample 与 AppConfig.Default 关键取值一致。
- 票18：FormatRulesStore Save→Load 往返忠实（全逐组开关）。
- 票19：纯函数测试——给定规则集合 → WipeStrategyResolver 生成命令与勾选一致。
- 票20：编译 + 既有测试全绿（纯移动无行为变化）；删死代码后 grep 零引用。
- 票21：复用 SourceLint helper 锁 CONTEXT.md 关键术语存在/措辞。

## Out of Scope

- 不做 double-DTO 架构重构（单 DTO / 源生成器）——中远期，另立 backlog。
- 不引入 DI 容器。
- 不重排第四轮 8 分支合并（已 push，main=c80ae91）。
- 不做 UDS keep-alive 复用（ADR 0057 裁决不变）。

## Further Notes

- 波次由 issue 的 Blocked by 字段唯一推导，见 README.md 波次表。
- backlog 正式立票（docs/backlog/B18–B22）建议随各票提交一并完成。
- 大脑已跑的 atomcode 调研结论（配置权威）：行业成熟模型 =「Schema 在代码、默认在代码、模板从 Schema 生成、用户文件只存差异、读写模型分离、往返靠测试兜底」；本项目手写双 DTO + 手写 sample 是漂移温床，与票17 直接对应。
- 最适心智模型（atomcode 对比矩阵，历史调研）：模块化单体（总纲，对应票20 Core 模块边界 + UI 层化）+ MVVM（UI 层，骨架已有）+ 守护进程/控制器（进程拓扑，0056/0057 已成熟）+ 吸收 Vertical Slice 内聚心智（对应票19 规则面板↔擦除引擎单真相源）；否决 Clean Architecture 全套（领域薄、单团队，四层仪式纯开销）与插件运行时（无第三方扩展场景）。本轮五票均落在该心智模型上，不引入新抽象。
- 一条新 atomcode 调研（本 spec「研究输入」完整提示词）已派发，若在途尚未落盘，子代理可用 ctx_search(source:"atomcode") 检索其结论。
