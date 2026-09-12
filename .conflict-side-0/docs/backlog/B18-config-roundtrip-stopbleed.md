# B18 — 配置 round-trip 对称性止血

- **优先级**: 高　**来源**: 架构恢复第五轮宏观评估（票 17）

## 问题与验收

权威配置读写往返不对称：`ui.sidebar_width` 两侧 DTO 均缺（读不进也存不回，拖拽宽度重启即丢）；`backup.retain_days` 读侧有、写侧缺（UI 保存即回落默认 30）；`config.sample.json` 的 audit.diagnostic_mode/log_level 与 `AppConfig.Default` 取值相反；遗留死键 `backup.retention`。验收：两侧 DTO 补齐 + 构造传参、sample 收敛、round-trip 测试锁定全字段对称、build 0 错 0 SCS。

## 完成记录（2026-09-03，票 17）

- 检查点 A/B：`AppConfigJson`（写）补 `ui.sidebar_width` + `backup.retain_days`，`AppConfigLoader`（读）补 `ui.sidebar_width` + `UiOptions` 构造传参；UI 拖拽→防抖→保存→重启恢复链路（ADR 0037/0052）自此贯通。
- 死键清除：config.json 与全部测试夹具移除 `backup.retention`（ADR 0006 裁决残留），grep 仅剩测试反向断言。
- sample 收敛：audit 段 `false/"info"` 对齐 Default；新增 sample↔Default 全段关键取值对齐测试。
- 测试：`AppConfigRoundTripTests` 6 例全字段往返锁定（含数组段序列相等），Core 151/151 + Integration 276/276 单槽绿；build 0 错 0 SCS；semgrep 双配置 0 发现。
- 报告：`.scratch/architecture-recovery/report-17-config-roundtrip-stopbleed.md`（受控副本 `docs/process/reports/17-config-roundtrip-stopbleed.md`）。
