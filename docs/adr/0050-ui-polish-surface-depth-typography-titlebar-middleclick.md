# ADR 0050: UI 精雕 — surface 深度补 overlay/shadow + typography 6 角色 + button 统一 + emoji 清零 + 中键快翻 + 自绘标题栏

**Date**: 2026-08-16
**Status**: Accepted
**Branch**: main (并行修复分支)
**Decisions**: 4 个决策点（A1-A4）通过 grill 流程确认

---

## 背景

ADR 0048 已落地 Semi.Avalonia 12.1 + Ursa 2.2 + Material.Icons 3.0 + Inter 字体 + 5 社区主题 + DesignTokens.axaml。但实测持续感受到"比之前更像草皮包子、质量低下特别草率"，根因不在设计系统缺位而在编排缺失：

- AppTheme.axaml 的 button variant（primary/ghost/nav）**未设 `Padding` setter**（用 Semi 默认 `Infinity,5,Infinity,6`），Browse 按钮 inline `Padding=4,0` 散值导致按钮高度参差
- 无 surface depth 第 4 层（overlay）+ 无 shadow/elevation token，sidebar/card 视觉贴合无投影 → "扁平贴纸"感
- 无 typography 6 角色系统（display/headline/title/body/caption/mono），MainWindow.axaml inline `FontSize=16 FontWeight=SemiBold` 散值（约 15 处）
- locale JSON 用 Unicode 符号 `✓ ✗ ⚠`（zh-CN/en 各 8 行，U+2713/2717/26A0），与 Material.Icons 信号语义混杂 → AI 感来源
- `MainWindow.axaml` 两个 ScrollViewer 无 `PointerPressed` handler → 中键 pan 失效（Avalonia 无内置中键 autoscroll，Files.App 已开源 attached behavior）
- `MainWindow.axaml` `Window` 元素完全无 `ExtendClientAreaToDecorationsHint` → 用系统原生标题栏，与 Semi.Avalonia 设计语言完全割裂

行业调研：三轮 `pplx_kimi_k26` + 两轮 `exa web_search`，覆盖 Semi.Avalonia 12.1 官方 + FluentAvalonia v2 + PleasantUI + Avalonia 12.1 WindowDrawnDecorations API (PR #20770) + Files.App 的 ScrollViewerMiddleClickExtensions.cs (exa 完整取回 236 行源码)。

## 决策

### A1: surface 深度 + typography 6 角色（根除"草皮包子"三根因）

补 `SemiColorOverlay` 第 4 层 surface（dialog/popover），引入 Semi 的 `SemiElevation1/2/3` shadow token 给 sidebar/settings-card/popover 的 `BoxShadow`。新建 6 个 typography style class（`.display/.headline/.title/.body/.caption/.mono`）绑 FontSize + FontWeight + LetterSpacing，MainWindow.axaml 的 `FontSize=16 FontWeight=SemiBold` 等 inline 散值迁移到这些 class。

**约束**：不引新 NuGet 依赖；Semi.Avalonia 12.1 自带的 elevation token 即用；Diff 限定 AppTheme.axaml + DesignTokens.axaml + MainWindow.axaml。

### A2: 按钮/间距/阴影统一 + emoji 清零（一次根除三子症）

3 个子项捆绑执行：

1. **Button padding 统一**：`AppTheme.axaml` 的 `Button.primary/.ghost/.nav/.danger` 各补 `Padding` setter，删除 MainWindow.axaml 所有 inline `Padding=4,0` `Height=28` `MinWidth=28` 散值，靠 style class 驱动。`+ - …` 按钮 Content 换 Material.Icons（`Plus/Minus/FolderOpen/DotsHorizontal`）。
2. **Locale 符号清零**：10 语言 JSON 中 `auto_saved`/`save_failed_exception`/`applied`/`config_apply_failed` 等 8 处 `✓ ✗ ⚠` Unicode 符号清零，UI 侧用 Material.Icons 或纯文本呈现状态，避免 application text 混杂 Unicode 符号。
3. **Margin token 迁移**：MainWindow.axaml 主要 Margin 散值迁移到 `{DynamicResource SpaceLg}` `{DynamicResource SpaceMd}` 等 token 绑定，遵循"所有 Margin/Padding 走 spacing token"原则。

**约束**：不引新依赖；不破坏 i18n 测试；不破坏 DesignSystemTests 11 断言。

### A3: 中键快翻 — 移植 Files.App 的 MiddleClickScrollBehavior attached behavior

新建 `src/PhotoPrivacy.Ui/Behaviors/MiddleClickScrollBehavior.cs` attached behavior，移植 Files.App 的 MIT 许可 `ScrollViewerMiddleClickExtensions.cs` 核心逻辑：

- `PointerPressed` 中键按下 → 锚定起点 → 启动 DispatcherTimer
- Timer tick：按 `_currentPosition - _anchorPosition` 计算 velocity → `ScrollViewer.Offset` 更新
- DeadZone (~10px) 防误触；SpeedFactor + MaxSpeedPerTick 上限；Escape/中键再次按下/任意其他键 退出
- 4 方向 cursor 切换：`SizeAll` / `SizeNorthSouth` / `SizeWestEast`
- 所有 ScrollViewer（config 页 + service 页 + Log 页 ListBox parent）挂 `behaviors:MiddleClickScrollBehavior.IsEnabled="True"`

**约束**：纯 Avalonia API（DispatcherTimer + ScrollViewer.Offset），无 raw Win32 hook；跨平台一致行为；source-lint test 断言 behavior class 存在 + ScrollViewer 有 IsEnabled="True" attached。

### A4: 自绘现代化标题栏 — ExtendClientAreaToDecorationsHint + ElementRole caption button

`MainWindow.axaml` 的 `<Window>` 加 `ExtendClientAreaToDecorationsHint="True"` + `WindowDecorations="None"`。自绘标题栏：

- `Grid RowDefinitions="40,*"` 顶行 = TitleBar Border 高 40，`WindowDecorationProperties.ElementRole="TitleBar"` 标记整段为 drag 区域 → 系统自动处理拖拽和双击最大化
- 标题栏左：`TextBlock Text="PhotoPrivacy"` + 当前模式 label（复用现有 ModeLabel binding）
- 右：3 个 Button 用 `ElementRole="MinimizeButton/MaximizeButton/CloseButton"` → 系统自动处理 click，**不需要手写 click handler**
- 3 个 caption button Content 用 `<materialIcons:MaterialIcon Kind="WindowMinimize/WindowMaximize/WindowClose" Width=14 Height=14 />`
- 全部走 theme token（`SemiColorBackground` / `SemiColorText2` / `SemiColorBorder`），light/dark/community variant 自适配
- `:maximized` 伪类加 padding 0；`:fullscreen` 隐藏 caption button
- 配合 Q1 的 `SemiElevation1` shadow 让标题栏投影 1px hairline border 与 sidebar depth ladder 一致

**跨平台兼容**：Win/macOS 完整支持；Linux "Limited support" 时 Avalonia 内部降级到原生标题栏不报错（Avalonia 12.1 WindowDrawnDecorations PR #20770 明确支持此降级）。

**约束**：不引 PleasantUI 新依赖（符合 Ponytail "不引新依赖，除非用户明确要求"原则）；用 Avalonia 12.1 官方 WindowDrawnDecorations API。

## 约束

- 遵循 AGENTS.md 安全规范：ExifTool 桥接安全、IPC 规范、文件系统规则不变
- 不破坏此前 i18n 分支的 16+ 个 LocalizationServiceTests
- 不破坏此前 IPC/性能分支的 119+ 个 core tests
- 不破坏此前 DesignSystemTests 11 个已有断言
- PowerShell 5.1 兼容
- `* text=auto eol=lf`，LF 规范
- 跨平台 Win/Linux/macOS 兼容（Semi.Avalonia 12.1.x 验证；ExtendClientArea Linux 降级到原生不报错）
- 不引新 NuGet 依赖（PleasantUI 被排除，用 Avalonia 12.1 官方 API 替代）

## 调研引用

- Semi.Avalonia 12.1 shadow/elevation 官方: https://github.com/irihitech/Semi.Avalonia
- Avalonia 12.1 WindowDrawnDecorations PR #20770: https://github.com/AvaloniaUI/Avalonia/pull/20770
- Avalonia 官方文档 ExtendClientAreaToDecorationsHint: https://docs.avaloniaui.net/docs/platform-specific-guides/windows
- Avalonia 官方文档 WindowDecorations.ElementRole: https://docs.avaloniaui.net/docs/how-to/window-how-to
- FluentAvalonia v2 ButtonPadding/ControlCornerRadius token: https://amwx.github.io/FluentAvaloniaDocs/pages/Resources
- Avalonia TextOptions (SubpixelAntialias/BaselinePixelAlignment): https://docs.avaloniaui.net/docs/graphics-animation/text-options
- Avalonia Styling best practices (style class over inline): https://docs.avaloniaui.net/docs/styling/style-best-practices
- Files.App MIT ScrollViewerMiddleClickExtensions.cs: https://github.com/files-community/Files/blob/main/src/Files.App/Extensions/ScrollViewerMiddleClickExtensions.cs
- Material.Icons.Avalonia: https://github.com/AvaloniaUtils/Material.Icons.Avalonia
- PleasantUI (备选被排除): https://github.com/onebeld/pleasantui
- pplx kimi_k26 调研：Avoiding AI-generated UI look / Button consistency / Spacing 8pt grid / Window chrome cross-platform
- exa web_search：Avalonia custom titlebar template / TextOptions / Files.app middle click autoscroll 完整源码取回
