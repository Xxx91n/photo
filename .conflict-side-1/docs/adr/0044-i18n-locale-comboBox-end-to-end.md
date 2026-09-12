# ADR 0044: i18n Locale ComboBox 端到端落地

**状态**: 已接受
**日期**: 2026-08-14

## 背景

ADR 0040 创建了 LocalizationService + LocalizeExtension + 三层 locale resolver，
AutomationTests 确认 AXAML 有 58 处 {ex:Localize} 绑定。但 UI 上没有任何
语言选择 ComboBox——用户无法切换语言。这是 grill 审计发现的"只引入后端
但 UI 没落地"的典型：SwitchLocale/CultureChanged/AvailableLocales 全就位，
但没有任何 UI 控件调用它们。

参考 ExifCleaner（Electron + React）：25 languages with in-app language
switching，change language from settings without restarting。Avalonia 官方
文档确认 ComboBox ItemsSource + SelectedItem + SelectionChanged 是标准模式。

## 决策

在配置页面的_behavior区域，与 ThemeVariant ComboBox 并排新增 LocaleVariant
ComboBox：

1. ItemsSource 绑定 LocalizationService.Instance.AvailableLocales（动态列表）
2. SelectedItem 绑定 MainWindowViewModel.CurrentLocale（新增属性）
3. SelectionChanged 调用 LocalizationService.Instance.SwitchLocale(selectedLocale)
4. UiOptions record 新增 string Locale = "zh-CN" 字段
5. AppConfigJson UiDto 新增 locale JSON 属性
6. ConfigEditCommand 新增 Locale 参数
7. ConfigEditor.UpdateConfig 写入 Locale
8. MainWindow.InitializeRuntime 从 config 读取 Locale 初始化 ComboBox

与 ThemeVariant/LogLevel 模式完全一致：防抖 500ms 写 config.json，
Worker 通过 IPC ReloadConfig 重读。

### 考虑的替代方案

**方案 B（拒绝）**: 在 MainWindow 顶部导航栏加 ComboBox。破坏现有导航栏
布局结构，且语言切换是低频操作不需要全页面可见。