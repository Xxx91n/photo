# B21 — UI 根目录层化 + 死代码清理

- **优先级**: 中　**来源**: 架构恢复第五轮宏观评估（票 20）

## 问题与验收

UI 根目录 23 个 .cs 未层化，服务类与入口/DTO/策略混居；ServiceManager 顶部契约类型与实现混在一文件；Systemd/Launchd 探针各自复制一份相同的 RunProcess；Core 遗留零引用死代码 `InFlightRegistry` 与 `IWipeStrategy`；`BackgroundUiOptions` 17 个公共可变属性被 5 处外部直写。验收：服务类归位 Services/ 且命名空间同步、契约抽 contracts、探针 RunProcess 合并、死代码删除（grep 零引用）、BackgroundUiOptions 公共面只读 + 写入收口具名方法、编译 + 既有测试全绿（纯移动无行为变化）。

## 完成记录（2026-09-03，票 20）

- 检查点 A：13 个服务类文件移入 `src/PhotoPrivacy.Ui/Services/` 并同步命名空间 `PhotoPrivacy.Ui.Services`（WorkerIpcClient/WorkerProcessManager/ServiceManager/ConnectionStateService/AuditTailService/TrayHost/ConfigEditor/UiSingleInstance/UiDiagnosticLog + Systemd/Launchd 状态探针与命令执行器共 4 个 ServiceManager 卫星实现）；BackgroundUiOptions/ConfigEditCommand 按 issue 清单留在根目录（DTO 非服务类）。根目录 .cs 由 23 → 10。
- 检查点 B：`ServiceCommandStatus/ServiceCommandResult/IScCommandExecutor/ServiceRuntimeState/IServiceStateProbe` 抽至 `Services/ServiceManagerContracts.cs`（ServiceStateProbe/ScCommandExecutor 随迁）；Systemd/Launchd 探针重复 RunProcess 逐字合并为 `Services/ServiceProbeProcess.cs`。
- 检查点 C：删除 `Core/Queue/InFlightRegistry.cs` 与 `Core/ExifTool/WipeStrategies/IWipeStrategy.cs`（全仓 grep 零引用；CONTEXT.md 措辞漂移属票 21 范围）；`BackgroundUiOptions` 收敛为公共面只读（init-only 构建期 12 属性 + private set 运行期 7 属性），外部写入经 `UpdateRuntimeState/UpdateHideFlags/SetShowMainWindow/SetConnectionState` 四个具名方法收口（Program/App/ServiceModeController/MainWindow + 测试共 9 个写入点全部改道），保持单实例共享语义，无快照替换的陈旧引用风险。
- 门禁：build 0 错 0 SCS；Core.Tests 与 IntegrationTests 单槽串行全绿；semgrep p/csharp 0 新增。
- 报告：`.scratch/architecture-recovery/report-20-ui-root-layering-deadcode.md`（受控副本 `docs/process/reports/20-ui-root-layering-deadcode.md`）。
