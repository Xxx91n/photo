# ADR 0051: UI 精雕第二轮 — Space token 统一 + elevation shadow ladder + 按钮过渡动画 + RequestAnimationFrame 中键滚动 + 标题栏四态

**Date**: 2026-08-17
**Status**: Accepted
**Branch**: main (并行修复分支)
**Decisions**: 4 个决策点（A1-A4）通过 grill 流程确认

## 背景

ADR 0050 完成了第一轮 UI 精雕（surface 4 层 overlay + typography 6 角色 + emoji 清零 + 中键快翻移植 + 自绘标题栏），但用户反馈"草皮包子感"未根除——ADR 0050 只搬运了基础组件，没有做第二遍打磨（Reddit r/ClaudeCode：treating the first output as a wireframe, not a deliverable）。具体四子症：

1. **AI 草率感残留**：view 内仍有 `Margin="12,0"` `Spacing="5"` 等 ad-hoc 散值（未引用 Space* token）；无 elevation shadow ladder；无 `:focus-visible` 键盘焦点 ring；字体渲染未加固 FontManagerOptions/FontFallbacks/TextOptions
2. **按钮按压闪现弹回**：AppTheme.axaml 的 Button 样式无任何 `Transitions` 定义，`:pressed` 伪类松开瞬间硬切 → "闪一下就没了"（Semi.Avalonia 零动画哲学的后果）
3. **中键滚动帧率不够顺畅**：DispatcherTimer 16ms 非vsync对齐，高刷屏合并帧/抖动；常量 10/0.12/28 偏离 Files.App 原版 12/0.12/32
4. **标题栏按钮不精致**：caption button 无 hover/pressed/inactive 四态色板，Padding 14,8 使高度不居中

## 调研来源

- Q1 UI质量心智模型: atomcode 深度调研（Reddit r/ClaudeCode + Avalonia Styling best practices + avalonia-pro-max 反模式清单 + 官方 TextOptions/FontManagerOptions API 文档 + PR #20107）
- Q2 按钮按压: atomcode 深度调研（Fluent/Semi.Avalonia/SukiUI 三源码直读 + 官方 control-transitions/easing-functions 文档 + issue #15704/#16179/#7626）
- Q3 中键滚动: atomcode 深度调研（Files.App 源码全文 + Avalonia TopLevel.RequestAnimationFrame 官方文档 + 维护者 maxkatz6/neuecc 确认 + issue #12977 + LibreScroll + SmoothScroll.Avalonia）
- Q4 标题栏: atomcode 深度调研（SukiUI PR #598 + FluentAvalonia AppWindowTitleBar 文档 + Ursa.Avalonia 2.x TitleBar PR #917 + Windows 11 设计规范 + v2rayN/Files.App 源码对比）

## 决策

### A1: Space token 统一 + elevation shadow ladder + 字体清晰度加固（深度打磨 Semi.Avalonia 基础）

零新依赖。全部基于现有 Semi + Inter + 官方 TextOptions API。

**Space token 统一**:
- 全局扫描 view 内所有 `Margin="N,..."` `Spacing="N"` 散值，替换为 `Space*` token 引用（DesignTokens 已定义 SpaceXxs→Xxxl）
- 禁止：`Margin="12,0"` 应改为 `Margin="{DynamicResource SpaceMd}"` 或等效 token 引用
- XAML Thickness 仍用字面量字符串（Padding="16"）触发 ThicknessTypeConverter（ADR 已约束）

**Elevation shadow ladder**:
- 在 DesignTokens.axaml 定义 4 级 BoxShadow token：`Elevation0` (无投影) / `Elevation1` (1dp) / `Elevation2` (2dp) / `Elevation4` (4dp)
- sidebar/settings-card/popover 引用对应 elevation token
- 配合 ADR 0050 的 overlay 第 4 层 surface

**字体清晰度加固**:
- Program.cs 加 `FontManagerOptions` 显式配 `FontFallbacks`（CJK 回退链：Inter → 系统中文字体）
- 正文数字列加 `FontFeatures="+tnum"`（表格数字，数据密集 UI 推荐）
- 静态 UI 文字加 `TextOptions.BaselinePixelAlignment="Aligned"`（Avalonia 11.3/12 新 API，PR #20107）
- 大标题 `TextOptions.TextHintingMode="None"` 保留字形
- 不全局 SubpixelAntialias 强开（Auto 由平台决定）

**focus ring 保留**:
- 补 `:focus-visible` 伪类样式（SemiColorPrimaryLight 半透明 border），保留键盘可达性不破坏视觉

### A2: 按钮过渡动画 — SukiUI 风格全属性 BrushTransition + scale(0.97)

在 `Button.primary` / `Button.ghost` / `Button.danger` / `Button.nav` / `Button.caption-btn` 基础样式上加 `Transitions` setter：

```xml
<Setter Property="Transitions">
  <Transitions>
    <BrushTransition Property="Background" Duration="0:0:0.15" />
    <BrushTransition Property="BorderBrush" Duration="0:0:0.15" />
    <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.075" />
  </Transitions>
</Setter>
<Setter Property="RenderTransform" Value="scale(1)" />
```

- `:pressed` 设 `RenderTransform="scale(0.97)"`（CSS 风格，可过渡；WPF 风格 ScaleTransform 不能 transition）
- `:pointerover` 用 `SineEaseOut`，`:pressed` 用 `QuadraticEaseInOut`（官方 easing 文档推荐）
- 必须 避免 `BackEaseInOut`/`ElasticEaseInOut`（issue #15704 触发 visibility 闪烁）
- `:disabled` 加 `DoubleTransition Property="Opacity" Duration="0:0:0.2"`
- 约 20 行 XAML 改动

### A3: 中键滚动 — 换 TopLevel.RequestAnimationFrame + 常量对齐 Files.App

- `DispatcherTimer` → `TopLevel.RequestAnimationFrame(Action<TimeSpan>)`，vsync 对齐消除合并帧
- 常量调回 Files.App 原版：`DeadZone=12, SpeedFactor=0.12, MaxSpeedPerTick=32`
- 用 TimeSpan 做帧率无关计算（高刷屏自动适配：`delta * (frameTime.TotalMilliseconds / 16.67)`）
- 处理 `TopLevel.GetTopLevel(sv)` null 边界（ScrollViewer 未挂树时 null → fallback 到 DispatcherTimer）
- static 单会话状态与窗口切换边界处理
- 保持线性比例速度模型（Files.App 生产验证，松键即停，不加惯性）
- 约 30 行改动，零新依赖

### A4: 标题栏四态伪类 + Win11 标准尺寸

- caption button 高度统一 32px（Win11 原生标准），Padding 改 `16,6` 使按钮宽约 46px
- 加 `Button.caption-btn:pointerover` → `Background="SemiColorBackground1"`（淡灰半透明）
- 加 `Button.caption-btn:pressed` → `Background="SemiColorBorder"`（更深灰）
- 加 `Button.caption-btn.danger:pressed` → `Background="SemiColorDanger"`（关闭红）
- 加窗口失活态：`Window:inactive .caption-btn Foreground="SemiColorText2"`（变灰）
- 间距 Margin 12px 对齐 Win11
- 约 25 行 XAML 改动

## 约束

- 零新依赖（全部基于现有 Semi.Avalonia + Inter + 官方 API）
- 跨平台兼容（TextOptions 是尽力映射，跨平台差异无法完全消除）
- TextOptions 限制（2026-08-17 实测）：Avalonia 12.1.1 binary 中 Avalonia.Media.TextOptions 是 readonly record struct，只暴露 SetTextHintingMode(Visual, TextHintingMode) 等 static method setter，未公开 TextHintingModeProperty / BaselinePixelAlignmentProperty 静态 AvaloniaProperty 字段。XAML attached property 写法 在 12.1.1 触发 AVLN2000 编译失败。FontFeatures +tnum (11.1+) 是兼容幸存者，已在 LogEntries.ListBox.TimeText 落地。TextHintingMode=None (大标题) 与 BaselinePixelAlignment=Aligned (静态正文) 的 spec intent 暂缓落地，等 Avalonia 12.2+ 暴露 XxxProperty 字段或改 code-behind OnLoaded 扫描 TextBlock 调 static setter（碎片化成本高，非 Ponytail 最短路径）。此限制不阻塞 ADR A1 BalanceItem (Space/Elevation/focus-visible/FontFallbacks) 完全落地。
- 每阶段 commit 后才能进下一阶段（回溯需要）
- 最终编译打包测活，验证进程存活
- source-lint test guard 固化闭环
