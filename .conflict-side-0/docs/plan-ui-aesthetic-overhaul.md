# UI 美观全面升级 — 实施计划表

> 基于 ADR 0048: docs/adr/0048-ui-aesthetic-overhaul.md
> 12 个 grill 决策点全部确认。6 阶段，每步含验收标准和 test 闭环。

## NuGet 包清单

| 包 | 版本 | 用途 |
|----|------|------|
| Avalonia | 12.1.1 | UI 框架 |
| Avalonia.Desktop | 12.1.1 | 桌面平台 |
| Avalonia.Themes.Simple | 12.1.1 | 基础主题 |
| Semi.Avalonia | 12.1.0.1 | Semi Design 主题 |
| Irihi.Ursa | 2.2.0 | Ursa 控件库 |
| Irihi.Ursa.Themes.Semi | 2.2.0 | Ursa Semi 主题 |
| Material.Icons.Avalonia | 3.0.2 | Material 图标 |
| Avalonia.Fonts.Inter | 12.1.1 | Inter 字体 |
| Fonts.Avalonia.CascadiaCode | 0.14.0 | Cascadia Code 字体 |

## 阶段 1: Avalonia 版本升级 + NuGet 包引入

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 1.1 | csproj: Avalonia 11.1.3 -> 12.1.1 全部包升级 | dotnet build 0 error |
| 1.2 | 新增 Semi.Avalonia 12.1.0.1 | NuGet restore 成功 |
| 1.3 | 新增 Irihi.Ursa + Irihi.Ursa.Themes.Semi | NuGet restore 成功 |
| 1.4 | 新增 Material.Icons.Avalonia | NuGet restore 成功 |
| 1.5 | 新增 Avalonia.Fonts.CascadiaCode | NuGet restore 成功 |
| 1.6 | App.axaml: FluentTheme -> SemiTheme + UrsaSemiTheme | 编译通过 |
| 1.7 | 保留 AppTheme.axaml StyleInclude（阶段 3 迁移） | 编译通过 |
| 1.8 | dotnet build 全解决方案 | 0 error 0 warning |
| 1.9 | dotnet vstest core 测试 | 119 全绿 |
| 1.10 | dotnet vstest i18n 测试 | 16 全绿 |

## 阶段 2: DesignTokens.axaml + fonts: scheme

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 2.1 | 创建 Styling/DesignTokens.axaml 独立文件 | 文件存在且 XML 合法 |
| 2.2 | 定义 4px spacing ramp (4/8/12/16/24/32/48) | token 定义正确 |
| 2.3 | 定义 radius ladder (2/4/8/12/16) | token 定义正确 |
| 2.4 | 定义 motion durations (75/150/250ms) | token 定义正确 |
| 2.5 | 字体嵌入式打包到 Assets/Fonts/ | 字体文件存在 |
| 2.6 | App.axaml 引入 fonts: scheme | FontFamily 引用正确 |
| 2.7 | dotnet build | 0 error |

## 阶段 3: AppTheme.axaml 迁移到 Semi token + surface depth

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 3.1 | 删除自定义 AppBgBrush 等 20 个 Color+Brush token | 无自定义 token 残留 |
| 3.2 | 迁移到 SemiColorBackground/1/Surface/Overlay 4 层 | surface depth 正确 |
| 3.3 | Button.primary 迁移到 Semi 色值 | 颜色一致 |
| 3.4 | Button.ghost 迁移到 Semi 色值 | 颜色一致 |
| 3.5 | Button.nav 迁移到 Semi 色值 | 颜色一致 |
| 3.6 | Border.settings-card 迁移到 Semi surface token | 视觉一致 |
| 3.7 | dotnet build | 0 error |
| 3.8 | source-lint: 无硬编码颜色值 | lint 通过 |

## 阶段 4: 5 个社区主题 variant ResourceDictionary

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 4.1 | 创建 Themes/NordDark.axaml | XML 合法 |
| 4.2 | 创建 Themes/Catppuccin.axaml | XML 合法 |
| 4.3 | 创建 Themes/Dracula.axaml | XML 合法 |
| 4.4 | 创建 Themes/TokyoNight.axaml | XML 合法 |
| 4.5 | 创建 Themes/OneDarkPro.axaml | XML 合法 |
| 4.6 | App.axaml 合并 ThemeVariant + ResourceDictionary | 编译通过 |
| 4.7 | ThemeVariant.Create 使用正确 | 5 variant 可切换 |

## 阶段 5: MainWindow.axaml 迁移 + transition + Material.Icons

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 5.1 | MainWindow.axaml 迁移到 Semi token | 编译通过 |
| 5.2 | 引入 Material.Icons 替换文本图标 | 图标显示 |
| 5.3 | transition: Button hover/pressed 150ms CubicEaseOut | 动画平滑 |
| 5.4 | transition: ListBoxItem hover/pressed 150ms CubicEaseOut | 动画平滑 |
| 5.5 | transition: Border hover 150ms CubicEaseOut | 动画平滑 |
| 5.6 | i18n: SemiLocale 与 LocalizationService 双系统并行 | 不冲突 |
| 5.7 | dotnet build | 0 error |

## 阶段 6: Test 闭环 + Git 提交

| 步骤 | 内容 | 验收标准 |
|------|------|----------|
| 6.1 | DesignSystemTests.cs: source-lint DesignTokens.axaml 存在 | test 通过 |
| 6.2 | source-lint: 无硬编码颜色值残留 | test 通过 |
| 6.3 | source-lint: fonts: scheme 引用正确 | test 通过 |
| 6.4 | source-lint: SemiTheme 正确引入 | test 通过 |
| 6.5 | source-lint: 5 个社区主题文件存在 | test 通过 |
| 6.6 | dotnet vstest 全部测试 | 135+ 全绿 |
| 6.7 | git commit 阶段 1-6 | 6 个 commit |
| 6.8 | git push origin main | push 成功 |
