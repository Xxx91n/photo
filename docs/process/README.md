# docs/process — 流程产物沉淀区（受版本控制的灾备权威副本）

> 立策：.scratch/architecture-recovery/WORKFLOW.md §4.4（票12，2026-09-02）。背景事故：ADR 0058 事故 1。

.scratch/ 被 .gitignore 忽略、不受版本控制，GitButler 栈手术曾令其整树蒸发。本目录承载关键流程产物的受控副本（轨 1）；issues/handoffs/prompts 等单票工作文件不逐份沉淀，由"动栈前仓库外快照"轨保护（`D:/Aworker/photo-snapshots/<时间戳>/`，附 SHA256 manifest）（轨 2）。

| 文件 | 来源（工作版） | 同步时机 |
|---|---|---|
| WORKFLOW.md | .scratch/architecture-recovery/WORKFLOW.md | 定稿/重要修订后；每票收口提交前 |
| round3-spec.md | .scratch/architecture-recovery/spec.md | 同上 |
| round3-README.md | .scratch/architecture-recovery/README.md | 同上 |
| reports/<票号>-<slug>.md | .scratch/architecture-recovery/report-<票号>-*.md | 每票报告写盘后随票提交 |

任一侧修订后须同步另一侧；以 .scratch 工作版为会话内权威，本目录为可复盘灾备权威。