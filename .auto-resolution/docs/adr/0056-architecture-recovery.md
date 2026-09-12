# ADR 0056: 架构恢复批次 —— UI 职责收敛四根支柱 + 脑/子窗口多轨工作流首轮实战

`Status: implemented` — 六票全部闭环，build 0 错误 0 SCS，Core 142/142，Integration 242/242（单槽串行）。

## 背景

用户在 2026-08-31 提出三大痛点：心智模型混乱、UI 越改越丑/按钮大小不一、内核不稳并与 IPC 纠缠。架构巡检（improve-codebase-architecture）+ 联网调研（30+ 一手来源）定位根因：**UI 进程承担了后台服务的职责**，且 ADR 0053 宣称的 MVVM 拆分未实际落地。

## 决策与落地（六票）

| 票 | 决策 | 落地 |
|---|---|---|
| 01 按钮 Size Ladder | AppTheme 是唯一权威；主按钮 Height=32 Padding=12,6；nav=40；新增 `Button.icon` 32×32 变体；视图全部行内覆盖清零 | AppTheme.axaml + MainWindow.axaml 12 处覆盖清零 + 5 个 source-lint guard |
| 02 TopLevel.Launcher | 打开目录从 `explorer/xdg-open/open` + `UseShellExecute=true` 改为 Avalonia 原生 `TopLevel.Launcher.LaunchDirectoryInfoAsync` | AGENTS.md §6 禁止项清除；3 个 guard |
| 03 IFolderWatcher 浅抽象 | 裁决（a）删除：接口零注入点（唯一消费方直接 new）；`IRecoveryScanner`/`IFileSystemWatcherFactory` 因真实测试替身而保留，移入 `WatcherInterfaces.cs` | 符合 Ponytail "删除胜过保留" |
| 04 IPC 客户端合并 | 全部协议方法收敛到 `WorkerIpcClient`（含新增 ProbeStatusSafeAsync 容错迁移）；WorkerProcessManager 只管进程启停 | 容错语义（JsonException/Socket/Timeout/IO 降级）不回归 |
| 05 日志双通道收口 | backfill 协调逻辑迁入 `AuditTailService`（backfillFetcher 契约 null=Worker 不可达；MergeBackfillLines 去重排序；_gate 内原子认领水位）；MainWindow BackfillRecentLogsAsync 删除 | 顺手修复三个真 bug：FlushPending 提前 return、backfill 水位前推丢行、无尾换行停滞饿死；9 个新测试 |
| 06 ServiceModeController | 新增 `Services/ServiceModeController.cs`（542 行）由 FakeOps/FakeHost/FakeView 三个缝注入；MainWindow.axaml.cs 由 2091 → 1531 行，13 个编排方法只剩转发器；`Process.Start(exiftool -ver)` 消除，版本探测走 IPC GetExifToolVersion | UI 不再直接引导后台服务时序 |

## 对 ADR 0053 的认账修正

ADR 0053 M2 宣称的 `StartupCoordinator`（Worker 连接、版本轮询、服务模式切换生命周期）经全仓 grep = **0 命中**，从未实现。05/06 落地后的实际承载是：

- Worker 连接：`StartupCoordinator` 所承担职责的实际现形为 `Program.cs` fire-and-forget + `MainWindow.axaml.cs InitializeRuntimeAsync`，未单独成类（现状，非概念承诺）。
- 服务模式编排：本 ADR 0056 由 `ServiceModeController` 承担，词源与 ADR 0053 不同——**将术语从"StartupCoordinator"更正为"ServiceModeController"**，避免宣称但实际不存在的类名残存于文档。

## 栈序实状与 pply 拓扑（但命令栈）

- 逻辑波次（issue Blocked by）：W1 = 01/02/03/04，W2 = 05(−04)，W3 = 06(−01−04)。
- GitButler 实栈（git log 实据）：
  - 栈 A：`arc-recovery/01-button-size-tokens` + `arc-recovery/03-folder-watcher-abstraction`（01 与 03 同文件测试共享，03 叠在 01 之上）；
  - 栈 B：`arc-recovery/04` → `02` → `06` → `05`（05 依赖 06 的 MainWindow 区域 hunk）。
- apply 顺序按实栈：01(A)→03(A)→04→02→06→05；或 04→02→06→05→01→03，彼此栈间不冲突。

## 做事流程（首轮脑/子窗口多轨制验证）

- 流程权威 `WORKFLOW.md` 固化于项目 `.scratch/architecture-recovery/`（gitignore）；偏离清单 8 项经用户批准后生效。
- 本批验证的子窗口专有纪律（已回流 WORKFLOW §7）：跨票共享文件一次只许一票碰；发现混入他人改动立即上报不自纠 amend；栈序可以动、他人内容绝对不可动；全量测试门禁由大脑收口单槽串行；报告断言以 `git log` 实物哈希为准。
- 工具教训：复杂结构写入用 Node 一次性脚本代替 shell 多段嵌套（本会话实测避免引号嵌套断连）。

## 遗留事项（backlog，待用户裁定是否立票）

1. `PublishApp_Should_Copy_Tray_Assets_To_Publish_Root` 的 publish-app.ps1 300s 预算在单机 Release 自包含构建即超 300s——建议加长预算或归入长时间冒烟 benchmark；与 dotnet 并行构建无锁冲突真实分离。
2. `EndToEndSmokeTests.cs` 无超时 WaitForExitAsync + watcher 进程不被清理，`ReleaseReadinessScriptValidationTests` 按进程名杀链——建议立一张"测试宿主守护"票（test.runsettings + MaxCpuCount=1 + Blame 收集超时）。
3. ~~FSW InternalBufferSize 默认 8KB~~ **勘误（2026-09-01 票03取证）**：监控目录 watcher 自 37837d5 起即 64KB；8KB 默认值仅适用于 AuditTailService 的审计 tail watcher。本项已由 backlog B03/票03 闭环（Error 并发合并守卫 + 64KB 落位锁定测试）。
4. `AuditTailService._backfillSucceeded` 无锁 bool——当前单 fetcher 线程安全，未来多 fetcher 需收进 `_gate`。
5. `WorkerIpcClient` 每 SendAsync 新建 transport 实例（原实现）——未来如需高频轮询可另票做连接复用/单例化。
6. source-lint 系列 helper 与 HardcodedChineseScanTests 同形重复——可在独立票抽公共断言 helper。
7. `UseShellExecute = needElevation` 变量形态盲区：source-lint 仅锁字面量 `= true`——如需"Ui 禁一切 shell 启动"的精神，可再扩展守卫。
8. ADR 0053 若后续补回 StartupCoordinator/God Object 100% MVVM 化，应先在 ADR 层面更正，再动代码。
