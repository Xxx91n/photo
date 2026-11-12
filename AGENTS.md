# AGENTS.md — PhotoPrivacy

> AI agent 工作规范。所有交互使用中文。

PhotoPrivacy：批量清理照片/视频/文档隐私元数据的跨平台桌面工具。GUI 入口为 Rust gpui 新 UI（`ui/`，发布名 `PhotoPrivacy.exe`）+ C#/.NET 10 后台 Worker + 服务 Worker，IPC 走命名管道 / Unix socket；旧 Avalonia Ui 工程已随 #61 切换退役。

## 怎么跑

- 构建（dotnet 侧 CI-only，见 `tests/TEST-CONVENTIONS.md` §3）：`dotnet build PhotoPrivacy.sln`（CI 上用 `--configuration Release`）。
- 测试：CI 跑 `dotnet test PhotoPrivacy.sln --configuration Release --filter "Category!=Smoke&Category!=ExifTool"`；dotnet 侧本机只跑静态检查（`git diff --check` 等），CI-only 纪律见 `tests/TEST-CONVENTIONS.md` §3。
- 新 UI（Rust，`ui/`）：Windows 实机构建用 `cd ui && cargo build -p photoprivacy-ui --target x86_64-pc-windows-gnu`（需 `rustup target add x86_64-pc-windows-gnu` 与 MinGW；`windres` 须在 PATH 或由 `WINDRES` 指定，否则清单嵌入失败）；`cargo fmt --all -- --check`、`cargo clippy -p photoprivacy-ipc -p photoprivacy-logs -p photoprivacy-rules -p photoprivacy-service --all-targets -- -D warnings`、`cargo test -p photoprivacy-ipc -p photoprivacy-logs -p photoprivacy-rules -p photoprivacy-service -p photoprivacy-ui`、`cargo check -p photoprivacy-ui` 本机也要过（CI 跑同样的检查）。勿引入 `gpui-pre-platform`（原因见 `ui/Cargo.toml` 注释）。
- 发布（发版机专用，agent 不主动触发，由人执行）：`scripts/publish-app.ps1` 或 `scripts/publish.sh`（内含 `dotnet publish`，写 `release/`）；SelfContained / PublishSingleFile / IncludeNativeLibrariesForSelfExtract / PublishTrimmed 只经脚本 `/p:`（self-contained 经 `--self-contained` 开关）传入，禁写回 csproj / 工程级 props；`PublishReadyToRun=true` 是唯一例外，中心化在 `Directory.Build.props`（#12 裁决，单一来源由 `Net10CompatibilityGuardTests` 锁定，同样勿写回 csproj / 工程级 props）。发布顺序与校验步骤的 runbook 见 `docs/release-runbook.md`（#26）。
- 安装：`scripts/install-service.ps1`、`scripts/install-systemd-service.sh`、`scripts/install-launchd-service.sh`。
- 发版前检查（发版机专用，agent 不主动触发，由人执行）：`scripts/release-readiness.ps1`（内含 test+smoke+publish）。

## 别改的目录

`release/`、`logs/`、`photo-snapshots/`、`bin/`、`obj/` 是本地产物；`config/config.json` 含敏感信息不提交（模板为 `config.sample.json`）。

## 代码里看不出来的坑

- ExifTool 必须 stay_open 长驻，禁每文件 spawn；备份镜像相对路径、同内容跳过、内容不一样就另存一份、原子写；IPC 认证以 OS 身份为边界；UI 线程与后台线程禁 sync-over-async；未知格式立即跳过；新 UI 视觉与主题消费 theme.rs（排版先行、弱边框），观感标准见 docs/agents/ui-smoke.md。
- 改 IPC 客户端或新 UI 连接代码前读 `docs/agents/ipc-contract.md`（帧、超时、端点、重试、认证边界都在里面说清了）。
- 改规则页或 `rules.json` 读写前读 `docs/agents/rules-contract.md`（五族×六组键名、缺键回退、原子写都在里面说清了）；新 UI 手工冒烟步骤和视觉证据要求见 `docs/agents/ui-smoke.md`。
- `GitButler Workspace Commit`（作者 `gitbutler@gitbutler.com`，提交标题即此串）会出现在工作区 HEAD，不代表你的工作，按 `git log` / 提交数判断会失真。`but config` 子命令里未找到关闭开关；别把分支建在它之上——以它为父，它会随 PR 落进 main（`c6f0b028` 先例），基点选 `origin/main`。

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues, operated through the `gh` CLI. See `docs/agents/issue-tracker.md`.

删文件、删机制、改文档章节时，先用 `gh search issues --state open "<被删的名字>"` 查还在引用它的票，同一批里改掉票面或关票，别让票面指向已经不存在的东西。

### Triage labels

Five canonical triage roles map one-to-one to `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

领域词汇统一放在仓库根目录的 `GLOSSARY.md`。怎么用它见 `docs/agents/domain.md`。
