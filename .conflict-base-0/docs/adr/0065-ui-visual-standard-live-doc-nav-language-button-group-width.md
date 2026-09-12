# ADR 0065: 页面级视觉规范活文档建立 —— nav 设计语言与按钮组宽度策略的关键取舍

**Date**: 2026-09-12
**Status**: Accepted
**Branch**: ui-craft/01-visual-standard-doc（GitButler 虚拟分支）
**来源**: ui-craft 轮票 01 + handoff 01 + spec（S2 规范级重塑）+ decision-ledger D-004 / D-005 / D-007 / D-009 + A-001 / A-003 / A-006 / A-009

---

## 背景

七轮 UI ADR（0048 / 0050 / 0051 / 0052 / 0054 / 0055 / 0056 / 0062）建立的是 **token 与变体级**规范，**页面级规范从未定义**（A-003）——这是「决策→落地→验证」链断在第三节的根因之一（D-001）。用户两大痛点：按钮长度与大小不一（A-001）；侧栏 nav hover「很 AI 很塑料」（A-002 / D-004）。

本 ADR **不重开**既有 ADR 已定的边界：

- 按钮反馈 = 纯色 / 透明度派，无 scale（ADR 0054 + ADR 0062 D2）；
- 禁弹性缓动 BackEaseInOut / ElasticEaseInOut（ADR 0051 A2）；
- 7 变体 × 5 态矩阵（ADR 0062 D3）；Button Size Ladder 唯一权威在 AppTheme.axaml（ADR 0056 票 01）；
- 主题基座 Semi.Avalonia + Ursa 不动（D-001）。

---

## 决策

### D1: 规范载体 = 活文档 + ADR 分工（D-009 落地）

新建 docs/design/ui-visual-standard.md 作为**唯一现役**页面级视觉标准（七节：按钮组宽度策略 / nav 反馈三态 / 表单行骨架 / 空态规范 / 间距节奏 / Toast 反馈链规划 / 验收标尺）。ADR 只记「为什么」，CONTEXT.md 只加词条指针（UI Visual Standard / Visual Baseline），后续 UI 票直接引用规范节号。

- **不选 A（单篇冻结 ADR 承载规范）**：规范会随票演化，冻结 ADR 会产生补丁链（锐评 §7.3「文档与正确性脱钩」的温床）。
- **不选「把全文搬进 CONTEXT.md」**：CONTEXT.md 是 glossary，承载规范全文会破坏其纯度（domain-modeling skill 既定规则）。

### D2: nav hover 三态设计语言 —— 沿用纯色派，补 Foreground 过渡与三态联动

**决策**：idle / hover / active 三态统一走**色彩 + 透明度**叙事，不引入 scale / 位移 / 弹性缓动；hover 时图标与文字**向 primary 叙事**（D-004，用户拍板）而非跳到近白；active 的 4px accent bar 在 hover 上给**低透明度预示**，使三态连贯。

**关键发现（票 01 静态核验，修正 D-004 的时点假设）**：D-004 记「pointerover 段无 Transitions setter」。变体段确实没有，但**票 26（ADR 0062，2026-09-04）已在全局 Button 选择器加了 BrushTransition Background/BorderBrush 150ms SineEaseOut**（AppTheme.axaml:337-347）。因此：

- D-004 根因①「无过渡硬切」**已部分缓解**（背景已有过渡）；
- **真正残留的硬切在 Foreground**——Button.nav:pointerover 同时改 Foreground（Text2→Text0），而全局 Transitions **不含 Foreground**；
- 推论：**「塑料感」的第一现场不是「没有动画」，而是「背景平滑动画 + 前景 0ms 硬切」的两路不同步**。票 02 的定值重点应放在 Foreground 过渡 + 色彩叙事 + accent 预示，而非重复加 Background 过渡。

**与工业证据的张力（T-1，须大脑裁定）**：atomcode 控件级调研显示 VS Code / WinUI / Discord / macOS 的 nav hover **全部为中性灰叠加，accent 只属 active/selected**，且 hover 多为「切换」而非「过渡」（WinUI 唯一可复用数值为 Button 的 83ms BrushTransition）。这与 D-004 的「hover 向 primary 叙事」方向相反。

- **本 ADR 不静默改向**：裁定前沿用 D-004（用户决策优先）；
- **裁定机制**：票 02 须同时产出「primary 叙事版」与「中性灰 + 亮度叙事版」两版 before/after 截图，按 ui-visual-standard.md §7 同机位人工对照，结论回写规范 §2.4 与 §8。

### D3: 按钮组宽度策略 —— 同组等宽（含文案悬殊例外）

**决策**：同组（同容器、语义同级、≥2 枚）按钮**等宽**，实现为「组内最长文案 MinWidth」或「Grid 等分列 + HorizontalAlignment=Stretch」；单枚按钮（服务页「安装」「卸载」各占独立行）保持 hug 内容 + 固定 Padding。等宽**只调 MinWidth / 列宽**，不改 Height / Padding / FontSize（尺寸权威仍在 AppTheme）。

- **R1-5 例外**：组内最长与最短文案长度差 >2 倍时，改用**样式层级区分主次**（accent vs 中性），不强行拉宽短按钮。
- **现状缺口（票 03）**：ServiceManagerPage.axaml:43-45「启动 / 停止 / 刷新」三枚 ghost、RulesPage.axaml:29-30「保存 / 重置」两枚 ghost 均无等宽约束（宽度 = 文案长度）。

**与工业证据的张力（T-2，须大脑裁定）**：调研显示只有**对话框语境**等宽（WinUI ContentDialog 五列 stretch + ButtonSpacing=8 + PR #3926 确认是设计意图；Apple HIG 的成组信号靠「同尺寸 / 同样式」而非等宽拉伸），**页内工具按钮行**四大平台（WinUI / Apple / M3 / VS Code）均为**内容自适应**。本项目 A-001 决策的「同组等宽」与之在页内语境不一致——裁定前沿用等宽（用户决策优先），由票 03 双版截图人工裁定。

### D4: 表单行骨架 —— 对齐 Apple 设置骨架 / WinUI SettingsCard，不重新发明

**决策**：沿用现有 settings-card / settings-row / row-divider 三段式（左标签 + 右控件），并按调研证据对齐三处：

- **B1 分隔线 inset**：现为通栏（BorderThickness 0,0,0,1）；macOS 侧要求分隔线左右各留 ≥20pt、WinUI SettingsCard 为行内分隔 → 改为自标签列起点 inset。
- **B2 控件列宽度统一**：现仅路径行被 PathPicker.inline-input Width=280 共享 class 收敛；ConfigPage.axaml:113 的 ComboBox Width=160 属**行内写宽**，与票 24（ADR 0061）已立的「Views 禁止内联宽度」口径冲突 → 引入 SettingsCardContentMinWidth 同位的控件列最小宽度约束。
- **B3 卡片间距**：ConfigPage.axaml:8 为 StackPanel Spacing=0（卡片直接相邻）；WinUI SettingsCard 官方示例为 Spacing=4、macOS 要求分组之间有间距形成视觉簇。
- **响应式（可选，暂不强制）**：SettingsCard 在 800px / 600px 断点把控件换行到标题下方；本项目窗口 MinWidth=740，属可复现场景，列为票 04 评估项。

**密度归属**：列表 / icon 密度法取自 VS Code / Discord，**不把 IDE 的高信息密度带进表单**（D-002：默认界面面向大众隐私用户）。

### D5: 空态 / 间距节奏 / Toast —— 本 ADR 不定值，只记录归属

- **空态**：日志页（log.empty / HasNoLogs）与规则页（rules.empty / HasNoVisibleRules）已由 ADR 0062 D5 落地；规范 §4 统一骨架与文案口径（E1–E5），服务页「未安装」态缺口交票 04。
- **间距节奏**：token 定义不变（DesignTokens.axaml）；规范 §5 新增**页面级消费规则** P1–P7，A-008 的 14 处 Margin 残留随票清（撞见才改）。
- **Toast**：A-009 **deferred**。规范 §6 只做规划（触发点 / 位置时长 / 视觉 / 语义色 / 无障碍 / 去重排队），实现票另裁；接线点已由 ADR 0062 D6 移交 ConfigEditor 保存完成回调。

### D6: 验收标尺 —— 三锚 + 截图基线，不建 CI 像素门禁

- 每张 UI 票验收 = **同机位 before/after 人工对照** + 对照 D-005 三锚的**具体设计点**；拍摄规程落 docs/process/reports/32-ui-manual-smoke.md §5.2，基线资产落 docs/design/screenshots/。
- **不上 CI 视觉回归像素门禁**（D-007 负向：审查负担 + 环境漂移；与 D-006「Headless 小步谨慎」同口径），Headless VRT 降 backlog。
- **视觉一致性不交给纯文本断言单独背锅**：守卫规则写入 tests/TEST-CONVENTIONS.md（R1 新断言必写「防什么」+ 票号；R2 撞红三档分类）。

### D7: 44px → 40px 词条修正（A-006 闭合核验）

CONTEXT.md「Sidebar Nav Item」词条原记 44px（ADR 0052 A1 原始规格），与 ADR 0055 A1 / ADR 0056 票 01 的 40px 及 AppTheme.axaml 实物（Button.nav / nav-action Height=40）矛盾。

- **核验结果**：grill 账本 A-006 标「已闭合（2026-09-12 grill 整理环节执行）」，但**工作区实物在票 01 开工时仍为 44px**（git log -- CONTEXT.md 最后一次相关提交为票 28，无 44px 修正提交）——**账本声明与磁盘实态不符，本票实际执行修正并呈报大脑**。
- 修正方式：词条改为 40px 并保留沿革说明，不静默删除历史口径。

---

## Consequences

- **正**：页面级规范首次有了唯一权威与节号引用体系；nav 与按钮组的「为什么」与「是什么」分离，规范可随票演化而不污染 ADR。
- **负**：新增 1 份活文档 + 1 份测试约定文件 + 1 份 ADR 需长期维护；规范与代码的漂移需靠每票 before/after 对照发现——report-32 终态为 28 项全部「未探活」，本票新增 G 组与 S1/S2 同为「未探活」初始态，无对照物则本节的验收机制不成立。
- **未决**：T-1（nav hover 色彩叙事）与 T-2（页内按钮组等宽）须大脑裁定；裁定前票 02 / 03 须以双版截图提供依据。

## 约束

- 不换主题基座、不引新 NuGet 依赖（D-001）。
- 不触碰 Core / Worker 源（D-008：锐评六条本轮范围外）。
- 本机零构建 / 零测试（CI-only）；验收交云端 .github/workflows/ci.yml。
- 版本控制遵循 WORKFLOW §4.2（GitButler 虚拟分支、一票一分支、不 push 不 PR）。

## 调研引用

- atomcode 5.0.9 控件级调研（2026-09-12）：VS Code activityaction.css / workbenchThemeService.ts / dialog.css；WinUI NavigationView_rs1_themeresources.xaml / Button_themeresources.xaml / ContentDialog_themeresources.xaml + PR #3926；Apple HIG Buttons / Sidebars / Color；M3 Dialogs Guidelines / Specs / Buttons Specs；CommunityToolkit SettingsCard.xaml；Discord 设计令牌（三信源交叉）；zenn《macOS Settings Window Guidelines》。完整 28 条来源见 .scratch/ui-craft/reports/01-visual-standard-doc.md §5。
- 既有 ADR：0050 / 0051 / 0052 / 0054 / 0055 / 0056 / 0062（规范 §0 与 §1–§5 的事实基线）。
- 规范正文：docs/design/ui-visual-standard.md（七节 + §0 三锚 + 附录 A）。
