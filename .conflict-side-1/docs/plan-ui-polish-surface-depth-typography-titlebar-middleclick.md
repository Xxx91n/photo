# UI 精雕全面修复 — 实施计划表

> 基于 ADR 0050: docs/adr/0050-ui-polish-surface-depth-typography-titlebar-middleclick.md
> 4 个 grill 决策点（A1-A4）全部确认。6 阶段，每步含验收标准和 test 闭环。

## 阶段 1: surface 深度补 overlay + shadow token + typography 6 角色（ADR A1）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 1.1 | DesignTokens.axaml: 新增 `SemiElevation1/2/3` shadow token 节点 + `SemiColorOverlay` 第 4 层 surface | 文件存在 + XML 合法 + 含新 token key | DesignSystemTests 扩展断言 |
| 1.2 | AppTheme.axaml: 新增 6 个 typography style class（.display/.headline/.title/.body/.caption/.mono）绑 FontSize + FontWeight | source-lint 含 6 个 class 名 | DesignSystemTests.Typography_6_Classes_Must_Exist |
| 1.3 | AppTheme.axaml: 给 sidebar/.settings-card 加 `BoxShadow="{DynamicResource SemiElevation"` 属性 | source-lint 含 BoxShadow + SemiElevation | DesignSystemTests.Card_Elevation_Should_Be_Present |
| 1.4 | MainWindow.axaml: inline `FontSize=16 FontWeight=SemiBold` 等 ~15 处散值迁移到 typography class | source-lint MainWindow.axaml 无 inline FontSize 行（仅 Header/Badge 子标签） | DesignSystemTests.MainWindow_No_Hardcoded_FontSize_In_Typography_Roles |
| 1.5 | dotnet build 全解决方案 | 0 error 0 warning | — |

## 阶段 2: 按钮/间距统一 + emoji 清零（ADR A2）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 2.1 | AppTheme.axaml: `Button.primary/.ghost/.nav/.danger` 各补 `Padding` setter（基础值 12,6 或 Semi baseline）| source-lint 4 个 button style 含 `Padding` | DesignSystemTests.Button_Variants_Should_Set_Padding |
| 2.2 | MainWindow.axaml: 删除所有 inline `Padding=4,0` `Height=28` `MinWidth=28` 散值（约 ~12 处）| source-lint MainWindow.axaml 无 `Padding=4,0` | DesignSystemTests.MainWindow_No_Inline_Button_Padding |
| 2.3 | MainWindow.axaml: `+ - …` 按钮 Content 换 `<materialIcons:MaterialIcon Kind="Plus/Minus/FolderOpen/DotsHorizontal"/>` | source-lint 含 MaterialIcon 内容 | DesignSystemTests.Browse_Buttons_Use_MaterialIcons |
| 2.4 | 10 语言 locale JSON: `auto_saved` / `save_failed_exception` / `applied` / `config_apply_failed` 等 8 处 `✓ ✗ ⚠` Unicode 符号清零 | 10 文件均不含 `✓` `✗` `⚠` 三字符 | LocalizationServiceTests.Unicode_Status_Symbols_Cleared |
| 2.5 | MainWindow.axaml: 主要 Margin 散值迁移到 `{DynamicResource SpaceMd/SpaceLg}` 等 token 绑定（约 ~15 处）| source-lint 主要页面 Margin 含 `{DynamicResource` | DesignSystemTests.MainWindow_Margins_Use_Space_Tokens |
| 2.6 | dotnet build 全解决方案 | 0 error 0 warning | — |
| 2.7 | dotnet vstest i18n + core test | 16+ i18n + 119+ core 全绿 | — |

## 阶段 3: 中键快翻 attached behavior — 移植 Files.App（ADR A3）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 3.1 | 新建 `src/PhotoPrivacy.Ui/Behaviors/MiddleClickScrollBehavior.cs` attached behavior | 文件存在 + 编译通过 + class sealed | — |
| 3.2 | Behavior 实现：PointerPressed 中键 → 锚定 + DispatcherTimer tick → ScrollViewer.Offset 更新 + cursor 切换 + Escape 退出 | source-lint 含 `DispatcherTimer` `IsMiddleButtonPressed` `ScrollableHeight` | — |
| 3.3 | MainWindow.axaml xmlns 加 `xmlns:behaviors="using:PhotoPrivacy.Ui.Behaviors"` | source-lint MainWindow.axaml 含 xmlns:behaviors | DesignSystemTests.Behaviors_Namespace_Bound |
| 3.4 | MainWindow.axaml config 页 + service 页 + Log 页 parent ScrollViewer 都加 `behaviors:MiddleClickScrollBehavior.IsEnabled="True"` | source-lint 至少 2 处 `IsEnabled="True"` attached | DesignSystemTests.MiddleClick_Behavior_Attached_To_ScrollViewer |
| 3.5 | dotnet build 全解决方案 | 0 error | — |
| 3.6 | source-lint test: behavior class 文件存在 + MainWindow.axaml 含 `behaviors:MiddleClickScrollBehavior` | test 绿 | — |

## 阶段 4: 自绘现代化标题栏（ADR A4）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 4.1 | MainWindow.axaml `<Window>` 加 `ExtendClientAreaToDecorationsHint="True"` + `WindowDecorations="None"` | source-lint 含这两属性 | DesignSystemTests.MainWindow_ExtendClientArea_Enabled |
| 4.2 | MainWindow.axaml 顶部 `Grid RowDefinitions="40,*"` 顶行 = TitleBar Border + `WindowDecorationProperties.ElementRole="TitleBar"` | source-lint 含 `ElementRole="TitleBar"` | — |
| 4.3 | MainWindow.axaml 右侧 3 caption button: `ElementRole="MinimizeButton/MaximizeButton/CloseButton"` + `<materialIcons:MaterialIcon Kind="Window*"/>` | source-lint 含三个 ElementRole 和三个 MaterialIcon | DesignSystemTests.Caption_Buttons_Use_ElementRole_And_MaterialIcons |
| 4.4 | 标题栏背景/`Text`/`Background` 全用 `{DynamicResource SemiColor*}`；`:maximized` BorderThickness=0 兜底 | source-lint 含 DynamicResource SemiColor | DesignSystemTests.TitleBar_Uses_Semi_Tokens |
| 4.5 | dotnet build 全解决方案 | 0 error | — |

## 阶段 5: Test 闭环扩展 + source-lint 断言

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 5.1 | DesignSystemTests 加 `Typography_6_Classes_Must_Exist`（断言 AppTheme.axaml 含 .display/.headline/.title/.body/.caption/.mono）| test 绿 | — |
| 5.2 | DesignSystemTests 加 `Card_Elevation_Should_Be_Present`（断言 AppTheme.axaml 含 `BoxShadow` 和 `SemiElevation`）| test 绿 | — |
| 5.3 | DesignSystemTests 加 `Button_Variants_Should_Set_Padding`（4 个 button style 均 Padding setter）| test 绿 | — |
| 5.4 | DesignSystemTests 加 `MainWindow_No_Inline_Button_Padding`（MainWindow.axaml 不含 `Padding=4,0`）| test 绿 | — |
| 5.5 | DesignSystemTests 加 `Browse_Buttons_Use_MaterialIcons`（MainWindow.axaml 不含 `Content="…"` Content="+` Content="-"` 在 Button 上）| test 绿 | — |
| 5.6 | DesignSystemTests 加 `MiddleClick_Behavior_Attached_To_ScrollViewer`（MainWindow.axaml 至少 2 处 `IsEnabled="True"` attached）| test 绿 | — |
| 5.7 | DesignSystemTests 加 `Caption_Buttons_Use_ElementRole_And_MaterialIcons`（含 three ElementRole 的 MainWindow.axaml）| test 绿 | — |
| 5.8 | LocalizationServiceTests 加 `Unicode_Status_Symbols_Cleared`（10 locale JSON 不含 `✓` `✗` `⚠`）| test 绿 | — |
| 5.9 | dotnet vstest 全部测试 | 135+ 全绿 (原有 11 + 新增 8 source-lint) | — |

## 阶段 6: 编译打包 + Git 提交

| # | 步骤 | 验收标准 |
|---|------|---------|
| 6.1 | dotnet build 全解决方案 | 0 error 0 warning |
| 6.2 | dotnet vstest core + integration test | 全绿 |
| 6.3 | `scripts/publish-app.ps1` 发 win-x64 到 `release/win-x64/` | release/win-x64/PhotoPrivacy.exe 存在 |
| 6.4 | 首次启动存活测活（启动 15s 无 FATAL）| 无 `[FATAL]` 日志 |
| 6.5 | git stage + commit（每阶段一个 commit，5 个 commit）| git diff --check 干净 |
| 6.6 | git push origin main | push 成功 |

## 验收标准汇总

- 阶段 1-4 每步有 build pass + source-lint test guard
- 阶段 5 总 8 个新 test guard 全绿（防回归）
- 阶段 6 release/win-x64/PhotoPrivacy.exe 首次启动存活（根基验证，根除此前"只打包不测活"教训）
- 原有 11 DesignSystemTests 不回归 + 原 16 i18n + 119 core 不回归
- 不引新 NuGet 依赖
- LF 规范（`git diff --check` 干净）

## 上游引用

- Files.App MIT `ScrollViewerMiddleClickExtensions.cs`: https://github.com/files-community/Files/blob/main/src/Files.App/Extensions/ScrollViewerMiddleClickExtensions.cs
- Avalonia 12.1 `WindowDrawnDecorations`: https://github.com/AvaloniaUI/Avalonia/pull/20770
- Avalonia `WindowDecorationProperties.ElementRole`: https://docs.avaloniaui.net/docs/how-to/window-how-to
- Semi.Avalonia 12.1 elevation/shadow: https://github.com/irihitech/Semi.Avalonia
