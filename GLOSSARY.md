# PhotoPrivacy Glossary

批量清理照片/视频/文档隐私元数据的跨平台桌面工具。GUI 入口为 Rust gpui 新 UI（`ui/`），后台与服务为 C#/.NET 10 Worker。本文件定义领域与治理术语；机制与取舍见代码注释与 `docs/`。

## 治理

**Design Marker（决策标记）**：源码里可检索的架构决策锚 `// Design(#<issue>): <决策>`，紧贴受约束声明；issue 号 = 决策存续出处。取舍判据见 `docs/agents/design-markers.md`，形态由 `DesignMarkerSourceTests` 守。
**不变量①–⑤（运行不变量表）**：correctness-round（2026-09）拍的五条系统级正确性不变量，源码/测试以「不变量N」编号引用（含「不变量①④」连写与同句间隔两种形态）。原表体住 D 系决策账本 D-002（未入仓，fa360d6 拆治理层后散佚），本词条为对账重建：存续出处定 GLOSSARY、编号引用不折算不退役（#63）；引用与表体的对账由 `InvariantReferenceReconciliationTests` 守。
- **不变量①（fail-closed）**：不可读、不识别、异常路径一律显式拒绝或可见告警，绝不静默放行、不静默降级。
- **不变量②（数据零意外丢失）**：用户数据零意外丢失——备份镜像布局结构性消歧、restart 与停机窗口不丢在途任务。
- **不变量③（最小攻击面，全档执行不降档）**：攻击面保持最小且任何执行档位不降档豁免；IPC 认证只信内核身份边界、不信用户态可伪造信号（PID/进程路径）是它在 IPC 面的具体应用，非定义全体。考古恢复：现行仓内无 ③ 引用点，取义自 correctness-round 票 04 报告（原表述「最小攻击面，全档执行不降档」），非新提。
- **不变量④（驻守可观测）**：不允许「看起来在跑实际没在跑」——常驻收益（stay_open）不被摧毁、探针轻量不误杀、降级必留痕。
- **不变量⑤（宣称-实现一致）**：宣称的承诺由机器断言兑现——对账守卫随 CI 硬失败，宣称数字由测试产出。

**Stack Migration（换栈）**：UI 框架栈层面的整体更换，与栈内重构相对。
**In-Stack Refactor（栈内重构）**：不换 UI 框架，只改栈内的结构与呈现模式。

## 业务

**Audit Log**：业务审计日志（JSONL，路径脱敏+哈希），记录 wipe、backup、config 事件。文件名按本地「今天」分卷（`audit-<yyyy-MM-dd>.jsonl`），写入端、IPC 回填、UI tail 三处同口径（#129），UTC 只保留在行内 `timestamp_utc`。
**Shard（分卷）**：单日审计 JSONL 触及 100MB 上限后按 `_N` 序号续写的文件族；`GetRecentLogs`（IPC Log Pull）与 UI tail 只认基础名 `audit-<date>.jsonl`，分卷行不经日志页返回——记录在案的接受行为（#135，生命周期归 #41），非缺陷。写入侧 rollover 现状与设计不符，见 #144。
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
**Hot Folder Guard**：非 dry-run 模式下 hot_folder 为空不再阻断启动，Worker 改入 Standby（待机）；例外是启用 `once=true`——不限运行模式，都必须拿到有效目录，否则以退出码 1 结束。
**Standby（待机）**：hot_folder 为空时 Worker 的常驻就绪态——IPC 已连通但不监控任何目录；配置页保存有效目录后经 `ReloadConfig` 启用监控，无需重启 Worker。
**Expert Mode Gate**：危险元数据组的专家模式开关与确认。
**Wipe Strategy Resolver**：按扩展名格式族选择清理策略，未知格式拒绝处理。
**Safe Color-Space Tags Wipe**：全量清除后回填 ICC 色彩标签，避免色偏。

## IPC 与服务

**IPC Transport**：Named Pipe（Windows）与 Unix Domain Socket（Linux/macOS）双后端抽象。
**Unix Domain Socket**：按模式分为 `worker.sock` 与 `worker-background.sock`。
**Service State Probe**：三平台服务状态探测，统一为枚举。
**Service Command Executor**：三平台服务操作，经提权辅助进程调用安装脚本。
**Flock Single Instance**：Unix 用锁文件实现单实例；Windows 用命名 Mutex。
**Heartbeat Service**：UI 后台心跳通过 `GetStatus` 探测 Worker，指数退避至 60s。
**Stale Socket Cleanup**：bind 前删除残留 socket。
**Endpoint Ownership**：仅端点属主在释放时删除 socket，客户端不删共享端点。
**SIGHUP Reload**：SIGHUP 触发配置热载，异常只记日志、不阻塞信号线程。
**Zombie Reap**：ExifTool 子进程经 Exited→WaitForExit 回收，防 Unix 僵尸。
**Shared Group Model**：服务与 GUI 用户共享组和 setgid 目录。
**Service Pipe ACL**：Windows 服务管道显式 DACL（#70）——安装者 SID 由 install-service.ps1 登记，服务端建例时授予读写；SID 缺失 fail-closed 回退 CurrentUserOnly。
**IPC Protocol Version**：消息可选 `v` 字段（默认 1），新功能以方法名扩展。
**Mode-Scoped Shutdown**：仅 CLI 模式可经 IPC 触发 Shutdown。
**Post-Action Probe Retry**：服务操作后指数退避重试探测。
**Elevation Verb**：提权入口统称 runas/pkexec/osascript。
**PS5.1 Compat**：Windows 脚本兼容 PowerShell 5.1。
**IPC Log Pull**：`GetRecentLogs` 从 Worker 拉审计尾部，补 FSW 盲区。
**Channel&lt;T&gt; File Pipeline**：BoundedChannel 背压的生产-消费文件管线。
**IpcClient**：UI↔Worker IPC 客户端（Rust 实现 `ui/crates/photoprivacy-ipc`）。

## 本地化

**Locale Path Resolver**：三层 locale 路径解析的唯一入口。
**Three-Layer Locale**：嵌入资源→exe 同目录→用户配置目录三层覆盖。
**RTL**：阿拉伯语从右到左，渲染层按页镜像（`flex_row_reverse` 左右镜像）。
**i18n Full Coverage**：文案全部走 locale JSON，状态判断用 enum/布尔。
**Nested Locale JSON**：locale key 按功能分组嵌套。
**10-Language Expansion**：支持 10 种语言，key 与 `en.json` 对齐。

## 发布

**RID**：.NET 运行时标识符；本项目 7 个。
**Shell（发布壳）**：编译产物不含 ExifTool 二进制，用户自配路径。
**Release Directory**：`release/<rid>/` 平铺输出，UI 与 Worker 可执行文件同目录。
**Publish Profile**：发布属性经发布脚本 `/p:` 传入，不写入 csproj。
**Manual Dispatch**：release.yml 仅手动触发；ci.yml 在 push/PR 自动跑测试。
**Deb Package**：Linux .deb 包，打包可执行文件与运行时依赖。
**App Bundle**：macOS .app 目录结构。
**LaunchDaemon**：macOS 系统服务管理器（plist + launchctl）。

## UI

**Aesthetic Anchor**：新 UI 的观感“像不像”标尺；六锚清单与取舍见 #49 T4 与 `docs/agents/ui-smoke.md`。
**Nav Capsule Language**：nav 三态 idle 透明 / hover 中性半透明胶囊 / active 主色实心胶囊。
**Nav Group Split**：主导航顶组 + utility 底组。
**Native Tray Menu**：原生菜单不可样式化，不做过渡。
**Visual Baseline**：4 页×关键态截图基线，人工目检见 `docs/agents/ui-smoke.md`，不上 CI 像素 diff。
**Source-lint Test**：编译期源码文本断言，约定见 `tests/TEST-CONVENTIONS.md`。
**CI Gate**：push/PR 自动跑过滤后的测试，是云端验收闸。

> AI生成
