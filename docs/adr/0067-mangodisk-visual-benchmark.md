# ADR 0067: 视觉对标主基准切换 —— 三锚降级为气质参考，MangoDisk 观感级全面对标

**Date**: 2026-09-14
**Status**: Accepted
**Branch**: ui-craft2/01-visual-standard-v2（GitButler 虚拟分支）
**来源**: ui-craft2 轮票 01 + spec §0 + decision-ledger D-005 / D-006 / A-001 / A-005 + 上轮账本 D-005（revised 关系）

---

## 背景

ui-craft 轮按上轮 D-005「三锚」（Wasabi 气场 × Apple 系统设置骨架 × VS Code·Discord 密度法）执行并交付了按钮等宽、nav 三态反馈、表单骨架对齐等成果。但用户在现役构建上复验后判定：侧栏四枚 nav 按钮「鼠标停留、移开的反馈效果很丑陋，大小、风格整体都很丑，或许应该参考别的库的效果」（ui-craft2 D-001）——**按三锚执行后的成品仍不达审美预期**，参照系本身成为问题。

圆角窗口裁决（ui-craft2 D-004）过程中用户给出新参照物：`harry0703/MangoDisk`——Tauri 2 + Rust + Vue 3 + Tailwind + shadcn-vue 风格组件（reka-ui / cva / vue-sonner），磁盘清理 + 隐私清理同赛道产品，2552★、持续活跃。用户明选「观感级全面对标」（D-005 = A）。

## 决策

### D1: 主基准 = MangoDisk，三锚降级为气质参考

`docs/design/ui-visual-standard.md` 升 v2.0，§0 改立 MangoDisk 主基准表（设计 tokens / 布局骨架 / 交互范式三族）；上轮三锚表保留为**气质参考**层——继续提供「克制 / 分组 / 密度」的心智约束与历史对照证据，但不再作为逐条定值来源。

- **上轮 D-005 标记 revised**：原记录保留于 `.scratch/_archive/ui-craft-2026-09-13/decision-ledger.md`，本轮账本 D-005 明示 revised 关系——三锚并未被证伪（其工程结论如等宽、Transitions、表单骨架仍大量沿用），而是参照系优先级被用户裁决替换。
- **已拍板决策的收编**：D-002（胶囊 hover）、D-003（主色实心胶囊）、D-004（圆角窗口）与 MangoDisk 语言一致，被本决策收编为其具体实例。

### D2: 复刻方式 =「看观感、自实现」，GPL-3.0 源码零拷贝

- 设计风格 / 观感 / 布局范式不受版权保护，可复刻；**MangoDisk 任何源码（TS / Vue / CSS / Rust）不得复制、移植、翻译进本仓库**（GPL-3.0 传染性）。实现全部 Avalonia XAML 自绘。
- 先例：Files.App Middle-Click 移植（CONTEXT.md 词条，MIT 许可下尚有授权；本例更严——连可读源码都不进仓，只取证观感定值）。
- 取证通道：`gh api` 只读其仓库文件取观感定值（token 值、布局尺寸、组件结构、sonner 配置），取证结果落规范附录 D；atomcode 深度调研补 Avalonia 生态映射与工业先例。

### D3: 「观感级对标」的语义边界

对标的对象是**视觉与交互形态**（token 值、构图、状态反馈、组件行为），**不是技术栈平移**：

- MangoDisk 的 Web 能力（Tailwind / color-mix / container queries / backdrop-filter / oklch）不得误当 Avalonia 免费能力；逐条按 Avalonia 机制映射——三层 ResourceDictionary（原始 token → ThemeDictionaries Light/Dark → `{DynamicResource}`）、`ControlTheme` + `Classes` 样式类、`Transitions` 白名单、`x:Double`/`CornerRadius` 资源（atomcode 调研 §2.1，Avalonia Discussion #16554 官方口径：无集中 token 表，四层色板结构为正解）。
- oklch 色值一律离线转 sRGB 后入 XAML（Avalonia #8450：无原生 oklch / 广色域支持）。
- 派生式（`calc()`）无 XAML 等价物，**预计算写死**（MangoDisk radius 阶梯 4/6/8/12 即 base-4/base-2/base/base+4 的展开值）。

### D4: 基准证据与既有决策的张力登记（T-3）

调研取证发现 MangoDisk 侧栏 nav-item 的 active 实物 = `sidebar-accent` 实心胶囊 + **3px×24px 圆头主色左缘条** + 字重 600，hover 前景不变色——与 D-003「主色实心胶囊 + 反白」及上轮 D-004「hover 向 primary 叙事」存在方向性偏差。

- **本 ADR 不静默改向**：D-003 / 上轮 D-004 为用户拍板的现行决策，效力保留至用户 / 大脑按 T-3 裁定（票 03 施工前）。
- 登记处：ui-visual-standard.md §2.4 基准对照表 + 附录 A.2 张力表 T-3 行；上轮 T-1（hover 色彩叙事）并入 T-3 一并呈报。

## Consequences

- **正**：审美参照系从「抽象三锚拼合」换成「同赛道成品实物」，每票 before/after 对照有了可比的观感基准；MangoDisk 的 token / 布局 / 范式实测值（附录 D）为票 02-08 提供逐条可对标的定值。
- **负**：MangoDisk 为 Web 技术栈，所有定值需经 Avalonia 机制映射，存在「看起来能画、实际要自绘」的成本；GPL-3.0 边界要求取证纪律长期维持（只读、零拷贝）。
- **未决**：T-3（nav active 实色胶囊 vs 基准 accent 胶囊+细条）须裁定；MangoDisk 亮色皮在 Windows 桌面工具语境的适配度待实机目检。

## 约束

- 不换主题基座（Semi.Avalonia + Ursa 不动）；不引新 NuGet 依赖除非票内调研 + 用户确认。
- 不触碰 Core / Worker 源（锐评六条属独立处置轮，D-007）。
- 本机零构建 / 零测试（CI-only）；验收交云端 .github/workflows/ci.yml + 用户目检。
- 版本控制遵循 WORKFLOW §4.2（GitButler 虚拟分支、一票一分支、不 push 不 PR）。

## 调研引用

- **atomcode 5.0.9 深度调研（2026-09-14）**：10 次检索（Exa 2 + Tavily 2 + AnySearch 6）/ 13 次原文核验。关键来源：Avalonia 官方 Resources / Control Themes 文档、Avalonia Discussion #16554（token 分层官方口径）、Semi.Avalonia Resource Customization 与 Theme and Class 文档、shadcn/ui + shadcn-vue Theming 与 Sidebar 文档（双源逐字一致确认 MangoDisk 实际栈）、Sonner 官网 + Emil Kowalski《Building a toast component》+ sonner issue #630、Avalonia 12 官方博客、Avalonia #8450（oklch 缺口佐证）、MangoDisk 仓库本体。
- **MangoDisk 取证**（gh api 只读）：`src/assets/themes/mangodisk.css` / `default.css` / `main.css`、`src/layouts/md-app-shell.vue` / `components/md-sidebar.vue` / `md-window-titlebar.vue`、`src/components/custom/md-empty-state.vue` / `md-confirm-dialog.vue` / `md-page-shell.vue`、`src/App.vue`（Toaster 配置）、`components.json`（shadcn-vue new-york 风格确认）。逐文件定值见规范附录 D。
- 既有 ADR：0065（活文档分工与 T-1/T-2 登记先例）、0048 / 0050 / 0051 / 0052 / 0054 / 0055 / 0056 / 0062（token 与变体级基线）。
