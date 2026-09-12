# 备份操作使用原子写入模式（temp + rename）

备份操作改为先 File.Copy 到 filename.bak.tmp，然后 File.Move(tmp, target, overwrite: true) 原子替换。临时文件与目标文件同目录同卷，File.Move 在 NTFS 上是原子操作。防止崩溃时备份文件损坏——用户依赖备份恢复原始照片，备份损坏 = 数据丢失。

## Considered Options

- **当前 File.Copy 够用不碰**：如果没有实际损坏报告，不过度工程。被否决——照片隐私工具的备份完整性属于安全相关。
