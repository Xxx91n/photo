# FileProcessedRecordStore 定时 compaction

保持 RecoverFromLog() 全量扫描不变。加定时 compaction（复用现有 _cleanupTimer）：每 10 分钟清理过期条目时，将未过期条目重写到新 NDJSON 文件，原子替换旧文件（temp + rename）。NDJSON 文件大小被控制在与 TTL 内活跃条目相当，恢复时间被自然约束。

## Considered Options

- **WAL + checkpoint 模式**：定期快照内存状态，恢复时从 checkpoint 开始。约 80+ 行代码。被否决——桌面工具不需要数据库引擎级别的恢复机制。
- **当前够用不碰**：Cleanup 只清 _cache 和过期条目，不截断 NDJSON 文件本身。被否决——文件仍包含过期行，持续增长。
