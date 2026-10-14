# PhotoPrivacy

批量清理照片/视频 EXIF 元数据和隐私信息的 C#/.NET 10 桌面工具。

> 本文件只做领域词汇索引：每词条 ≤2 句定义 + 禁则 + 权威指针。机制与取舍细节一律在 docs/adr/ 与 docs/design/，不在此处复述。

## Language

**Audit Log**: 业务审计日志（JSONL，路径脱敏+哈希），事件名含 wipe_skipped_unknown / backup_skipped / backup_sidecar_versioned / config_warning。_Avoid_: access log, activity log

**Diagnostic Log**: 运行诊断日志（启动/配置/ExifTool 交互/异常），统一走 Serilog 管道。_Avoid_: system log, debug log

**Backup**: 源文件清理前的原始副本；镜像相对路径布局（源无法归位则 `_unsorted/文件名.短哈希` 兜底），同内容跳过、分歧写 `名字.时间戳.扩展名` 旁路版本，绝不静默覆盖；受 ADR 0006 retention 兜底。_Avoid_: snapshot, checkpoint, archive；单文件扁平覆盖（ADR 0068）

**Quarantine**: 处理失败或可疑文件的隔离目录。_Avoid_: isolation, sandbox

**Processed Record**: 已处理文件 NDJSON append log，崩溃恢复后幂等跳过。_Avoid_: history, journal

**Hot Folder**: Worker 监听并自动处理新文件的热目录。_Avoid_: watch folder, input directory

**Schema Version**: 配置 schema 版本（当前 1），DTO 默认值兜底隐式迁移，不主动回写。_Avoid_: config version, migration version

**Atomic Write**: 写临时文件 → flush → File.Replace/File.Move 原子替换，防崩溃损坏。_Avoid_: safe write, transactional write

**Compaction**: NDJSON 日志未过期条目重写+原子替换以控大小（ADR 0009）。_Avoid_: vacuum, defragment

**RID (Runtime Identifier)**: .NET 目标平台标识符；本项目 7 个 RID（ADR 0010）。_Avoid_: platform, target

**Shell (发布壳)**: 编译产物不含 ExifTool 二进制，用户自配路径（合规同 ExifToolGUI）。_Avoid_: bundle, package

**Release Directory**: `release/<rid>/` 平铺输出，UI 与 Worker 可执行文件同目录（ADR 0010/0039）。_Avoid_: publish directory, output folder

**Publish Profile**: 发布属性集合（PublishSingleFile/IncludeNativeLibrariesForSelfExtract 等）经 publish 脚本 `/p:` 参数传入，不写入 csproj（ADR 0035；守卫 PublishPropertiesCsprojGuardTests）。_Avoid_: build config, deployment config

**Manual Dispatch**: release.yml 仅 workflow_dispatch 手触；ci.yml 在 push/PR 自动跑测试（对 ADR 0016 部分修订）。_Avoid_: auto release, tag-triggered release

**Deb Package**: Linux .deb 包，control 声明 Avalonia native 依赖（ADR 0018）。_Avoid_: apt package

**App Bundle**: macOS .app 目录结构（Info.plist+图标，ditto 打包）。_Avoid_: application bundle

**LaunchDaemon**: macOS 系统服务管理器（plist+launchctl，ADR 0014）。_Avoid_: launchd agent, macOS service

**IPC Transport**: NamedPipe/UDS 双后端抽象，按 OS 切换实现（ADR 0019）。_Avoid_: IPC backend, transport layer

**Unix Domain Socket**: Linux/macOS 原生 IPC；按模式双 socket（`worker.sock` / `worker-background.sock`，ADR 0019/0029）。_Avoid_: UDS, Unix socket

**Service State Probe**: 三平台服务状态探测抽象（ServiceController/systemctl/launchctl → 统一枚举，ADR 0020）。_Avoid_: service monitor, status checker

**Service Command Executor**: 三平台服务操作抽象，经提权辅助进程调安装脚本（ADR 0021）。_Avoid_: service installer, service manager

**Flock Single Instance**: Unix 单实例经 FileStream.Lock 锁文件；`Global\` Mutex 仅 Windows 可靠。_Avoid_: PID file, lock guard

**Heartbeat Service**: UI 后台心跳 ping Worker，指数退避至 60s cap（ADR 0025）。_Avoid_: keepalive, health monitor

**Stale Socket Cleanup**: bind 前无条件删残留 socket；DisposeAsync 的删除仅限端点属主（见 Endpoint Ownership）。_Avoid_: socket unlink, endpoint cleanup

**Endpoint Ownership**: `ownsEndpoint`（服务端实例）才在 DisposeAsync 删 socket，客户端用完即弃不删（票11 修复，UdsEndpointOwnershipGuardTests 锁定）。_Avoid_: 无条件删除；客户端删共享端点

**SIGHUP Reload**: 信号触发配置热载；回调为 `Func<Task>` 经 `Task.Run` 编组，与 IPC ReloadConfig 同路径，异常自日志不阻塞信号线程（ADR 0027/0033）。_Avoid_: 信号回调内 sync-over-async；signal reload, hot config

**Zombie Reap**: ExifTool 子进程 `Exited`→`WaitForExit` 回收退出码防 Unix 僵尸（ADR 0027）。_Avoid_: child cleanup, process reap

**Shared Group Model**: 服务与 GUI 用户共享组+setgid 目录；隔离区经 IPC 代理读不直接开放（ADR 0028）。_Avoid_: ACL model, group permissions

**IPC Protocol Version**: 消息可选 `v` 字段（默认 1），新功能以方法名扩展为主（ADR 0030）。_Avoid_: protocol negotiation, version handshake

**Mode-Scoped Shutdown**: Shutdown 仅 CLI 模式可触发，服务模式回「请用 systemctl/launchd」（ADR 0031）。_Avoid_: service stop, forced shutdown

**Post-Action Probe Retry**: 服务操作后指数退避重试探测（~25s/8 次，ADR 0032）。_Avoid_: status wait, action confirm

**Config Reload Validation**: 重载前先验 schema 与 ExifTool 路径，失败留旧配置并报错（ADR 0033）。_Avoid_: config guard, schema check

**Elevation Verb**: 提权统称 runas/pkexec/osascript，参数走 `ArgumentList`（ADR 0024）。_Avoid_: privilege escalation, admin prompt

**PS5.1 Compat**: Windows 脚本 `#Requires -Version 5.1` 兼容声明，Desktop 兜底 sc.exe（ADR 0024）。_Avoid_: PS compat, script version

**Locale Path Resolver**: 三层 locale 路径解析集中 `LocalePathResolver` 一处（ADR 0040）。_Avoid_: i18n resolver, locale loader

**Localize Extension**: `Text="{ex:Localize key}"` MarkupExtension，CultureChanged 自动刷新（ADR 0040）。_Avoid_: i18n markup, translation binding

**Three-Layer Locale**: 嵌入资源→exe 同目录→用户配置目录三层覆盖（ADR 0040）。_Avoid_: locale stack, i18n layers

**RTL (Right-to-Left)**: 阿拉伯语从右到左，CultureChanged isRtl 绑 `FlowDirection`（ADR 0040/0047）。_Avoid_: BiDi, mirror layout

**Backup Atomic Replace**: `File.Replace` 原子 swap+随机后缀 temp（ADR 0041）。_Avoid_: atomic backup swap, safe replace

**Async File Operations**: `IFileOperations` 异步扩展真异步 I/O，同步版留兼容（ADR 0041）。_Avoid_: non-blocking file ops, async IO

**Backup TTL (RetainDays)**: size+TTL 单遍双淘汰（先过期后最老，ADR 0042）。_Avoid_: backup expiry, time retention

**Backup Default Directory**: 默认 `{HotFolder}/bak`（共用 ResolveDefaultBackupDir；Worker 自注入 AutoExcluded，ADR 0043）。_Avoid_: backup folder, bak dir

**Backup Retention Async**: `_cleanupTimer` 10 分钟 tick 触发，失败不阻塞主流程（ADR 0042）。_Avoid_: retention throttle, cleanup retention

**Temp File Route**: 原地清除需备份时先复制 `%TEMP%/PP_*` temp，清完原子回填（FileTaskPipeline）。_Avoid_: temp copy route, safe modify route

**Locale Persist**: `ui.locale` 字段经 SelectionChanged→防抖 500ms 写盘（ADR 0044/0037）。_Avoid_: language setting, culture config

**Hot Folder Guard**: 非 dry_run 时空 hot_folder 拒绝启动抛校验异常（ADR 0045）。_Avoid_: empty folder fallback, silent skip

**IPC Log Pull**: `GetRecentLogs` 拉 Worker 审计尾部补 FSW 盲区（ADR 0046）。_Avoid_: log push, audit stream

**i18n Full Coverage**: code-behind/服务层中文全部提取 locale JSON；状态判断用 enum/布尔不用字符串 Contains（ADR 0047）。_Avoid_: AXAML-only i18n, partial localization

**Tray i18n Refresh**: TrayHost 订阅 CultureChanged 自动刷新菜单项（ADR 0047）。_Avoid_: manual tray refresh, tray static labels

**Nested Locale JSON**: flat key 迁移嵌套结构按功能分组（ADR 0047）。_Avoid_: flat locale keys, dot-separated keys

**10-Language Expansion**: 4→10 语言，key 与 en.json 对齐校验（ADR 0047）。_Avoid_: 4-language limit, hardcoded language list

**Semi.Avalonia**: Semi Design 的 Avalonia 实现，替代 FluentTheme 作主题基座（MIT，ADR 0048）。_Avoid_: FluentTheme overlay, custom theme from scratch

**Ursa.Avalonia**: Avalonia 控件扩展库（MIT），与 Semi 搭配（ADR 0048）。_Avoid_: hand-rolled custom controls

**Semi Token**: SemiColor*/SemiBackground* 语义 token 替代硬编码色值（ADR 0048）。_Avoid_: AppBgBrush, hardcoded hex colors in XAML

**Surface Depth**: 背景四层语义 Background<Background1<Surface<Overlay（ADR 0048/0050）。_Avoid_: flat single-bg, arbitrary opacity stacking

**Community Theme ResourceDictionary Override**: 社区主题=ResourceDictionary 覆写 token 经 MergedDictionaries 合并，非 ThemeVariant.Create（ADR 0048 A4）。_Avoid_: hardcoded theme switching, per-theme conditional compilation

**DesignTokens.axaml**: 设计 token 文件（spacing/radius/motion ramp），全局 DynamicResource（ADR 0048）。_Avoid_: inline magic numbers in XAML

**fonts: scheme**: `fonts:Inter#Inter` 必须经 `.WithInterFont()` 注册才生效（commit db152c9，DesignSystemTests 锁定）。_Avoid_: 手写 DefaultFontFamily 而不调 WithInterFont；假设 Assets/Fonts/ 存在

**Source-lint Test**: 编译期源码文本断言（防措辞/结构回潮）；约定见 tests/TEST-CONVENTIONS.md。_Avoid_: runtime-only UI test

**WithInterFont Registration**: `fonts:Inter#Inter` 唯一注册方式（Program.cs `.WithInterFont()`）。_Avoid_: DefaultFontFamily 手赋；自写 EmbeddedFontCollection

**IPC Probe Resilience**: 探测必须 catch `JsonException`——stale pipe 坏 JSON 返回 false 不杀 UI（commit a6ef02b，ADR 0035）。_Avoid_: 只 catch IO/Timeout 漏 JsonException

**Padding Literal Quantization**: Thickness 用字面量串触发 TypeConverter；禁 DynamicResource Double 赋 Thickness（commit 92f7790）。_Avoid_: DynamicResource 直赋 Thickness（InvalidCastException）

**Surface Depth 4-Layer + Elevation Shadow**: 四层背景+SemiElevation token 投影（ADR 0050 A1）。_Avoid_: 平铺无深度；自写 BoxShadow 散值

**Typography 6 Role Class**: `.display/.headline/.title/.body/.caption/.mono` 六角色绑 token（ADR 0050 A1）。_Avoid_: TextBlock 行内 FontSize/FontWeight 散布

**Middle-Click Autoscroll Behavior**: Files.App MIT 移植 attached behavior，ScrollViewer 一行 IsEnabled 挂载（ADR 0050 A3）。_Avoid_: 假设 Avalonia 原生有中键 pan；自写未参考成熟实现

**WindowDrawnDecorations + ElementRole**: 托管 drawn decorations API+语义标记；自绘按钮仍需手写 click handler（ADR 0050 A4）。_Avoid_: PleasantUI 新依赖；误当 ElementRole 自动接管自绘按钮 click

**Unicode Status Symbol Cleanup**: 应用文本零 Unicode 状态符号（✓✗⚠），状态用 Material.Icons（ADR 0050 A2，LocalizationServiceTests）。_Avoid_: 文本混用 Unicode 符号

**Space Token Spend Cleanup**: Margin/Spacing 只许引用 Space* token（2-48 ramp，ADR 0051 A1）。_Avoid_: ad-hoc 字面量；等距均匀布局

**Elevation Shadow Ladder**: Elevation0/1/2/4 四级 BoxShadow token（ADR 0051 A1）。_Avoid_: 自写 BoxShadow 散值

**Button Transition Animation**: 按钮过渡=BrushTransition/ThicknessTransition/DoubleTransition ~150ms；scale(0.97) 按压方案已由 ADR 0054 去除（TransformOperationsTransition 在 Avalonia 12.1.1 不存在）。_Avoid_: 无 Transitions 硬切；WPF ScaleTransform；BackEaseInOut/ElasticEaseInOut

**RequestAnimationFrame Middle-Click Scroll**: DispatcherTimer→TopLevel.RequestAnimationFrame vsync 对齐，dt 帧率无关（ADR 0051 A3）。_Avoid_: 16ms timer 非 vsync；偏离 Files.App 常量

**Sidebar Nav Item (40px Icon+Text)**: nav 项 40px icon+text，active 用胶囊语言，尺寸权威在 AppTheme.axaml（ADR 0055 A1/0056 票01；ui-visual-standard §2）。_Avoid_: 44px 旧值；纯文字无 active 指示

**Exponential Scroll Smoothing**: 指数平滑 `v+=(targetV-v)(1-exp(-k·dt))` k≈15（ADR 0052 A2）。_Avoid_: 线性阶跃；DispatcherTimer；松键硬停

**Theme Swatch Grid**: 色板 RadioButton+WrapPanel；theme_id/theme_variant 双轴独立，派生值不回写（ADR 0052 A3）。_Avoid_: 主题无 UI 入口；硬编码色值；高饱和 Primary

**Titlebar Content Dedup**: 标题栏只放窗口控制钮，信息归侧栏头（ADR 0052 A4）。_Avoid_: 标题栏与侧栏重复；自绘 drag handler

**Resizable Sidebar (GridSplitter)**: 170–400px 可拖拽，DragCompleted→防抖 500ms 写 `ui.sidebar_width`（ADR 0052 A5）。_Avoid_: 固定 200px；无 Min/Max 约束；拖完不持久化

**Default Path Watermark**: 配置 TextBox Watermark 显示默认路径（DefaultPaths 三端兼容，ADR 0052 A6）。_Avoid_: 空 string 默认；无提示

**Native Tray Menu (Immutable)**: NativeMenuItem 纯数据类不可样式化，不做 Transitions（ADR 0052 A7）。_Avoid_: 给原生菜单加样式；为一致弃原生菜单

**Two-Phase Async Startup**: Main 同步入口+`Task.Run` 后台连 Worker+Dispatcher 回填（ADR 0053 M1）。_Avoid_: `async Task Main`；Main 内 GetResult/.Result/.Wait()

**Channel<T> File Pipeline**: `BoundedChannel(4096, Wait)` 生产-消费背压管线（ADR 0053 M3）。_Avoid_: GetFiles 全量加载；SemaphoreSlim 仅限流无背压

**Wipe Strategy Resolver**: 按扩展名分格式族派 `IWipeStrategy`，未知格式拒处理 `WipeResult.UnknownFormat`（ADR 0053 决策6a）。_Avoid_: 全格式统一 `-all=`

**Expert Mode Gate**: `IsExpertMode` ToggleSwitch+确认弹窗，危险列非专家置灰（ADR 0053 M6b）。_Avoid_: 默认放任改 ICC/MakerNotes/Adobe/Time/CommonIFD0

**Format Rules Store**: 自定义规则存 `config/rules.json`+500ms 防抖+恢复默认二次确认（ADR 0053 M6e）。_Avoid_: 仅内存修改；直接覆盖不弹确认

**ItemsRepeater (retired)**: 官方已退役，本项目禁用，列表/矩阵用 DataGrid/ListBox（ADR 0053 M4/M6c）。_Avoid_: 引入 ItemsRepeater 包

**Per-Format Warning**: 面板按格式族显警告（PDF 红/EPS 黄/HEIC 橙/未知拒绝，ADR 0053 M6f）。_Avoid_: 静默通过不警告

**Safe Color-Space Tags Wipe**: `-all= --icc_profile:all -tagsfromfile @ -colorspacetags`（ADR 0053 M6a）。_Avoid_: `-all=` 无回填致色变；RAW/HEIC/PNG 全 ICC 删

**Nav Group Split**: 主导航顶组+utility 底组分组（ADR 0055 A1）。_Avoid_: 动态可见项混主导航组留空位

**RAF Watchdog**: RAF 停摆>32ms 由 DispatcherPriority.Render 16ms 保底步进，dt 走 Stopwatch（ADR 0055 A2）。_Avoid_: 纯 Background timer 饥饿；纯 RAF 冻结

**Theme Preset i18n**: 预设名绑 `preset.*` key，禁硬编码（ADR 0055 A3）。_Avoid_: swatch TextBlock 硬编码预设名

**App-Local Hot Folder**: 默认 `BaseDirectory/hot` 非系统图片目录；`Backup.Directory` 空值=运行时动态解析（ADR 0055 A4/0045）。_Avoid_: 指向 MyPictures；子目录名与 BackupPathResolver 不一致

**Button Size Ladder**: 按钮尺寸权威 AppTheme.axaml（内容 32/侧栏 40/icon 32×32），禁行内覆盖，FontSize 只许 token 梯度（ADR 0056 票01，DesignSystemTests）。_Avoid_: 行内 Padding/Height/MinWidth 覆盖；非 token 字号

**Launcher Open**: 打开目录一律 `TopLevel.Launcher.LaunchDirectoryInfoAsync`（ADR 0056 票02，UiLauncherSourceTests）。_Avoid_: UseShellExecute=true；explorer/xdg-open 分支

**Typed WorkerIpcClient (单入口)**: UI↔Worker IPC 唯一入口 WorkerIpcClient，版本经 GetStatus DTO（ADR 0056 票04）。_Avoid_: WorkerProcessManager 复述 IPC 方法；UI 直建 transport

**AuditTail Backfill Coordinator**: 日志双通道（FSW tail+IPC backfill）协调唯一驻留 AuditTailService（ADR 0056 票05）。_Avoid_: MainWindow 再建 backfill；去重外置

**ServiceModeController**: 服务模式编排唯一承载（Services/ServiceModeController.cs），依赖缝 IServiceManagerOps/IUiHost/IViewModelView（ADR 0056 票06；ADR 0053 Errata 由误称 StartupCoordinator 更正）。_Avoid_: MainWindow 编排回潮；UI spawn exiftool

**Composition Root (AppComposition)**: DI 唯一装配点（Composition/AppComposition.cs）；容器外手写 new 仅限 CompositionRootSourceTests 登记的视图缝白名单。_Avoid_: 未登记服务层手写 new；绕过组合根

**WindowPollingHostedService**: 版本 1s+服务状态 3s 轮询 IHostedService，InitializeRuntime 后 Activate（Services/WindowPollingHostedService.cs；ADR 0025 同构）。_Avoid_: code-behind Task.Run 轮询；构造直注 ServiceModeController；忘 Activate

**UI Visual Standard**: 页面级视觉规范唯一权威=docs/design/ui-visual-standard.md 活文档（ADR 0065）。_Avoid_: 规范全文冻结进 ADR；在 CONTEXT 复述细则

**Visual Baseline**: 4 页×关键态同机位截图基线（docs/design/screenshots/，拍摄规程见 ui-visual-standard.md §7）；不上 CI 像素 diff。_Avoid_: 无基线一次性探活；CI 像素门禁

**Nav Capsule Language**: nav 三态=idle 透明 / hover 中性半透明胶囊 / active 主色实心胶囊+on-primary 深字+SemiBold（ui-visual-standard §2；ADR 0069）。_Avoid_: hover 无实体底色块；active 与 hover 同浅色

**Rounded Window (DWM Corner Preference)**: MainWindow.axaml 显式 `WindowCornerPreference="Round"`，Win10 自动直角 no-op（ui-craft2 D-004）。_Avoid_: 透明自绘圆角；依赖 Default 默认值；读回 DwmGetWindowAttribute 断言

**MangoDisk Benchmark**: UI 观感复刻基准=harry0703/MangoDisk（GPL-3.0 只看观感不拷代码，ADR 0069）。_Avoid_: 拷 GPL 代码；半新半旧混排；误把 Web CSS 能力当 Avalonia 免费能力

**CI Gate**: push/PR 自动跑 `dotnet test --filter "Category!=Smoke&Category!=ExifTool"` 的云端验收闸（.github/workflows/ci.yml）。_Avoid_: 以本机构建/测试替代 CI 终判
