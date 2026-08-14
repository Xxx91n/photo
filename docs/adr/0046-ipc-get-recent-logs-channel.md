# ADR 0046: IPC GetRecentLogs 日志拉取通道

**状态**: 已接受
**日期**: 2026-08-14

## 背景

WorkerIpcMethods 只有 7 个方法（Ping/GetStatus/Pause/Resume/
GetExifToolVersion/Shutdown/ReloadConfig），没有日志推送通道。
Worker 审计日志写 JSONL 文件，UI AuditTailService 用 FSW tail 读。

FSW tail 在正常工作时不丢事件，但有两个盲区：
1. UI 重连后只能看到 FSW 启动后的新事件，错过重连前的历史日志
2. FSW 内部缓冲区溢出时静默丢事件（Windows 64KB 默认缓冲区）

## 决策

新增 WorkerIpcMethods.GetRecentLogs 方法，UI 主动拉取 Worker 最近 N 条
审计日志事件（JSONL tail）：

1. WorkerIpcContracts 新增 GetRecentLogs 常量
2. WorkerIpcRequest 不变（method + id + v）
3. WorkerIpcResponse.Data 扩展：新增 RecentLogsDto（List<LogEntry>）
4. WorkerIpcServerHostedService.HandleRequest 新增 GetRecentLogs case
5. Worker 端读取当天审计日志文件尾部 N 行返回
6. UI 端 WorkerProcessManager 新增 GetRecentLogsAsync 方法
7. UI 重连/启动时调用 GetRecentLogs 补位历史

### 考虑的替代方案

**纯 FSW tail（拒绝）**: 只用 AuditTailService FSW tail。重连盲区 +
缓冲区溢出丢事件不可控。IPC 拉取补位花费约 40 行代码，消除盲区。

**Worker 主动推送（拒绝）**: Worker 通过 IPC 主动推日志到 UI。需要
持久连接 + 双向通信，当前 IPC 是请求-响应模式不适用。拉取模式更简单。

### 后果

- UI 启动/重连后立即显示历史日志，不依赖 FSW 事件
- GetRecentLogs 只读当天文件尾部，O(N) 无缓存，调用频率低（仅重连时）
- WorkerStatusDto 不变，新数据通过 response.Data 扩展传递