# 票 28 返修报告（28-fix）— CI 红灯修复（架构恢复第七轮）

- 日期：2026-09-05
- 依据：`.scratch/architecture-recovery/review-28-ci-pipeline-repair.md`（首脑复核，票 28 判 ❌ 返修）
- 分支：round7/28-ci-pipeline-repair（续用原票分支，返修 commit 叠于 40fc946 / ae925b1 之上）
- 返修 commit：959d5173a1c6a3f08a29b4d053cebfd7226650fc（短哈希 959d517，git log 实物）
- 红灯实物：run 33925388409（验证分支 round7/28-ci-pipeline-repair @ ae925b1，大脑推送）——test job 红，Core.Tests 23 失败 + IntegrationTests 23 失败（合计 46，与复核报告一致）

## 1. 失败 → 根因 → 修法对照（46/46 全覆盖）

### 组 A1 — ExifToolBridge 连锁失败（Core 14 例）

- **根因链（SUT 实物）**：`AppConfig.Default.ExifTool.Path` = `DefaultPaths.ResolveExifToolPath()`（src/PhotoPrivacy.Core/Constants/DefaultPaths.cs）——Windows 无候选命中时回退绝对路径 `C:\Program Files\ExifTool\exiftool.exe`（测试全绿的原因）；Unix 无候选命中时回退裸 `"exiftool"`（相对路径），被 `ExifToolBridge.ValidateExifToolPath` 的 `Path.IsPathFullyQualified` 拒绝（ExifToolBridge.cs:266-269，错误消息「ExifTool 路径必须是绝对路径: exiftool」与云端日志逐字一致）。
- **修法**：ExifToolBridgeTests 类内新增 `Config`（`AppConfig.Default with { ExifTool = ... Path = 平台绝对路径 }`，Windows=C:\Program Files\ExifTool\exiftool.exe、Unix=/usr/bin/exiftool）；15 处 `new ExifToolBridge(process, Config)` 全部改引用。校验只查绝对性不查存在性，fake 进程无文件依赖，测试语义（stay_open/生命周期/事件数据）不变。

### 组 A2 — Windows 路径字面量断言（Core 9 例 + Integration 1 例）

| 文件 | 失败例 | 根因 | 修法 |
|---|---|---|---|
| DefaultPathsTests | 1 | 断言 Windows 回退路径恒等，Unix 实物为裸 `exiftool` | 按 OS 分支：Windows 保持原断言；Unix 断言「绝对路径或单段文件名」二态（镜像 SUT 行为） |
| RuleEngineTests | 2 | 期望值 `D:\...` 字面量 vs SUT `Path.GetRelativePath/Combine` 的 `/` 分隔结果（pos 6/8 逐字符 diff，云端日志实锤） | 输入/期望全改 `Path.Combine` 平台中立构造，断言逻辑不变 |
| FileTaskPipelineTests | 3 | 同上（隔离路径/fixed_directory 拼接），InMemory 记录的是 Combine 产物 `D:\hot\_quarantine/D:\hot\a.jpg` | 同上，三例输入/期望改 Combine |
| WipeRuleEngineTests | 2 | `BuildWipeTaskBlock(@"D:\hot\a.jpg",...)` 入参被 SUT `IsPathFullyQualified` 拒（ArgumentException 路径必须是绝对路径） | 入参改 `Path.Combine(Path.GetTempPath(), ...)` |
| ExifToolCommandBuilderTests | 1 | 同上 | 同上 |
| AuditTailServiceFormattingTests | 1 | `BuildAuditPath` = `Path.Combine`，期望值反斜杠字面量在 Linux 不匹配 | 断言改 `Path.Combine(logDirectory, "audit-2026-04-19.jsonl")` 镜像 |

### 组 B1 — Windows 服务管理流程（Integration 12 例）

- **根因（SUT 实物）**：`ServiceManager.InstallCore/UninstallCore/Start/RunSc` 均以 `if (!OperatingSystem.IsWindows()) return ServiceCommandResult.Skipped(...use_systemd...)` 开头（src/PhotoPrivacy.Ui/Services/ServiceManager.cs:75/140/184/318）——Linux 上 Expected Failed/Success/ElevationCancelled/exit code 全部得 Skipped；`BuildReconfigArguments` 的 `ValidatePathForScCommand` 对 `C:\Program Files\...` 抛绝对路径异常。`ServiceModeController.UpdateServiceButtons`（:246-251）非 Windows 返回 AllDisabled 且不刷新状态文本，两个 Controller 测试锁的正是 Windows 终态语义。
- **修法**：ServiceManagerTests 10 例加 `if (SkipOnNonWindows()) { return; }`（helper 一处定义）；ServiceModeControllerTests 2 例加 `if (!OperatingSystem.IsWindows()) { return; }`。守卫先例：ProcessExifToolProcessTests.cs:22、ServiceStateProbeAndWaitTests.cs:13/24。systemd/launchd 探针在非 Windows 的行为另有 ServiceStateProbeAndWaitTests 覆盖，语义不丢失。

### 组 B2 — Smoke 组 + 死过滤转活（Integration 10 例）

- **根因**：`powershell` Win32Exception ×4（EndToEndSmokeTests:24、SmokeScriptDiagnosticsTests:121、ReleaseReadinessScriptValidationTests:13/47 直接 spawn powershell，Linux 无此命令）+ `PhotoPrivacyWorker.dll` FileNotFoundException ×6（DryRunOutputFlowTests:402、InstanceConflictAuditTests:130/150 探测 Debug 构建产物——CI `dotnet test` 不产生 Debug 输出，且 CI-only 政策下 CI 内不应再 build Debug）。
- **修法（死过滤转活，不改任何 Smoke 测试体）**：复核实锤 `git grep Trait tests/` = 0——过滤串 `Category!=Smoke&Category!=ExifTool` 自旧 release.yml 继承，仓库从未建立 trait 机制，从未排除过任何测试。本返修为 Smoke 目录 5 个测试类加类级 `[Trait("Category","Smoke")]`、RealExifToolProbeTest 加 `[Trait("Category","ExifTool")]`，使 CI 过滤串**首次真实生效**：powershell/DLL 依赖/真实 ExifTool 依赖共 10+ 例按设计退出 CI 门禁，它们在本地发布前 gate（release-readiness.ps1 全量 test）仍会运行。

### 组 D — 复核 ⚠️ 计数口径（勘误）

- 复核指出：报告声明 10 写「23 项 node 检查」与 commit 信息「22 项」口径不一。已勘误两份报告（.scratch 主本 + docs 副本同步）标注 22 为实际数；本轮返修的验证脚本独立重数，不沿用旧口径。

## 2. 改动面

- 15 个测试文件，127+/44-（git show --stat 959d517 实物）；零 SUT（src/）改动、零 workflow 改动、零他人物品。
- CI 门禁语义（ci.yml）不变；release.yml 不变。

## 3. 本机静态门禁（CI-only 政策下）

- node 字符串感知括号平衡：15 文件全部 0 偏差（剔除字符串字面量/注释后计数；首轮 4 FAIL 均为验证脚本口径假阳性——2 文件 BOM 为 HEAD 既有形态未动、1 文件 balance=1 来自 JSON 测试数据字符串、1 处守卫计数把文档注释示例计入——逐项对照 git HEAD 实物核实后修正口径复跑 ALL-PASS）。
- 守卫调用点 10 处 + helper 定义 1 处；Trait 6 处（5×Smoke + 1×ExifTool）逐文件回读确认。
- `git diff --check` exit 0；LF 无 CRLF。
- 本机未运行 dotnet（CI-only）；本票运行类验收 = 大脑重推验证分支后的 CI run 实物。

## 4. 云端验收（已闭环，2026-09-05 回填）

- **返修一轮**（959d517 + 报告 8e6526d）→ run 33944424867（HEAD 8e6526d）：IntegrationTests **273/273 全绿**（首轮 294 总数中 21 例为被过滤 Smoke/ExifTool 类，排除生效），CoreTests 181/183——仅剩 ExifToolBridgeTests 2 例 WipeMetadataAsync 失败：wipe **目标路径** @"D:\hot\*.jpg" 字面量在 Linux 非绝对，被 BuildProbeTaskBlock → ValidatePathForExifToolProtocol 拒（第一轮只修了 ExifTool 程序路径，目标路径漏网）。
- **返修二轮**（9dd4de9）：两例入参改 Path.Combine 临时目录绝对路径（断言只锁 TASK_DONE/超时语义，与路径无关）→ **run 33944636423 = SUCCESS**，test job 绿：**Core 183/183 + Integration 273/273，0 失败**。
- 幽灵 run 消除留证：三次推送三次真实 "CI" run（33944424867 / 33944636423 + 首轮 33925388409），run 名为 workflow 名而非文件路径，jobs 非空、日志完整。
- 票 28 完成定义五条全部达成：①三元根除 ✅ ②push/PR 测试门禁 + 手动发布守卫 ✅ ③ci.yml/release.yml 拆分 ✅ ④**CI 验证分支 run 实物绿** ✅（33944636423）⑤报告双轨 ✅。ADR 0016 修订说明见主报告 §2，随收口沉淀。

## 5. 移交记录（已由本窗口按用户指令执行推送，闭环）

- 预期：ci.yml push 触发 → test job 过滤后 Core/Integration 套件在 ubuntu-latest 全绿（46 失败中：46 例被根因修复或按设计排除，0 例遗留未处置）。
- 风险留观：Filter 生效后跑的测试集合首次变化（排除 Smoke/ExifTool 类），可能暴露其余潜伏平台假设——红则按流程再开返修窗。
- 移交大脑：重推验证分支触发 run；绿则回填本报告第 4 节、issue 28 checkbox 置满、ADR 0016 修订沉淀随收口。
