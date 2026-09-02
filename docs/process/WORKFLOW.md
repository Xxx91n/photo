# WORKFLOW.md — PhotoPrivacy 多窗口编排权威（第二轮重建版）

> 2026-09-01 重建说明：本文件在 GitButler move/undo 事故中与 .scratch 全树同灭，现按会话上下文重建。条款语义延续第一轮 DEVIATIONS（已批准）所确立的本地适配。

## §1 角色
- 大脑（本 session）：宏观调查、派发、复核、收口。不亲自实现。
- 窗口：一票一窗，亩产无旁骛。

## §4 执行纪律
### §4.2 版本控制（唯一来源）
- GitButler 虚拟分支；每票一独立分支；禁裸 git 写命令；不 push 不 PR，除非用户明令。
- commit 信息中文、带票号；哈希以 git log 实物为准。
### §4.3 共享文件与停手规则
- 跨票共享文件一次只许一票碰；GitButler 依赖拒绝时按 §7 处置链处理，处理不了留未提交区+报告移交大脑。

### §4.4 流程产物持久化（B12 立策，2026-09-02）

依据：`.scratch/` 整目录被 .gitignore 忽略（`git ls-files` 计 0），不受版本控制；GitButler 栈手术曾令其整树蒸发（ADR 0058 事故 1）。流程产物（WORKFLOW/spec/issues/handoffs/prompts/reports/波次表）按双轨并用保护：

- **轨 1 — docs/ 沉淀（定稿即沉淀）**
  - 触发时机：任一流程文件定稿或重要修订后；每票收口提交前。
  - 动作：WORKFLOW.md、spec.md、轮次 README 与票报告的受控副本落 `docs/process/`（票报告落 `docs/process/reports/`），随票提交进 git；issues/handoffs/prompts 等单票工作文件由轨 2 快照保护，不逐份沉淀。`.scratch` 内为工作版，`docs/process/` 为受版本控制的灾备权威副本，任一侧修订后须同步另一侧。
- **轨 2 — 仓库外快照（动栈前强制）**
  - 触发时机：执行任何 GitButler 历史改写或丢弃类操作之前——move / undo / resolve cancel / squash / discard / uncommit / branch delete / pull，无论目标分支是否属于本票。
  - 动作：整树复制 `.scratch/architecture-recovery/` 到仓库外时间戳目录（约定 `D:/Aworker/photo-snapshots/<yyyyMMdd-HHmmss>/`），附 manifest.json（逐文件字节数 + SHA256）；快照完成前禁止动栈。
- **实测留证**：2026-09-02 完成一轮无破坏实测（快照 → but pull → 演练分支 move/squash/uncommit/discard 序列 → 逐点哈希比对零丢失），证据链见 `.scratch/architecture-recovery/report-12-workflow-artifact-persistence.md` 及其 docs 沉淀副本。

## §5 门禁
- build 0 错 0 SCS；相关测试套件绿；触及安全面跑 semgrep。
- 测试执行**单槽串行**，多窗口不得并发跑 test。

## §7 教训登记（累计）
1. 壳层嵌套引号/$ 吞字 → 一切产物用 Write/node 脚本落盘。
2. 测试单槽串行（并行 testhost 爆 MSB3027/锁）。
3. 报告 commit 以 git log 物理哈希为准，but slug 不作数。
4. 同文件多票须在设计期给出显式 Blocked by（票01/02 撞车复盘）。
5. **.scratch 全树会被 GitButler move/resolve-cancel/undo 序列清空（2026-09-01 实爆）**——已立双轨防护（docs/ 沉淀 + 动栈前仓库外快照），条款见 §4.4，2026-09-02 起强制执行。
6. 分支大搬迁（move）会引起真实三方内容冲突，不动 rhymes with 高价值未提交改动。
