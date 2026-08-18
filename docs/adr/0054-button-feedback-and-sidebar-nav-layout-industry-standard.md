# ADR 0054: 按钮反馈去除 scale + 侧栏导航按钮等宽布局

`Status: implemented` — 3 bug 修复落地，build 0 error，Core 140 + Integration 202 测试全过

## 背景

用户反馈 3 个 UI 问题：
1. 规则页面点击后闪退
2. 侧栏按钮宽度不统一（"服务管理器"比"配置"宽，因为文字长度不同）
3. 按钮按下反馈太花哨（scale 缩放动画）

## 调研来源

- **atomcode 2026-08-18 两轮调研**（Exa+Tavily+AnySearch 三引擎）
- Avalonia Fluent Button.xaml 源码（master 分支全文）
- SukiUI Button.axaml 源码（全文 + fetch_output 续读）
- NN/g Button States: Communicate Interaction（2025-04-25 全文）
- Fluent 2 Color Interaction States（官方全文）
- WCAG 2.2 SC 2.3.3 Understanding（全文）
- MDN prefers-reduced-motion（2026-06 修订）
- GitHub discussion #16100（Avalonia 官方维护者 stevemonaco）
- FluentAvalonia NavigationViewItem 模板源码

## 决策（grill 3 项方案 A/A/A）

### Bug1: 规则页面闪退 — catch 块加日志（方案 A）

`ApplyCommunityThemeResources` 的 catch 块之前吞掉所有异常，`ResourceInclude` 加载失败时无诊断信息。
改为 `catch (Exception ex)` + `UiDiagnosticLog.Write` 输出失败信息。

**不改结构**：index-replace Fallback-C（Avalonia #9691 认证）保持不变，slot 1 fallback 到空 ResourceDictionary 已正确。

### Bug2: 侧栏按钮等宽 — Grid Auto,* 替换 StackPanel（方案 A）

**根因**：4 个导航按钮内容是 `StackPanel Orientation="Horizontal"`（icon + text）。横向 StackPanel 测量时给子元素无限宽度，不会 stretch——按钮内容宽度由文字长度决定。

**修复**：改为 `Grid ColumnDefinitions="Auto,*"`。icon 固定 20px 在 column 0，TextBlock 在 column 1 `HorizontalAlignment="Stretch" + TextTrimming="CharacterEllipsis"`。容器约束 200px sidebar 宽度，所有按钮内容填满宽度。

**为什么不选 ListBox + NavigationView 模式**（atomcode Fix B 完整版）：需要重构选中项绑定逻辑 + ViewModel 变更，改动过大。当前 Button + Classes="nav active" + SetNavButtonActive 模式已可用，Grid Auto,* 是最小改动达到等宽效果。

### Bug3: 按钮反馈去 scale — 纯颜色过渡（方案 A）

**根因**：ADR 0051 A2 引入了 SukiUI 风格 `scale(0.97)` pressed + `TransformOperationsTransition`。SukiUI 的 3-7% 缩放幅度 + hover 放大 = 花哨感来源。

**修复**：删除全局 `Button:pressed` 的 `scale(0.97)` + `TransformOperationsTransition` + `RenderTransform="scale(1)"`。保留 `BrushTransition` 颜色过渡 150ms（`SineEaseOut`）。

**行业依据**：
- NN/g: pressed = "very slight color change or very short and minimal animation"，100-150ms
- Fluent 2: 交互状态 = 组件随交互变深（rest→hover→pressed），focus = 加粗描边
- WCAG 2.2 SC 2.3.3: scale = motion animation（前庭障碍触发源），颜色/透明度不算
- Avalonia 官方 Fluent: `scale(0.98)` 75ms + 颜色，但 2% vs 3% 差异显著
- SukiUI: `scale(0.97)` + hover `scale(1.03)` = 花哨

**选择纯颜色而非 scale(0.98)**：企业级隐私工具应对齐 VS Code / Windows 11 Settings 标准（纯颜色变化），而非 Avalonia 默认。

`:focus-visible` 描边已有（`BorderBrush=SemiColorPrimaryLight BorderThickness=2`），符合 NN/g focus 用描边而非颜色的要求。

## 测试

- `Button_Transitions_Easing_Must_Be_Specified`：移除 `QuadraticEaseInOut` 断言，保留 `SineEaseOut`
- `Button_Transitions_Must_Include_TransformOperationsTransition` → 重写为 `Button_Transitions_Must_Not_Include_Scale_Pressed`：断言 `DoesNotContain scale(0.97)` + `DoesNotContain TransformOperationsTransition`
- Core 140 passed, Integration 202 passed, build 0 error
