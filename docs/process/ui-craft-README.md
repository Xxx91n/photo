# README — ui-craft 轮（UI 审美规范级重塑）

生成：2026-09-12 ｜ 输入：.scratch/ui-craft/decision-ledger.md（D-001~D-009 决策 + A-001~A-010 问题簿）

## 产物索引

| 工件 | 路径 |
|---|---|
| 决策账本（唯一权威） | .scratch/ui-craft/decision-ledger.md |
| spec | spec.md |
| 票据（4） | issues/01~04 |
| handoff（4 + 下轮交接） | handoffs/01~04 + .scratch/ui-craft/handoffs/next-round.md |
| 窗口启动器（4） | prompts/01~04 |
| 票报告（执行窗交付） | reports/NN-slug.md（4 份已落盘） |
| 第 1 波大脑复核汇总 | review-ui-wave1.md |
| 返修启动器（波 1 复核后） | prompts/01-fix-context-entries.md、prompts/04-fix-report-registration.md |

## 波次表（从 issues Blocked by 推导，禁止新造顺序）

| 波次 | 票 | Blocked by | 共享文件依据 |
|---|---|---|---|
| 波 1 | 01 visual-standard-doc | None | 新文件为主（docs/design/、tests 约定区、ADR） |
| 波 2 | 02 nav-hover-feedback | 01 | Styling/AppTheme.axaml（nav 段） |
| 波 3 | 03 button-system-cleanup | 02 | Styling/AppTheme.axaml + src/PhotoPrivacy.Ui/Views/MainWindow.axaml |
| 波 4 | 04 pages-visual-alignment | 03 | src/PhotoPrivacy.Ui/Views/MainWindow.axaml + Pages/*.axaml |

说明：全串行非设计偏好而是共享文件纪律（WORKFLOW §4.3，一次一票同一文件）的推导结果。

## 状态表（2026-09-13 复核后更新）

| 票 | 复核结论 | 关键问题 | 处置 |
|---|---|---|---|
| 01 | ✅ 返修 ACCEPT（09-13 复核票） | P0 已修：两词条实物 1/1、与恢复源 sha256 逐字节一致、零回退、原报告三处勘误+§9、双轨一致、提交范围干净、未 push；3 项 P2 见 review。根因表述订正（复核子代理实测）：词条曾进入 mns 历史提交实物（42,660B blob aabfaed3），系其后票 04 but pull 重放+oplog restore 中被逐出，非「从未进入任何提交」 | 闭票；P2-1 表述订正随轮清扫 |
| 02 | ✅ PASS（6 项 P2 口径噪声） | 三根因修复/双轨/守卫全部实物证实；active 对比度 nord 3.81/dracula 4.15 低于 AA（观察项未登记） | 通过；P2 清单见复核报告 |
| 03 | ✅ PASS（6 项 P2） | 零裸按钮/等宽/附录 B 实物证实；快照落仓库内 photo-snapshots/ 不符 §4.4 仓库外约定（P2） | 通过；P2 清单见复核报告 |
| 04 | ⚠️ P1 · 04-fix 未执行 | 报告 §10 否认 pull 与 suo/sky commit+reflog 矛盾；快照落仓库内；实质交付全 PASS（A-007 删 Width=200/19 项 diff/清零/守卫 6 条）。09-13 实查：全盘无 04-fix 报告、分支无新提交——复核票未执行 | P1 转 backlog（启动器 prompts/04-fix-report-registration.md 有效待重派） |

**共通未达成项**：4 票均未 push（§4.2 无令不推）、CI 云端证据全缺——下一动作 = 大脑推验证分支触发云端验收。


## 收口合并（2026-09-13 大脑执行）

- 动栈前快照 ZERO-LOSS 269/269（D:/Aworker/photo-snapshots/20260913-143827）
- but pull：无新上游（up to date）；rl 依赖锁解铃（zqo 归位票 01 分支）
- 四支 --no-ff 按栈序合入 main：6185ef8(01) → 637eb45(03) → b3a93ba(04) → 3cfe884(02)；CONTEXT.md land 冲突按票 01 报告 §4.5-D-1(c) 一次性解析（取双侧并存版，5 词条计数全 1、0 冲突标记）
- **硬验收实测揪出 CI 历史盲区**：NavFeedbackSourceTests.cs 5 处 C# 转义缺陷（票 02 写入未转义引号串，node 沙箱复演测不出 Roslyn 语法错——首建 13 errors）+ L28 verbatim 修正 + L85 对齐 AppTheme 实物 Setter 形态；CONTEXT.md 被票 01 40px 改写击穿的措辞钉补回「4 按钮（…）」枚举（D-006 三档=保留+回归 guard 口径）。修复提交 f1e4f13
- 门禁终态：**build 0 错 0 SCS；Core 191/191 + Integration 329/329（CI filter 全绿）**（317→329=+12 守卫链吻合）
- 打包：release/win-x64/PhotoPrivacy.exe 177MB + PhotoPrivacy-0.1.0-uicraft-win-x64.zip 102MB（0.1.0-uicraft）
- 启动测活：进程 Responding=True；logs/ui+worker 双日志产出；InitializeRuntime done→TrayHost.IsVisible→Worker connect result endpoint 解析；测活进程已清理
- push：**未执行**（待用户明令）
## Frontier（2026-09-13 第 1 波复核后重算）

| 状态 | 项 | 说明 |
|---|---|---|
| 可开工（唯一） | **04-fix** | 登记修正（§10 如实登记 pull+快照落点、§4.1/§1 计数复算、复核后修复记录）；启动器 prompts/04-fix-report-registration.md 有效；窗口注意勿越权碰票 01 双轨报告与 CONTEXT.md |
| 已闭 | 01-fix | ACCEPT；票 01 分支 xov/mns {conflicted} 残留但四票 mergesCleanly 已全 true，不阻塞 land（land 时大脑统一解析） |
| 待用户 | push 授权 | 4+1 分支推验证分支触发 CI 云端门禁（ci.yml push 自动触发）——4 票完成定义「CI 云端绿」唯一未达项 |
| 待用户 | 探活 28+8 项 + 基线截图 | D-007 用户侧动作，票 02/04 视觉验收依赖 |
| 待裁定（登记不阻塞） | T-1 nav 色彩叙事 vs 中性灰（双版截图）；T-2 等宽策略；rl report-32 依赖锁 | 票 01/02 呈报张力，land 前裁 |
| 下轮 | 锐评六条 grill（D-008） | 本轮收口后 |
## 用户侧前置动作（不属于任何票）

- 探活 28 项执行 + docs/design/screenshots/ 基线截图（D-007；票 04 验收依赖其落地）

## 范围外（显式）

- 锐评六条 P0/P1 → A-010 deferred，下轮 grill 处置（D-008）
- Toast 实现 → A-009 deferred（规范文档含规划节）
- CI 视觉回归像素门禁、主题基座更换（D-001/D-007 负向）

## 三段覆盖声明（对账闸产物，2026-09-12）

| 段 | 规则 | 结果 |
|---|---|---|
| 段 1 | A current ⊆ spec 声明 | 通过（7/7：A-001~A-005、A-007、A-008） |
| 段 2 | A current ⊆ 票声明并集 | 通过（7/7） |
| 段 3 | 票声明并集 = spec 声明全集 | 差 1：**A-010 显式范围外**（deferred 无票，D-008 裁定下轮 grill 处置）——非缺漏，已登记 |

**全局决策无票（预期）**：D-001（诊断框架/基座不动）与 D-008（锐评六条下轮）为 spec 级全局决策，无独立票属设计预期；D-001 见 spec Problem Statement/Solution，D-008 见 spec Out of Scope。

## 路径偏差声明（须用户确认）

用户指令模板指定写入 `.scratch/architecture-recovery/`；该目录已有第七轮 spec.md（7017B）、issues/、handoffs/、prompts/ 在途产物，同名写入会覆盖第七轮工件。故本轮全部产物改写至 `.scratch/ui-craft/`（slug=ui-craft，与 decision-ledger、next-round handoff 同源）。**未覆盖、未删除 architecture-recovery 任何既有文件。**
