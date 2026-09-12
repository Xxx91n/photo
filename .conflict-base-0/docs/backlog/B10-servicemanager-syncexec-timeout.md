# B10 — ServiceManager.cs:126 同步 WaitForExit 无超时

- **优先级**: 中　**来源**: 票01 登记邻域观察

## 问题与验收

sc.exe 同步 WaitForExit 无超时，极端下可卡服务管理操作。验收：加超时（参照票01 WaitForExitWithTimeoutAsync 范式）+ source-lint 或行为测试锁定。
