# AGENTS.md — PhotoPrivacy

> AI agent 工作规范。所有交互使用中文。

PhotoPrivacy：批量清理照片/视频/文档隐私元数据的跨平台桌面工具。GUI 入口为 Rust gpui 新 UI（`ui/`，发布名 `PhotoPrivacy.exe`）+ C#/.NET 10 后台 Worker + 服务 Worker，IPC 走命名管道 / Unix socket；旧 Avalonia Ui 工程已随 #61 切换退役。

## 怎么跑

- 构建（dotnet 侧 CI-only，见 `tests/TEST-CONVENTIONS.md` §3）：`dotnet build PhotoPrivacy.sln`（CI 口径 `--configuration Release`）。
- 测试：CI 跑 `dotnet test PhotoPrivacy.sln --configuration Release --filter "Category!=Smoke&Category!=ExifTool"`；dotnet 侧本机只做静态门禁（`git diff --check` 等），CI-only 纪律见 `tests/TEST-CONVENTIONS.md` §3。
- 新 UI（Rust，`ui/`）：Windows 实机构建用 `cd ui && cargo build -p photoprivacy-ui --target x86_64-pc-windows-gnu`（需 `rustup target add x86_64-pc-windows-gnu` 与 MinGW；`windres` 须在 PATH 或由 `WINDRES` 指定，否则清单嵌入失败）；`cargo fmt --all -- --check`、`cargo clippy -p photoprivacy-ipc -p photoprivacy-logs -p photoprivacy-rules -p photoprivacy-service --all-targets -- -D warnings`、`cargo test -p photoprivacy-ipc -p photoprivacy-logs -p photoprivacy-rules -p photoprivacy-service -p photoprivacy-ui`、`cargo check -p photoprivacy-ui` 本机也要过（CI 跑同口径）。勿引入 `gpui-pre-platform`（原因见 `ui/Cargo.toml` 注释）。
- 发布（发版机专用，agent 不主动触发，由人执行）：`scripts/publish-app.ps1` 或 `scripts/publish.sh`（内含 `dotnet publish`，写 `release/`）；发布属性只经脚本 `/p:` 传入，禁写回 csproj。
- 安装：`scripts/install-service.ps1`、`scripts/install-systemd-service.sh`、`scripts/install-launchd-service.sh`。
- 发版门禁（发版机专用，agent 不主动触发，由人执行）：`scripts/release-readiness.ps1`（内含 test+smoke+publish）。

## 别改的目录

`release/`、`logs/`、`photo-snapshots/`、`bin/`、`obj/` 是本地产物；`config/config.json` 含敏感信息不提交（模板为 `config.sample.json`）。

## 代码里看不出来的坑

- ExifTool 必须 stay_open 长驻，禁每文件 spawn；备份镜像相对路径、同内容跳过、分歧写旁路版本、原子写；IPC 认证以 OS 身份为边界；UI 线程与后台线程禁 sync-over-async；未知格式立即跳过；新 UI 视觉与主题消费 theme.rs（排版先行、弱边框），观感以 #49 T4 六锚与 docs/agents/ui-smoke.md 为准。
- 改 IPC 客户端或新 UI 连接代码前读 `docs/agents/ipc-contract.md`（帧、超时、端点、重试、认证边界的事实源指针）。
- 改规则页或 `rules.json` 读写前读 `docs/agents/rules-contract.md`（五族×六组键名、缺键回退、原子写的事实源指针）；新 UI 手工冒烟与视觉证据口径见 `docs/agents/ui-smoke.md`。
- `GitButler Workspace Commit`（作者 `gitbutler@gitbutler.com`）是 GitButler 的集成提交，HEAD 上是它时按 `git log` / 提交数判断会失真。无配置开关（`but config`、`but config feature` 均无此项）；GitButler 0.23 发布说明称只 applied 一条分支时不再生成、多分支仍生成。别从它起新分支——包装提交会随 PR 落到 main（先例 `c6f0b028`），新分支从 `origin/main` 起。

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues, operated through the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles map one-to-one to `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: domain vocabulary lives in `GLOSSARY.md` at the repo root. See `docs/agents/domain.md`.

### Design markers

写或改 `// Design(#<issue>): <决策>` 注释时读 `docs/agents/design-markers.md`（选号、挂级、正文取舍）。
