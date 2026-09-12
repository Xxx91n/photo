# B16 — 测试宿主 dotnet run 兜底清零

- **优先级**: 中　**来源**: 票09 遗留 R4（架构恢复第四轮 · 票15）

## 问题与验收

InstanceConflictAuditTests / DryRunOutputFlowTests 等测试宿主直启路径仍带 dotnet run 兜底（票09 只清了 scripts/），保留编译噪声与管道风险。改 DLL-first 直启（参照票09 smoke.ps1 已合并模式），DLL 缺失时显式报错并给出构建指引；source-lint guard 从 scripts/ 扩域到 tests/ 直启宿主（含拆参形态）。

验收：tests/ 直启点 dotnet run 清零；guard 扩域锁定；受影响测试单槽绿。
