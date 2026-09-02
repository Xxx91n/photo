# B15 — release-readiness 四步全链路输出捕获

- **优先级**: 低　**来源**: 票09 报告遗留 #2（report-09-smoke-dll-first.md「遗留风险」第 2 条）

## 问题与验收

release-readiness.ps1 的 [1/3] dotnet test 与 [4/4] force-kill 仍为直接流式调用（无输出捕获），
失败现场只有退出码没有子进程输出尾部，诊断靠人工复跑。验收：[1/3][4/4] 两步纳入
Invoke-ScriptWithCapture（复用票09 捕获机制，不另造），四步全链路失败自带 stdout/stderr 尾部。

## 完成记录（2026-09-02，票16）

- 四步全捕获：release-readiness.ps1 的 dotnet test 与 force-kill 两处改走 Invoke-ScriptWithCapture，
  与 smoke/publish 同机制；[1/3] 经临时包装脚本复用同一捕获函数。
- 回归锁定：tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessFullCaptureGuardTests.cs（新文件，
  §4.3 不动票15 在途的 SmokeScriptDiagnosticsTests.cs）断言四步均经捕获调用、直连形态清零。
- 门禁：build 0 错误 0 SCS；Core.Tests 145/145 + IntegrationTests 276/276 单槽全绿；
  semgrep scripts 0 findings；PS 5.1 解析 OK；git diff --check clean。
- 报告：.scratch/architecture-recovery/report-16-release-readiness-full-capture.md（受控副本 docs/process/reports/）。
- 编号备注：spec 原拟"票16=B17"，但票13 窗口因 B14 被票14 占用而顺延取 B17（B17-uds-transport-guard.md），
  本票顺延取空缺的 B15，避免编号冲突；README 登记由大脑收口时统一整理。
