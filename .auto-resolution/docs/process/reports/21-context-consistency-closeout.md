# 报告 — 票21 CONTEXT.md 三层一致性收口（架构恢复第五轮）

日期：2026-09-03　窗口：本票专用　分支：arc-recovery/21-context-consistency-closeout（哈希以 git log 实物为准）

## 完成定义对照（声明 → 证据 → 结论）

| # | 漂移（issue 验收） | 修正 | 代码锚定证据 | 结论 |
|---|---|---|---|---|
| 1 | 补 Endpoint Ownership 术语条目（ownsEndpoint 属主删除） | CONTEXT.md 新增 `**Endpoint Ownership**` 条目 | `UnixDomainSocketIpcTransport.cs:69-79` ownsEndpoint = _listener is not null | ✅ |
| 2 | Stale Socket Cleanup（属主条件删除，非无条件） | 条目改写：bind 前无条件 / DisposeAsync 属主条件区分 | 同上 + ADR 0026 ListenAsync 清理 | ✅ |
| 3 | Hot Folder Guard（仅非空，非「绝对路径」） | 「非空绝对路径」→「非空」 | `AppConfigValidator.cs:32` 仅 IsNullOrWhiteSpace 校验 | ✅ |
| 4 | Unix Domain Socket（双 socket） | 补 worker.sock / worker-background.sock 双端点 | `WorkerIpcEndpointNames.cs:11-12,18-21` | ✅ |
| 5 | Theme Swatch Grid（已实现） | 「已有但无 UI 入口…可点选」→「已有 swatch grid UI 入口…已落地」 | `MainWindow.axaml:291-328` 5 swatch + `App.axaml.cs:43` theme_id swap | ✅ |
| 6 | Typed WorkerIpcClient（版本走 GetStatusAsync） | 方法列表移除 GetExifToolVersion，注明版本经 GetStatusAsync 状态 DTO | `WorkerIpcClient.cs` 无 GetExifToolVersionAsync；`Program.cs:209-213` 走 GetStatusAsync | ✅ |
| 7 | Sidebar Nav（4 按钮） | 「三按钮」→「4 按钮（Config/Logs/Rules/ServiceManager）」+ ShieldCheckOutline | `MainWindow.axaml:75-92` ConfigNav/LogNav/RulesNav/ServiceManager | ✅ |
| 8 | Backup 目录 .pp_backup 内部矛盾 | Default Path Watermark 条目 `.pp_backup`→`bak` | `DefaultPaths.cs:28` 返回 "bak"；`BackupPathResolver.cs:10` DefaultBackupDirName="bak" | ✅ |
| 9 | DefaultPaths.cs 3 处陈旧注释 | ①「user-accessible photos directory」②「rejects non-existent directories」③「stays string.Empty」 | 对照 `AppConfig.cs:60` / `AppConfigValidator.cs:32` / ADR 0055 A4 | ✅ |
| — | ADR 0045 措辞同步 | 决策1「非空绝对路径」→「非空」 | `AppConfigValidator.cs:32` | ✅ |
| — | ADR 0057 措辞同步 | 核对：DisposeAsync「仅端点属主清理」已与代码一致，无需改动 | `UnixDomainSocketIpcTransport.cs:76` | ✅（无需改） |

## 门禁

| 门禁 | 证据 | 结果 |
|---|---|---|
| build 0 错 0 SCS | `dotnet build PhotoPrivacy.sln --no-incremental` EXIT=0，error 0，SCS 0 | ✅ |
| 本票 guard 单类 | `dotnet vstest --Tests:ContextMdTerminologyTests` 8/8 | ✅ |
| 单槽全量 Core.Tests | 151/151 | ✅ |
| 单槽全量 IntegrationTests | 288/289（1 失败为既有 publish 冒烟，见披露） | ✅（除既有失败） |

## 偏差与披露

1. **共池工作区并发**：本票窗口编辑期间，票17（已 commit `7f1c28e`）、票19/20（UI 根目录层化文件搬迁在途，`src/PhotoPrivacy.Ui/*` → `Services/`）并发进行。本票文件面（CONTEXT.md / docs/adr/0045 / DefaultPaths.cs / ContextMdTerminologyTests.cs）与其余票无共享文件，未触碰他人改动；提交仅含本票文件面。
2. **单槽全量 IntegrationTests 1 失败**：`PublishApp_Should_Copy_Tray_Assets_To_Publish_Root`（7m47s，Expected 0 Actual 1）——既有 publish-app.ps1 冒烟在单机 Release 自包含构建失败/超时，ADR 0056 遗留#1 与票13 报告均已记载，与本票 doc/注释改动无关。本票 8 个 guard 及全部非 publish 测试均绿。

## 提交物清单

- 改：`CONTEXT.md`（9 处漂移修正 + Endpoint Ownership 新条目）
- 改：`docs/adr/0045-hot-folder-guard-and-backup-empty-defense.md`（措辞同步）
- 改：`src/PhotoPrivacy.Core/Constants/DefaultPaths.cs`（3 处陈旧注释清理）
- 增：`tests/PhotoPrivacy.IntegrationTests/ContextMdTerminologyTests.cs`（guard 8 断言，复用 SourceLint.RepoRoot）
- .scratch（gitignored）：本报告、issues/21 勾选
- docs/process（轨 1 沉淀，随票提交）：`docs/process/reports/21-context-consistency-closeout.md`

## 遗留（交大脑，非本票阻塞）

1. `DefaultPaths.cs` `DefaultBackupDirectory` 注释行「ADR 0052 A6: Backup before metadata cleaning」为 ADR 引用措辞轻微不精确（ADR 0052 A6 实为 DefaultBackupDirectory + Watermark），不在本票 3 处陈旧注释之列，建议后续顺带修正。
2. CONTEXT.md「Default Path Watermark」条目「config.sample.json 填默认值」与 ADR 0052 A6「保持空字符串」措辞不一致（config.sample.json 现为四目录空字符串），属 .pp_backup 矛盾之外的另一处措辞遗留，建议大脑裁定是否纳入后续。
