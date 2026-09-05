# 报告 — 票 30 返修：新守卫被考古注释击穿（架构恢复第七轮 · 30-fix）

- 日期：2026-09-05　窗口：票 30 返修窗　分支：arc-recovery/30-settings-vm-sync（GitButler 虚拟分支，WORKFLOW §4.2，change ID rqo 不变，amend 追加修复），未 push 不 PR（§4.2）
- 启动器：prompts/30-fix-ci-red.md；必读清单 8 份已逐份读全（review-30 / handoff-30 / issue-30 / spec / WORKFLOW / SettingsVmSyncSourceTests / MainWindowConfigHotReloadSourceTests / SourceLint.cs）。
- 触发：run 33964621208 红灯（Integration 3 失败 / 308 通过；Core 191/191 全绿）——新守卫 `Assert.DoesNotContain` 用 `File.ReadAllText` 读原始全文，命中「票 30：XXX 已删除」考古保留注释。

## 1. 声明 → 证据 → 结论

| # | 声明（启动器检查点） | 证据 | 结论 |
|---|---|---|---|
| A | 先质检复核结果：review-30 逐条回仓库实物自证 | ① `gh run view 33964621208 --log-failed`：恰 3 失败（SettingsVmSyncSourceTests.MainWindow_Should_Not_Contain_SelectionChanged_Handlers / MainWindow_Should_Not_Contain_SyncCombo_Backfill_Methods / MainWindowConfigHotReloadSourceTests.MainWindow_Source_Should_Expose_Explicit_Apply_Config_Action_Chain），Found 串 `OnThemeVariantSelectionChanged`（pos 26437，上下文 `// 票 30：OnThemeVariantSelecti…`）、`ApplyConfigForCurrentModeAsync`（pos 39390，上下文 `// 票 30：ApplyConfigForCurrent…`），失败行号 30/27 与 review 记录一致；② rg：MainWindow.axaml.cs 14 处「票 30：…已删除/迁至此处」注释（628/652/655/657/662/666/748/751/754/756/824/941/976/1004），全为 `//` 行注释、无行内字符串含 `//`；③ 守卫读取方式：SettingsVmSyncSourceTests.cs:17 与 MainWindowConfigHotReloadSourceTests.cs:15 均为 `File.ReadAllText`（原始全文），CompositionRootSourceTests.cs:83 既有先例用 `SourceLint.ReadStripped`（SourceLint.cs:33 剥 `//` 行注释）；④ but status：30 叠 29、zz clean、tip 1db632d 与 review 记录重推 commit 一致 | ✅ review-30 四条结论全部吻合，无错误需呈报 |
| B | 修复（最小改动）：守卫对注释免疫，走既有 ReadStripped 模式，拦截语义不放宽 | SettingsVmSyncSourceTests：5 处 .cs 断言改 `SourceLint.ReadStripped`（MainWindow 处理器/订阅形态、Sync 族、VM 索引属性与联动、ThemeSwatch IsChecked）；ConfigPage.axaml 与 ThemeSwatch.axaml 断言、行数断言保持原始全文口径（axaml 无行注释语义、行数口径必须含注释行）；MainWindowConfigHotReloadSourceTests：全部断言改 `SourceLint.ReadStripped`（含 2 处死代码 DoesNotContain）；被删成员回潮拦截零放宽——反向对照（node 沙箱）：向剥注释后的源注入 `SyncThemeVariantComboSelection(...)`/`ApplyConfigForCurrentModeAsync(...)` 模拟代码，断言必红（recurrence-trips=true），当前源剥注释后零命中（comment-immune=true） | ✅ |
| C | 对照完成定义输出声明→证据→结论表；云端绿为唯一验收 | 本表即对照输出；issue 30 完成定义第 2 条（主题/语言/日志级别切换行为不变：CI 验证分支守卫测试全绿）在云端 run 转绿后方可勾选——由大脑推送复核 | ✅（云端验收移交大脑） |
| D | 本窗口不推送、不本地构建测试；commit 后交大脑推送 | 本机零 build/test 运行；验证载体 = node 沙箱语义预演（模拟 ReadStripped 逐行剥注释 + 22 项断言命中预演 ALL-PASS + 反向对照通过），非 CI 证据；不推送 | ✅ |

## 2. 关键改动清单（2 文件，均为测试）

- 修改 tests/PhotoPrivacy.IntegrationTests/Ui/SettingsVmSyncSourceTests.cs：`Read` helper 保留供 axaml/行数断言；.cs 断言统一 `ReadStripped`（直调 SourceLint.ReadStripped，同参签名）；类 doc 注明返修口径与拦截语义不放宽声明；行数断言注释修正「约 -230 行」→「-279 行」（review ⚠️ 口径噪声项之一，顺手修正）。
- 修改 tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs：`File.ReadAllText(sourcePath)` → `SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs")`；`using System.Text` 随 Encoding 使用清零移除。
- 源码（src/）零改动：考古保留注释原样保留（本票删注释反而抹掉迁移审计痕迹；守卫免疫化是正解）。

## 3. 机制与口径

- ReadStripped 语义（SourceLint.cs:33-38）：`File.ReadAllLines` 逐行 `StripLineComment`（首个 `//` 起截断）后拼接。MainWindow.axaml.cs 114 处 `//` 全为真注释（rg 验证无行内字符串含 `//`），剥除不影响任何代码断言；`"msg.auto_saved"`、`ApplyRuntimeConfigToUiState();` 等正向断言全部位于代码行。
- 注释免疫边界：`/* */` 块注释不在剥除范围（与既有守卫一致）——本票 14 处考古注释全为 `//` 行注释形态，断言面闭合；后续若引入块注释形态的成员名需另立规约（报告披露）。
- 拦截语义证明：正向（当前源剥注释 22 项断言全绿）+ 反向（注入回潮代码必红）双对照，node 沙箱实物 `.codex-tmp/t30fix-verify.js`（已清理）。

## 4. CI-only 边界与验证声明

- 本机零 build/test/lint 运行；第 B 项「预演 ALL-PASS」为沙箱静态口径，不替代 CI 云端证据。
- 需大脑推送验证分支触发 workflow：预期 Core 191/191 + Integration 311/311 全绿（3 失败均为守卫断言口径问题，源码行为面未变）。
- 若仍红：预期红点仅剩 (a) xUnit 断言消息格式差异（无影响）、(b) 未知运行时差异——云端日志实物再取证，启动器交大脑。

## 5. 与并行窗口的隔离（WORKFLOW §4.3）

- 本返修只触碰 2 个测试文件（首窗新增/迁移的同族守卫），源码与 .scratch 流程文件零改动。
- 未触碰票 31 在途面（轮询下沉未开工）。

## 6. 遗留

- review ⚠️ 口径噪声其余项（diff stat 178+/331- 计数口径、ThemeSwatch.axaml 基线行数 16/17、报告哈希自引用机制）已在 report-30 披露或属 GitButler 机制说明，不在本返修票面，维持披露不动。
- 测试方法名 `Should_Shink` 拼写（review 登记项）：方法名含在守卫语义内未变更（避免无谓 diff），留待后续票顺手更正。
