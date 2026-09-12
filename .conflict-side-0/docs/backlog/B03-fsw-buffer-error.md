# B03 — FSW 64KB 缓冲 + Error 事件全量重读

- **优先级**: 高　**来源**: report-05 遗留事项

## 问题

`FileSystemWatcher.InternalBufferSize` 维持默认 8KB，审计日志/监控目录高频写入场景存在缓冲溢出丢事件风险（dotnet/runtime#14645 已知问题）。当前靠轮询兜底覆盖，未根治。

## 验收

1. `InternalBufferSize` 提升至 64KB（注意：缓冲在非分页池，仅监控目录 watcher 提升）。
2. 订阅 `Error` 事件：缓冲溢出时触发全量重读（重扫目录/重置水位），而非静默丢。
3. 压力测试：高频写入下 0 丢事件（新测试锁定）。
