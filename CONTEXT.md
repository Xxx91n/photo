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


**IPC Transport**:
跨平台 IPC 传输抽象层，统一封装 Windows NamedPipe 和 Unix Domain Socket 两种传输后端，按 OS 切换实现。
_Avoid_: IPC backend, transport layer

**Unix Domain Socket**:
Linux/macOS 原生 IPC 机制，使用 .NET System.Net.Sockets + AddressFamily.Unix + UnixDomainSocketEndPoint，监听 /run/photoprivacy/worker.sock（Linux）或 ~/Library/Application Support/photoprivacy/worker.sock（macOS）。
_Avoid_: UDS, Unix socket

**Service State Probe**:
跨平台服务状态探测器抽象接口，Windows 用 ServiceController/sc.exe、Linux 用 systemctl is-active/is-enabled、macOS 用 launchctl list 映射到统一 ServiceRuntimeState 枚举。
_Avoid_: Service monitor, status checker

**Service Command Executor**:
跨平台服务操作执行器抽象接口，通过提权辅助进程（Windows runas / Linux pkexec / macOS osascript）调用平台特定安装脚本，结果通过 Process.ExitCode 返回。
_Avoid_: Service installer, service manager

**Flock Single Instance**:
Unix 单实例保证，通过 FileStream.Lock 独占锁文件（/run/photoprivacy/worker.lock、~/Library/Application Support/photoprivacy/ui.lock）防止多实例运行。Mutex 的 Global\ 前缀在 Unix 不可靠，仅 Windows 用 Mutex。
_Avoid_: PID file, lock guard

**Heartbeat Service**:
UI 进程后台心跳服务，定时 ping Worker（复用现有 IPC Ping），指数退避（1s→2s→4s→...→60s cap），状态通过 INotifyPropertyChanged 暴露给 ViewModel，UI 按钮绑定状态门控。
_Avoid_: Keepalive, health monitor

**Stale Socket Cleanup**:
Unix Domain Socket 服务端绑前无条件删除残留 socket 文件（File.Exists + File.Delete），防止前次崩溃残留导致 EADDRINUSE。NamedPipe 无此问题（内核对象）。
_Avoid_: Socket unlink, endpoint cleanup

**SIGHUP Reload**:
Linux/macOS 服务管理发送 SIGHUP 触发配置重载，与 IPC ReloadConfig 复用同一代码路径。systemd unit 配 ExecReload=/bin/kill -HUP \$MAINPID。
_Avoid_: Signal reload, hot config

**Zombie Reap**:
ExifTool 子进程退出时通过 Process.Exited 事件调用 WaitForExit 同步回收退出码，防止 Unix 僵尸进程驻留。
_Avoid_: Child cleanup, process reap

**Shared Group Model**:
Linux/macOS 服务用户与 GUI 用户共享同一组（photoprivacy），setgid 目录（chmod 2770）保证成员可读写、新建子目录继承组。Quarantine 不开放给 GUI 直接访问，通过 IPC 代理读。
_Avoid_: ACL model, group permissions

**IPC Protocol Version**:
WorkerIpcRequest/Response 中的可选 v 字段（默认 1，nullable）。老客户端发不含 v 的消息按 v1 处理。新功能以方法名扩展为主，version 用于协议能力协商。
_Avoid_: Protocol negotiation, version handshake

**Mode-Scoped Shutdown**:
WorkerIpcMethods.Shutdown 仅在 CLI 模式可触发；Service/Background 模式收到 Shutdown 请求直接返回错误提示「请通过 systemctl/launchd 停止服务」，保护多用户共享场景。
_Avoid_: Service stop, forced shutdown

**Post-Action Probe Retry**:
服务安装/卸载等操作成功后立即重试探测目标状态，指数退避（100ms→200ms→...→12.8s），最多 8 次约 25 秒，避免用户等心跳 5 秒才能看到结果。
_Avoid_: Status wait, action confirm

**Config Reload Validation**:
Worker 收到 ReloadConfig IPC 时先验证新配置 schema 和 ExifTool 路径合法性，验证通过才热切换内部 accessor；失败保留旧配置，IPC 返回错误让 GUI 显示。
_Avoid_: Config guard, schema check

**Elevation Verb**:
各平台提权方式统称：Windows Verb=runas (UAC ShellExecute)，Linux pkexec (polkit)，macOS osascript with administrator privileges。所有参数走 ProcessStartInfo.ArgumentList 自动转义。
_Avoid_: Privilege escalation, admin prompt

**PS5.1 Compat**:
Windows PowerShell 脚本用 #Requires -Version 5.1 + #Requires -PSEdition Desktop,Core 声明兼容。Remove-Service 在 PowerShell 7+ 可用，Desktop 兜底用 sc.exe delete。
_Avoid_: PS compat, script version

