# Round 7 Spec — 架构恢复第七轮（CI/CD 修复 + GUI 心智模型深化）

沉淀自 .scratch/architecture-recovery/spec.md（受控副本，2026-09-07 收口归档）。

## Problem Statement
CI/CD 28/28 全红 0 绿（幽灵 run）；MainWindow code-behind 1433 行窗口级状态同步分散，缺组合根/DI 心智模型。

## Solution
五张垂直切片票 28-32：CI 修复 → 组合根+DI → SettingsService+镜像收敛 → 轮询下沉 → 探活清单。

## 关键决策
- 票 28 对 ADR 0016 部分修订：测试门禁 push/PR 自动、发布仍手动（用户随第七轮派发裁定）。
- atomcode Q1/Q2：组合根 + SettingsService + VM 属性驱动（StabilityMatrix/Files.App/MarkSmith 模板，零发明）。
- CI-only 政策：唯一运行类验收 = CI 验证分支 run 实物（由大脑推送验证分支）。

## Testing Decisions
行为不变票以现有守卫 + 新增 source-lint（CompositionRoot 5 / SettingsVmSync 6 / WindowPolling 6）在 CI 验证分支全绿为准；不引 Avalonia.Headless。

## User Stories / Out of Scope
详见 .scratch 归档副本（_archive/architecture-recovery-round7-2026-09-07/）。
