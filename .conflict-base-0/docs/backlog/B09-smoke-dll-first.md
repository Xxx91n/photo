# B09 — smoke.ps1 改 DLL-first

- **优先级**: 高　**来源**: 票01 遗留

## 问题与验收

smoke.ps1 每次 dotnet run 可能触发重编译（~20s 构建噪声，曾致管道缓冲死锁湖的真身）。改为参照 InstanceConflictAuditTests.StartCli 的 DLL-first 直启，PublishApp 冒烟时同步捕获脚本输出（合并观察项3）。验收：smoke 路径无 dotnet run；失败时日志含脚本 stdout/stderr 尾部。
