# ADR 0048: UI 美观全面升级 — Semi.Avalonia + Ursa + 设计 token 系统

**Date**: 2026-08-15
**Status**: Accepted
**Branch**: main (并行修复分支)
**Decisions**: 12 个决策点（A1-A12）通过 grill 流程确认

---

## 背景

项目 UI 当前是 Avalonia 11.1.3 + FluentTheme + 自写 210 行 AppTheme.axaml。行业调研（kimi_k26 + exa 两轮）识别出多个"一眼看上去 AI 生成"的根因：

- 只有 Light/Dark 双层 surface（AppBg/SidebarBg/CardBg），无 surface0/1/2/overlay 深度
- spacing 是 ad-hoc 散值（0/2/4/5/6/8/10 共 7 种），无 4px/8px ramp
- 圆角混用 4/6/7/10，无统一 ladder
- typography 只 3 个角色（11/12/13），无 display/headline/title/body/mono 6+ 层级
- 无微交互/transition
- 无图标集
- 字体靠系统安装 fallback，跨平台不一致
- 只有 light/dark，无 Nord/Catppuccin/Dracula 等社区流行主题

## 决策

### A1: UI 库基座 — Ursa.Avalonia + Semi.Avalonia
Semi 提供完整 ControlTheme（替换 FluentTheme），Ursa 提供 50+ 企业控件。MIT 许可，跨平台 Win/Linux/macOS。.NET Foundation 项目。社区参考项目多。

### A2: Avalonia 版本升级路径 — 升级到 11.3.7+ 稳定线
Semi.Avalonia 11.1.x EOL，必须 11.3.7+。全 solution 包版本统一升级。ThemeDictionaries 语义不变，CompiledBindings 默认开启不变，Style selector 语法不变。全量 test 闭环验证。

> 实施落地版本：Avalonia 12.1.1 + Semi.Avalonia 12.1.0.1 + Ursa 2.2.0 + Material.Icons.Avalonia 3.0.2 + Fonts.Avalonia.CascadiaCode 0.14.0。grill 确认 11.3.7 时为 11.x 线最新稳定版；实施期 Avalonia 12.1.x 已稳定发布，沿用同一升级路径原则落地到 12.1.1。

### A3: 自定义 style 迁移策略 — 全面改用 Semi 命名
20 个自定义 Color+Brush token（AppBgBrush/SidebarBgBrush 等）迁移到 Semi 的 SemiColor* 命名体系。AppTheme.axaml 只保留 Semi 没有的领域 class（settings-card/row-divider/nav 等）。MainWindow.axaml 所有 DynamicResource 引用更新。

### A4: 多主题实现策略 — custom ThemeVariant + per-variant ResourceDictionary
每个社区主题（NordDark/CatppuccinMocha/Dracula/TokyoNight/OneDarkPro）注册为 custom ThemeVariant（ThemeVariant.Inherit fallback 到 Dark），配合独立 .axaml ResourceDictionary 覆盖 SemiColor* token。运行时 RequestedThemeVariant = customVariant 切换，DynamicResource 自动传播。

### A5: 字体策略 — 全嵌入式打包 + fonts: scheme + typography token 系统
正文用 fonts:Inter#Inter 全路径（Avalonia.Fonts.Inter NuGet），等宽用 fonts:CascadiaCode#Cascadia Code（新增 Avalonia.Fonts.CascadiaCode NuGet），CJK 回退加 NotoSans。定义 6+ typography roles 为 TextBlock style class（.display/.headline/.title/.body/.caption/.mono），每个 role 绑 FontSize + FontWeight + LetterSpacing token。

### A6: 图标集 — Material.Icons.Avalonia
Semi.Avalonia 官方推荐 Material Design Icons（Pictogrammers）。6000+ 图标，MIT/ODC 许可，覆盖 dashboard/folder/settings/play-pause/warning/shield 等本项目所需全部图标。与 Semi 设计语言天然对齐。

### A7: 设计 token 系统 — 独立 DesignTokens.axaml + 全局 DynamicResource
新建 Styling/DesignTokens.axaml 定义 Space.XS=4/Space.S=8/Space.M=12/Space.L=16/Space.XL=24 和 Radius.Small=6/Radius.Medium=10/Radius.Large=14 作为 double/CornerRadius resource。Motion 定义 Transitions 全局 style。MainWindow.axaml 散值迁移到 {DynamicResource ...}。

### A8: 微交互/动画 — 克制全局 transition（Button + ListBoxItem + Border）
Button: BrushTransition Background+BorderBrush 150ms CubicEaseOut + TransformOperationsTransition scale(0.98) 100ms pressed。ListBoxItem: BrushTransition Background 150ms CubicEaseOut。Border: BrushTransition Background 200ms。不加 page switch 动画。配合 Motion.Duration token 集中管理。

### A9: i18n 与 Semi Locale 协作 — 双系统并行
保留自建 LocalizationService + {ex:Localize key} + 10 语言 JSON + CultureChanged 全套不动。SemiTheme Locale 属性绑定当前 culture，只负责 Semi 内置控件文案中文化。两者不冲突。此前 i18n 分支 16 个 LocalizationServiceTests 全部保留不回归。

### A10: surface 深度层级 — 迁移到 Semi surface token 体系 + 补 overlay 层
AppBgBrush → SemiColorBackground（底层）。SidebarBgBrush → SemiColorBackground1（一级提升）。CardBgBrush → SemiColorSurface（二级提升）。新增 SemiColorOverlay（三级，dialog/popover）。补 sidebar BoxShadow elevation 用 Semi shadow token。配合自定义 variant token 覆盖同一套 key，切换主题时 depth 语义不变。

### A11: test 闭环 — source-lint test 扩展
沿用已验证 pattern（grep AXAML 文本断言模式存在/缺失），新建 DesignSystemTests.cs 验证：MainWindow.axaml 不含硬编码散值；App.axaml 含 SemiTheme 不含 FluentTheme；DesignTokens.axaml 定义全部 token；社区主题 variant 文件存在；fonts: 全路径引用存在；Button/ListBoxItem/Border 有 Transitions。不引 Avalonia.Headless（避免 ServiceManager/IPC 集成测试超时风险）。

### A12: Git 分支策略 — main 分阶段提交
阶段 1: Avalonia 版本升级 + NuGet 引入 + csproj。阶段 2: DesignTokens.axaml + 字体 fonts: scheme。阶段 3: AppTheme.axaml 迁移到 Semi token + surface depth。阶段 4: 5 个社区主题 variant ResourceDictionary。阶段 5: MainWindow.axaml 迁移 + transition + 图标。阶段 6: source-lint test 闭环。每阶段 dotnet build + 一个 commit，全部完成最后 git push。遵循 AGENTS.md git 规范（* text=auto eol=lf，LF 规范）。

## 约束

- 遵循 AGENTS.md 安全规范：ExifTool 桥接安全、IPC 规范、文件系统规则不变
- 不破坏此前 i18n 分支的 16 个 LocalizationServiceTests
- 不破坏此前 IPC/性能分支的 119 个 core tests
- PowerShell 5.1 兼容
- * text=auto eol=lf，LF 规范
- 跨平台 Win/Linux/macOS 兼容（Semi.Avalonia 12.1.x 验证）

## 调研引用

- Semi.Avalonia: https://github.com/irihitech/Semi.Avalonia (12.1.x, MIT)
- Ursa.Avalonia: https://github.com/irihitech/Ursa.Avalonia (.NET Foundation, MIT, 50+ controls)
- Material.Icons.Avalonia: https://github.com/AvaloniaCommunity/Material.Icons.Avalonia
- Avalonia Theme Variants: https://docs.avaloniaui.net/docs/styling/theme-variants
- Avalonia Custom Fonts: https://docs.avaloniaui.net/docs/styling/custom-fonts
- Avalonia Control Transitions: https://docs.avaloniaui.net/docs/graphics-animation/control-transitions
- Fluent 2 Layout (4px grid): https://fluent2.microsoft.design/layout
- Material surfaces/depth: https://m2.material.io/design/environment/surfaces.html
- Catppuccin palette: https://github.com/catppuccin/catppuccin
- Nord palette: https://github.com/nordtheme/nord
- Semi.Avalonia resource customization: https://docs.irihi.tech/semi/en/docs/advanced/resource-customization/
- Semi.Avalonia localization: https://docs.irihi.tech/semi/en/docs/advanced/localization/
