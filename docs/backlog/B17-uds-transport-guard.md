# B17 — UDS 属主修复回归守卫

- **优先级**: 中　**来源**: 票11 遗留 R2（架构恢复第四轮票13）

## 问题与验收

票11 修复的 UnixDomainSocketIpcTransport 端点属主清理（DisposeAsync 仅属主删除 socket 文件）缺形态级回归守卫，未来重构可能回退。验收：SourceLint 源断言 guard 锁定 ownsEndpoint 语义 + File.Delete 属主包裹形态，负向自证（注入违例样本判红），单槽测试绿。

## 完成记录（2026-09-02，架构恢复第四轮票13）

- guard 落地：`tests/PhotoPrivacy.IntegrationTests/UdsEndpointOwnershipGuardTests.cs`（纯分类器 ClassifyDisposeAsync + 3 断言：属主判定正则锚定、File.Delete 词法落在 if (ownsEndpoint) 平衡块内、修复说明注释契约锁定；复用 SourceLint 共享 helper）。
- 负向自证：内存变异真实源码 3 违例样本（属主检查短路 true / 非属主条件守护 / 属主判定与监听器脱钩）全部判红 + 变异未生效自保护。
- 门禁：build 0 错 0 SCS；guard 3/3；IntegrationTests 单槽首轮 275/275；Core.Tests 145/145；semgrep p/csharp（guard 文件 + src/PhotoPrivacy.Ipc）0 findings。
- 报告：.scratch/architecture-recovery/report-13-uds-transport-guard.md（受控副本 docs/process/reports/13-uds-transport-guard.md）。
- 编号备注：spec 原拟"票13=B14"，因票14 窗口先行占用 B14 而顺延取 B17。
