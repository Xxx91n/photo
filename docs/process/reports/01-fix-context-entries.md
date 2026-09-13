# Report — 票 01 返修：CONTEXT.md 两词条补落（ui-craft 轮）

- **窗口**：票 01 返修窗口（子 Agent）
- **启动器**：`.scratch/ui-craft/prompts/01-fix-context-entries.md`
- **开工声明**：必读清单 8 份已逐份读全（原报告 §1 表第 4 行与 §4.5、handoff 完成定义、issues、spec Implementation Decisions 第 1 条 + 覆盖声明、decision-ledger D-009 / A-006、WORKFLOW §4.2 / §4.3 / §4.4、CONTEXT.md 工作区实物、docs/design/ui-visual-standard.md 现物），无缺失，未触发「停下呈报」。C1–C3 已逐条自证，确认复核结论属实后动手。

## 1. 声明（本窗口主张）

1. 复核查出的 P0 缺失**属实**：CONTEXT.md 两词条在最终实物中 grep 计数为 0。
2. 根因较票面所述更严重：不只是「resolve cancel 回滚致词条未入提交」，而是**两词条从未进入任何提交**（详见 §5）。
3. 修复以仓库外快照原文还原，零回退、零冲突标记、插入位置对齐恢复源。
4. 原报告 §1 表第 4 行的「PASS（C2）」为**不实结论**，已勘误并保留修改痕迹。
5. 本机零构建零测试（CI-only）。

## 2. 证据 → 结论 对照表

| # | 声明 | 证据（可复现 / 实测值） | 结论 |
|---|---|---|---|
| C1-1 | 两词条确为 0 命中（不采信复核自述，亲自 grep） | 返修前 `CONTEXT.md` = 41,560 B；`grep -Fc "**UI Visual Standard**"` = **0**；`grep -Fc "**Visual Baseline**"` = **0** | **属实** |
| C1-2 | 40px 修正在位 | `**Sidebar Nav Item` ×1、`40px` ×2、`44px` ×2（沿革保留） | **属实** |
| C2-1 | 恢复源可用且两词条含 `_Avoid_` 行 | `photo-snapshots/20260913-010146/ticket-all-protect/CONTEXT.md` = 42,862 B；`UI Visual Standard` 行 406–408、`Visual Baseline` 行 410–412，各含标题 + 正文 + `_Avoid_` | **属实** |
| C2-2 | 恢复源同时含上游 ADR 0064 两词条（双侧并存基准） | 恢复源 `**Composition Root` ×1、`**WindowPollingHostedService**` ×1 | **属实** |
| C3-1 | 当前工作区已含上游两词条与 40px（不得回退） | 返修前实测：`**Composition Root` ×1、`**WindowPollingHostedService**` ×1、`**Sidebar Nav Item (40px` ×1 | **属实** |
| R-1 | **两词条从未进入任何提交**（根因） | 逐提交 `git show <c>:CONTEXT.md`：xov / sln / zrn / mns / uyn **均 41,560 B，两词条计数恒为 0** | **属实** |
| F-1 | 修复后实物到位 | `CONTEXT.md` = 42,862 B / 413 行，与恢复源**逐字节相同**；UVS=1、VB=1 | **达成** |
| F-2 | 插入位置对齐恢复源 | `ServiceModeController`(394) → `Composition Root`(398) → `WindowPollingHostedService`(402) → `UI Visual Standard`(406) → `Visual Baseline`(410) | **达成** |
| F-3 | 路径引用与实物一致 | 词条内引用 `docs/design/ui-visual-standard.md`、`docs/design/screenshots/`，两者磁盘均存在 | **达成** |
| F-4 | 零回退 | 上游两词条 ×1→×1；40px ×1→×1；44px 沿革保留；冲突标记 0→0；无 BOM、0 CRLF | **达成** |
| E-1 | 原报告勘误完成 | §1 表第 4 行 / §2 交付物表 / §4.5-C 三处改写 + 新增 §9 返修记录；双轨逐字一致 32,225 B | **达成** |
| S-1 | 本机零构建零测试 | 未执行 `dotnet build` / `dotnet test`；`git diff --check` 为空 | **达成** |

## 3. 专属验收项对照

| 验收项 | 实测 | 结果 |
|---|---|---|
| CONTEXT.md 两词条实物存在（grep ≥1）且含 `_Avoid_` 行 | UVS=1（行 406–408）、VB=1（行 410–412），`_Avoid_` 行实测存在 | **PASS** |
| 上游两词条 + Sidebar Nav Item 40px 修正零回退 | 见 F-4 | **PASS** |
| 报告勘误完成，双轨逐字一致 | 32,225 B，`.scratch` 主本 == `docs/process/reports` 副本 | **PASS** |
| 本机零构建零测试；静态门禁自证 | 见 S-1 | **PASS** |

## 4. 提交

| 提交 | 内容 |
|---|---|
| `koy` | CONTEXT.md 补落两词条（41,560 B → 42,862 B），分支头 `conflicted: false` |
| `qqt` | 原报告勘误三处 + 新增 §9 返修记录（26,792 B → 32,225 B） |

两笔均落在 `ui-craft/01-visual-standard-doc`，遵循 WORKFLOW §4.2（未 push）。

## 5. 返修记录（缺失根因）

### 5.1 事实链

1. 票 01 原窗口在 `mns` 提交中写入了包含两词条的 CONTEXT.md 解析内容，工作区曾达 42,862 B。
2. 但 GitButler 的 **hunk 级提交未捕获这两段**：提交实物为 42,660 B，工作区为 42,862 B，差 202 B；该差异当时被误判为「上游第 53/61/62 行发布条目，非丢失」（见原报告 §4.7）。
3. 票 04 窗口执行 `but pull` 推进基准后，工作区 CONTEXT.md 落在 **41,560 B** 版本——含上游 ADR 0064 两词条与 40px 修正，**但不含本票两词条**。
4. 逐提交取证（§2 表 R-1）证实：**五个提交的 CONTEXT.md 实物恒为 41,560 B、两词条计数恒为 0**，即两词条从未真正落盘。

### 5.2 根因判定

- **直接根因**：GitButler hunk 级提交在多次回滚 / 重放（`resolve cancel --force` → `oplog restore 41438d5` → 后续 `but pull`）中未捕获该文件的这两段新增，导致提交实物与工作区长期脱节。
- **放大因素**：原窗口以「六件与备份逐字节一致」自证完整性，**未做交付项级（词条级）grep 计数核验**，故漏检。
- **教训**：文件级字节一致性**不能替代交付项级计数核验**。凡「某交付项已落盘」的结论，必须以该交付项自身的唯一标识做计数取证，而非以容器文件的一致性推断。

### 5.3 修复

- 恢复源：`photo-snapshots/20260913-010146/ticket-all-protect/CONTEXT.md`（42,862 B，WORKFLOW §4.4 轨 2 仓库外快照）。
- 前置校验：恢复源与返修前工作区**前 404 行逐字节相同**，差异仅为待补区块 → 整体还原即等价于按原文补入，且不触碰上游两词条与 40px。
- 结果：42,862 B / 413 行，与恢复源逐字节一致。

## 6. 遗留与呈报（未处置，交大脑）

1. **票 01 分支的 `xov` 与 `mns` 现为 `conflicted: true`**——源自票 04 窗口 `but pull` 推进基准（其提交信息自述「but pull 后票01分支呈 conflicted 状态交其窗口处理」）。分支头 `koy`、`qqt` 及 `uyn` / `zrn` / `sln` 均 `conflicted: false`，且 `but branch list` 显示**四票 `mergesCleanly` 现全为 `true`**，故尚不阻塞合流；是否清理 `xov` / `mns` 的冲突标记请裁定。
2. **原报告 §4.5-D-1 与 §4.7 的结论对当前状态已过时**——彼时「mergesCleanly: false 阻塞票 01 / 票 03」已因后续 rebase 解除。该两节仅作历史留档，已在新版 §9.6 注明。
3. **`rl`（report-32 探活清单扩充）仍在 `zz` 未提交区**，依赖锁未解（`lines 87–102 / 105 / 141–159 depends on commit yqn`）。本次返修未触碰，维持原处置。
4. 用户侧 28 项探活与基线截图仍为 0（CI-only 禁 agent 本机构建与运行），视觉验收机制待用户执行后方成立。

## 7. 静态自证

- 本机零构建、零测试（CI-only）。
- `git diff --check` 为空。
- CONTEXT.md：无 BOM、0 CRLF、0 冲突标记。
- 报告双轨（`.scratch` 主本 + `docs/process/reports` 副本）逐字一致。
- 本返修报告同步沉淀至 `docs/process/reports/01-fix-context-entries.md`（WORKFLOW §4.4 轨 1）。