# 报告 — 票 20 UI 根目录层化 + 死代码清理（架构恢复第五轮）

日期：2026-09-03　窗口：票 20 专属　版本控制：WORKFLOW §4.2（GitButler 虚拟分支 `ticket-20-ui-root-layering-deadcode`，未 push）

## 声明 → 证据 → 结论对照

### 检查点 A — 服务类归位 Services/ + 命名空间同步

- **声明**：散在 UI 根目录的服务类归入 `Services/`，命名空间同步为 `PhotoPrivacy.Ui.Services`，目录结构反映真实分层（纯移动无行为变化）。
- **证据**：
  - 移动 13 个文件至 `src/PhotoPrivacy.Ui/Services/`：issue 点名的 9 个服务类（WorkerIpcClient/WorkerProcessManager/ServiceManager/ConnectionStateService/AuditTailService/TrayHost/ConfigEditor/UiSingleInstance/UiDiagnosticLog）+ ServiceManager 卫星实现 4 个（SystemdStateProbe/LaunchdStateProbe/SystemdCommandExecutor/LaunchdCommandExecutor——它们实现本票抽出的 IServiceStateProbe/IScCommandExecutor 契约，属服务管理子系统，随 ServiceManager 归位避免根目录残留碎片）。
  - 根目录 .cs 由 23 → 10：仅剩入口（App.axaml.cs/Program.cs）、DTO（AuditLogEntry/BackgroundUiOptions/ConfigEditCommand/ExifToolVersionSnapshot）、纯策略（ImmediateModeSwitchPolicy/MainWindowRuntimePolicy/ServiceUiPolicy/TrayPolicy）。BackgroundUiOptions/ConfigEditCommand 按 issue 清单留在根目录（DTO 非服务类）。
  - 命名空间同步：13 个移动文件声明行 `PhotoPrivacy.Ui` → `PhotoPrivacy.Ui.Services`；根消费方补 `using PhotoPrivacy.Ui.Services;`（App/Program/BackgroundUiOptions/ImmediateModeSwitchPolicy/ServiceUiPolicy）；Services/ 既有 3 文件（ServiceModeController 等）经 C# 外层命名空间解析零改动；19 个测试文件补 using。
  - 编译 0 错误；单槽首轮 IntegrationTests 281/281 绿（本轮跑出 294 为后续增量，见检查点 D）。
- **结论**：✅ 通过。

### 检查点 B — ServiceManager 类型抽 contracts + 探针 RunProcess 合并

- **声明**：ServiceManager 顶部 enum/record/接口抽到 contracts 文件；Systemd/Launchd 探针重复的 RunProcess 合并。
- **证据**：
  - `Services/ServiceManagerContracts.cs`（新建）：`ServiceCommandStatus`、`ServiceCommandResult`、`IScCommandExecutor`、`ServiceRuntimeState`、`IServiceStateProbe` 五类型原样迁出，Windows 默认实现 `ServiceStateProbe`/`ScCommandExecutor` 随迁（命名先例对齐既有 `ServiceModeControllerContracts.cs`）。ServiceManager.cs 602 → 451 行，只保留 ServiceManager 本体。
  - `Services/ServiceProbeProcess.cs`（新建）：SystemdStateProbe 与 LaunchdStateProbe 各自的私有 `RunProcess`（两份逐字节相同：3s 超时/双流重定向/UseShellExecute=false/返回 trimmed stdout）合并为单一 `Run(fileName, arguments)`；两探针调用点替换、私有方法删除、无用 using 清理。语义逐字保留（未修原实现的 stderr 不读形态，纯移动纪律）。
  - 源断言守卫 `UiLauncherSourceTests.UseShellExecute_Whitelist_Covers_ServiceManager_Elevation_Site` 路径同步至 `Services/ServiceManager.cs`（白名单按文件名匹配，needElevation 语义不变）。
- **结论**：✅ 通过。ServiceStateProbeAndWaitTests/ServiceManagerTests 全绿（fake 依据 contracts 注入）。

### 检查点 C1 — 死代码删除（grep 零引用）

- **声明**：删除 `Core/Queue/InFlightRegistry.cs` 与 `Core/ExifTool/WipeStrategies/IWipeStrategy.cs`。
- **证据**：
  - 删除前全仓 grep（src+tests+docs+scripts，排除 bin/obj）：`InFlightRegistry` 仅自身定义文件命中；`IWipeStrategy` 仅自身定义文件 + 文档命中（ADR 0053 历史记录不动；CONTEXT.md:334 的 `IWipeStrategy` 措辞漂移属票 21 CONTEXT.md 收口范围，已在票 21 分支处理）。代码零引用。
  - 同目录兄弟文件（DebounceQueue/RecentFingerprintCache/FileFingerprint；WipeStrategyResolver/WipeFormatFamily/WipeStrategyResult）不引用二者；WipeStrategyResolver 即 ADR 0053 M6a 的最终形态（静态 resolver），IWipeStrategy 为被取代的接口残留。
  - 删除后 `dotnet build` 0 错误。
- **结论**：✅ 通过。

### 检查点 C2 — BackgroundUiOptions 收敛不可变（去除可变字段外写）

- **声明**：BackgroundUiOptions 公共面收敛为只读，外部对可变字段的直写清零，变更经类内具名方法收口。
- **证据**：
  - `BackgroundUiOptions.cs` 重写：12 个构建期属性改 `{ get; init; }`（WorkerExecutablePath/ConfigPath/AuditDirectory + 9 个 Func 委托）；7 个运行期状态属性改 `{ get; private set; }`（RuntimeKind/WorkerEndpointName/UseTrayIcon/HideMainWindowOnStartup/HideTrayIcon/ShowMainWindow/ConnectionState）；新增 4 个具名变更方法 `UpdateRuntimeState(runtimeKind, endpointName, useTrayIcon)`、`UpdateHideFlags(hideMainWindowOnStartup, hideTrayIcon)`（内含原 MainWindow 热重载的 tray 模式 UseTrayIcon 联动，逐字等价）、`SetShowMainWindow`、`SetConnectionState`。
  - 设计裁决：采用「只读公共面 + 具名方法」而非 record+with 快照替换——本类型被 MainWindow/ServiceModeController/TrayHost/Program 委托四方持有引用，快照替换会引入陈旧引用（stale snapshot）风险（如 Program 连接回调更新后 Controller/托盘读旧端点），违背「纯移动无行为变化」门禁；单实例共享 + 方法收口达成「去除可变字段外写」验收语义且行为逐字等价。已在本票评审中注明，如需快照化可另票。
  - 外部写入点收口（9 处，全部改走具名方法）：Program.cs BuildRuntimeOptions（重写为 init 构造 + UpdateRuntimeState/UpdateHideFlags；连接回调三连写 → UpdateRuntimeState；ConnectionState → SetConnectionState）、App.axaml.cs ShowMainWindow → SetShowMainWindow、ServiceModeController.cs 4 处模式切换 → UpdateRuntimeState、MainWindow.axaml.cs 热重载 3 连写 → UpdateHideFlags。
  - 顺带清除：`UiProgram.Options` 静态可变属性（重构后零引用的死代码，属「可变字段外写」面）。
  - 测试同步：ServiceModeControllerTests 的对象初始化器改 init 写入 + `UpdateRuntimeState` 收口（3 处），StopAsync 断言仍读同一实例的最终状态（实例身份保持，断言语义不变）。
  - 源断言守卫随形态迁移（语义不变）：`MainWindowUninstallFlowSourceTests` 卸载后钉回 BackgroundPipe、`MainWindowServiceSwitchSourceTests` 安装/启动后切 service——断言从直接赋值形态改锁 `options.UpdateRuntimeState("tray"|"service", ...)` 新形态（ADR 0056 先例：guard 随迁移更新）。
- **结论**：✅ 通过。

### 检查点 D — 编译 + 既有测试全绿（单槽串行）

- **声明**：build 0 错 0 SCS；Core + Integration 测试全绿。
- **证据**：
  - `dotnet build PhotoPrivacy.sln --no-incremental`：0 错误，grep `SCS` 零命中（1054 个警告均为既有 CA 风格警告，较票 17 时的 1012 增量来自并行票 18/19/21 的新代码，非本票引入，SCS 维持零）。
  - 单槽串行（test.runsettings）：`PhotoPrivacy.Core.Tests` 183/183 绿；`PhotoPrivacy.IntegrationTests` 294/294 绿（数量较票 17 的 151/276 增长来自并行票 18/19 落栈的新测试）。
  - semgrep `p/csharp` 扫描 `src/PhotoPrivacy.Ui + src/PhotoPrivacy.Core`：0 findings 0 errors（本票触及安全敏感面：ServiceManager 的 sc.exe 启动、UiSingleInstance 管道 ACL、WorkerProcessManager 进程启动——均纯移动无改动）。
- **结论**：✅ 通过。

## 版本控制（WORKFLOW §4.2/§4.4）

- GitButler 虚拟分支 `ticket-20-ui-root-layering-deadcode`，commit 中文带票号；未 push；未执行 §4.4 触发清单内的历史改写/丢弃操作，仍按票 17 稳妥先例在提交前跑了仓库外快照（manifest + workflow-verify ZERO-LOSS）。commit 实物哈希按 WORKFLOW §7.3 以 `git log --grep 票20` 实物为准（but slug wvz 不作数；UiLauncherSourceTests 路径同步经 amend 归入同一 commit）。
- 本票只圈选票 20 文件（2 删除 + 15 移动/新建 + 6 源文件修改 + 19 测试修改 + B21 backlog + 报告沉淀副本）；工作区内他人的 `results/deepseek-v4-pro/*` 未提交产物与票 18 在途 Configuration 改动零触碰。
- commit 实物哈希以 `git log` 为准（WORKFLOW §7.3）。

## 偏离与备注

- 移动清单较 issue 点名的 9 个服务类多含 4 个探针/执行器：理由为它们实现本票抽出的 IServiceStateProbe/IScCommandExecutor 契约且仅被 ServiceManager 消费，属同一服务管理子系统；若大脑裁定超范围可回退这 4 个文件的移动（独立无耦合）。
- 「不可变快照」实现采用只读面 + 具名方法收敛（非 record 快照替换），理由见检查点 C2；「去除可变字段外写」验收语义达成。
- 启动器指派用 ctx 读取任务书：本窗口无 ctx 工具，按任务书兜底条款退回内置文件读取，任务书全文与 5 份必读均已读全。
- 完成定义逐条满足：服务类归位 + 命名空间同步 ✅；契约抽取 ✅；探针 RunProcess 合并 ✅；死代码删除（grep 零引用）✅；BackgroundUiOptions 收敛不可变 ✅；编译 + 既有测试全绿（纯移动无行为变化）✅；报告落 .scratch 并沉淀 docs/process/reports/ ✅。
