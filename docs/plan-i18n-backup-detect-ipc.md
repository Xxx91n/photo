# PhotoPrivacy i18n + 备份 + 检测 + IPC 修复总计划

> 基于 grill A1-A2 + domain-modeling ADR 0044-0046 固化。
> 每一步必须有 test 闭环，不自欺欺人。

## 阶段一：i18n Locale ComboBox 端到端（ADR 0044）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 1 | ✅ UiOptions record 加 Locale 字段 | build 通过 | — |
| 2 | AppConfigJson UiDto 加 locale JSON 属性 | 旧 config 缺该字段自动默认 | AppConfigLoaderTests |
| 3 | ConfigEditCommand 加 Locale 参数 | build 通过 | — |
| 4 | ConfigEditor.UpdateConfig 写入 Locale | json 输出含 locale | ConfigEditorRoundTripTests |
| 5 | MainWindowViewModel 加 CurrentLocale 属性 | PropertyChanged 通知 | MainWindowViewModelTests |
| 6 | MainWindow.axaml 新增 LocaleVariantComboBox | ItemsSource 绑定 AvailableLocales | — |
| 7 | MainWindow.axaml.cs SelectionChanged handler | 调用 SwitchLocale + 防抖写盘 | — |
| 8 | MainWindow.InitializeRuntime 从 config 初始化 | ComboBox 显示当前 locale | — |
| 9 | locale JSON 文件加 config.desc.language 键（复用已有 settings.language） | 所有 4 locale 文件同步 | — |
| 10 | build + test 全通过 | 0 error + 114+ Core test 通过 | — |

## 阶段二：Hot Folder 守卫 + 备份空值防御（ADR 0045）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 11 | AppConfigValidator 非 dry_run 空路径报错 | 抛 AppConfigValidationException | AppConfigValidatorTests 新增用例 |
| 12 | RuleEngine.ResolveBackupPath 空路径返回 null | 不生成无意义相对路径 | RuleEngineTests 新增用例 |
| 13 | EnumerateFiles 空路径保护 | 不崩，静默跳过 | — |
| 14 | build + test | 0 error + test 通过 | — |

## 阶段三：IPC GetRecentLogs 通道（ADR 0046）

| # | 步骤 | 验收标准 | test |
|---|------|---------|------|
| 15 | WorkerIpcMethods 加 GetRecentLogs 常量 | build 通过 | — |
| 16 | WorkerIpcResponse.Data 扩展 RecentLogsDto | JSON 序列化正确 | — |
| 17 | WorkerIpcServerHostedService HandleRequest 新增 case | 返回尾部 N 行 | — |
| 18 | WorkerProcessManager 加 GetRecentLogsAsync | UI 可调用 | — |
| 19 | UI 重连时调用补位历史 | 日志列表立即填充 | — |
| 20 | build + test | 0 error + test 通过 | — |

## 阶段四：闭环验证

| # | 步骤 | 验收标准 |
|---|------|---------|
| 21 | publish win-x64 | 0 error |
| 22 | 启动 release exe 存活 10s | 不崩溃 |
| 23 | UI 可见 LocaleVariantComboBox | 下拉显示 zh-CN/en/ja/ar |
| 24 | 切换语言即时生效 | UI 文字刷新无需重启 |
| 25 | 配置 hot_folder 后 Worker 处理文件 | 审计日志有 file_detected |
| 26 | commit + push | git diff --check 通过 |