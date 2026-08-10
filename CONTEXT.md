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


**RID (Runtime Identifier)**:
.NET 运行时标识符，指定目标平台架构组合（如 win-x64、linux-arm64、osx-arm64）。本项目支持 7 个 RID，匹配 ExifTool 上游平台覆盖范围。
_Avoid_: Platform, target

**Shell (发布壳)**:
本项目的编译产物（UI + Worker），不含 ExifTool 二进制。用户自行下载 ExifTool 并配置路径，与 ExifToolGUI 的法律合规模式一致。
_Avoid_: Bundle, package

**Release Directory**:
统一发布输出目录 release/<rid>/，替代旧 publish/ 路径。每个 RID 子目录内为平铺结构，Worker 在 worker/ 子目录。
_Avoid_: Publish directory, output folder

**Publish Profile**:
csproj PropertyGroup 中的发布属性集合（SelfContained、PublishSingleFile、IncludeNativeLibrariesForSelfExtract 等），确保本地和 CI 构建一致。
_Avoid_: Build config, deployment config

**Manual Dispatch**:
GitHub Actions workflow_dispatch 手动触发模式，不在 push/PR 上自动运行。用户通过 GitHub UI 手动触发构建。
_Avoid_: Auto build, CI trigger

**Deb Package**:
Linux .deb 安装包，control 文件声明 Avalonia native 依赖（libx11-6 等），用 dpkg-deb --build 手动构建。
_Avoid_: Debian package, apt package

**App Bundle**:
macOS .app 目录结构，含 Info.plist 和图标，用 ditto 打包。
_Avoid_: macOS app, application bundle

**LaunchDaemon**:
macOS 系统服务管理器，通过 plist XML 配置 + launchctl load 注册服务。
_Avoid_: launchd agent, macOS service

**Compaction**:
（同前）NDJSON append log 定时压缩，重写未过期条目到新文件，原子替换旧文件。

_Avoid_: Vacuum, defragment

**Marketplace**:
（保留占位，无新术语）
