# 报告 — 票13 UDS 属主修复回归守卫（架构恢复第四轮）

日期：2026-09-02　窗口：本票专用　分支：arc-recovery/13-uds-transport-guard（哈希以 git log 实物为准）

## 检查点 A — SourceLint guard 存在（ownsEndpoint + File.Delete 属主包裹形态）

- guard 文件：`tests/PhotoPrivacy.IntegrationTests/UdsEndpointOwnershipGuardTests.cs`（新增，LF 无 BOM，167 行）。
- 断言文本（guard 位置与锁定形态，issue 验收第 3 条）：
  - 类名 `UdsEndpointOwnershipGuardTests`，纯分类器 `ClassifyDisposeAsync(string transportSource)`（internal static，供负向自证喂样本）。
  - 锁定形态 1：DisposeAsync 体内必须存在属主判定 `ownsEndpoint\s*=\s*_listener\s+is\s+not\s+null`（正则锚定）。
  - 锁定形态 2：DisposeAsync 体内每个 `File.Delete(` 必须词法落在 `if (ownsEndpoint)` 平衡花括号块内（`ExtractBalancedBlock` 提取块体，deleteIdx ∈ [scanIdx, braceIdx+block.Length) 判定）。
  - 附带锁定 3：`DisposeAsync_Should_Document_The_Ownership_Rationale` — 修复说明注释（"only by the endpoint owner" + "B11 finding"）是修复契约一部分，静默删除也判红。
  - 范围说明：ListenAsync 内 `File.Delete`（ADR 0026 stale-socket 清理，bind 前执行）不在本 guard 范围，guard 注释中明示。
- 复用共享 helper：`SourceLint.RepoRoot` / `SourceLint.Read` / `SourceLint.StripLineComment`（B06 唯一实现约定）。

## 检查点 B — 负向自证（注入违例样本判红）

`Uds_Ownership_Guard_Negative_Self_Proof`：内存变异真实源码（不触碰文件），3 个违例样本全部判红 + 变异未生效自保护：

| 注入违例样本 | 判红依据 |
|---|---|
| `bool ownsEndpoint = true;` + `if (true)`（属主检查短路，退回票11 修复前的无条件删除形态） | 形态 1 正则不匹配 + 形态 2 找不到 `if (ownsEndpoint)` |
| `if (ownsEndpoint)` → `if (_socketPath.Length > 0)`（删除由非属主条件守护） | 形态 2 找不到属主条件块 |
| `ownsEndpoint = _listener is not null` → `ownsEndpoint = _socketPath is not null`（属主判定与活监听器脱钩） | 形态 1 正则不匹配 |
| 自保护：任一变异 Replace 未生效（源码形态漂移致样本=绿源） | `MUTATION DID NOT APPLY` 判红，防负向自证空转 |

## 检查点 C — 单槽测试绿 + build 0 错 0 SCS

| 门禁 | 证据 | 结果 |
|---|---|---|
| build 0 错 0 SCS | `.scratch/architecture-recovery/closeout4-build-ticket13.log`（--no-incremental 全量）：0 error，SCS 出现 0 次（CA1707/CA1307 为测试成员命名/断言风格警告，与本仓既有 guard 文件一致，非 SCS） | ✅ |
| 本 guard 单类 | vstest --Tests:UdsEndpointOwnershipGuardTests：3/3 通过（含负向自证） | ✅ |
| 单槽全量 IntegrationTests | `.scratch/architecture-recovery/closeout4-vstest-ticket13.log`：首轮 275/275 通过（本票窗口独占运行）；此后共池工作区有并行窗口活动（详见下节披露） | ✅（首轮全绿） |
| 单槽全量 Core.Tests | 145/145 通过 | ✅ |
| semgrep（触及 Ipc 安全面） | `closeout4-semgrep-ticket13.json`：p/csharp 对新 guard 文件 + src/PhotoPrivacy.Ipc 全目录 0 findings | ✅ |

## 偏差与披露

1. **backlog 编号避让**：spec 建议"票13=B14"，但共池工作区中票14 窗口已先行占用 B14（`docs/backlog/B14-snapshot-scripts-promotion.md` 实物在案）。为避免撞号，本票 backlog 立票取 **B17**（第四轮四票 B14–B17 的最后一个），guard 文件头注释同步标注 "Ticket 13 (B17)"。
2. **全量重跑受跨票进行中干扰**（如实披露，非本票文件面）：
   - 重跑轮 1：4 失败（LocalizationServiceTests ×2 = bin 目录 locale 文件丢失；ServiceModeControllerTests ×2），总测试数 275→276 —— 并行窗口构建/测试打掉 bin 状态所致（WORKFLOW §5 单槽红线针对的场景）。重建后 locale 文件恢复，失败消失。
   - 重跑轮 2：1 失败 `ReleaseReadinessScriptValidationTests.PublishApp_Should_Copy_Tray_Assets_To_Publish_Root`——被测对象 `scripts/release-readiness.ps1` 工作区 diff 带"票16:"注释标记，属票16 窗口半成品（git status 在案）。本票 3 个 guard 与票11 相关 Ipc 测试在该轮全部通过（失败清单核实无 Uds/WorkerIpc 条目）。
   - 本票门禁以**独占窗口的首轮 275/275** 为准；重跑偏差逐条归因如上，均非本票改动引入。
3. 共池纪律：未触碰票14/15/16 的任何在途文件；提交仅含本票文件面。

## 提交物清单

- 新增：`tests/PhotoPrivacy.IntegrationTests/UdsEndpointOwnershipGuardTests.cs`（guard 3 断言 + 纯分类器 + 负向自证）
- 新增：`docs/backlog/B17-uds-transport-guard.md`（backlog 立票 + 完成记录）
- .scratch（gitignored）：`report-13-uds-transport-guard.md`、issues/13 勾选、closeout4 门禁留证 ×3、README 状态表更新
- docs/process（轨 1 沉淀，随票提交）：`docs/process/reports/13-uds-transport-guard.md`

## 遗留（交大脑）

- 无阻塞遗留。轮次四票合并次序与 push 归 R5 大脑流程动作。
