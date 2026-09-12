# Spec — 架构恢复第三轮（B09–B13 收口）

来源（架构报告）：docs/backlog/README.md 第二轮观察项 + docs/backlog/B09–B13 五份票卡 + docs/adr/0058 二轮收口 ADR。B13 按票注并入 B09。

## Problem Statement

发布链路与工程治理仍有四个已知毛刺：smoke.ps1 走 dotnet run 带来 ~20s 编译噪声且失败时无脚本输出可诊断；ServiceManager.cs 存在一处无超时的同步 WaitForExit；ADR 0057 的 IPC 测量只覆盖 Windows 命名管道；.scratch 曾在 GitButler 栈操作中整树蒸发（ADR 0058 事故 1），流程产物无持久化保证。

## Solution

四个互相独立的小票：smoke 脚本改 DLL-first 并捕获失败输出；ServiceManager 同步等待加超时；跨平台 UDS 实测补数据；流程产物持久化策略成文并实测一轮。

## User Stories

1. As a 发布者, I want smoke 直启已发布 DLL, so that 冒烟不被 dotnet run 编译噪声拖慢/污染。
2. As a 发布者, I want 冒烟失败时看到脚本 stdout/stderr 尾部, so that 空产出等失败可现场诊断而不必人工复跑。
3. As a 维护者, I want ServiceManager 所有外部进程等待带超时, so that 极端情况下服务管理操作不永久卡死 UI。
4. As a 架构决策者, I want UDS 传输的实测数据, so that ADR 0057 的跨平台结论有实证而非按频率推导。
5. As a 流程参与者, I want 流程产物（WORKFLOW/spec/issues/handoffs/reports）在栈手术下不丢, so that 多窗口工作可复盘、可续接。

## Implementation Decisions

- 冒烟脚本改为 DLL-first 直启，参照 InstanceConflictAuditTests.StartCli 既有模式；不新增第二套启动逻辑。
- 失败输出捕获与 DLL-first 同票交付（B13 并入 B09），不拆两票。
- ServiceManager 超时改造参照票 01 已合并的 WaitForExitWithTimeoutAsync 范式，不引入新机制。
- UDS 测量为纯测量票：数据落盘 benchmarks 记录格式（沿用二轮 ../../_archive 中 benchmarks 体例），结论相悖才回票；无 Linux/macOS 环境时记录未测原因回大脑裁决，禁止编造。
- 流程产物持久化：策略成文进 WORKFLOW，且至少一轮实测验证无丢失。

## Testing Decisions

- 沿用单槽串行 vstest 门禁（test.runsettings MaxCpuCount=1）。
- 每票至少一个回归锁定：source-lint guard 或行为测试，优先复用 SourceLint 共享 helper 与票 01 进程超时范式。
- 冒烟改动以脚本级验证 + 既有 Smoke 测试套件保持绿为准。

## Out of Scope

- 不重构发布脚本整体架构；不改 IPC 协议（ADR 0057 裁决不变更）。
- 不给 .scratch 建立通用备份系统，只管流程产物持久化策略。

## Further Notes

四票互不阻塞，均可独立开工。完成定义见各 handoff；版本控制遵循 WORKFLOW §4.2。
