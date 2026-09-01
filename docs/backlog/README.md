# Backlog — 架构恢复遗留事项（2026-09-01 立票）

来源：ADR 0056 §遗留事项 + 六票报告的"遗留风险"节。2026-09-01 用户裁决：8 项全部立票。

| 编号 | 标题 | 优先级 | 来源 |
|---|---|---|---|
| B01 | publish 冒烟测试 300s 超时预算治理 | 高 | report-01/02 偏差 |
| B02 | Smoke 测试无超时 WaitForExit + testhost 残留 | 高 | report-05 遗留 |
| B03 | FSW 64KB 缓冲 + Error 事件全量重读 | 高 | report-05 遗留 |
| B04 | `_backfillSucceeded` 无锁 bool 收敛进 `_gate` | 低 | report-05 遗留 |
| B05 | WorkerIpcClient 每次 SendAsync 新建传输 → 连接复用 | 中 | report-04 遗留 |
| B06 | source-lint 测试 helper 去重提取 | 低 | report-02 code-review |
| B07 | `UseShellExecute = needElevation` 守卫盲区扩展 | 中 | report-02 偏差 2 |
| B08 | ADR 0053 "StartupCoordinator" 措辞后续修正 | 低 | report-06 / ADR 0056 |

执行约定：沿用 WORKFLOW §4.2（GitButler 独立分支、单槽串行测试门禁）；每票完成须带 source-lint 或行为测试锁定。
