# CONTEXT.md 词条（ui-craft2 grill 定稿）— 待提交工作区副本（防丢失保险）

> **状态说明**：本词条组已在工作区 CONTEXT.md 落盘（uncommitted），因 GitButler 引擎悬空依赖
> （`line 411 depends on commit koy`——koy 已随上轮 land 进 main 6bf244e，但引擎账目仍记忆该引用）
> 无法即时 `but commit`。**T0 票开工第一件事：把工作区词条作为票面首件提交。**
> 本文件是逐字保险副本：即使工作区被 pull/discard 清掉，从此文件恢复即可（插入位置 = Visual Baseline 词条之后）。
> 决策依据：decision-ledger.md D-002/003/004/005（本目录已提交副本）。

---

**Visual Baseline**（修订）:
运行侧视觉验证的基线资产：docs/design/screenshots/ 下 4 页（配置/日志/规则/服务管理）× 关键态（默认 / hover / active / 空态）的同机位截图，配合 report-32 探活清单（docs/process/reports/32-ui-manual-smoke.md §2）共同构成 UI 票的验收对照物。每张 UI 票交付 = 同机位 before/after 人工对照 + 对照审美三锚（Wasabi 气场 / Apple 系统设置骨架 / VS Code·Discord 密度法；ui-craft2 轮起以 MangoDisk 观感级对标为主基准，三锚降级为气质参考——见词条 MangoDisk Benchmark 与 Nav Capsule Language，T0 票统一修订本段）。拍摄规程见 ui-visual-standard.md §7；不上 CI 像素 diff 门禁（D-007 负向，降 backlog）。
_Avoid_: 无基线资产的一次性人工探活；CI 视觉回归像素门禁（审查负担 + 环境漂移）

**Nav Capsule Language（胶囊导航语言）**（新增）:
侧栏导航按钮（Button.nav / Button.nav-action）的视觉语言：idle 透明 → hover 半透明中性圆角胶囊底色块 → active 主色实心胶囊 + 反白前景，三态层次分明（ui-craft2 D-002/D-003）。上轮的 4px 左缘 accent 指示条槽位体系（BorderThickness 4,0,0,0 + Transparent 占位）随之退役；BrushTransition 三路过渡机制（Background/BorderBrush/Foreground ~150ms）保留复用。反白前景对主色底的对比度须达 WCAG AA（≥4.5:1），五主题色板逐一验证（nord/dracula 上轮观察项须解决或显式豁免）。落地载体 docs/design/ui-visual-standard.md（T0 票升级）。
_Avoid_: hover 无实体底色块的"纯变色"反馈（用户判塑料感的直接原因）；active 与 hover 同为浅色导致看不出选中

**Rounded Window (DWM Corner Preference)**（新增）:
Windows 侧窗口圆角的唯一实现路径：MainWindow.axaml 显式设 Win32Properties.WindowCornerPreference="Round"（Avalonia 12.1.1 原生附加属性，底层 DwmSetWindowAttribute(33)）；Win10（<Build 22000）官方语义 ignored = 安全 no-op 直角，无需版本分支；最大化/贴边/VM 由系统 by design 自动切直角，零代码。必须显式设 Round（默认值 Default 曾致 Avalonia #9660 无边框窗硬边回归）。透明窗口自绘圆角路线被 MS 官方三分类判死（per-pixel alpha 自绘阴影窗 "cannot ever be rounded"）且 macOS/Linux 透明支持矩阵先天不统一——永久排除（ui-craft2 D-004，atomcode 调研裁决存 docs/process/ui-craft2/atomcode-q4-rounded-window-verdict.md）。验收=实机目检矩阵，禁止"读回 preference 断言"。
_Avoid_: 透明窗口+内容层模拟圆角（B/C 路线）；依赖 Default 默认值；读回 DwmGetWindowAttribute 做程序化断言

**MangoDisk Benchmark（复刻基准）**（新增）:
UI 观感的复刻参照物：harry0703/MangoDisk（Tauri 2 + Vue + shadcn 风格，GPL-3.0，2026-08 创建即 2500+★，磁盘清理+隐私赛道）。ui-craft2 轮起全部页面观感级对标：设计 tokens（圆角/间距/色彩/阴影/字号）+ 布局骨架（侧栏+卡片内容区）+ 交互范式（toast/确认流/空态）。复刻方式="看观感、自实现"（同 Files.App Middle-Click 移植先例）——设计风格可复刻，GPL 源码禁止拷贝/移植/翻译进本仓库，实现全部 Avalonia XAML 自绘（D-005）。施工触及的页面必须整页达新基准，禁止"半新半旧"混排。
_Avoid_: 拷贝 GPL-3.0 代码（传染风险）；半新半旧的页面混排；把参照物技术栈（Web CSS）能力误当成 Avalonia 免费能力
