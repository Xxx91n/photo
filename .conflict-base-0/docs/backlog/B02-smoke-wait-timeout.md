# B02 — Smoke 测试无超时 WaitForExit + testhost 残留治理

- **优先级**: 高　**来源**: report-05 遗留事项

## 问题

`EndToEndSmokeTests.cs:32` `WaitForExitAsync()` 无超时，watcher Worker 不退出时测试永久挂起；且 Smoke 子进程残留会继承 testhost stdout 管道导致 EOF 永不达（vstest 全量挂死已实证两轮：testhost CPU=0、publish 子进程链残留）。`ReleaseReadinessScriptValidationTests` 杀全系统同名进程属危险动作。

## 验收（按 atomcode 调研分层修复）

1. L1：全部子进程等待加超时 + tree-kill（杀进程树而非同名进程）。
2. L2：`test.runsettings` 落地（MaxCpuCount=1 + LongRunningTestSeconds 阈值）。
3. L3（可选，CI 时）：`/Blame:CollectHangDump;TestTimeout=15m`。
4. 全量 vstest 连续 3 次无挂死。
