# 报告 — 票 27 Config Editor Round-Trip Completion（架构恢复第六轮）

- 日期：2026-09-04　窗口：票 27 工作窗　分支：gitbutler/workspace（HEAD 5e17be4）
- 启动器：prompts/27-config-roundtrip-completion.md；必读清单已逐份读全（handoff/issue/spec/WORKFLOW/ADR 0007/0037/0045）。
- 动栈前快照：本票未执行 GitButler 历史改写/丢弃类操作（无 move/undo/squash/discard），无需 §4.4 轨2 快照。

## 1. 声明 → 证据 → 结论

| # | 声明（完成定义） | 证据 | 结论 |
|---|---|---|---|
| 1 | 新增 ConfigFileWatcher，手改 config.json → UI 自动同步 | 新增 src/PhotoPrivacy.Ui/Services/ConfigFileWatcher.cs（166 行）：FSW(Changed/Created/Renamed) + 500ms 防抖 + SuppressNextReload 合并抑制；MainWindow.InitializeRuntime 构造并 Start()，onReload → Dispatcher.UIThread.Post(ApplyRuntimeConfigToUiState)；ConfigEditor.OnSelfWrite 写盘前抑制反弹；OnClosed Dispose + 解绑 | ✅ |
| 2 | 收敛 3 处双 DTO 漂移点（Ui.Locale / Ui.ThemeVariant / Backup.Suffix 写侧默认值对齐读侧） | AppConfigJson.BackupDto.Suffix/UiDto.ThemeVariant/UiDto.Locale/UiDto.ThemeId/UiDto.SidebarWidth 默认值改 AppConfig.Default.*；AppConfigJson 投影处 Suffix 空白→Default、ThemeVariant/Locale ?? Default；AppConfigLoader.UiDto/BackupDto DTO 默认 + 运行时构造双重空白兜底 | ✅ |
| 3 | 手改→同步测试 | ConfigFileWatcherTests：Manual_Edit_Should_Trigger_Reload_After_Debounce_Window / Suppress_Next_Reload_Should_Skip_Self_Write_Trigger / Rapid_Edits_Should_Coalesce_Into_Single_Reload（5/5 绿，含 ConfigEditorRoundTripTests + MainWindowConfigHotReloadSourceTests 过滤器共 5 例） | ✅ |
| 4 | 漂移点收敛回归 + round-trip 全字段 | AppConfigRoundTripTests 增 2 个 Theory：ToIndentedJson_Should_Emit_Aligned_Defaults_For_Triple_Drift_Points（5 InlineData）+ Loader_Should_Fallback_To_Default_When_Field_Is_Missing（3 InlineData）；原有 round-trip 全字段断言不回归（14/14 绿） | ✅ |
| 5 | build 0 错 0 SCS | dotnet build PhotoPrivacy.sln --verbosity minimal：0 错误；SCS 计数=0（625 警告全为既有 CA 系列，非本票新增类别） | ✅ |
| 6 | 单槽 Core.Tests + IntegrationTests 绿 | test.runsettings MaxCpuCount=1：Core 191/191 + Integration 311/311（均 0 失败） | ✅ |
| 7 | semgrep 0 发现 | p/csharp + p/security-audit：181 规则 / 146 文件 / 0 findings | ✅ |
| 8 | 报告双轨沉淀 | 本文件 + docs/process/reports/27-config-roundtrip-completion.md 副本 | ✅ |

## 2. 关键改动清单

- 新增：src/PhotoPrivacy.Ui/Services/ConfigFileWatcher.cs（FSW 监听 + 500ms 防抖 + 合并抑制，抑制标志在防抖触发时消费以应对单次写盘多次 Changed 事件）。
- 漂移收敛：AppConfigJson.cs 写侧 DTO 默认值对齐 AppConfig.Default（Suffix/ThemeVariant/Locale/ThemeId/SidebarWidth）+ 投影处空白兜底；AppConfigLoader.cs 读侧 DTO 默认 + 运行时构造双重空白兜底。
- 抑制通道：ConfigEditor.cs 新增静态 OnSelfWrite 回调，写盘前 invoke；MainWindow 启动时绑定 watcher.SuppressNextReload，OnClosed 解绑。
- MainWindow.axaml.cs：InitializeRuntime 启动 watcher + OnClosed 释放（仅 +18 行，未触碰票 24 Pages 拆分的 *Control 访问器）。
- 测试：新增 ConfigFileWatcherTests（3 例，IntegrationTests）；AppConfigRoundTripTests 增 2 Theory（8 InlineData，Core.Tests）。

## 3. 口径说明（审计员可复算）

- 3 处双 DTO 漂移点定位：写侧 AppConfigJson.AppConfigDto 与读侧 AppConfigLoader.AppConfigDto 各自内联声明同名字段但默认值不同。收敛前：Backup.Suffix 写侧 string.Empty / 读侧 ".bak"；Ui.ThemeVariant 写侧 string.Empty / 读侧 "system"；Ui.Locale 写侧 null / 读侧 null（读侧构造 ?? "zh-CN"）。收敛后：写侧 DTO 默认值、写侧投影、读侧 DTO 默认值、读侧构造四处均以 AppConfig.Default 为唯一权威。
- Suppress 语义：UI 自身 SaveConfig 写盘会触发 FSW，SuppressNextReload 在写盘前调用一次，防抖窗口到期时统一消费抑制标志，合并单次写盘触发的全部 Changed 事件，避免 UI→磁盘→UI 死循环。
- 防抖窗口 500ms 与 ADR 0037 instant-apply 同窗口，保持交互一致性。

## 4. 与并行窗口的隔离（WORKFLOW §4.3/§4.4）

- 本票只触碰：AppConfigJson.cs、AppConfigLoader.cs、ConfigEditor.cs、MainWindow.axaml.cs（仅 watcher 接线 18 行）、ConfigFileWatcher.cs（新增）、ConfigFileWatcherTests.cs（新增）、AppConfigRoundTripTests.cs、双轨报告。
- 未触碰他人在途物品：Views/Pages/（票 24 Shell+Pages 拆分，已提交 HEAD）、MainWindow.axaml、MainWindowServiceAdapters.cs、docs/process/reports/{22,23,24-config-authority-audit}.md、results/**。
- GitButler 历史改写类操作未执行（无 move/undo/squash/discard），§4.4 轨2 快照非必需。

## 5. 结论

票 27 完成定义 4 项全部满足（issue 清单 4 项全勾）：ConfigFileWatcher 磁盘→UI 回环、3 处双 DTO 漂移收敛、手改→同步 + 漂移回归 + round-trip 全字段测试、build 0 错 0 SCS + 单槽双套件绿 + semgrep 0 发现。遗留：UI 手工探活（手改 config.json 后界面即时刷新实机走查）建议随轮末冒烟一并执行。
