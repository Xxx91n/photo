# Backlog — 架构恢复遗留事项（2026-09-01 立票）

来源：ADR 0056 §遗留事项 + 六票报告的"遗留风险"节。2026-09-01 用户裁决：8 项全部立票。

## 第一轮（B01–B08）— 全部清零 2026-09-01

| 编号 | 标题 | 状态 | 落地分支 |
|---|---|---|---|
| B01 | publish 冒烟测试超时预算 | ✅ 参数化（env 默认 900s） | arc-recovery/02-publish-timeout-budget |
| B02 | Smoke 无超时治理 | ✅ 超时化+树杀+runsettings+管道排空 | arc-recovery/01-smoke-test-timeout |
| B03 | FSW 64KB + Error 重读 | ✅ 含并发合并守卫真修复 | arc-recovery/03-fsw-buffer-error |
| B04 | `_backfillSucceeded` 锁收敛 | ✅ 读写进 _gate | arc-recovery/04-backfill-flag-lock |
| B05 | IPC 传输复用评估 | ✅ 测量裁决不改（ADR 0057） | arc-recovery/05-ipc-transport-reuse |
| B06 | source-lint helper 去重 | ✅（含 nn/ovl 收口尾巴） | arc-recovery/07-source-lint-helper-dedup |
| B07 | UseShellExecute 守卫白名单 | ✅ 白名单+负向自证 | arc-recovery/06-shell-execute-guard |
| B08 | ADR 0053 errata | ✅ Errata 段 + CONTEXT 同步 | arc-recovery/08-adr53-errata |

## 第二轮观察项（待用户裁定是否立票）

1. smoke.ps1 改 DLL-first（去 dotnet run 编译噪声；现状已被管道排空兜底，非紧急）
2. ServiceManager.cs:126 sc.exe 同步 `WaitForExit()` 无超时（票01 登记的邻域观察）
3. PublishApp 冒烟失败时捕获脚本输出，便于诊断空产出等并发污染
4. 跨平台 UDS 传输测量（05 结论按调用频率推导成立；如需绝对数值再立票）
5. 流程产物持久化：.scratch 曾被 GitButler 操作清空（ADR 0058 事故 1）——评估流程文件入 docs/ 或动栈前快照

执行约定：沿用 WORKFLOW §4.2（GitButler 独立分支、单槽串行测试门禁）；每票完成须带 source-lint 或行为测试锁定。
