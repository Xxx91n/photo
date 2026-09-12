# 报告 — 票14 快照/校验脚本转正裁决（架构恢复第四轮）

日期：2026-09-02　窗口：本票专用　分支：arc-recovery/14-snapshot-scripts-promotion（哈希以 git log 实物为准）

## 收口速览（交大脑）

- **裁决**：转正——原 `.codex-tmp` 临时脚本已灭失（票12 用后清理、git 全历史无提交），按 report-12 行为规格 + 7 份真实 manifest 标本忠实重建为 `scripts/workflow-snapshot.js` / `scripts/workflow-verify.js`（去票号化命名，零依赖纯 Node）。
- **提交**：分支 `arc-recovery/14-snapshot-scripts-promotion` 单提交（哈希以 `git log` 实物为准，未 push），7 文件，零外来文件。
- **栈依赖**：`docs/process/WORKFLOW.md` 基线由票12 分支承载（GitButler 依赖拒绝实证），本分支已锚定其上——合并次序必须在 12 之后。
- **门禁**：build 0 错 0 SCS；守卫 vstest 7/7（单槽串行）；semgrep scripts/ findings=0、errors=0；留证 `closeout4-{build,vstest,semgrep}-ticket14.*`。
- **实测**：真树快照 29 文件 ZERO-LOSS 29/29；篡改/缺失/新增负向自证 exit 1/1/0；manifest schema 与既有标本逐字段一致。
- **动栈前置快照**：`photo-snapshots/20260902-{200443,202018,202351}`（各附 SHA256 manifest，全 ZERO-LOSS）。
- **遗留**：无阻塞——backlog README B14–B17 登记表、报告哈希引用惯例，留轮次收口（R5）。

## 检查点 A — 裁决结论（先于执行成文）

**裁决：转正。** 原 `.codex-tmp/b12-snapshot.js` / `b12-verify.js` 以 `scripts/workflow-snapshot.js` / `scripts/workflow-verify.js` 身份转正受维护（源守卫 + AGENTS §10 登记 + WORKFLOW §4.4 工具段同步）。本节先于任何执行动作写盘（含 WORKFLOW 工具段同步前的裁决稿状态）。

理由（基于勘察实物，非记忆合成）：

1. **轨 2 是常驻承重策略**：WORKFLOW §4.4 动栈前快照为强制条款，当日已实际使用 7 次（D:/Aworker/photo-snapshots/ 七个时间戳目录，含一次 pull 前置与六次演练序列前置）——快照/校验工具是流程基础设施，不是一次性脚本。
2. **弃用路径不成立**：勘察实态为脚本已灭失（`.codex-tmp/` 为空；`git log --all -- .codex-tmp` 为空 = 全历史从未提交），票12 按临时脚本纪律"用后清理"已自行删除。弃用裁决的"删除 + WORKFLOW 注明替代手段"面对的是无物可删，而"替代手段"若为每窗口手搓脚本，恰好复刻无形态锁定、无守卫的漂移风险——强制策略不能停留在无受维护实现的状态。
3. **忠实重建证据充分**：行为语义有 report-12 实测记录背书（整树复制 + manifest.json 逐文件字节数/SHA256；校验输出 missing/changed/added 与 ZERO-LOSS 判定），manifest 格式有 7 个真实快照标本（schema 在 074101→083352 间保持稳定：snapshot/takenAt/source/fileCount/totalBytes/files[{file,bytes,sha256}]）。
4. **成本低收益即时**：两脚本零依赖纯 Node 内置模块；转正后形态由源守卫锁定，WORKFLOW §4.4 工具段与实际一致性受回归保护（本票红线）。

身份映射：`b12-snapshot.js → workflow-snapshot.js`、`b12-verify.js → workflow-verify.js`（去票号化命名，轮次无关；WORKFLOW §4.4 工具段同文）。

## 完成定义对照（声明 → 证据 → 结论）

| 声明（handoff 完成定义） | 证据 | 结论 |
|---|---|---|
| 裁决结论成文 | 本报告「检查点 A」节（执行前写盘）；WORKFLOW §4.4 工具段（.scratch 工作版 + docs/process/WORKFLOW.md 受控副本，Buffer.compare 逐字节一致 = true）；docs/backlog/B14 裁决记录 | ✅ |
| 按结论完成转正：脚本在 scripts/ | scripts/workflow-snapshot.js（3,309B）/ scripts/workflow-verify.js（3,526B），纯 Node 内置 fs/path/crypto，无子进程无网络；LF 无 BOM（first3=[35,33,47]） | ✅ |
| 有源守卫 | tests/PhotoPrivacy.IntegrationTests/WorkflowSnapshotScriptGuardTests.cs：7 断言（脚本居位+禁 .codex-tmp 复辟 / manifest schema 锁定 / verify 输出形态 / WORKFLOW 工具段与实际一致（红线）/ AGENTS §10 登记 / 守卫自证 SourceLint.Read 缺文件必抛） | ✅ |
| AGENTS §10 登记 | AGENTS.md §10 外部工具路径表新增两行（流程快照 / 快照校验，含用法与退出码语义），LF 无 BOM 保持 | ✅ |
| 弃用路径：脚本删除、WORKFLOW §4.4 工具段同步 | （弃用路径未采；转正路径的 WORKFLOW 同步已含工具段成文 + 原临时身份退役声明） | N/A |
| build 绿 | dotnet build PhotoPrivacy.sln --no-incremental：0 错误（「已成功生成」）、SCS 警告 0、真实 compiler/MSBuild error 0；1005 条既有 CA 警告与本票无关 | ✅ |
| 改 scripts/ 则 semgrep 留证 | semgrep scan --config p/csharp --config p/security-audit --json ./scripts：findings=0，semgrep internal errors=0；原始 JSON 留档 closeout4-semgrep-ticket14.json | ✅ |
| 守卫测试绿 | dotnet vstest IntegrationTests.dll --Tests:WorkflowSnapshotScriptGuardTests --settings:test.runsettings（单槽串行）：通过 7 / 失败 0 / 总计 7（82ms） | ✅ |
| 报告写入 report-14-*（声明→证据→结论对照） | 本文件；受控副本 docs/process/reports/14-snapshot-scripts-promotion.md | ✅ |

## 实测记录（dogfood 留证）

| # | 动作 | 结果 |
|---|---|---|
| 1 | `node scripts/workflow-snapshot.js`（真树默认参数） | 29 文件 / 71,945B → D:/Aworker/photo-snapshots/20260902-200443（本票动栈前置快照，第 8 号） |
| 2 | `node scripts/workflow-verify.js <快照>` | ZERO-LOSS 29/29 哈希一致，exit 0 |
| 3 | manifest schema 对照既有标本 20260902-083352 | 顶层与条目键集逐字段一致（fileCount,files,snapshot,source,takenAt,totalBytes / bytes,file,sha256），SHA256 64 位十六进制 |
| 4 | 临时树负向自证（篡改 a.txt） | CHANGED a.txt + LOSS DETECTED（missing=0 changed=1 added=0），exit 1 |
| 5 | 临时树负向自证（恢复 + 删 c.txt + 增 d.txt） | MISSING c.txt + ADDED d.txt + LOSS DETECTED（added 不计入损失判定），exit 1 |
| 6 | 临时树清理 | rmSync 成功，确认不存在 |

## 偏差与披露

- **脚本为忠实重建而非原样迁移**：原脚本从未入版本控制且已被票12 清理，无原始字节可迁。重建依据 = report-12 行为记载 + 7 个真实 manifest 标本格式；命名去票号化（b12-* → workflow-*），语义（整树复制/SHA256 manifest/ZERO-LOSS 判定/missing-changed-added 输出）与既有快照产物完全兼容——已用旧脚本时代的真实标本做 schema 对照证实。
- **CA1707 披露**：新守卫测试方法名含下划线，产生 7 条 CA1707 警告（日志中 ×2 因多目标编译重复计入）。与仓库既有守卫测试同类同域（同 build 日志中 SmokeScriptDiagnosticsTests 8 条、ProcessHygieneGuardTests 6 条、FswFolderWatcherTests 等），属既定测试命名容忍模式，非本票新增破例。SCS/安全类警告为零。
- **WORKFLOW 工具段锚点首插失败一次**：全行锚点因转写差异未命中，改用行内短锚（`；快照完成前禁止动栈。\n`）定位后插入成功；双侧同步以 Buffer.compare 逐字节校验，未发生内容损坏。
- **快照 20260902-200443 兼职**：本次 dogfood 快照同时充当本票后续 GitButler 提交动栈的前置快照（第 8 号），一物两用。
- **栈依赖披露（spec Implementation Decisions 遵循）**：docs/process/WORKFLOW.md 工具段行的基线文件由票12 分支（arc-recovery/12-workflow-artifact-persistence）提交承载——首次提交尝试被 GitButler 依赖拒绝实证（line 26 depends on tku），按规程将本分支 `but branch new --anchor` 栈于 12 之上；其余六个提交物（两脚本/守卫测试/AGENTS §10/B14/报告受控副本）均为新建文件或基线在 common base，无栈依赖。
- **首次提交尝试省略文件 ID 被原子拒绝（零副作用）**：无 ID 的 `but commit` 会卷入工作区全部在途改动（票13/15/16 的在途文件与他窗 .gitignore），GitButler 原子拒绝且未建分支；改用显式 7 文件 ID 重提。§4.3 共享文件停手规则获工具层兜底验证。

## 提交物清单

- scripts/workflow-snapshot.js、scripts/workflow-verify.js（转正脚本，零依赖）
- tests/PhotoPrivacy.IntegrationTests/WorkflowSnapshotScriptGuardTests.cs（源守卫，7 断言）
- AGENTS.md（§10 两行登记）
- docs/backlog/B14-snapshot-scripts-promotion.md（立票 + 裁决与完成记录）
- .scratch/architecture-recovery/WORKFLOW.md §4.4 工具段 + docs/process/WORKFLOW.md 受控副本（逐字节一致）
- docs/process/reports/14-snapshot-scripts-promotion.md（本报告受控副本）
- .scratch：issues/14 三项勾选 + Status: done、README 状态表票14 done、本报告
- 门禁留证：closeout4-build-ticket14.log / closeout4-vstest-ticket14.log / closeout4-semgrep-ticket14.json（.scratch/architecture-recovery/）

## 遗留（交大脑）

- 无阻塞遗留。可选优化：docs/process/README.md 索引表的「来源（工作版）」列对 spec.md 映射为 round3-spec.md，第四轮 spec 定稿后由大脑统一更新轮次命名。
- 快照脚本重建过程无原字节可考（历史从未提交），若后续发现与原实现行为分歧，以 WORKFLOW §4.4 条款语义为仲裁基准。
