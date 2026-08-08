# PhotoPrivacy

批量清理照片/视频 EXIF 元数据和隐私信息的 C#/.NET 10 桌面工具。

## Language

**Audit Log**:
业务审计日志，记录每个文件的处理事件（清理、跳过、隔离等），JSONL 格式，含路径脱敏和哈希。
_Avoid_: Access log, activity log

**Diagnostic Log**:
运行诊断日志，记录系统运行状态（启动、配置加载、ExifTool 交互、异常等）。统一通过 Serilog 管道写入。
_Avoid_: System log, debug log

**Backup**:
源文件的原始副本，在 ExifTool 清理前创建，用于恢复原始照片。单文件覆盖模式（同文件只保留最新备份）。
_Avoid_: Snapshot, checkpoint, archive

**Quarantine**:
隔离区，存放处理失败或可疑文件的目录。
_Avoid_: Isolation, sandbox

**Processed Record**:
已处理文件记录，NDJSON append log，用于崩溃恢复后跳过已处理文件，实现幂等性。
_Avoid_: History, journal

**Hot Folder**:
监控热目录，Worker 监听此目录下的新文件并自动处理。
_Avoid_: Watch folder, input directory

**Schema Version**:
配置文件 schema 版本号，当前为 1。通过 DTO 默认值兜底实现隐式迁移，不做主动迁移写回。
_Avoid_: Config version, migration version

**Atomic Write**:
原子写入模式：写临时文件 → flush → File.Replace/File.Move 原子替换，防止崩溃时文件损坏。
_Avoid_: Safe write, transactional write

**Compaction**:
压缩清理，将 NDJSON append log 中未过期的条目重写到新文件，原子替换旧文件，控制文件大小。
_Avoid_: Vacuum, defragment
