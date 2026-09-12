# PhotoPrivacy i18n Full Coverage 总计划

> 基于 grill Q1-Q9 (ADR 0047) + domain-modeling 固化。
> 每一步必须有 test 闭环，不自欺欺人。

## 阶段一：嵌套 JSON 重构 + 新 key 补全（ADR 0047 A2/A4）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 1 | 4 现有 locale JSON (zh/en/ja/ar) flat→嵌套重构 | JSON.parse 通过，key 集合与原 flat 完全对齐 | 脚本校验 |
| 2 | 新增 code-behind 所需 key（dialog.*/status.*/msg.*/tray.*） | 嵌套结构追加 30-40 key | 脚本校验 |
| 3 | config.desc.language 改为中性文案（"选择界面显示语言"风格） | 10 语言全部更新 | — |
| 4 | LocalizationService 支持嵌套 JSON 解析 | Dictionary<string,string> 改为 JsonNode 递归展开为 dot-path key | LocalizationServiceTests |
| 5 | build + test | 0 error + test 通过 | — |

## 阶段二：code-behind + ServiceManager 全量提取（ADR 0047 A1/A7）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 6 | MainWindow.axaml.cs 文件对话框标题提取 | 6 处 Title 改为 Get("dialog.*") | — |
| 7 | MainWindow.axaml.cs 状态消息提取 | "检测中…"/"未找到 ExifTool" 等改为 Get() | — |
| 8 | MainWindow.axaml.cs 保存结果提取 | "✓ 已自动保存" 等改为 Get() | — |
| 9 | MainWindow.axaml.cs 服务状态提取 | "服务模式"/"托盘运行中" 等改为 Get() | — |
| 10 | MainWindow.axaml.cs 错误提示提取 | "未找到 Worker" 等改为 Get() | — |
| 11 | ServiceManager.cs 服务状态名提取 | GetStatusText 的 9 处中文改为 Get() | — |
| 12 | ServiceManager.cs 错误消息提取 | Success/Failed/Skipped 默认消息改为 Get() | — |
| 13 | build + test | 0 error + test 通过 | — |

## 阶段三：ViewModel 逻辑去中文依赖（ADR 0047 A7）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 14 | MainWindowViewModel Contains("运行") 等状态判断改为 enum/布尔标志 | 6 处 Contains 改为 IsRunning/IsPaused/IsServiceMode 等属性 | ViewModelTests |
| 15 | MainWindow.axaml.cs Contains("暂停") 等同步改 | 所有依赖中文匹配的逻辑改 enum | — |
| 16 | build + test | 0 error + test 通过 | — |

## 阶段四：TrayHost i18n 接入（ADR 0047 A5）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 17 | TrayHost.cs 3 处硬编码中文改为 Get("tray.*") | 构造时取 tray.pause/tray.open/tray.exit | — |
| 18 | TrayHost 订阅 CultureChanged 事件 | 回调刷新所有 NativeMenuItem.Header | TrayHostTests |
| 19 | ExifTool 托盘图标 ToolTipText 接入 i18n | Get("app.title") | — |
| 20 | build + test | 0 error + test 通过 | — |

## 阶段五：10 语言扩展（ADR 0047 A3/A8）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 21 | pwm pro 翻译 ko.json | JSON.parse + key 集合与 en.json 对齐 | 脚本校验 |
| 22 | pwm pro 翻译 de.json | JSON.parse + key 集合对齐 | 脚本校验 |
| 23 | pwm pro 翻译 fr.json | JSON.parse + key 集合对齐 | 脚本校验 |
| 24 | pwm pro 翻译 es.json | JSON.parse + key 集合对齐 | 脚本校验 |
| 25 | pwm pro 翻译 pt.json | JSON.parse + key 集合对齐 | 脚本校验 |
| 26 | pwm pro 翻译 ru.json | JSON.parse + key 集合对齐 | 脚本校验 |
| 27 | csproj EmbeddedResource 确认新 JSON 被打包 | build 后 bin 里有 10 个 locale 文件 | — |
| 28 | AXAML LocaleVariantComboBox 新增 6 个 ComboBoxItem | 10 语言全部可选 | — |
| 29 | build + test | 0 error + test 通过 | — |

## 阶段六：持久化链路验证 + 测试闭环（ADR 0047 A6/A9）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 30 | 验证 ui.locale 持久化链路完整 | ComboBox→vm→ConfigEditor→config.json→启动读取→Initialize | ConfigEditorRoundTripTests |
| 31 | LocalizationServiceTests: 10 语言 key 集合一致性 | 每语言 key 集合 == en.json key 集合 | 自动校验 |
| 32 | TrayHostTests: CultureChanged 触发后菜单文案刷新 | SwitchLocale 后 menu item text 变化 | — |
| 33 | ConfigEditorRoundTripTests: Locale 持久化往返 | 写入→读取→值一致 | — |
| 34 | 全量 build + Core test + Integration test | 0 error + test 通过 | — |

## 验收标准汇总

- 0 处硬编码中文字符串在 src/PhotoPrivacy.Ui/ 源码中（注释除外）
- 10 个 locale JSON 文件，key 集合完全一致
- 语言切换即时刷新 AXAML + code-behind + TrayHost
- ui.locale 持久化到 config.json，重启不丢失
- ViewModel 逻辑不依赖任何中文字符串
- dotnet build 0 error + Core Tests 全部通过
