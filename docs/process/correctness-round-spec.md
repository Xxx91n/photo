# Spec — correctness-round（锐评处置轮）

> **来源**：grill 裁定账本 `.scratch/correctness-round/decision-ledger.md`（D-001~D-008）+ A 系对账闸 `.scratch/architecture-recovery/decision-ledger.md`（A-001~A-009，摩擦点原文引自 `.codex-tmp/锐评.txt`）。
> **数据源纪律**：本文全部结论以两本账本为唯一来源；证据细节（九条复核 file:line、atomcode 调研原文）以指针引用，不在此复制——ctx 索引 source：`correctness-round fact-check 586a29a（锐评九条复核）`、`atomcode-backup-disambig`、`atomcode-ipc-auth`。
> **方法论**：锐评行号系 2026-09-12/13 时点禁止直接采信；审美工程与正确性工程两线隔离（锐评票=真测试+编译验证）。

## Problem Statement

覆盖：A-001、A-002、A-003、A-004、A-005、A-006、A-007、A-008、A-009（全部，逐条原文见 A 系账本）。

锐评对项目提出九个摩擦点：六条功能正确性（格式黑洞挂死/空配置落 CWD/排除清单 fail-open/IPC 无认证/性能三件/备份互覆盖）与三条工程文化（source-lint 占比/枚举黑名单漏报/宣称-实现脱钩）。2026-09-15 对 main@586a29a 逐条实物复核：九条全部维持原判（仍存在），无一被后续提交修复或驳倒。用户裁定：六条功能指控全部本轮修（D-004）；工程文化按行为补强优先+点状升级处置（D-003）。裁定标尺 = 五条不变量（D-002）：①fail-closed ②用户数据零意外丢失 ③最小攻击面 ④驻守可观测 ⑤宣称-实现一致；每条修复须映射至少一条不变量，不映射或冲突的方案不予采纳。

## Solution

覆盖：A-001~A-009（总览，方案细节见 Implementation Decisions 各节）。

六票串行修复（D-008）：P0 数据面（格式黑洞+宣称收敛+对账断言）→ 配置面（空配置兜底+排除清单真通配）→ 备份面（镜像相对路径+内容分歧旁路版本化）→ IPC 面（认证分层+承诺改写+部署配套）→ 驻守稳定性（健康检查探针+撤 sync-over-async+缓冲读评估）→ 收口（黑名单点状升级+对账收口+整轮审计）。顺序=优先级；一票一 GitButler 分支；首脑复核协议沿用 ui-craft2（逐条实物验证/对照表/账本核对/违规呈报/README 登记）。

## User Stories

视角来源：D-002 语境（非专业用户 + 无人值守长期运行，弱网/低配/无人工干预为默认前提）。

- 作为特殊人群用户，我投入常见照片后**程序不会卡死、也不会静默跳过我以为在处理的文件**——每个跳过都在审计里可见（A-001/A-004①④）。
- 作为特殊人群用户，我的**原始照片备份永不互相覆盖、也永不静默丢失**——跨目录同名各有备份，同文件重处理保留最新（A-006②）。
- 作为无人值守场景的运行者，**失败必须可见**：审计/日志记录每个异常，不允许「看起来在跑实际没在跑」（A-005④）；**我的审计清单不被无认证端点吐出**（A-004③）。

## Implementation Decisions

### ID1 格式覆盖与挂死链（覆盖 A-001、A-009）

挂死链修复独立于扩容节奏：未知格式一律立即跳过 + wipe_skipped_unknown 落审计事件，永不挂。宣称收敛：allowed_extensions 收窄到当前映射面（jhc 死条目顺带清理），README/文档数字同步，不支持格式明示。对账断言（allowed⊆mapped 集合差=0）随本节入 CI。同义反复 fake（AutoEmitTaskDone）替换为协议级 fake。长尾 RAW 维持不支持且明示；RAW 永不 -all= 为扩容硬约束。（D-005/D-008.1）

### ID2 配置兜底与排除清单（覆盖 A-002、A-003）

AppConfigLoader 对 quarantine/audit 空串回落 DefaultPaths（对齐 Backup.Suffix 先例）；AppConfigValidator 拒空并给出可读错误。MatchesPattern 实现真通配（* / ?）；validator 对不可能匹配的模式告警；不识别≠静默。（D-008.2）

### ID3 备份布局与冲突策略（覆盖 A-006）

镜像相对路径布局：备份根下按源文件相对监控根的子路径镜像目录；多监控根共享 backup 目录时以根目录名做一层前缀消歧。冲突规则：目标槽位存在时内容相同→跳过；内容不同→写 名字.时间戳.扩展名 旁路版本；绝不静默覆盖。「同文件重处理保留最新」由同槽位+内容相同跳过承接。工程细节：源路径无法归位监控子树时回退文件名+路径短哈希兜底；防穿越复用 WatchPathFilter 边界判定；符号链接拒绝；备份写入临时文件+原子改名；Windows 长路径/结尾点空格规范化；旁路版本受 ADR 0006 retention 兜底。CONTEXT.md Backup 词条随票精确化。（D-006）

### ID4 IPC 认证分层（覆盖 A-004）

前置闸：单文件冒烟实验（IncludeNativeLibrariesForSelfExtract true/false 各一次）留证——CurrentUserOnly 若复现 ADR 0035 坑，Windows 降级 P/Invoke 或显式 SetAccessControl fallback。A 主机制：Windows PipeOptions.CurrentUserOnly + FirstPipeInstance（仅 ListenAsync 预建的首实例；accept 后预建下一实例的结构不得加标志）；Unix socket 文件模式收紧（background 0600 / service 0660+组边界，to-spec 定值）+ 对端凭据校验。B 纵深：Unix 必落（SO_PEERCRED/getpeereid，accepted socket 上查）；Windows 按 to-spec 实验定（可能仅 DACL——DACL 已内核强制且两条 B 路径均有缺陷）。C（握手令牌）不做；AGENTS.md 承诺改写：「认证依赖 OS 身份边界（文件权限+对端凭据）+ 首实例防抢占；同 OS 用户进程不在防御范围内」。配套：Linux RuntimeDirectoryMode=0750 + GUI 用户组供给；UiSingleInstanceIpc 同享保护。.NET 10 无 bind 后 0600 自动加固，文件模式由传输层显式负责。（D-007）

### ID5 驻守稳定性（覆盖 A-005）

(a) 健康检查改轻量探针 + _startLock 持锁修复；(c) 撤 accept 循环内 sync-over-async；(b) 缓冲读随票评估——牵动 ADR 0057 裁决面可降级，降级须回报裁定不得静默。（D-008.5）

### ID6 测试与守卫策略（覆盖 A-007、A-008、A-009）

source-lint 守卫存量一件不删不缩；每条采纳的功能修复票必须自带「能抓住原 bug 的行为测试」（真测试，非读源码文本断言）；守卫防回潮措辞/结构，行为测试防程序做错事，二者共存。点状升级：CompositionRoot 黑名单补 new ServiceModeController( / new AuditTailService( / new ConfigFileWatcher( 或改结构性断言，不做全局 AST/分析器级整改；升级后守卫须失效即红预演留证。宣称-实现机器对账断言入 CI（ID1 落地、本节收口核验）。（D-003）

### ID7 票结构与开工前置（覆盖 A-001~A-009 的执行编排）

六票串行、顺序=优先级、一票一 GitButler 分支（WORKFLOW §4.2）；各票禁夹带他票面；扩容批禁入首期（收口后按批立票，每批=一票含真实 exiftool 验证路径）；开工前置闸 = gitbutler/workspace 重锚定 main（WORKFLOW §4.4 快照先行，等用户明令）；CI-only 承接：真测试走 CI，CI 编译为写盘有效性唯一凭证。（D-008）

## Testing Decisions

覆盖：A-001、A-002、A-003、A-004、A-005、A-006（各票行为测试）+ A-007/A-009（守卫共存与对账）。

每票验收 = ①行为测试在场且能抓住原 bug（挂死票喂未映射格式断言不挂死+可见跳过；备份票双子目录同名两备份都在；IPC 票跨用户拒绝+首实例抢占 fail-fast）②对账断言（票 1 起持续在场）③CI 绿（build 0 错 + 测试通过）④既有守卫不红（失效即红预演仅限新增守卫）⑤双轨报告（reports/NN-report.md 主本 + docs/process/reports/ 定稿沉淀）⑥首脑复核协议。

## Out of Scope

- 高置信常见格式扩容批（D-005 第二步）——首期六票禁入，收口后按批立票（D-008.8）。
- 握手令牌纵深（D-007.4）——威胁模型外的同用户对手；未来 LocalSystem 服务↔多用户 UI 形态出现时再议。
- 纯文档改写作为认证方案单独成立（D-007）——仅作为 A+B 落地后的承诺口径修正。
- source-lint 全局收缩 / AST 级守卫整改（D-003 约束）。
- backlog 清账（15 项全局裁定+实机目检回填）、票 08 CI-only 违规追认、g0 处置——锐评轮收口后承接（D-001.5）。
- 栈操作：gitbutler/workspace 重锚定属用户明令事项，不随票执行（D-008.9）。

## Further Notes

- 必读材料：`.codex-tmp/锐评.txt`（摩擦点原文）、`.scratch/correctness-round/decision-ledger.md`（D 系裁定）、`.scratch/architecture-recovery/decision-ledger.md`（A 系对账）、`.scratch/architecture-recovery/WORKFLOW.md`（§4.2/§4.3/§4.4 纪律）。
- ADR 落点：随对应施工票收口（沿用仓库现行惯例；候选：票 3 备份布局、票 4 IPC 认证、轮收口轮总结 ADR）。
- 波次表：`.scratch/architecture-recovery/README.md`（从 issue Blocked by 推导，六票全串行）。
