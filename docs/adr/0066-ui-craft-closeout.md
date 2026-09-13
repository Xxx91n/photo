# ADR 0066 — ui-craft 轮收口：UI 审美规范级重塑（S2）

日期：2026-09-13 ｜ 状态：已收口（push 待用户明令）｜ 前序：ADR 0064（第七轮）

## 背景

用户实机痛点：按钮长度大小不统一、nav hover 反馈「很 AI 很塑料」、整体无审美感，并质疑 Avalonia 社区不发达。grill 轮（`.scratch/ui-craft/decision-ledger.md`，D-001~D-009）确立诊断：基座（Semi+Ursa，社区第 2/3 名且活跃）不动，问题在「决策→落地→验证」链断在验证节（28 项探活零执行、source-lint 锁字符串不锁视觉、页面级规范缺位）。范围 S2 规范级重塑（4 票串行设计）。

## 决策（票面）

| 票 | 内容 | 覆盖 |
|---|---|---|
| 01 | UI 视觉规范活文档 `docs/design/ui-visual-standard.md`（七节+三锚+附录 A/B/C，v1.2）+ ADR 0065 + CONTEXT 两词条（UI Visual Standard/Visual Baseline）+ 44px→40px 订正 + TEST-CONVENTIONS R1/R2 守卫规则 + report-32 探活扩充（G 组 6 项+四页拍摄规程） | A-003/A-005/A-006 |
| 02 | nav 三根因修复：Transitions 三路 150ms SineEaseOut（Foreground 一路为原 0ms 硬切即塑料感现场）、hover 前景 SemiColorPrimary 色彩叙事、accent 常驻槽位（4,0,0,0+Transparent 占位+pointerover 点亮）；禁 scale/弹性缓动；NavFeedbackSourceTests 4 条 | A-002 |
| 03 | 零裸按钮 16/16（票面「2 枚裸按钮」经 git blame 542c5059 证实系单行 grep 假阳性）、SharedSizeGroup 等宽（ServiceActions 3+RuleActions 2，弃固定 MinWidth 的 10 语言理由）、规范附录 B 变体对账表 | A-001 |
| 04 | 四页对齐 19 项（对照规范节号）、A-007 删 MainWindow 内部 Border Width=200 硬钉（判定缺陷）、A-008 离轨清零+Spacing token 化、ConfigPage 三枚 inline-control 迁址、PagesVisualAlignmentSourceTests 6 条、空态评估 | A-007/A-008 |

返修：01-fix（P0 词条丢失补落，ACCEPT）。

## 合并拓扑

main(8a96564) ← 6185ef8(01) ← 637eb45(03) ← b3a93ba(04) ← 3cfe884(02) ← f1e4f13(收口硬验收修复)。四支 --no-ff 按栈序（ft→ra→cr→ui）；CONTEXT.md land 冲突按票 01 报告 §4.5-D-1(c) 一次性解析（取双侧并存版 42862B：上游 ADR 0064 两词条+票 01 两词条+40px，5 词条计数全 1、0 冲突标记）。

## 门禁证据（收口硬验收，用户明令本机执行）

- 动栈前快照 ZERO-LOSS 269/269（`D:/Aworker/photo-snapshots/20260913-143827`）；but pull up to date；rl 依赖锁解铃（zqo 归位票 01 分支）
- **build 0 错 0 SCS**
- **Core 191/191 + Integration 329/329**（CI filter `Category!=Smoke&Category!=ExifTool` 全绿；317→329 = NavFeedback 4 + PagesVisualAlignment 6 + DesignSystem 票 03 两断言，守卫链吻合）
- 打包：`release/win-x64/PhotoPrivacy.exe`（177MB）+ `PhotoPrivacy-0.1.0-uicraft-win-x64.zip`
- 启动测活：进程 Responding=True；logs/ui+worker 双日志产出（InitializeRuntime done→TrayHost.IsVisible→Worker connect result endpoint 解析）；测活进程已清理
- push：未执行（待用户明令）

## 硬验收揪出的 CI 历史盲区（本轮最大教训）

**NavFeedbackSourceTests.cs 存在 5 处 C# 转义缺陷**（票 02 窗口写入了未转义引号串：`Property="Foreground"` 应为 `Property=\"Foreground\"`，另有 L28 正则 `\s\S` 需 verbatim、L85 需对齐 AppTheme 实物 Setter 形态）——首次 `dotnet build` 即 13 errors。**全部既有验证手段都没抓到**：窗口静态门禁、子代理复核、node 沙箱断言复演（node 不经 Roslyn）。教训：**CI 首跑不可被任何静态复演替代**；本缺陷若无收口硬验收将持续到 CI push 首跑才暴露。

次要：票 01 的 40px 改写击穿 `ContextMdTerminologyTests` 措辞钉（`4 按钮（…）` 枚举丢失）——D-006 三档处置=保留+词条补回枚举（f1e4f13）。

## 违规与未闭项（单独呈报，不追认）

1. **04-fix 复核票未执行**：全盘无报告、分支无新提交——票 04 报告 §10「未执行 pull」不实否认与 §4.1 计数（15 项 vs 19 行）未修正，P1 转 backlog
2. 01-fix 报告 P2-1：根因表述「从未进入任何提交」过度（实测 blob aabfaed3 证明词条曾入 mns 提交，系票 04 pull 重放+oplog restore 逐出）——README 已订正，报告原文未改
3. 票 03 快照落仓库内（§4.4 约定仓库外）P2
4. report-32 锚点索引仍记 Width=200 旧实态（P2 观察项）
5. g0 残留分支（票 32 终态收口 oxw+snp/yqn conflicted）未合并未删——转 backlog

## Backlog（待用户裁定是否立票）

1. 04-fix：票 04 报告 §10/§4.1/「15 项」登记修正（启动器有效待重派）
2. 运行侧探活 28+8 项 + docs/design/screenshots/ 基线截图（D-007 用户侧动作；票 02/04 视觉验收依赖；A-004 deferred 的闭票条件）
3. CI push 门禁首跑（与 push 同一动作；预期全绿但「CI 首跑不可替代」教训仍在）
4. Ursa Toast 反馈实现票（A-009 deferred；规范 §6 规划已备）
5. CI 视觉回归（Headless+Skia）评估（D-007 负向降 backlog）
6. active 态对比度 nord 3.81/dracula 4.15 低于 AA 的观察项（票 02 复核发现，未登记到报告）
7. CONTEXT.md Button Transition Animation 词条仍记 ADR 0054 已删的 scale(0.97)（票 03 呈报）
8. g0 残留分支处置（合并或删除，需先解 snp/yqn conflicted）
9. 锐评六条 P0/P1 下轮 grill 专项（D-008 既定）

## 三层一致性

CONTEXT.md：UI Visual Standard/Visual Baseline 词条指向 docs/design/ui-visual-standard.md 与 docs/design/screenshots/（实物均存在）；Sidebar Nav Item 40px 与 AppTheme 实物 nav/nav-action Height=40 双验；Manual Dispatch/Composition Root/WindowPollingHostedService 沿用。ADR 0065 D1-D7 与规范文档分工一致。代码实物抽查：AppTheme nav 段 150ms×11/SineEaseOut/Height 40/BorderThickness 4,0,0,0、SharedSizeGroup 4+3——与规范 §1/§2 声明一致。
