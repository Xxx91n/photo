# Report — 票 15 测试宿主 dotnet run 兜底清零（架构恢复第四轮）

- **窗口**: 单票工作窗（本会话）
- **分支**: `arc-recovery/15-test-host-dotnet-run-purge`（GitButler 虚拟分支）
- **提交**: 本票分支 tip（哈希以 git log 实物为准，大脑收口复核）
- **状态**: 完成，待大脑收口
- **日期**: 2026-09-02

## 开工复述（检查点）

Blocked by: None — can start immediately（issue Status: ready-for-agent；四票 13/14/15/16 Blocked by 全 None 同波并行）。
必读清单 7 份已读全：handoffs/15、issues/15、spec.md、WORKFLOW.md、report-09-smoke-dll-first、report-09b-tail-commit、docs/backlog/B09。
本票 delta：检查点 A（tests/ 直启宿主 dotnet run 清零 + DLL 缺失显式报错）、检查点 B（guard 扩域）、检查点 C（受影响测试单槽绿）。

## 勘察结论（改动前）

- tests/ 直启点共 3 处，全部带 dotnet run 兜底，且为**拆参形态**（`fileName="dotnet"` + `arguments="run --project …"`），字面 `dotnet run` 搜不到：
  1. `InstanceConflictAuditTests.StartCli`（服务宿主）
  2. `InstanceConflictAuditTests.RunCliOnceAsync`（--once 宿主）
  3. `DryRunOutputFlowTests.RunCliAsync`（--once 宿主；其 `framework` 参数仅被兜底分支消费）
- 兜底判定为 `File.Exists(workerDll)`（仅 Debug 路径）失败即回退 `dotnet run`。
- 既有 guard `SmokeScript_Should_Be_DllFirst_Without_DotnetRun` 只锁 scripts/smoke.ps1，未覆盖 tests/ 宿主。

## 声明 → 证据 → 结论对照

### 检查点 A — tests/ 直启宿主 dotnet run 清零 + DLL 缺失显式报错

| 声明 | 证据 | 结论 |
|---|---|---|
| 3 处直启点兜底删除，改 DLL-first 直启 | 两宿主 if/else 兜底整段删除；DLL 不存在 → `throw new FileNotFoundException("Worker DLL not found: <路径>. Test hosts are DLL-first; build it first with: dotnet build PhotoPrivacy.sln", workerDll)` | ✅ |
| 拆参残留全树清零 | `rg -n "run --project" tests/ -g "!**/bin/**" -g "!**/obj/**"` = 0 命中（改动前 3 命中且全在两宿主兜底） | ✅ |
| DLL 缺失显式报错（行为探针） | 临时移走 `src/PhotoPrivacy.Worker/bin/Debug/net10.0/PhotoPrivacyWorker.dll` 后单跑 `DryRunOutputFlowTests.CliHost_Should_Exit_When_ExifTool_Path_Is_Bad`：exit=1、34ms 失败，异常原文 `System.IO.FileNotFoundException : Worker DLL not found: …` 且含 `dotnet build PhotoPrivacy.sln` 构建指引；DLL 恢复后同测试复跑绿（exit=0） | ✅ |
| 死参数清理 | `RunCliAsync` 的 `string framework = "net10.0"` 仅被兜底分支消费，随兜底删除（全文件无调用方传第 4 参） | ✅ |

### 检查点 B — guard 扩域锁定 tests/ 直启宿主

| 声明 | 证据 | 结论 |
|---|---|---|
| 新 guard 存在且绿 | `SmokeScriptDiagnosticsTests.CliTestHosts_Should_Be_DllFirst_Without_DotnetRunFallback`（票09 guard 文件扩域）：全 tests/ 源码树扫描拆参兜底标记（排除 bin/obj）断言 0 命中；双宿主 `DoesNotContain "dotnet run"` + `Contains "PhotoPrivacyWorker.dll"`（DLL-first 在位）+ `Contains "dotnet build"`（缺失指引在位） | ✅ |
| guard 自引用规避 | 兜底标记以 `"run --" + "project"` 常量拆写拼接，守卫源码自身不含字面标记 → 全树 rg 审计 0 命中（本票检查对象与守卫同在 tests/ 树，必须规避自引用；票09 guard 检查对象在 scripts/ 无此问题） | ✅ |
| guard 批次绿 | SmokeScriptDiagnosticsTests 单槽重跑 4/4 绿（3 既有 + 1 新增），3s | ✅ |

### 检查点 C — 受影响测试单槽重跑绿

| 声明 | 证据 | 结论 |
|---|---|---|
| 受影响测试全绿 | `dotnet vstest …IntegrationTests.dll --tests:…InstanceConflictAuditTests,…DryRunOutputFlowTests --settings:test.runsettings`：7/7 绿 10s；加 guard 批次合计受影响 11/11 绿 | ✅ |
| 单槽串行 | test.runsettings `MaxCpuCount=1`（单槽串行门禁不变） | ✅ |

### 门禁（WORKFLOW §5）

| 门禁 | 证据 | 结论 |
|---|---|---|
| build 0 错 0 SCS | `dotnet build PhotoPrivacy.sln --no-incremental`：0 错误、SCS 计数 0；991 警告为全量重建下 src/ 既有 CA/CS 基线，三个触碰文件 grep 警告 = 无命中 | ✅ |
| semgrep（触及面） | `semgrep scan --config p/csharp --config p/security-audit --json <3 个触碰文件>`：findings=0；3 个 errors 均为 PartialParsing（semgrep C# 解析器不支持 `$$"""` 原始插值字符串，三处均为改动前既有语法；编译正确性权威证据为 dotnet build 0 错） | ✅ |
| git diff --check | 提交前复验（见版本控制节） | ✅ |
| 行尾/BOM | 三个触碰文件写盘后 BOM=false、CRLF=0（LF 归一），符合 .gitattributes `*.cs text` | ✅ |

## 关键设计决策

1. **拆参形态识别**：兜底为 `fileName="dotnet"; arguments="run --project …"`，字面 `dotnet run` 搜不到；guard 以 `run --project` 为兜底标记（改动前全树恰 3 命中且全为兜底），而非仅锁字面。
2. **guard 标记拆写**：`"run --" + "project"` 常量拼接避免守卫源码自引用，使全树 rg 审计为 0 命中。
3. **缺失即抛、不做兜底**："清零"为字面验收（票09 同款纪律）；异常消息带构建指引，与 smoke.ps1 的 throw 指引同构。Debug DLL 由 IntegrationTests→Worker 的 ProjectReference 链保证在门禁流程必存在。
4. **负路径用探针取证**：DLL 临时改名 → 单测负路径（exit=1 + FileNotFoundException 原文）→ 恢复 → 复跑绿；不新增专用负路径测试（源断言 guard 已锁形态，探针证据记录于本报告）。
5. **backlog 编号映射**：spec Further Notes 拟 "docs/backlog/B14–B17"，实际由并行窗口先行占用调整为票13→B17、票16→B15（各自报告/立票在案），本票按剩余空位取 B16，避让撞号。

## 偏差与说明

- `DryRunOutputFlowTests.RunCliAsync` 签名变化：删除 `framework` 死参数（无调用方使用），属兜底删除的自然结果，非行为变更。
- docs/backlog/README.md 仅追加"第四轮"小节与 B16 行；B14/B15/B17 由票13/14/16 窗口随各自提交登记。
- docs/process/README.md 表新增 round4-spec/round4-README 两行（轨1）；docs/process/WORKFLOW.md 与 .scratch WORKFLOW.md 已逐字一致，本票未重沉淀 WORKFLOW。
- semgrep 对 tests/ 树默认按内置 ignore 跳过（0 files scanned），触及面证据以显式文件扫描为准。

## 并行窗口共存（§4.3）

工作区存在 arc-recovery/09/10/11/12 四个已收口分支、`zz [uncommitted]` 的 `.gitignore` 改动，以及票13/16 窗口的在途实物（but diff 实见：scripts/release-readiness.ps1 与 ReleaseReadinessFullCaptureGuardTests.cs 属票16、UdsEndpointOwnershipGuardTests.cs 与 B17/reports/13 属票13、B15 属票16）——均非本票产物，未触碰、未提交。本票触碰文件全集：

- tests/PhotoPrivacy.IntegrationTests/Smoke/InstanceConflictAuditTests.cs
- tests/PhotoPrivacy.IntegrationTests/Smoke/DryRunOutputFlowTests.cs
- tests/PhotoPrivacy.IntegrationTests/Smoke/SmokeScriptDiagnosticsTests.cs
- docs/backlog/B16-test-host-dotnet-run-purge.md（新增）
- docs/backlog/README.md（追加第四轮小节）
- docs/process/README.md（表增 2 行 + 尾注）
- docs/process/round4-spec.md（新增，轨1 沉淀）
- docs/process/round4-README.md（新增，轨1 沉淀）
- docs/process/reports/15-test-host-dotnet-run-purge.md（新增，轨1 沉淀）

与票 13/14/16 的文件面（Ipc guard、scripts/.codex-tmp+AGENTS、release-readiness.ps1）零重叠；docs/backlog/README.md 与 docs/process/README.md 为跨票登记类文件，仅追加、不改动他票行。

## 遗留

无。tests/ 直启宿主已 DLL-only，spec User Story 3"dotnet run 编译噪声与管道风险整个清零"达成。
