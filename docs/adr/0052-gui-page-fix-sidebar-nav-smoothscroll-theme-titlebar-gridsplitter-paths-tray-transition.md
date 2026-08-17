# ADR 0052: GUI 页面修复 — 侧栏导航统一 + 中键指数平滑 + 主题色板网格 + 标题栏去重 + 侧栏拖拽调宽 + 目录默认路径 + 托盘菜单保持原生

**Date**: 2026-08-17
**Status**: Accepted
**Branch**: main (并行修复分支)
**Decisions**: 7 个决策点（A1-A7）通过 grill 流程确认

---

## 背景

ADR 0050/0051 完成了 surface depth / typography / 按钮过渡 / RAF 中键滚动的深度精雕。本轮聚焦用户实测反馈的 7 个具体 GUI 页面问题：侧栏按钮大小不统一、中键滚动不平滑、主题不可选且土、标题栏重复、侧栏宽度卡死、配置目录空、托盘按钮 hover 闪烁。

## 调研来源

- A1 侧栏导航: atomcode 调研（HTTP 403 concurrency 冲突中断），pplx_kimi_k26 fallback 成功 — Material Design 3 导航规范 + Fluent 2 + Apple HIG + v2rayN/Semi.Avalonia 源码
- A2 中键平滑: atomcode 深度调研成功 — Files.app 源码 + LibreScroll + SmoothScroll.Avalonia + Firefox/Chromium + Lembkea《Improved Lerp Smoothing》全文 + 17 信源
- A3 主题色彩: atomcode 深度调研成功 — MD3 色角色 + Fluent 2 token + Apple HIG + Semi.Avalonia 源码 + 18 信源
- A4 标题栏去重: atomcode 深度调研成功 — VS Code/Slack/Discord/Teams/Files.app 源码 + Ursa PR #917 + WindowsAppSDK + 15 信源
- A5 GridSplitter: atomcode 深度调研成功 — Avalonia GridSplitter 官方文档+源码 + Semi ControlTheme + v2rayN 实证 + 18 信源
- A6 目录默认路径: 代码实证 — DefaultPaths.cs 已有三端兼容路径，缺 BackupDirectory + Watermark
- A7 托盘过渡: atomcode 调研（403 中断但源码验证完成） — NativeMenuItem.cs 源码 + TrayIconImpl.cs + PR #21426 状态 + discussion #8301

## 决策

### A1: 侧栏导航按钮统一化 — 44px 高 + icon(20px)+text + 4px 左侧 accent bar

- 三按钮（Config/Logs/ServiceManager）统一 Height=44，Padding="16,0,0,0"（icon+text 左对齐）
- 每按钮加 Material.Icons 20px 图标：Config→`material:Settings`，Logs→`material:FileDocumentOutline`，ServiceManager→`material:ServerNetwork`
- icon 与 text 间距 12px（SpaceMd token），文字用 .body class
- active 态：4px 左侧 accent bar（`SemiColorPrimary`）+ 背景半透明 primary
- hover：背景 `SemiColorFill1`，120ms SineEaseOut 过渡（复用 ADR 0051 全局 Button Transitions）
- 当前问题：`Button.nav` Padding="12,8" vs 其他 "12,6"，纯文字无图标，无 active indicator
- 标准来源：Material 3 Navigation drawer item height=44、Fluent 2 NavViewItem、Apple HIG sidebar。v2rayN/Semi.Avalonia 侧栏均 44px + icon+text + active bar

### A2: 中键滚动指数平滑 — v += (targetV - v) * (1 - exp(-k*dt))，消除死区阶跃+松键急停

- 当前 RAF + 常量（12/0.12/32）已正确，但"一闪而过"根因：常量阶跃（死区外瞬间 0→v）+ 松键急停（无减速）
- 速度模型换指数逼近：`v += (targetV - v) * (1 - exp(-15*dt))`，k≈15 s⁻¹（半衰期 ~46ms）
- dt 钳制 ≤100ms（窗口暂停/大延迟保护），`|v| < 1` 停机
- 死区加迟滞（进入死区后阈值缩半）防边界抖动
- 保留 RAF + frameScale（ADR 0051 A3 不变）
- 参考：Lembcke《Improved Lerp Smoothing》帧率无关数学 + LibreScroll 摩擦衰减 + SmoothScroll.Avalonia 停机阈值 2.0
- 约 40 行改动（MiddleClickScrollBehavior.cs 速度更新段 + 死区逻辑）

### A3: 主题色板网格 — swatch grid + 明暗 ComboBox + theme_id 持久化 + MD3 角色补强

- 当前 5 主题文件存在（Catppuccin/Dracula/NordDark/OneDarkPro/TokyoNight）但无 UI 入口，仅 system/light/dark ComboBox
- 设置页加色板网格（RadioButton + WrapPanel，每个 swatch 显示该主题 primary 色 + name）
- 保留 system/light/dark ComboBox 作明暗切换（独立于主题预设）
- `UiOptions` 加 `string ThemeId = "catppuccin"` 字段，持久化到 `config.json ui.theme_id`
- 加载时恢复：`theme_id` 选预设主题文件，`theme_variant` 选 system/light/dark
- MD3 角色补强：5 主题文件各加 `SemiColorSurfaceDim`/`Bright`+`ContainerLow`/`High`+`OnColor` 语义角色
- 深色 #121212 基调，Primary 去饱和到 70-80%（隐私工具低饱和基调，Apple/Fluent 2 共识）
- 约 80 行 XAML（swatch grid） + 15 行 C#（ThemeId 字段+加载/保存） + 5 主题文件角色补强

### A4: 标题栏去重 — 删除标题文本+模式标签，只留窗口按钮，模式写入 Window.Title

- 当前标题栏 Left 侧 `<TextBlock Text="{Binding AppTitle}"/> + <TextBlock Text="{Binding ModeLabel}"/>` 与侧边栏头部重复
- 删除标题栏内 AppTitle + ModeLabel TextBlock，左侧留空作拖拽区（ElementRole="TitleBar" 已设，Os 自动处理拖拽）
- ModeLabel 写入 `Window.Title` 属性绑定（OS 任务栏/Alt-Tab 可见，UI 不重复）
- 可选：左缘放 16px 应用图标（非必须）
- 参考：Discord/VS Code/Slack 标题栏只放窗口按钮，模式/状态在侧边栏
- 约 10 行 XAML 删除 + 2 行 Window.Title 绑定

### A5: 侧栏宽度可拖拽 — GridSplitter 200/4/* + Min 170 / Max 400 + 防抖持久化

- ColumnDefinitions 从 `200,*` 改为 `200,4,*`（中间 4px 列放 GridSplitter）
- `<GridSplitter Grid.Column="1" ResizeDirection="Columns" Background="Transparent" />`
- Column[0] 加 `MinWidth=170 MaxWidth=400`（GridSplitter 自动遵守约束）
- GridSplitter hover 高亮（背景 SemiColorBorder 0.3 opacity 120ms）
- DragCompleted → 读取 `col[0].ActualWidth` → Math.Clamp(170,400) → 防抖 500ms 写 `ui.sidebar_width`（复用 ADR 0037 防抖链路）
- `UiOptions` 加 `double SidebarWidth = 200` 字段
- 加载时恢复：`Math.Clamp(cfg.Ui.SidebarWidth, 170, 400)` 赋 ColumnDefinitions[0].Width
- 参考：Avalonia GridSplitter 官方文档 + v2rayN 实证 + Semi.Avalonia ControlTheme
- 约 15 行 XAML + 20 行 C#（事件+字段+加载+保存）

### A6: 配置目录默认路径 — DefaultBackupDirectory + Watermark 提示

- DefaultPaths.cs 加 `DefaultBackupDirectory` → `<hotFolder>/.pp_backup`（空则 string.Empty）
- AppConfig.Default 引用 DefaultPaths 作默认值（BackupDirectory 不再 string.Empty）
- config.sample.json 填默认值（HotFolder/QuarantineDirectory/AuditLogDirectory/BackupDirectory 全部用 DefaultPaths 引用）
- 配置页 TextBox 加 `Watermark` 绑定 XxxPathHint（显示当前默认路径，空时灰底提示）
- Material.Icons 3.0 已有，Watermark 是 Avalonia TextBox 原生属性（零新依赖）
- 约 15 行 C#（DefaultPaths + AppConfig.Default） + 30 行 XAML（Watermark 绑定） + config.sample.json 更新

### A7: 托盘菜单保持原生菜单 — 不加过渡动画，消除 hover 闪烁

- atomcode 源码验证：`NativeMenuItem` 是纯数据类（`INativeMenuItemExporterEvents`），无视觉模板/无样式键
- 全局 `Button:pressed scale(0.97)` 过渡只作用于 Avalonia 视觉树内 Button，托盘菜单不在视觉树内
- macOS/Linux = 平台原生菜单（NSMenu/DBus），完全不可样式化
- Windows = TrayPopupRoot + MenuFlyoutPresenter（管理型 popup），PR #21426 试图换原生 Win32 菜单但未合并
- 决策：保持 NativeMenu 不变，承认 OS 原生菜单的平台限制
- 用户反馈"hover 闪烁难案"来自全局 Button :pointerover BrushTransition 0.15s 在视图内按钮上的渲染闪烁，而非托盘菜单
- 需排查并修复：AppTheme.axaml `Button:pressed` 伪类松开瞬间的画笔硬切 → 已在 ADR 0051 A2 处理
- 若仍有闪烁，检查 `Button:pointerover` 是否缺少 `:pressed` 同款 TransformOperationsTransition
- 零代码改动（托盘保持原生菜单）

## 约束

- 零新依赖（全部基于现有 Semi.Avalonia + Material.Icons + Avalonia 原生 API）
- 跨平台兼容（Watermark/GridSplitter/RequestAnimationFrame 三端一等支持）
- 每阶段 commit 后才能进下一阶段（回溯需要）
- 最终编译打包测活，验证进程存活
- source-lint test guard 固化闭环
