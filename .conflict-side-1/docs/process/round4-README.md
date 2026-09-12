# 架构恢复 — 第四轮（第三轮遗留清零）

轮次来源：第三轮四票报告遗留节 + 大脑修复提示词登记处（R2/R3/R4 + report-09 遗留 #2）。票09b 尾巴已于 2026-09-02 核验解除（cb2e3d5，3/3 绿）。

## 状态表

| 票 | 标题 | 来源遗留 | Blocked by | 波次 | 状态 |
|---|---|---|---|---|---|
| 13 | UDS 属主修复回归守卫 | 票11 遗留 R2 | None | 1 | done（report-13-uds-transport-guard.md） |
| 14 | 快照/校验脚本转正裁决 | 票12 遗留 R3 | None | 1 | done（转正 workflow-snapshot/verify.js，report-14） |
| 15 | 测试宿主 dotnet run 兜底清零 | 票09 遗留 R4 | None | 1 | done（report-15-test-host-dotnet-run-purge.md） |
| 16 | release-readiness 全链路捕获扩展 | 票09 遗留 #2 | None | 1 | ready-for-agent |

## 波次推导（仅由 Blocked by 字段推导）

四票 Blocked by 全 None → 同属第一波，frontier = {13, 14, 15, 16}，可全并行。
文件面：13 动 Ipc guard、14 动 scripts/.codex-tmp + AGENTS、15 动 Smoke 测试宿主、16 动 release-readiness.ps1——无两票同文件。

## 备注

- backlog 正式立票（docs/backlog/B14–B17）建议随票提交，登记格式沿用 B09–B13。
- 第三轮分支合并（R5）与 push 仍为大脑流程动作，独立于本波开工。
- WORKFLOW.md 保留原位（含 §4.4），第三轮产物归档见 .scratch/_archive/architecture-recovery-round3-2026-09-02/。

## 自检基线（生成时点）

违禁词扫描、必读路径存在性、≤60 行限——见大脑自检输出。
