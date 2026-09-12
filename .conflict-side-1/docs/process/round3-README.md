# 架构恢复 — 第三轮（B09–B13 收口）

轮次来源：docs/backlog/README.md 第二轮观察项（用户裁决全部立票；B13 并入 B09）。

## 状态表

| 票 | 标题 | 对应 backlog | Blocked by | 波次 | 状态 |
|---|---|---|---|---|---|
| 09 | smoke.ps1 DLL-first + 失败输出捕获 | B09+B13 | None | 1 | ready-for-agent |
| 10 | ServiceManager 同步 WaitForExit 超时化 | B10 | None | 1 | ready-for-agent |
| 11 | 跨平台 UDS 传输测量补测 | B11 | None | 1 | done（report-11-uds-transport-measure.md） |
| 12 | 流程产物持久化策略 | B12 | None | 1 | done（report-12-workflow-artifact-persistence.md） |

## 波次推导（仅由 Blocked by 字段推导）

四票 Blocked by 全部为 None，同属第一波，可全部并行开工；frontier = {09, 10, 11, 12}。无后续波次。
文件碰撞检查：09 动 scripts/ 与 Smoke 测试；10 动 ServiceManager.cs 与其测试；11 动 benchmarks 与 ADR 0057；12 动 WORKFLOW/AGENTS 文档——零重叠。

## 目录

- spec.md — 第三轮 spec（来源：docs/backlog B09–B13）
- issues/ — 四票（09–12）
- handoffs/ — 四票窗口交接
- prompts/ — 四份窗口启动器（≤60 行，硬规则自检已过）
- WORKFLOW.md — 流程权威（恢复自二轮归档，逐字节一致）
