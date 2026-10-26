# PhotoPrivacy Glossary

批量清理照片/视频/文档隐私元数据的 C#/.NET 10 跨平台桌面工具。本文件定义领域与治理术语；机制与取舍见代码注释与 `docs/design/`。

## 治理

**Design Marker（决策标记）**：源码里可检索的架构决策锚 `// Design(#<issue>): <决策>`，紧贴受约束声明；issue 号 = 决策存续出处。取舍判据见 `docs/agents/design-markers.md`，形态由 `DesignMarkerSourceTests` 守。

## 业务

**Audit Log**：业务审计日志（JSONL，路径脱敏+哈希），记录 wipe、backup、config 事件。
**Diagnostic Log**：运行诊断日志（启动/配置/ExifTool 交互/异常），统一走 Serilog。
**Backup**：源文件清理前的原始副本；镜像相对路径布局，同内容跳过、分歧内容写时间戳旁路版本，绝不静默覆盖。
**Quarantine**：处理失败或可疑文件的隔离目录。
**Processed Record**：已处理文件的 NDJSON 追加记录，崩溃恢复后据此幂等跳过。
**Hot Folder**：Worker 监听并自动处理新文件的热目录。
**Atomic Write**：写临时文件后原子替换，防止崩溃留下半写文件。
**Compaction**：NDJSON 日志按过期时间重写收敛大小。
**Temp File Route**：原地清除需备份时先复制到临时文件，清完原子回填。

## 配置与清理

**Schema Version**：配置 schema 版本（当前 1），缺失字段用 DTO 默认值兜底，不主动回写。
**Config Reload Validation**：重载配置前校验 schema 与 ExifTool 路径，失败保留旧配置。
**Format Rules Store**：自定义清理规则持久化到 `config/rules.json`。
**Hot Folder Guard**：非 dry-run 模式下 hot_folder 为空时拒绝启动。
**Expert Mode Gate**：危险元数据组的专家模式开关与确认。
**Wipe Strategy Resolver**：按扩展名格式族选择清理策略，未知格式拒绝处理。
**Safe Color-Space Tags Wipe**：全量清除后回填 ICC 色彩标签，避免色偏。

## IPC 与服务

**IPC Transport**：Named Pipe（Windows）与 Unix Domain Socket（Linux/macOS）双后端抽象。
**Unix Domain Socket**：按模式分为 `worker.sock` 与 `worker-background.sock`。
**Service State Probe**：三平台服务状态探测，统一为枚举。
**Service Command Executor**：三平台服务操作，经提权辅助进程调用安装脚本。
**Flock Single Instance**：Unix 用锁文件实现单实例；Windows 用命名 Mutex。
**Heartbeat Service**：UI 后台心跳 ping Worker，指数退避至 60s。
**Stale Socket Cleanup**：bind 前删除残留 socket。
**Endpoint Ownership**：仅端点属主在释放时删除 socket，客户端不删共享端点。
**SIGHUP Reload**：SIGHUP 触发配置热载，异常只记日志、不阻塞信号线程。
**Zombie Reap**：ExifTool 子进程经 Exited→WaitForExit 回收，防 Unix 僵尸。
**Shared Group Model**：服务与 GUI 用户共享组和 setgid 目录。
**IPC Protocol Version**：消息可选 `v` 字段（默认 1），新功能以方法名扩展。
**Mode-Scoped Shutdown**：仅 CLI 模式可经 IPC 触发 Shutdown。
**Post-Action Probe Retry**：服务操作后指数退避重试探测。
**Elevation Verb**：提权入口统称 runas/pkexec/osascript。
**PS5.1 Compat**：Windows 脚本兼容 PowerShell 5.1。
**IPC Log Pull**：`GetRecentLogs` 从 Worker 拉审计尾部，补 FSW 盲区。
**Two-Phase Async Startup**：Main 同步入口，后台连接 Worker 后经 Dispatcher 回填。
**Channel&lt;T&gt; File Pipeline**：BoundedChannel 背压的生产-消费文件管线。
**WindowPollingHostedService**：版本 1s、服务状态 3s 轮询的后台宿主服务。
**ServiceModeController**：服务模式编排的唯一承载。
**AuditTail Backfill Coordinator**：FSW tail 与 IPC backfill 双通道的唯一协调者。
**Typed WorkerIpcClient**：UI↔Worker IPC 的唯一入口。
**Composition Root**：DI 唯一装配点 `Composition/AppComposition.cs`。

## 本地化

**Locale Path Resolver**：三层 locale 路径解析的唯一入口。
**Three-Layer Locale**：嵌入资源→exe 同目录→用户配置目录三层覆盖。
**Localize Extension**：XAML `ex:Localize` MarkupExtension，语言切换自动刷新。
**RTL**：阿拉伯语从右到左，CultureChanged 时绑定 FlowDirection。
**i18n Full Coverage**：code-behind/服务层文案全部走 locale JSON，状态判断用 enum/布尔。
**Nested Locale JSON**：locale key 按功能分组嵌套。
**10-Language Expansion**：支持 10 种语言，key 与 `en.json` 对齐。

## 发布

**RID**：.NET 运行时标识符；本项目 7 个。
**Shell（发布壳）**：编译产物不含 ExifTool 二进制，用户自配路径。
**Release Directory**：`release/<rid>/` 平铺输出，UI 与 Worker 可执行文件同目录。
**Publish Profile**：发布属性经发布脚本 `/p:` 传入，不写入 csproj。
**Manual Dispatch**：release.yml 仅手动触发；ci.yml 在 push/PR 自动跑测试。
**Deb Package**：Linux .deb 包，control 声明 Avalonia native 依赖。
**App Bundle**：macOS .app 目录结构。
**LaunchDaemon**：macOS 系统服务管理器（plist + launchctl）。

## UI

> 旧 UI 视觉词条（`UI Visual Standard`、`MangoDisk Benchmark` 等）均指冻结旧 Avalonia UI；新 UI 的视觉标尺见 #49 T4。

**Aesthetic Anchor**：新 UI 的观感“像不像”标尺；六锚清单与取舍见 #49 T4，MangoDisk 仅保留给冻结旧 UI。
**Semi.Avalonia**：Semi Design 的 Avalonia 实现，主题基座。
**Ursa.Avalonia**：Avalonia 控件扩展库，与 Semi 搭配。
**Semi Token**：SemiColor*/SemiBackground* 语义 token，替代硬编码色值。
**Surface Depth**：背景四层语义 Background<Background1<Surface<Overlay。
**DesignTokens.axaml**：设计 token 文件（spacing/radius/motion），全局 DynamicResource。
**Typography 6 Role Class**：`.display/.headline/.title/.body/.caption/.mono` 六角色绑 token。
**Nav Capsule Language**：nav 三态 idle 透明 / hover 中性半透明胶囊 / active 主色实心胶囊。
**Nav Group Split**：主导航顶组 + utility 底组。
**Theme Swatch Grid**：色板单选，`theme_id`/`theme_variant` 双轴独立持久化。
**Resizable Sidebar**：170–400px 可拖拽，拖拽结束后防抖持久化。
**Middle-Click Autoscroll Behavior**：中键自动滚动的 attached behavior。
**Native Tray Menu**：原生菜单不可样式化，不做过渡。
**Unicode Status Symbol Cleanup**：文本不用 ✓✗⚠，状态用 Material.Icons。
**ItemsRepeater (retired)**：官方已退役，本项目禁用。
**Rounded Window**：DWM 圆角偏好 Round，Win10 自动 no-op。
**UI Visual Standard**：页面级视觉规范唯一权威 `docs/design/ui-visual-standard.md`。
**Visual Baseline**：4 页×关键态截图基线，不上 CI 像素 diff。
**MangoDisk Benchmark**：UI 观感复刻基准，只看观感不拷 GPL 代码。
**Source-lint Test**：编译期源码文本断言，约定见 `tests/TEST-CONVENTIONS.md`。
**CI Gate**：push/PR 自动跑过滤后的测试，是云端验收闸。
