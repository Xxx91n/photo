# ADR 0058 — 架构恢复第二轮收口（backlog B01–B08 清零）

- 日期：2026-09-01
- 状态：已收口（push 待用户指令）
- 上游：ADR 0056（六票）、docs/backlog/（8 票立票，用户 2026-09-01 裁决全部立项）

## 8 票结果一览

| 票 | 标题 | 裁决/结果 | 关键证据 |
|---|---|---|---|
| 01 | Smoke 测试无超时治理（B02） | 落地 | 无参 WaitForExitAsync = 0；杀进程改 tree-kill（GetProcessesByName = 0）；test.runsettings（MaxCpuCount=1 + TestSessionTimeout）+ xunit.runner.json（longRunningTestSeconds=120）；修复后 3 连跑 Core 145 + Integration 251 全绿无挂死 |
| 02 | publish 冒烟超时预算（B01） | 参数化（三选一） | 环境变量 PHOTOPRIVACY_PUBLISH_TIMEOUT_SECONDS 默认 900s；FromSeconds(300) grep = 0；run5 全量 251/251 |
| 03 | FSW 缓冲 + Error 恢复（B03） | 部分原有 + 新修复 | 64KB 自 37837d5 已生效（0056 #3 措辞已勘误）；本票真修复：连续 Error 的并发 Stop/Start 合并守卫（_recoveryGate + _recoveryQueued）；400 文件压测 0 丢事件 |
| 04 | _backfillSucceeded 锁收敛（B04） | 落地 | 读经加锁属性、写移入 EnqueueBackfillLines 的 _gate 段（与交付原子）；backfill 9 测试 + AuditTail 15/15 绿 |
| 05 | IPC 传输复用评估（B05） | **不改（测量裁决）** | ADR 0057：新建传输净开销 0.03–0.30ms（噪声区）、1Hz 心跳 ≈0.1% 单核；复用需服务端帧化+协议升 V，不值；触发重估条件已列明 |
| 06 | UseShellExecute 守卫白名单（B07） | 落地 | 白名单（false / ServiceManager needElevation）+ ClassifyUseShellExecuteLine 收口 + 负向自证 8 样本（3红5绿）+ runas 配对锁 |
| 07 | source-lint helper 去重（B06） | 落地（附尾巴） | SourceLint.cs 单一实现 + 自证 9 用例；19 文件改调；私有副本/硬编码 D: 路径清零；窄门禁实测 55/55 |
| 08 | ADR 0053 errata（B08） | 落地 | ADR 0053 文末追加 Errata 段（+14/-0，历史正文不动）；CONTEXT.md 交叉引用同步 |

## 终门禁（单槽串行，证据：.scratch/architecture-recovery/closeout2/final-gates.log）

- `dotnet build PhotoPrivacy.sln`：0 错误
- Core.Tests：145/145
- IntegrationTests：260/260（较一轮基线 242 净增 18 = 二轮新增守卫/自证/缝测试）

## 重要事故与处置（供后鉴）

1. **.scratch 全树蒸发**：收口期执行 `but move`（票07 遗留补提交所需栈序调整）→ 冲突 → resolve/undo 后，`.scratch/`（含一轮 _archive）被整体清空。代码零损失；流程产物从会话上下文重建。教训已记 WORKFLOW §7：流程文件要尽早沉淀 docs/，动栈前快照。
2. **票07 遗留 nn/ovl 补提交放弃本地栈内调整**：强行 move 引发真实三方内容冲突（6 文件双方语义改动），已回退保持原拓扑。处置：未提交区两文件（ProcessHygieneGuardTests/DesignSystemTests 的 helper 迁移）保留on disk，**远端合并完成后在 main 上落一笔收口提交**。
3. 分支拓扑（实物栈）：07→04backfill→05log→06svc→06guard→02launcher→04ipc；01smoke→02publish；03fsw；08→closeout-docs；05ipc-eval、backlog-tickets 独立。

## 遗留观察项（新一轮 backlog 评议，未自动立票）

- smoke.ps1 仍走 `dotnet run`（管道排空已兜底；DLL-first 属另票）
- ServiceManager.cs:126 同步 `WaitForExit()` 无超时（票01 登记，grep 确认存在）
- PublishApp 冒烟留给失败输出捕获（run4/02 域观察）
- 跨平台 UDS 传输测量未做（05 结论按 1Hz 调用频率推导，跨平台成立）
- 文档治理流程改进：流程产物持久化策略（见事故 1）

## 决策

- 本轮所有"不改"裁决（05）以测量数据为准，防止无证据优化。
- ADR 0056 #3 措辞已就地勘误（保留原文删除线）。
