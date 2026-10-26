# AGENTS.md — PhotoPrivacy

> AI agent 工作规范。所有交互使用中文。

PhotoPrivacy：批量清理照片/视频/文档隐私元数据的 C#/.NET 10 跨平台桌面工具。Avalonia UI + 后台 Worker + 服务 Worker，IPC 走命名管道 / Unix socket。

## 怎么跑

- 构建：`dotnet build PhotoPrivacy.sln`（CI 口径 `--configuration Release`）。
- 测试：`dotnet test PhotoPrivacy.sln --configuration Release --filter "Category!=Smoke&Category!=ExifTool"`。
- 发布/安装/门禁：`scripts/publish-app.ps1` 或 `scripts/publish.sh`；`scripts/install-service.ps1`、`scripts/install-systemd-service.sh`、`scripts/install-launchd-service.sh`；`scripts/release-readiness.ps1`；发布属性只经脚本 `/p:` 传入，禁写回 csproj。

## 别改的目录

`release/`、`logs/`、`photo-snapshots/`、`bin/`、`obj/` 是本地产物；`config/config.json` 含敏感信息不提交（模板为 `config.sample.json`）。

## 代码里看不出来的坑

- ExifTool 必须 stay_open 长驻，禁每文件 spawn；备份镜像相对路径、同内容跳过、分歧写旁路版本、原子写；IPC 认证以 OS 身份为边界；UI 线程与后台线程禁 sync-over-async；未知格式立即跳过；视觉尺寸/字号只消费 DesignTokens token，按钮/nav 尺寸权威在 AppTheme.axaml。

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues, operated through the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles map one-to-one to `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: domain vocabulary lives in `GLOSSARY.md` at the repo root. See `docs/agents/domain.md`.

### Design markers

写或改 `// Design(#<issue>): <决策>` 注释时读 `docs/agents/design-markers.md`（选号、挂级、正文取舍）。
