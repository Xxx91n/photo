# 报告 — 票 24 Shell + Pages/ 拆分（架构恢复第六轮）

- 日期：2026-09-04　窗口：票 24 工作窗　分支：arc-recovery/24-shell-pages-split（GitButler 虚拟分支）
- 启动器：prompts/24-shell-pages-split.md；必读清单已逐份读全（handoff/issue/spec/WORKFLOW/atomcode 报告/ADR 0052/0050/0056）。
- 动栈前快照：`node scripts/workflow-snapshot.js .scratch/architecture-recovery D:/Aworker/photo-snapshots/20260904-票24-preflight/20260904-030605`（52 文件 / 236215 字节 + manifest SHA256）。

## 1. 声明 → 证据 → 结论

| # | 声明（完成定义） | 证据 | 结论 |
|---|---|---|---|
| 1 | MainWindow.axaml ≤180 行，仅标题栏+侧栏+GridSplitter+4 页引用 | 实测 139 行（原 637）；grep 4 页引用 `views:{Config,Logs,Rules,ServiceManager}Page` 各 1 处；`MainWindowShellSourceTests.MainWindow_Shell_Should_Not_Exceed_180_Lines` 绿 | ✅ |
| 2 | Views/Pages/{Config,Logs,Rules,ServiceManager}Page.axaml 各 ≤220 行 | ConfigPage 211 / LogsPage 63 / RulesPage 62 / ServiceManagerPage 67（`Pages_Should_Exist_And_Not_Exceed_220_Lines`×4 绿） | ✅ |
| 3 | 4 页 x:DataType 编译绑定零 AVLN 警告 | `dotnet build --no-incremental` 全解 AVLN=0（原 8 处 AVLN5001 经 `Watermark→PlaceholderText` 清零，行为不变） | ✅ |
| 4 | 行为零回归：日志流/中键滚动/GridSplitter/5 色板/10 语言 | 处理器与绑定逐字保留，仅接线目标改为页面控件访问器：AuditTailService 回调 `AppendLogBatch` 未动；`MiddleClickScrollBehavior.IsEnabled="True"` 2 挂载点（Config/ServiceManager）在页文件保持；`OnSidebarSplitterDragCompleted`/`RestoreSidebarWidth` 留守 shell.cs；5 色板 RadioButton×5 + `OnThemePresetSwatchClick`/`SyncThemeSwatchSelection` 全数接线；10 语言 ComboBox + `OnLocaleSelectionChanged`/`SyncLocaleComboSelection`/`RefreshI18nComboBoxItems` 保留；Integration 311/311 绿 | ✅（代码等价 + 测试全绿；UI 手工探活移交用户冒烟） |
| 5 | 顺带修复 5 处 Service→View 直写，改走 VM 中转 | adapters SetPauseResumeAvailability/SetServiceButtons 改写 `vm.PauseResumeAvailable`/`vm.ServiceButtons`；InitializeRuntime 190-191 直写同步清除；VM 新增 `ServiceButtons`(ServiceButtonState)/`PauseResumeAvailable`/`PauseResumeContent`；ServiceManagerPage 四按钮 + shell 暂停按钮改 XAML 绑定；`ServiceAdapters_Must_Not_DirectWrite_View_Controls` 绿 | ✅ |
| 6 | source-lint：MainWindow 行数上限 + Pages 存在 + code-behind 无长活订阅 | 新增 `tests/.../Ui/MainWindowShellSourceTests.cs` 14 断言全绿（行数/存在性/`+=` 禁令/直写禁令/VM 中转证据） | ✅ |
| 7 | h2 违例清零 + Width=280 ×4 收敛为共享 class | 全 Views 目录 `Classes="h2"`=0（RulesPage 514→title）；`Width="280"`=0，AppTheme `TextBox.inline-input` 增 `Width=280` 单一权威（source-lint 双向锁定） | ✅ |
| 8 | 交付 ADR：页面文件化 + shell 最小化 + Pages/ 规约 | `docs/adr/0061-shell-pages-split-page-fileization.md`（D1-D6 + 规约 5 条） | ✅ |
| 9 | build 0 错 0 SCS | `dotnet build --no-incremental`：0 错误、SCS=0（1073 警告全为 CA 系列既有面，非本票新增类别） | ✅ |
| 10 | 测试单槽串行 | Core 191/191 + Integration 311/311（test.runsettings MaxCpuCount=1） | ✅ |
| 11 | semgrep（项目 AGENTS §3.3） | `p/csharp + p/security-audit` 0 findings | ✅ |
| 12 | AGENTS.md Avalonia 时效纠错 | 技术栈行改「Avalonia UI 12.1.1」（csproj 实为 12.1.1）；ADR 0035 历史表述保留 | ✅ |

## 2. 关键改动清单

- 新增：`src/PhotoPrivacy.Ui/Views/Pages/{ConfigPage,LogsPage,RulesPage,ServiceManagerPage}.axaml` + 4 个 code-behind（访问器-only）。
- 降维：`MainWindow.axaml` 637→139 行 shell；`MainWindow.axaml.cs` 接线块改经 `*Control` 访问器（24 处订阅目标重定向），实现体零迁移（保持 1548→1549 行预算内）。
- VM：`MainWindowViewModel` 增 `ServiceButtons`/`PauseResumeAvailable`/`PauseResumeContent`（+`RefreshLocaleDependent`/RuntimeStatus setter 通知扩散）。
- 样式：`AppTheme.axaml` `TextBox.inline-input` +`Width=280` 单一权威。
- 测试：新增 `MainWindowShellSourceTests`（14）；`DesignSystemTests` 5 处 + `MainWindowSourceDiagnosticTests` 1 处断言随文件形态迁移（扫 Views 目录聚合，语义不变）。
- 文档：ADR 0061 + 本报告双轨（.scratch 主 + docs/process/reports 副本）+ AGENTS.md 版本修正。

## 3. 口径说明（审计员可复算）

- 行数口径：文件文本按 `\n` 切分总行数（含空行/注释行），与 source-lint `LineCount` 一致。
- 「5 处直写」清单：MainWindowServiceAdapters.cs L28（PauseResumeButton.IsEnabled）、L29（.Content）、L75-78（四枚服务按钮 IsEnabled）、MainWindow.axaml.cs L190-191（InitializeRuntime 直写暂停按钮）——全部改为 VM 中转，source-lint 断言锁定不回归。
- 长活订阅判定：页面 code-behind 出现任何非注释 `+=` 即违例（页面生命周期短于窗口，订阅目标超页存活即泄漏）；现有 4 个 code-behind 零 `+=`。

## 4. 与并行窗口的隔离（WORKFLOW §4.3/§4.4）

- 本票只触碰：Views/（shell+Pages）、MainWindow.axaml.cs、MainWindowServiceAdapters.cs、MainWindowViewModel.cs、AppTheme.axaml、IntegrationTests 3 个文件、新增测试 1 个、ADR 0061、AGENTS.md 一行、双轨报告。
- 未触碰他人在途物品：`ConfigFileWatcher*`/`ConfigEditor.cs`/`AppConfigJson.cs`/`AppConfigLoader.cs`/`AppConfigRoundTripTests.cs`（票 27 在途）、`docs/process/reports/{22,23,24-config-authority-audit}.md`（勘察窗口沉淀）、`results/**`（他人物品）；提交时排除。
- GitButler 历史改写类操作未执行（无 move/undo/squash/discard），快照仅作开工前置保险。

## 5. 结论

票 24 完成定义 9 项全部满足（行号/计数以本报告 §1 与 `git show` 实物为准）。遗留：UI 手工探活（5 色板/10 语言/中键/拖宽/日志流实机走查）建议随轮末冒烟一并执行；票 25（PathPicker/NavButton/ThemeSwatch 抽取）与票 26（token gate）在 Pages/ 地基上继续。
