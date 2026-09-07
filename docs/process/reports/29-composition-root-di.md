# 报告 — 票 29 组合根 + DI 容器骨架（架构恢复第七轮)

- 日期：2026-09-05　窗口：票 29 工作窗　分支：arc-recovery/29-composition-root-di（GitButler 虚拟分支，WORKFLOW §4.2）｜change ID owp，未 push 不 PR（§4.2）；实物哈希以收口时 git log 实物为准（WORKFLOW §7.3）
- 启动器：prompts/29-composition-root-di.md；必读清单 8 份已逐份读全（handoff / issue / spec / WORKFLOW / ADR 0053 / 0056 / 0060 / 0061）。
- Blocked by 现状：票 28 已闭环（97f192e，run 33945462090 SUCCESS，Core 183/183 + Integration 273/273），阻塞解除，本票 ready-for-agent。
- 动栈前快照：本票未执行 GitButler 历史改写/丢弃类操作（无 move/undo/squash/discard/uncommit/branch delete/pull），无 §4.4 轨 2 触发条件。提交时 GitButler 依赖拒绝（MainWindowViewModel.cs 322 行依赖 arc-recovery/24-shell-pages-split，票 24 VM 中转区同区域），按 Hint 处置链 but branch new arc-recovery/29-composition-root-di --anchor arc-recovery/24-shell-pages-split 建锚定分支后原命令重试成功——纯增分支非改写，未动既有栈序。

## 1. 声明 → 证据 → 结论

| # | 声明（完成定义 / issue checkbox） | 证据 | 结论 |
|---|---|---|---|
| 1 | 应用启动处装配容器并注册核心服务为单例 | 新增 src/PhotoPrivacy.Ui/Composition/AppComposition.cs（114 行，组合根）：Program.Start 调 AppComposition.Build(config, configPath, workerPath)（启动处装配），ServiceCollection 单例注册 WorkerIpcClient / WorkerProcessManager / ServiceManager / IServiceManagerOps / ConnectionStateService / FormatRulesStore / LocalizationService / MainWindowViewModel / MainWindow + BackgroundUiOptions，BuildServiceProvider 收口 | ✅ |
| 2 | MainWindow/MainWindowViewModel 改构造注入 | App.axaml.cs：desktop.MainWindow = _services.GetRequiredService&lt;MainWindow&gt;()（容器解析，DataContext 手写 new MainWindowViewModel() 清零）；MainWindow 构造签名 (MainWindowViewModel, IServiceManagerOps, WorkerProcessManager, WorkerIpcClient)——ServiceManagerOps/WorkerProcessManager/WorkerIpcClient/ServiceManager 四连 new 手写依赖链从 MainWindow 移除，窗口只保留自身视图缝（MainWindowUiHost / MainWindowViewModelView）落位；MainWindowViewModel 构造注入 LocalizationService + FormatRulesStore（可选参数默认回落保留无参兼容，MainWindowViewModelTests 6 处无参构造不受影响） | ✅ |
| 3 | 静态单例至少收敛一个入口为容器注册（检查点 B） | LocalizationService.Instance（构造私有 Lazy 单例）注册进容器：AddSingleton&lt;LocalizationService&gt;(_ => LocalizationService.Instance)——容器与静态引用为同一实例；MainWindowViewModel 内静态直引收敛为构造注入 _localization（字段初始化器 2 处 + 属性表达式 4 处 + ApplyLocaleFlowDirection 1 处 + 构造兜底 1 处），ViewModel 源剥注释后 Instance 引用恰剩 1（构造默认回落，由新守卫锁定） | ✅ |
| 4 | 运行时选项构建收口组合根 | Program.BuildRuntimeOptions（约 90 行装配逻辑，票 20 快照 + 委托闭包语义整体）迁入 AppComposition.BuildRuntimeOptions；Program.cs 253→184 行，不再手写 new 服务链 | ✅ |
| 5 | 行为不变（守卫面静态自查） | AppStartupPolicyTests / UiProgramSourceSingleInstanceTests / UiAvailabilityTests / SourceLintTests / DesignSystemTests / MainWindowServiceSwitchSourceTests / MainWindowConfigHotReloadSourceTests / LocalizationServiceTests / UiThreadSafetyRegressionTests / MainWindowShellSourceTests / MainWindowSourceDiagnosticTests / UiLauncherSourceTests / HardcodedChineseScanTests 涉及改动文件的全部断言（含 Task.Run(async、StartWithClassicDesktopLifetime、Thread.Sleep(120)、.WithInterFont()、msg.applied、OnInstallServiceClick 精确串、CJK 扫描）逐条程序化复核通过（53 项检查 0 失败）；新增守卫 CompositionRootSourceTests（5 例）锁定组合根骨架 | ✅（静态口径；CI 云端背书由大脑推送验证分支确认） |
| 6 | 报告双轨沉淀 | 本文件 + docs/process/reports/29-composition-root-di.md 副本随 commit | ✅ |

## 2. 关键改动清单（7 文件）

- 新增 src/PhotoPrivacy.Ui/Composition/AppComposition.cs（114 行）：组合根唯一装配点；core 服务单例注册 + BuildRuntimeOptions 收口；FormatRulesStore 注册使容器可完整解析 MainWindowViewModel 构造（与 VM 可选参数默认值同一 config 目录）。
- 修改 src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj：+1 行 PackageReference Microsoft.Extensions.DependencyInjection 10.0.6（与既有 Microsoft.Extensions.Configuration.CommandLine 同版本线）。
- 修改 src/PhotoPrivacy.Ui/Program.cs（253→184 行）：启动处 AppComposition.Build 装配；GetRequiredService 取 RuntimeOptions/ConnectionState；BuildAvaloniaApp(services) 经 AppBuilder.Configure(() => new App(services)) 工厂把容器传给 App；BuildRuntimeOptions 整体迁出；PhotoPrivacy.Ipc using 随引用清零移除。
- 修改 src/PhotoPrivacy.Ui/App.axaml.cs：App 增 IServiceProvider 构造注入；MainWindow 自容器解析；DataContext 手写 new 清零。
- 修改 src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs：构造注入四依赖（VM/IServiceManagerOps/WorkerProcessManager/WorkerIpcClient）；CreateServiceModeController 工厂方法删除；_workerIpc 字段改构造赋值；文件其余 1300+ 行零触碰；BOM 保留、LF 保持。
- 修改 src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs：构造注入 _localization/rulesStore；字段初始化器迁移构造函数（_runtimeStatus/_exifToolVersion/RulesPanel）；Instance 直引收敛为 _localization（仅构造兜底 1 处 Instance）。
- 新增 tests/PhotoPrivacy.IntegrationTests/Ui/CompositionRootSourceTests.cs（5 例 source-lint）：锁 AppComposition 单例注册清单 + BuildServiceProvider、Program 启动处装配且组合根外禁手写核心服务 new、App 容器解析 MainWindow、MainWindow 构造注入防回潮、VM 恰 1 处 Instance 兜底。

## 3. 设计裁决与口径（spec 研究输入 Q1/Q2 落地）

- 容器选型：Microsoft.Extensions.DependencyInjection（IServiceCollection/IServiceProvider）——BCL 生态默认、spec 工业模板（StabilityMatrix/Files.App/MarkSmith）同一选型；不引第三方容器（Autofac 等）。
- App 获取容器：AppBuilder.Configure 泛型工厂重载（Func&lt;TApp&gt;）——Avalonia 官方 API，XML 注释即 "useful for passing of dependencies to TApp"；二进制验证：本机 NuGet 缓存 Avalonia.Controls 11.1.3 ref 程序集 appFactory 参数命中，Avalonia master 源码确认重载延续至 12.x。
- 行为等价依据：WorkerIpcClient / WorkerProcessManager / ServiceManager 均无实例字段状态（每次 SendAsync 自建 transport、ServiceManager 仅注入 executor/probe），进程级单例与原先散落多实例语义等价；ConnectionStateService 有状态（Timer/CTS），保持原生命周期（Program.Start 置 Connecting → 退出时 Dispose），仅改为容器解析同一实例。
- BuildRuntimeOptions 迁移逐字保留票 20 注释与委托闭包语义（读取 App.RuntimeOptions 当前值）；UpdateRuntimeState 尾部 modeKind=="tray" 短路化简为 !config.Ui.HideTrayIcon（等价）。
- MainWindow 不做 Localization 收敛：MainWindowConfigHotReloadSourceTests 锁定 MainWindow.axaml.cs 内 LocalizationService.Instance.Get("msg.applied") 原文，且 MainWindow 1433 行内 16 处 Instance 引用跨多方法（含 static）——spec Out of Scope「静态单例逐票收敛」下，本票仅收敛 ViewModel 入口（检查点 B 达成口径：一个入口 = MainWindowViewModel）。
- MainWindowViewModel 可选参数构造注入：兼容面 = MainWindowViewModelTests 6 处无参构造 + 容器解析；容器路径显式注册 FormatRulesStore + LocalizationService（不依赖默认值回退），手动路径走 C# 可选参数默认（null → 回落 Instance），两条路径同一实例。

## 4. CI-only 边界与验证声明

- 本机零 build/test/lint 运行（CI-only 政策）；检查点 5 的"全绿"为源代码静态复核口径（与既有守卫测试同构的字符串断言程序化复算，53 项 0 失败），不替代 CI 云端证据。
- 需大脑推送验证分支触发 workflow：预期 Core + Integration 全绿（改动面 = 组合根装配 + 构造注入，行为路径未变；新增 5 例守卫随行）。
- 若 CI 红：预期红点集中在 (a) 新 NuGet 包还原（Microsoft.Extensions.DependencyInjection 10.0.6 与其他 M.E.* 包版本一致性）、(b) 新守卫字符串与实际源的口径差、(c) MEDI 解析 MainWindow 构造参数匹配——均已静态预检；返修启动器交大脑按 .scratch 流程处理。

## 5. 与并行窗口的隔离（WORKFLOW §4.3）

- 本票只触碰上列 7 文件；未触碰 ServiceModeController.cs / MainWindowServiceAdapters.cs / ServiceManager.cs / WorkerIpcClient.cs / ConfigFileWatcher.cs 等他票在途面。
- MainWindow.axaml / Views/Pages / AppTheme.axaml 零改动（票 24/25/26 产物保持）。
- .scratch 内本票工作文件（prompts/29、handoffs/29、issues/29）由轨 1 沉淀 + 仓库外快照纪律保护；本票无栈手术，未触发 §4.4 轨 2。

## 6. 遗留（spec Out of Scope，逐票迁移）

- 其余静态单例入口：App.RuntimeOptions（静态可写属性，被 ServiceModeController/UiHost/TrayHost 等跨层引用）、UiDiagnosticLog、MainWindow 内 16 处 LocalizationService.Instance 直引——均按 spec Out of Scope 留后续票。
- 全量 DI 迁移（TrayHost/AuditTailService/ConfigFileWatcher 等仍由 InitializeRuntime 内 new）——骨架已立，逐票收敛。
- RulesPanelViewModel(FormatRulesStore) 已具备注入缝，票 30 VM 属性驱动收敛可直接消费容器。
