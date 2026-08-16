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


**Locale Path Resolver**:
跨平台 locale 文件路径解析器，集中计算三层 locale 来源（嵌入式资源 → exe 同目录 → 用户配置目录），所有 OS 特定路径分支集中在 `LocalePathResolver` 一处，不在调用点硬编码。
_Avoid_: i18n resolver, locale loader

**Localize Extension**:
Avalonia MarkupExtension（`LocalizeExtension : MarkupExtension`），AXAML 中 `Text="{ex:Localize nav.config}"` 解析为 locale 字符串。订阅 `LocalizationService.CultureChanged` 事件 + WeakReference 持有目标控件，语言切换时自动刷新 UI。
_Avoid_: I18n markup, translation binding

**Three-Layer Locale**:
i18n 文件分层架构：(1) 嵌入式资源（DLL 内，永远可用不可删）→ (2) exe 同目录 `Localization/Locales/`（便携/安装均可用）→ (3) 用户配置目录 `locales/`（per-user 覆盖）。后者覆盖前者同 key。参考 env-manager 同款心智模型。
_Avoid_: Locale stack, i18n layers

**RTL (Right-to-Left)**:
从右到左的语言布局支持，当前支持阿拉伯语（ar）。`LocalizationService` 切换到 RTL locale 时通过 CultureChanged 事件传递 isRtl 标志，`MainWindow` 绑定 `FlowDirection` 自动切换布局方向。
_Avoid_: BiDi, mirror layout

**Backup Atomic Replace**:
备份原子替换模式，使用 `File.Replace(source, dest, null, true)` 跨平台原子 swap（Windows ReplaceFile API / Unix rename(2)），temp 文件加随机后缀防并发碰撞，消除 `File.Move(overwrite)` 的崩溃窗口。
_Avoid_: Atomic backup swap, safe replace

**Async File Operations**:
`IFileOperations` 的异步扩展（`CopyAsync`/`AtomicCopyAsync`/`MoveAsync`），`CopyAsync` 用 FileOptions.Asynchronous + CopyToAsync 真异步 I/O；pipeline 热路径调用 async 版本，同步方法保留兼容现有测试/mock。
_Avoid_: Non-blocking file ops, async IO

**Backup TTL (RetainDays)**:
备份保留的时间维度策略，`BackupOptions.RetainDays`（默认 30 天）。`EnforceMaxSizeAsync` 单遍遍历同时应用 size + TTL 双淘汰：先删过期文件，再按最老优先直到 size 上限。与 `AuditOptions.RetainDays` 概念对称。
_Avoid_: Backup expiry, time retention

**Backup Default Directory**:
备份默认目录 `{HotFolder}/bak`（统一两端点计算：`RuleEngine.ResolveBackupPath` 和 `MetadataCleanerWorker` 共用 `ResolveDefaultBackupDir` helper，修复 bak vs _backup 不一致 bug）。Worker 启动时自动注入 `AutoExcludedDirectories` 防止 FSW 监听自身备份。
_Avoid_: Backup folder, bak dir

**Backup Retention Async**:
`BackupRetentionService.EnforceMaxSizeAsync` 在 `_cleanupTimer`（10 分钟）tick 中触发，脱离 pipeline 热路径。原 per-file 同步调用删除。失败不阻塞主流程（try-catch + audit log）。
_Avoid_: Retention throttle, cleanup retention

**Temp File Route**:
`FileTaskPipeline` 的备份-清除路由：当 ExifTool 原地清除且需要备份时，先把 sourcePath 复制到临时文件（`%TEMP%/PP_*` 前缀），ExifTool 操作 temp，清除后 AtomicCopy(sourcePath, backupPath) 再把 temp 移回 sourcePath。temp 加 `PP_` 前缀，Worker 启动扫描残留 `.codex-tmp` 清理。
_Avoid_: Temp copy route, safe modify route

**Locale Persist**:
i18n 语言偏好的 config.json 持久化字段 `ui.locale`（如 "zh-CN"/"en"/"ja"/"ar"）。UI LocaleVariantComboBox SelectionChanged 触发 SwitchLocale + 防抖 500ms 写 config.json，与 ThemeVariant/LogLevel 同走 ConfigEditor 即时应用管线。首次启动从 config 读取，无 config 时 DetectSystemLocale 兜底。
_Avoid_: Language setting, culture config

**Hot Folder Guard**:
AppConfigValidator 对空 hot_folder 的校验策略：非 dry_run 模式下 hot_folder 必须为非空绝对路径，否则抛 AppConfigValidationException 阻止 Worker 启动。静默跳过空路径会导致用户误以为 Worker 在工作但实际什么都没处理。
_Avoid_: Empty folder fallback, silent skip

**IPC Log Pull**:
WorkerIpcMethods.GetRecentLogs IPC 方法，UI 主动拉取 Worker 最近 N 条审计日志事件（JSONL tail）。补位 AuditTailService FSW tail 的盲区：UI 重连后立即获取历史日志，不依赖 FSW 没有错过的事件。Worker 端读取审计日志文件尾部返回，UI 端合并到 ObservableCollection。
_Avoid_: Log push, audit stream


**i18n Full Coverage**:
i18n 从 AXAML-only 扩展到全 UI 层覆盖：code-behind (MainWindow.axaml.cs) 和 ServiceManager.cs 的硬编码中文字符串全部提取到 locale JSON，通过 `LocalizationService.Instance.Get("key")` 取值。ViewModel 不再用中文字符串做 `Contains` 状态判断，改为 enum/布尔标志（见 ADR 0047）。
_Avoid_: AXAML-only i18n, partial localization

**Tray i18n Refresh**:
TrayHost 订阅 `LocalizationService.CultureChanged` 事件，语言切换时自动刷新 NativeMenuItem.Header。构造时用 `Get("tray.pause")` 初始化，事件回调重建所有菜单项文案。不依赖调用方持有 TrayHost 引用手动刷新（见 ADR 0047）。
_Avoid_: Manual tray refresh, tray static labels

**Nested Locale JSON**:
locale JSON 从 flat key（`nav.config`）迁移到嵌套结构（`{"nav": {"config": "配置"}}`），10 语言各 ~130 key。嵌套结构按功能分组（nav/btn/status/mode/settings/config/service/dialog/tray/theme/loglevel/msg），提高维护清晰度（见 ADR 0047）。
_Avoid_: Flat locale keys, dot-separated keys

**10-Language Expansion**:
从 4 语言（zh-CN/en/ja/ar）扩展到 10 语言（+ko/de/fr/es/pt/ru），对齐 env-manager 支持范围。以 en.json 为基准，pwm pro 逐语言翻译，key 集合与 en.json 完全对齐校验（见 ADR 0047）。
_Avoid_: 4-language limit, hardcoded language list

**Semi.Avalonia**:
Semi Design 的 Avalonia 实现，提供完整 ControlTheme 套件（Button/TextBox/ComboBox 等），替代 FluentTheme 作为 UI 主题基座。MIT 许可。
_Avoid_: FluentTheme overlay, custom theme from scratch

**Ursa.Avalonia**:
.NET Foundation 支持的 Avalonia 控件扩展库，50+ 控件，MIT 许可。与 Semi.Avalonia 天然搭配。
_Avoid_: hand-rolled custom controls when Semi/Ursa already provides them

**Semi Token**:
Semi.Avalonia 定义的语义色值（SemiColorBackground/Background1/Surface/Overlay/Primary/TextPrimary），替代自定义 Color+Brush。迁移后 AppTheme.axaml 不再保留硬编码色值。
_Avoid_: AppBgBrush, hardcoded hex colors in XAML

**Surface Depth**:
4 层背景语义层级：Background < Background1 < Surface < Overlay。从浅到深递进，用于卡片、弹窗、模态框等视觉层次。
_Avoid_: flat single-bg, arbitrary opacity stacking

**Community Theme ResourceDictionary Override**:
社区主题（NordDark/Catppuccin/Dracula 等）是独立 .axaml ResourceDictionary，直接覆写 SemiColor*/SemiBackground* brush token。切换时在 Application.Resources.MergedDictionaries 合并/退出对应 ResourceDictionary，DynamicResource 自动向 Semi 控件传播。RequestedThemeVariant 只在 Light/Dark/Default 三档内置 variant 间切换（MainWindow.axaml.cs NormalizeThemeVariant switch），社区主题不注册 custom ThemeVariant——实际机制是 ResourceDictionary override，不是 ThemeVariant.Create（见 ADR 0048 A4 修正）。
_Avoid_: hardcoded theme switching, Conditional compilation per theme

**DesignTokens.axaml**:
独立的设计 token 文件，定义 spacing ramp(4/8/12/16/24/32/48px)、radius ladder(2/4/8/12/16)、motion durations(75/150/250ms)。全局 DynamicResource 引用。
_Avoid_: inline magic numbers in XAML

**fonts: scheme**:
Avalonia 字体引用方案，FontFamily="fonts:Inter#Inter" 跨平台一致。字体方案必须通过 Program.cs 的 .WithInterFont() AppBuilder 扩展注册 Avalonia.Fonts.Inter NuGet 包的 InterFontCollection(EmbeddedFontCollection) 才生效；Inter 字体嵌在该 NuGet 包 dll 内，项目无 Assets/Fonts/ 目录。手写 DefaultFontFamily=fonts:Inter#Inter 而不调 .WithInterFont() 会让 collection 未注册，主题请求 SemiBold(600) 时抛 InvalidOperationException: Could not create glyphTypeface（见 commit db152c9）。回归 guard: DesignSystemTests.Inter_Font_Scheme_Must_Be_Registered_Via_WithInterFont。
_Avoid_: 写 DefaultFontFamily=fonts:Inter#Inter 而又不调 .WithInterFont()；假设 Assets/Fonts/ 目录存在（实际不存在）

**Source-lint Test**:
编译时静态检查 test：验证 XAML 文件存在、无硬编码颜色值、fonts: scheme 引用正确、SemiTheme 正确引入、5 个社区主题文件存在。沿用已验证 pattern，不引 Avalonia.Headless。
_Avoid_: runtime-only UI test, Avalonia.Headless（超时风险）
**WithInterFont Registration**:
AppBuilder 扩展方法，注册 Avalonia.Fonts.Inter 包内的 InterFontCollection(EmbeddedFontCollection)。是 fonts:Inter#Inter scheme 的唯一正确注册方式。缺少它则字体 collection 未注册，主题 fonts:Inter 请求任意 FontWeight(如 SemiBold=600) 时 Avalonia 无法创建 glyphTypeface → 启动崩溃 InvalidOperationException。见 commit db152c9，回归 guard DesignSystemTests.Inter_Font_Scheme_Must_Be_Registered_Via_WithInterFont 锁定 Program.cs 必含 .WithInterFont()。
_Avoid_: 用 DefaultFontFamily 手赋 fonts:Inter#Inter 替代 .WithInterFont()；自己写 EmbeddedFontCollection 而不用官方扩展

**IPC Probe Resilience**:
WorkerIpcClient 探测 Worker 是否存活必须 catch System.Text.Json.JsonException（不只 OperationCanceled/IO/Timeout/Socket）。stale pipe 残留 1 字节坏 JSON 时，JsonSerializer 反序列化抛 JsonException，未捕获会杀 UI 进程导致 release 启动即崩。捕获后视为 Worker 不可用返回 false，绝不杀 UI。符合 ADR 0035 心智模型：IPC 探测失败应降级而非崩溃。见 commit a6ef02b。
_Avoid_: IPC 探测只 catch IO/Timeout 而漏 JsonException；stale pipe 坏 JSON 导致 UI 进程崩溃

**Padding Literal Quantization**:
XAML Thickness 必须用字面量字符串如 Padding="16" 触发编译期 ThicknessTypeConverter，而非 DynamicResource Double 赋 Thickness（跳过 TypeConverter → InvalidCastException 导致布局测量阶段崩溃）。字面量化是 DesignTokens spacing ramp 的 XAML 消费形式。见 commit 92f7790。
_Avoid_: DynamicResource Double 直接赋 Thickness 属性（TypeConverter 被跳过 → InvalidCastException）
**Surface Depth 4-Layer + Elevation Shadow**:
4 层背景语义层级再加 `SemiElevation1/2/3` shadow token：Background < Background1 < Surface < Overlay。之前 ADR 0048 只到 surface 3 层缺 overlay，无 BoxShadow elevation 导致视觉"扁平贴纸"。本 ADR 0050 引入 Semi.Avalonia 12.1 的 elevation token 给 sidebar/settings-card/popover 投影，配合 overlay 第 4 层用于 dialog/modal 深度分离。见 ADR 0050 A1。
_Avoid_: 平铺无深度区分；自写 BoxShadow 散值而非用 Semi elevation token

**Typography 6 Role Class**:
6 个 TextBlock style class：`.display` (20/Bold)、`.headline` (18/SemiBold)、`.title` (16/SemiBold)、`.body` (14/Normal)、`.caption` (11/Normal)、`.mono` (12/Normal, CascadiaCode)。绑 FontSize + FontWeight 到 design token，MainWindow.axaml 的 inline `FontSize=16 FontWeight=SemiBold` 散值迁移到这些 class。符合 Fluent/Avalonia "style class over inline" 行业原则。见 ADR 0050 A1。
_Avoid_: inline `FontSize=N FontWeight=X` 在 TextBlock 上散布；自写 typography 但不绑 design token

**Middle-Click Autoscroll Behavior (Files.App Port)**:
Avalonia 无内置中键 pan/autoscroll，必须手写 attached behavior。本项目移植 Files.App MIT `ScrollViewerMiddleClickExtensions.cs` 为 `PhotoPrivacy.Ui.Behaviors.MiddleClickScrollBehavior`：PointerPressed 中键 → 锚定 + DispatcherTimer tick → `ScrollViewer.Offset` 更新；DeadZone 防误触；SpeedFactor + MaxSpeedPerTick 上限；Escape/中键再次/任意键 退出；4 方向 cursor 切换。没有 LOD 逻辑；所有 ScrollViewer 一行 `IsEnabled="True"` attached。见 ADR 0050 A3。
_Avoid_: Avalonia 原生假设有中键 pan；自写未参考 Files.App 成熟实现的 minified 行为

**WindowDrawnDecorations + ElementRole**:
Avalonia 12.1 通过 PR #20770 引入 `WindowDrawnDecorations` 托管的 drawn decorations API，搭配 `WindowDecorationProperties.ElementRole` attached 属性：标记 `ElementRole="TitleBar"` 让系统自动处理 drag + 双击最大化（无需手写 drag handler）。注意：`ElementRole="MinimizeButton/MaximizeButton/CloseButton"` 是 chrome hit-test 的语义标记；caption button click 的"系统自动接管"只在 `WindowDrawnDecorations` ControlTheme 模板里（对名为 `PART_MinimizeButton` 等 template part 的 Button 订阅 Click）才生效。本项目在 client area 自绘 Button（非那段 template 上下文），所以仍需手写 `OnMinimizeClick/OnMaximizeClick/OnCloseClick` click handler 驱动 `WindowState`/`Close()`；`ElementRole` 在此仅作语义/无障碍标记，不替 click 逻辑。配合 `ExtendClientAreaToDecorationsHint=True` + `WindowDecorations=None` 实现自绘现代化标题栏，再加 `Window:maximized`/`Window:fullscreen` pseudoclass style 让标题栏 padding 0、fullscreen 隐藏 caption button。跨平台：Win/macOS 完整支持，Linux "Limited support" 时 Avalonia 内部降级到原生不报错。本项目替代被排除的 PleasantUI 新依赖方案请见 ADR 0050 A4。
_Avoid_: PleasantUI 新依赖; 手写 drag handler（系统已接管）；把 `ElementRole` 自绘 caption button click 误当成自动接管

**Unicode Status Symbol Cleanup**:
locale JSON 用 `✓ ✗ ⚠`（U+2713/2717/26A0）Unicode 符号呈现保存成功/失败/警告，与 Material.Icons 信号语义混杂 → AI 感来源。本 ADR 0050 A2 把 10 语言 JSON 的 8 处 Unicode 符号清零（"✓ Auto-saved" 改为 "Auto-saved"），UI 侧用 Material.Icons（`Check/Close/Alert`）或纯文本呈现状态，避免 application text 混杂 Unicode 符号。回归 guard: LocalizationServiceTests.Unicode_Status_Symbols_Cleared。
_Avoid_: 在 application text 中用 Unicode 符号替代 Material.Icons；（UI 上）混杂信号图标语义
**Space Token Spend Cleanup**:
view 内 Margin/Spacing ad-hoc 散值（如 Margin="12,0" Spacing="5"）是 AI 感 UI 第一根因"everything is evenly spaced"的来源。ADR 0051 A1 要求全局替换为 DesignTokens Space* token 引用（SpaceXxs→Xxxl = 2/4/8/12/16/24/32/48）。XAML Thickness 仍用字面量字符串触发 ThicknessTypeConverter（ADR 约束不变）。Reddit r/ClaudeCode 社区共识：专业 UI 的间距是 4/8px ramp 的分级节奏，而非处处相同。见 ADR 0051 A1。
_Avoid_: 视图中散布 ad-hoc Margin/Spacing 字面量而不引用 Space* token；等距均匀布局

**Elevation Shadow Ladder**:
4 级 BoxShadow token（Elevation0 无投影 / Elevation1 1dp / Elevation2 2dp / Elevation4 4dp）在 DesignTokens.axaml 定义，配合 ADR 0050 Surface Depth 4-Layer 的 overlay 第 4 层给 sidebar/settings-card/popover 投影。之前只有 surface 背景分层无 shadow token 导致"扁平贴纸感"。Material surfaces/depth 规范 + avalonia-pro-max 反模式清单均要求 ≥3-4 层 surface + elevation shadow。见 ADR 0051 A1。
_Avoid_: 无 elevation shadow 的平铺无深度区分；自写 BoxShadow 散值而非用 token

**Button Transition Animation**:
AppTheme.axaml Button 变体加 Transitions setter（BrushTransition 0.15s + TransformOperationsTransition 0.075s），:pressed 设 RenderTransform="scale(0.97)"（CSS 风格可过渡，WPF 风格 ScaleTransform 不能 transition）。修复"闪现弹回"根因：无 Transitions 时 :pressed 伪类松开瞬间硬切画笔。官方 easing 文档推荐按压 QuadraticEaseInOut、hover SineEaseOut。必须避免 BackEaseInOut/ElasticEaseInOut（issue #15704 触发 visibility 闪烁）。SukiUI 同款模式（100-350ms 全属性 + scale(0.97)），Semi.Avalonia 零动画硬切哲学不选。见 ADR 0051 A2。
_Avoid_: Button 样式无 Transitions 导致 :pressed 闪现硬切；WPF 风格 ScaleTransform（不能 transition）；BackEaseInOut/ElasticEaseInOut

**RequestAnimationFrame Middle-Click Scroll**:
MiddleClickScrollBehavior.cs 的 DispatcherTimer 16ms 换为 TopLevel.RequestAnimationFrame（Avalonia 11.0+ 官方等价物 of CompositionTarget.Rendering，与渲染循环/显示器帧率同步），消除合并帧/抖动。帧率无关计算：delta * (frameTime.TotalMilliseconds / 16.67) 让高刷屏自动适配。常量对齐 Files.App 原版：DeadZone=12, SpeedFactor=0.12, MaxSpeedPerTick=32。TopLevel.GetTopLevel(sv) null 边界 fallback 到 DispatcherTimer。保持线性比例速度模型（松键即停，不加惯性）。见 ADR 0051 A3。
_Avoid_: DispatcherTimer 16ms 非 vsync 对齐导致高刷屏抖动；偏离 Files.App 成熟常量值
