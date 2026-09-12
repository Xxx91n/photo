# ADR 0059 — 架构恢复第三、四轮收口

- 日期：2026-09-03
- 状态：已收口（8 分支合并进 main，待 push 指令）
- 关联：ADR 0056（架构恢复）、ADR 0057（IPC 传输 PERCALL）、ADR 0058（round2 收口）

## 背景

架构恢复工作流第三轮（票 09/09b/10/11/12）与第四轮（票 13/14/15/16）共 8 分支已全部完成并经大脑逐票复核（守卫源码、提交文件集、分支落位、栈拓扑、rg 抽查 + 门禁实跑）。本 ADR 记录合并决策、验证结果、交叉核对与三层文档一致性发现，以及遗留事项（待用户裁决是否立票）。

## 决策：合并顺序（base→tip 拓扑序，--no-ff）

8 分支按依赖拓扑 base→tip 逐支 `git merge --no-ff` 合并进 main：

1. arc-recovery/12-workflow-artifact-persistence（独立，09 依赖它）
2. arc-recovery/09-smoke-dll-first（锚定 12）
3. arc-recovery/16-release-readiness-full-capture（锚定 09）
4. arc-recovery/15-test-host-dotnet-run-purge（锚定 16）
5. arc-recovery/14-snapshot-scripts-promotion（锚定 15）
6. arc-recovery/10-servicemanager-syncexec-timeout（独立）
7. arc-recovery/11-uds-transport-measure（独立）
8. arc-recovery/13-uds-transport-guard（独立）

- 合并前 `but pull`（无新上游提交）。
- 零冲突；main tip = `35b8746`（8 个 merge commit）。
- 合并后 main 树与合并前 GitButler workspace（`ec46577`）`git diff` 为空——零内容丢失。

## 验证（构建闭环）

- `dotnet build PhotoPrivacy.sln`：0 错误、0 SCS（1006 条既有 CA 风格基线警告，非本票引入）。
- `dotnet vstest Core.Tests`：145/145 通过。
- `dotnet vstest IntegrationTests`：276/276 通过（单槽串行，test.runsettings MaxCpuCount=1）。
- 四票守卫 + 受影响测试（单槽）：22/22 通过。

## 交叉核对发现（reports ↔ README，非阻断）

| # | 矛盾 | 说明 |
|---|---|---|
| 1 | 票09 分支本地 renumber：`082efcb`→`93f4525`、`cb2e3d5`→`a21ad1c` | report-09/09b、report-16 正文与 commit message 仍引用旧哈希；origin 仍留旧哈希 |
| 2 | report-13 守卫头注释 `Ticket 13 (B14)` | 提交信息与 backlog 均 B17，头注释未回改 |
| 3 | report-16 文件计数 "2改+2新增" | 实物 1 改 + 3 新增 |
| 4 | docs/backlog/README.md 第四轮表缺 B14/B15/B17 | 仅 B16 一行；report-15 与 report-16 登记口径不一 |
| 5 | report-16 "B14=B15" 笔误 | 应为"票14=B15" |

## 三层文档一致性发现（CONTEXT.md ↔ docs/adr ↔ 代码）

- Stale Socket Cleanup：CONTEXT.md "无条件删除" 仅指 ListenAsync bind 前清理，与 ADR 0026 + 代码一致；未区分 DisposeAsync 属主条件删除，措辞歧义。
- Endpoint Ownership（票11+13 的 `ownsEndpoint` 属主清理）在 ADR 0057 已记载、代码已落地，但 CONTEXT.md 缺术语条目（缺口）。
- Hot Folder Guard：CONTEXT.md 称"非空绝对路径"，代码仅校验非空；`DefaultPaths.cs` 注释也夸大"rejects non-existent directories"。
- ADR 0056 遗留 #5、ADR 0058 两条遗留观察项（smoke dotnet run、跨平台 UDS 未测）已被第三/四轮兑现，标注过时。
- docs/adr/ 此前止于 0058，第三、四轮无收口 ADR（本 ADR 补记）。

## 遗留事项（待用户裁决是否立票）

A. 文档修正类：票09 哈希 renumber 引用更新；report-13 头注释 B14→B17；report-16 计数/笔误；docs/backlog/README.md 补 B14/B15/B17 登记。
B. CONTEXT.md 补条目：Endpoint Ownership；Stale Socket Cleanup 措辞区分两处语义；Hot Folder Guard 措辞收敛（或补代码校验）。
C. ADR 标注：0056 遗留 #5、0058 遗留观察项标注"已兑现"。
D. 持久化缺口：docs/process/reports/ 缺 round3 报告 09/09b/10/11（票12 之前无沉淀机制）。
E. 流程纪律：单槽红线被并行波次踩线（bin/ 竞争，票13 报告已披露）——并行波次门禁应错峰串行。

## 未完成（等用户指令）

- push main：本 ADR 收口后，push 需用户明确指令。
