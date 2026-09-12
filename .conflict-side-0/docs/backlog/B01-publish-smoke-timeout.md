# B01 — publish 冒烟测试 300s 超时预算治理

- **优先级**: 高　**来源**: report-01/report-02 偏差 1

## 问题

`ReleaseReadinessScriptValidationTests.PublishApp_Should_Copy_Tray_Assets_To_Publish_Root` 内嵌 300s 超时，而本机 `publish-app.ps1` Release 自包含发布直跑约 5–6 分钟；多窗口并行构建时必然溢出，造成票 01/02 验收期的假性失败（已取证：同参数直跑 exit 0，断言目标文件存在）。

## 验收

1. 超时不再硬编码 300s：改为可配置参数或提升到 ≥900s。
2. 或将该用例归类 long-running，与快速门禁分离（`dotnet test --filter` 可排除）。
3. 全量 IntegrationTests 单槽串行跑通，无超时性失败。
