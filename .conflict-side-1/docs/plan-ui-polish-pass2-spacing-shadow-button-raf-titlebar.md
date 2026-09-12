# 计划表: UI 精雕第二轮 — ADR 0051

**ADR**: docs/adr/0051-ui-polish-pass2-spacing-shadow-button-raf-titlebar.md
**决策点**: 4 个（A1-A4）grill 确认
**预算**: 1 亿 token
**目标**: 根除草皮包子感，达到专业 UI 设计师精雕水准

## 阶段总览

| 阶段 | 内容 | 决策 | 验收 |
|------|------|------|------|
| 1 | Space token 统一 + elevation shadow ladder | A1 | 全 view 无 ad-hoc Margin/Spacing 散值；elevation token 引用 |
| 2 | 字体清晰度加固 + focus ring | A1 | FontManagerOptions + FontFallbacks + TextOptions 配置；focus-visible 伪类 |
| 3 | 按钮过渡动画 | A2 | Button 样式含 Transitions；:pressed scale(0.97) + 75ms 过渡 |
| 4 | 中键滚动 RequestAnimationFrame | A3 | DispatcherTimer 换 RequestAnimationFrame；常量对齐 Files.App |
| 5 | 标题栏四态伪类 + Win11 尺寸 | A4 | caption-btn hover/pressed/inactive 三态色板；Padding 16,6 |
| 6 | 编译打包测活 + test guard | 全部 | dotnet build 0 error；DesignSystemTests 全绿；exe 进程存活 |

## 阶段 1: Space token 统一 + elevation shadow ladder

**步骤 1.1**: DesignTokens.axaml 加 4 级 BoxShadow elevation token
- Elevation0 (无投影) / Elevation1 (1dp) / Elevation2 (2dp) / Elevation4 (4dp)
- 验收: DesignTokens.axaml 含 4 个 Elevation* token

**步骤 1.2**: 全局扫描 MainWindow.axaml ad-hoc Margin/Spacing 散值
- 扫描所有 Margin 和 Spacing 字面量，替换为 Space* token 引用
- 验收: script 报告 0 个未引用散值

**步骤 1.3**: sidebar/settings-card/popover 引用 elevation token
- 加 BoxShadow 引用到对应 Border
- 验收: 至少 3 处 elevation 引用

**步骤 1.4**: git commit 阶段1 — Space token 统一 + elevation shadow ladder

## 阶段 2: 字体清晰度加固 + focus ring

**步骤 2.1**: Program.cs 加 FontManagerOptions 显式 FontFallbacks
- Inter → 系统中文字体回退链
- 验收: Program.cs 含 FontManagerOptions + FontFallbacks

**步骤 2.2**: 正文数字列加 FontFeatures 表格数字
- Log 页/统计数字列加表格数字特性
- 验收: 至少 2 处 FontFeatures 引用

**步骤 2.3**: 静态 UI 文字加 TextOptions.BaselinePixelAlignment
- 大标题 TextHintingMode=None
- 验收: TextOptions 配置正确

**步骤 2.4**: AppTheme.axaml 补 :focus-visible 伪类
- Button 变体 :focus-visible → border SemiColorPrimaryLight 半透明
- 验收: AppTheme 含 focus-visible 伪类样式

**步骤 2.5**: git commit 阶段2 — 字体清晰度加固 + focus ring

## 阶段 3: 按钮过渡动画

**步骤 3.1**: AppTheme.axaml Button.primary/ghost/danger/nav 加 Transitions
- BrushTransition 0.15s + TransformOperationsTransition 0.075s
- 基础 RenderTransform=scale(1)
- 验收: 5 个 Button 变体含 Transitions setter

**步骤 3.2**: :pressed 伪类设 scale(0.97)
- :pointerover 用 SineEaseOut；:pressed 用 QuadraticEaseInOut
- 验收: :pressed 含 scale(0.97) + :disabled 含 Opacity transition

**步骤 3.3**: AppTheme.axaml caption-btn 加 Transitions
- BrushTransition 0.1s（caption 按钮更快）
- 验收: caption-btn 含 Transitions

**步骤 3.4**: git commit 阶段3 — 按钮过渡动画 SukiUI 风格 scale(0.97)

## 阶段 4: 中键滚动 RequestAnimationFrame

**步骤 4.1**: MiddleClickScrollBehavior.cs DispatcherTimer → RequestAnimationFrame
- TopLevel.GetTopLevel(sv)?.RequestAnimationFrame(ScrollFrame)
- null 边界 fallback 到 DispatcherTimer
- 帧率无关计算：delta * (frameTime.TotalMilliseconds / 16.67)
- 验收: 代码含 RequestAnimationFrame 调用 + null fallback

**步骤 4.2**: 常量对齐 Files.App
- DeadZone: 10 → 12, SpeedFactor: 0.12 不变, MaxSpeedPerTick: 28 → 32
- 验收: 常量值与 Files.App 一致

**步骤 4.3**: static 单会话状态边界处理
- Stop() 清理 RequestAnimationFrame 循环
- 验收: Stop() 正确取消 RAF 循环

**步骤 4.4**: git commit 阶段4 — 中键滚动 RequestAnimationFrame vsync + 常量对齐 Files.App

## 阶段 5: 标题栏四态伪类 + Win11 尺寸

**步骤 5.1**: caption button Padding 改 16,6（高度 32px 对齐 Win11）
- MainWindow.axaml 3 个 caption button Padding=16,6
- 验收: Padding 值统一 16,6

**步骤 5.2**: AppTheme.axaml 加 caption-btn hover/pressed 伪类
- :pointerover → SemiColorBackground1；:pressed → SemiColorBorder
- .danger:pressed → SemiColorDanger
- 验收: 3 个伪类样式定义

**步骤 5.3**: 窗口失活态
- Window:inactive .caption-btn Foreground=SemiColorText2
- 验收: inactive 伪类定义

**步骤 5.4**: git commit 阶段5 — 标题栏四态伪类 + Win11 标准尺寸

## 阶段 6: 编译打包测活 + test guard

**步骤 6.1**: dotnet build PhotoPrivacy.sln 0 error/0 warning
**步骤 6.2**: DesignSystemTests + LocalizationServiceTests 全绿
- 新增 test: Button_Transitions_Defined, Caption_Btn_Four_State_Pseudoclasses, Space_Token_References, Elevation_Token_Defined, MiddleClick_RAFAPI_Used
**步骤 6.3**: dotnet publish + 测活
- scripts/publish-app.ps1 → release/win-x64/PhotoPrivacy.exe
- 启动 exe，进程 12s 存活，无 FATAL
**步骤 6.4**: git commit 阶段6 — source-lint test guard + 测活闭环
**步骤 6.5**: git push origin main
