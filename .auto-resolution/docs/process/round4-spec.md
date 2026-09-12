# Spec — 架构恢复第四轮（三轮收口遗留清零）

来源（架构报告）：第三轮各票报告的「遗留」节 + 大脑 README 修复提示词登记处 R2/R3/R4 + report-09 遗留 #2。

## Problem Statement

第三轮四票留四个明确的技术尾巴：UDS 端点属主修复（票11）缺回归守卫；流程快照脚本（票12）还是 .codex-tmp 临时身份；测试宿主（InstanceConflictAuditTests/DryRunOutputFlowTests）仍带 dotnet run 兜底（票09 只清了 scripts/）；release-readiness 的 [1/3] dotnet test 与 [4/4] force-kill 两步未走输出捕获（票09 按票面未覆盖）。

## Solution

四张独立小票：源断言守卫锁定 UDS 属主清理形态；快照/校验脚本转正或弃用裁决并执行；测试宿主 dotnet run 清零（guard 扩域）；release-readiness 剩余两步纳入 Invoke-ScriptWithCapture 全链路捕获。

## User Stories

1. As a 维护者, I want UDS 修复有形态级回归守卫, so that 未来重构不破属主清理语义。
2. As a 流程参与者, I want 快照脚本身份明晰（转正受维护或显式弃用）, so that 不留幽灵工具。
3. As a CI 维护者, I want 测试宿主也 DLL-only, so that dotnet run 编译噪声与管道风险整个清零而非一半。
4. As a 发布者, I want release-readiness 每一步失败都自带输出尾部, so that 任何一环失败都现场可诊断。

## Implementation Decisions

- 票13 采用 SourceLint 源断言（复用共享 helper），不做端到端 UDS 测试（Windows 无法跑 UDS 端到端，票11 已实证）。
- 票14 为「裁决票」：先给结论（转正 scripts/ 或 弃用删除），再执行结论；转正则加源守卫并在 AGENTS.md §10 登记。
- 票15 参照票09 已合并的 DLL-first 模式改测试宿主；guard 从 scripts/ 扩到 tests/ 直启处。
- 票16 复用票09 Invoke-ScriptWithCapture，不另造捕获机制。
- 四票均不依赖第三轮四分支已合并——若改动文件与在途分支重叠（票16 的 release-readiness.ps1），遵循实际栈依赖披露。

## Testing Decisions

- 单槽串行门禁不变；每票至少一个回归锁定。
- 票14 若选弃用路径：验收为「脚本删除 + 引用清零 + 文档同步」，不需要行为测试。

## Out of Scope

- 不评估 UDS keep-alive 复用（ADR 0057 裁决不变）。
- 不重排第三轮分支合并次序（R5 归大脑，push 待用户指令）。

## Further Notes

backlog 正式立票（docs/backlog/B14–B17）建议随各票提交一并完成； Blocked by 全 None，四票同波可并行。
