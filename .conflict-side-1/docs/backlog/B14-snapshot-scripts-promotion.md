# B14 — 快照/校验脚本转正裁决

- **优先级**: 中　**来源**: B12 遗留（票12 报告"遗留"节；架构恢复第四轮票14）

## 问题与验收

WORKFLOW §4.4 轨 2 的执行脚本 b12-snapshot.js / b12-verify.js 暂居 .codex-tmp（gitignored 临时身份，票12 收口已按临时脚本纪律清理）。裁决：转正为 scripts/ 受维护脚本（+ 源守卫 + AGENTS §10 登记），或弃用删除并在 WORKFLOW §4.4 注明替代手段。验收：裁决结论成文；按结论执行；WORKFLOW §4.4 工具段与实际一致；build 绿，改 scripts/ 则 semgrep 留证。

## 裁决与完成记录（2026-09-02，票14）

- 裁决：**转正**。依据：脚本实体已被票12用后清理（.codex-tmp 为空、git 全历史无记录），而轨 2 是常驻强制策略（当日 7 次实际使用），弃用路径将使强制条款无受维护实现；按 report-12 行为规格 + 既有快照 manifest 格式标本忠实重建为 `scripts/workflow-snapshot.js` / `scripts/workflow-verify.js`（去票号化命名）。
- 落地：两脚本纯 Node 内置 fs/path/crypto（零依赖、无子进程）；源守卫 tests/PhotoPrivacy.IntegrationTests/WorkflowSnapshotScriptGuardTests.cs（7 断言含守卫自证）；AGENTS.md §10 登记用法；WORKFLOW §4.4 工具段成文且 .scratch 与 docs/process/ 双侧逐字节一致。
- 实测：真树快照 29 文件 → ZERO-LOSS 29/29 哈希一致；manifest schema 与既有标本（074101–083352）逐字段一致；篡改/缺失/新增负向自证 exit 1/1/0。
- 门禁：build 0 错 0 SCS；semgrep scripts/ 留证（见报告门禁节）。
- 报告：.scratch/architecture-recovery/report-14-snapshot-scripts-promotion.md（受控副本 docs/process/reports/14-snapshot-scripts-promotion.md）。
