# AGENTS.md — PhotoPrivacy

> AI agent 工作规范。所有交互使用中文。

## 项目定位

PhotoPrivacy：批量清理照片/视频/文档隐私元数据的 C#/.NET 10 跨平台桌面工具。三进程架构：Avalonia UI + 后台 Worker（托盘）+ 服务 Worker（Windows Service / systemd / launchd），进程间经命名管道 / Unix socket IPC。

## 技术栈

C# 13 / .NET 10（预期 SDK 10.0.201）/ Avalonia 12.1.1 / ExifTool stay_open / Named Pipes + Unix Domain Socket / Serilog / xUnit + Shouldly + Moq。包管理 Central Package Management（Directory.Packages.props）。

## 怎么跑

- 构建：`dotnet build PhotoPrivacy.sln`（CI 口径 `--configuration Release`）
- 测试：`dotnet test PhotoPrivacy.sln --configuration Release --filter "Category!=Smoke&Category!=ExifTool"`（与 ci.yml 同口径；Smoke/ExifTool 类需实机环境）
- 发布：`scripts/publish-app.ps1`（Windows）/ `scripts/publish.sh`（Linux/macOS）——发布属性只经脚本 `/p:` 参数传入，禁写回 csproj
- 发布前一键门禁：`scripts/release-readiness.ps1`（构建+冒烟+SCS 安全扫描）
- 安全扫描：`semgrep scan --config p/csharp --config p/security-audit ./src`；构建期 SecurityCodeScan 自动跑（SCS 警告不得新增）

## 目录约定

- `src/`：四工程——Core（纯逻辑）/ Ipc（管道+socket）/ Worker（清理+服务宿主）/ Ui（Avalonia）
- `tests/`：Core tests + IntegrationTests；约定见 tests/TEST-CONVENTIONS.md
- `docs/adr/`：架构决策记录（只记难逆转+无上下文会惊讶+真实取舍的决定，单段式）
- `docs/backlog/`：开放事项索引（open-items-*.md 为唯一题源）
- `docs/design/`：视觉规范活文档（ui-visual-standard.md）
- `config/config.json`：敏感信息不提交，`config.sample.json` 模板提交
- `.scratch/`、`.codex-tmp/`、`photo-snapshots/`、release/、logs/：本地目录不入版本控制

## 工程红线

以下产品级规则由守卫测试或历史事故钉死，违反=CI 红或真实安全风险：

- **fail-closed 五不变量**：未知格式跳过不挂死；无意外用户数据丢失；最小攻击面；驻守可观测；公开宣称=实现。
- **ExifTool 一律 stay_open 长驻**，禁每文件 spawn；`UseShellExecute=false`；参数走 `ArgumentList`；退出经 Exited→WaitForExit 回收防僵尸。
- **备份语义**：镜像相对路径布局（ADR 0068）；目标已存在时同内容跳过、分歧内容写时间戳旁路版本，绝不静默覆盖；写盘一律原子写（temp+rename/File.Replace）。
- **路径校验**：所有外部路径过验证；备份/隔离区拒绝 reparse point 链接逃逸与空值回退 CWD。
- **IPC 认证=OS 身份边界**（ADR 0067）：Windows `PipeOptions.CurrentUserOnly`+首实例 `FirstPipeInstance`（仅预建首实例生效）；Unix socket `0600`（CI）/`0660`+组边界（服务）+`SO_PEERCRED`/`getpeereid` 对端校验；同 OS 用户进程不在防御范围；X-External-Requests 同源策略不受信（不随 AOT 发布）。
- **禁 sync-over-async**：UI 线程与后台线程均禁 `.Result`/`.Wait()`/`GetAwaiter().GetResult()` 调 IPC 等异步工作；信号回调（SIGHUP）异步编组不阻塞信号线程（WorkerIpcSyncOverAsyncGuardTests）。
- **NamedPipe 禁 `NamedPipeServerStreamAcl.Create`**（跨平台不安全，ADR 0035）；不依赖 pipe 名保密/客户端 PID 校验/abstract socket。
- **配置防抖即时写盘**（500ms，无"应用"按钮）；清空日志必须重置读取偏移；审计/诊断日志敏感信息脱敏。
- **Service/Background 双模式独立**：各持 Mutex 单实例锁 + 独立 IPC 端点；服务模式必须有文件日志 sink 保可观测。
- **FSW 事件驱动**，禁轮询为主路径；FileStream/进程/管道资源一律 `using`/`await using`；安全敏感异常不静默吞。
- **i18n**：UI 文案禁硬编码中文，走 `LocalizationService`+locale JSON；语义判断用 enum/布尔不用字符串 `Contains`；新文案 10 语言全 key 覆盖。
- **视觉纪律**：间距/尺寸/字号只消费 DesignTokens token；禁视图行内硬编码覆盖；按钮/nav 尺寸单一权威 AppTheme.axaml（DesignSystemTests 钉死）。

## 指针

- 开放事项：docs/backlog/（唯一题源索引 `open-items-2026-10-14.md`）
- 领域词汇：CONTEXT.md；视觉规范：docs/design/ui-visual-standard.md；测试约定：tests/TEST-CONVENTIONS.md
- 决策记录：docs/adr/
- VCS 纪律：GitButler（`but`）承担全部版本控制写操作；一任务一分支；提交信息中文；不 push、不建 PR 除非用户明令。
