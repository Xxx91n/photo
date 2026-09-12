# 报告 — 票16 release-readiness 全链路捕获扩展（架构恢复第四轮）

- **窗口**: 单票工作窗（本会话）　**日期**: 2026-09-02
- **分支**: `arc-recovery/16-release-readiness-full-capture`（GitButler 虚拟分支；栈锚定 `arc-recovery/09-smoke-dll-first` 之上；git 实物哈希以 git log 为准）
- **状态**: 完成，待大脑收口　**Backlog**: B15（编号顺延说明见「偏差与披露」）

## 开工复述（检查点）

Blocked by: None — can start immediately（issue Status: ready-for-agent）。必读清单 7 份读全：
handoffs/16、issues/16、spec.md、WORKFLOW.md、report-09-smoke-dll-first.md、docs/backlog/B13、启动器 prompts/16。

## 声明 → 证据 → 结论对照

### 检查点 A — [1/3] dotnet test 与 [4/4] force-kill 纳入 Invoke-ScriptWithCapture

| 声明 | 证据 | 结论 |
|---|---|---|
| [1/3] dotnet test 改走捕获调用 | release-readiness.ps1：[1/3] 现经临时包装脚本（photo-ready-dotnet-test-*.ps1，内容 `& dotnet test "<sln>"` + `exit $LASTEXITCODE`）调用 Invoke-ScriptWithCapture；失败时打印双尾部后 throw | ✅ |
| [4/4] force-kill 改走捕获调用 | release-readiness.ps1：[4/4] 现经 Invoke-ScriptWithCapture -Label "verify-force-kill-cleanup.ps1" 调用（-Version 透传），原直接 `powershell -File` + $LASTEXITCODE 分支删除 | ✅ |
| 四步全捕获（dotnet test / smoke / publish / force-kill 同机制） | 新 guard ReleaseReadinessFullCaptureGuardTests 断言四步 label 均以 `Invoke-ScriptWithCapture -Label` 形态存在，且直连形态（`dotnet test "$repoRootPhotoPrivacy.sln"`、`-File "$repoRootscriptserify-force-kill-cleanup.ps1"`）清零 | ✅ |

### 检查点 B — 测试锁定四步全捕获

| 声明 | 证据 | 结论 |
|---|---|---|
| 新 guard 测试锁定四步全捕获 | tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessFullCaptureGuardTests.cs（新文件，未动票15 在途的 SmokeScriptDiagnosticsTests.cs）；vstest 过滤 13/13 绿（含本 guard + ReleaseReadinessScriptValidationTests + SmokeScriptDiagnosticsTests） | ✅ |

### 检查点 C — 单槽门禁绿 + 报告落盘

| 声明 | 证据 | 结论 |
|---|---|---|
| build 0 错 0 SCS | dotnet build PhotoPrivacy.sln（含 --no-incremental 复核）：0 错误 0 SCS 计数 | ✅ |
| 全套测试单槽串行绿 | dotnet vstest Core.Tests + IntegrationTests --settings:test.runsettings（MaxCpuCount=1）：145/145 + 276/276 绿；本次改动前后各验一轮过滤套件 | ✅ |
| semgrep（触及 scripts 安全面） | semgrep scan --config p/csharp --config p/security-audit ./scripts：0 findings | ✅ |
| PowerShell 5.1 解析 | Parser::ParseFile scripts/release-readiness.ps1：PARSE OK（临时 .ps1 规避内联 $ 吞字，含前后对比） | ✅ |
| git diff --check | exit=0 | ✅ |
| 报告落盘 | 本文件 + 受控副本 docs/process/reports/16-release-readiness-full-capture.md | ✅ |

## 栈依赖如实披露（§4.3/§4.4）

- 本票仅动 2 个文件 + 2 个新增：scripts/release-readiness.ps1（改走捕获）、ReleaseReadinessFullCaptureGuardTests.cs（新增 guard）、docs/backlog/B15（立票）、docs/process/reports/16（报告副本）。
- release-readiness.ps1 刚被票09/09b 改过（cb2e3d5 尾部补提），本票在其上叠加，遵循实际栈：改动起点 = 已含 Invoke-ScriptWithCapture 定义的提交版本。
- 最终栈拓扑（but status 实物）：arc-recovery/14 → 15 → **16** → 09 → 12（16 物理锚定 09 之上，git merge-base 验证 09 ∈ 16 祖先链；14/15 为并行窗口分支，未触碰）。
- 工作区存在票13/14/15 在途未提交改动（SmokeScriptDiagnosticsTests.cs、DryRunOutputFlowTests.cs、InstanceConflictAuditTests.cs、UdsEndpointOwnershipGuardTests.cs、workflow-snapshot/verify.js、docs/process/*、docs/backlog/README.md 等）；本票未触碰、未提交上述任何文件。
- 测试运行期间观察到 testhost.runtimeconfig.json 缺失导致两次 vstest 首启失败，重建 IntegrationTests 工程后恢复（与票15 在途测试宿主改动无涉）。

## 偏差与披露

- **backlog 编号顺延 B17→B15**：spec 原拟票13=B14/B14=B15 等对应，但票14 窗口先行占用 B14、票13 窗口顺延取 B17（其文件注明"因票14 先行占用 B14 而顺延取 B17"）；本票原建 B17 文件与票13 撞号，遂删改取空缺的 B15。docs/backlog/README.md 的 B14–B17 登记表由票15 窗口在途维护，按 §4.3 本票不碰，登记项由大脑收口统一整理。
- **dotnet test 经临时包装脚本而非直连**：Invoke-ScriptWithCapture 只接受脚本路径；[1/3] 以运行时生成的临时 .ps1（内容固定 ASCII，UTF8 无 BOM）承载 dotnet test 命令，复用同一捕获函数，不另造机制（符合 spec「票16 复用票09 Invoke-ScriptWithCapture，不另造捕获机制」）。
- **成功路径可观测性**：与 smoke/publish 步一致，成功时也打印尾部（gate 场景可接受）。

## 遗留

- 无。R2 解除后合并流程由大脑执行（第三轮分支合并与 push 仍为大脑流程动作，本票不 push）。
