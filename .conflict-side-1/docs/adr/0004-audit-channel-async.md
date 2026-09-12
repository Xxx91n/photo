# 审计日志使用 Channel<T> 异步批量写入 + 文件大小滚动

JsonLineAuditLogger 用 System.Threading.Channels.Channel<T> 作为写入队列，后台 task 批量 flush。按天 + 按大小双滚动（100MB 切文件），retainedFileCountLimit 限制总文件数。Serilog.Sinks.Async 的同款模式，但审计日志不进 Serilog，保留独立格式和路径脱敏。

## Considered Options

- **当前量级够用不碰**：File.AppendAllTextAsync 在 NTFS 上对 append 有内核优化，加文件大小上限但不加异步缓冲。审计日志不是热路径。被否决——用户选择了完整异步方案。
