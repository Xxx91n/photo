# ADR 0047: i18n Full Coverage + Tray Refresh + 10-Language Expansion

> Status: Accepted
> Date: 2026-08-14
> Decisions: A1-A10 (grill Q1-Q9 + Q10 confirm)

## Context

PhotoPrivacy 的 i18n 存在以下问题：
1. AXAML 已用 `{ex:Localize key}` 绑定大部分 UI，但 code-behind (MainWindow.axaml.cs) 有 ~40 处硬编码中文字符串
2. TrayHost.cs 有 3 处硬编码中文（暂停/打开主窗口/退出），不随语言切换刷新
3. ServiceManager.cs 有 ~15 处硬编码中文（服务状态名/错误消息）
4. ViewModel 用中文 `Contains("运行")` 做状态判断——换语言后逻辑会断裂
5. 仅 4 语言（zh/en/ja/ar），env-manager 已支持 10 语言
6. 语言下拉提示文案 "在中文/英语/日语/阿拉伯语之间切换" 枚举语言名，新增语言需要改文案

## Decisions

### A1: 增强现有 LocalizationService（不引入新依赖）
- code-behind 通过 `LocalizationService.Instance.Get("key")` 取字符串
- TrayHost 订阅 `CultureChanged` 事件自动刷新 NativeMenuItem.Header
- 零新 NuGet 包，最小 diff

### A2: 迁移到嵌套 JSON 结构
- flat key（`nav.config`）→ 嵌套（`{"nav": {"config": "配置"}}`）
- 97 key 全部重写为嵌套结构
- 新增 key 遵循嵌套规范

### A3: 扩展到 10 语言
- 新增 6 语言：ko/de/fr/es/pt/ru
- 以 en.json 为基准模板，pwm pro 逐语言翻译
- JSON 语法 + key 集合一致性校验

### A4: 语言提示改为中性文案
- "选择界面显示语言" / "Select interface display language"
- 不枚举语言名，自动适配新增语言

### A5: TrayHost CultureChanged 事件订阅刷新
- 构造时用 `Get("tray.pause")` 初始化
- 订阅 `CultureChanged`，回调中刷新所有 NativeMenuItem.Header

### A6: 验证现有 ui.locale 持久化链路
- ComboBox → vm.CurrentLocale → ConfigEditCommand.Locale → config.json ui.locale → 启动时读取 → Initialize(locale)
- 补断链，不引入第二个持久化源

### A7: code-behind 全部提取 + ViewModel 逻辑改 enum/状态码
- 文件对话框标题 → `dialog.select_exiftool` 等 key
- 状态消息 → `status.detecting`/`msg.exiftool_not_found`
- ViewModel `Contains("运行")` → `IsRunning` 布尔标志 / enum

### A8: pwm pro 逐语言翻译 + 校验
- 每语言一次 pwm 调用，传入完整 key-value
- JSON.parse 校验 + key 集合与 en.json 完全对齐

### A9: 测试闭环
- LocalizationServiceTests: 10 语言 key 集合一致性
- ConfigEditorRoundTripTests: Locale 持久化往返
- TrayHostTests: CultureChanged 触发后菜单文案刷新

## Consequences

- 所有用户可见字符串经 LocalizationService，零硬编码中文
- 10 语言各 ~130 key，新增 key 需同步 10 文件
- ViewModel 不依赖中文字符串做逻辑判断
- TrayHost 切语言无需重启即时刷新
