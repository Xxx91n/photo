# UI Fixes + Config Reload Design

**Goal**
修复已知功能性 Bug（服务模式暂停崩溃、主题切换无效、备份逻辑错误、路径选择缺失、监控目录未自动创建、服务模式应用配置失败），并完成 UX 改进（仅“应用配置”、日志设置区、ExifTool 自动检测、统一窗口/托盘图标）。

**Scope**
涉及 UI、Core Pipeline、Worker IPC 热重载、配置模型和测试。所有修改遵循既有架构：GUI 不直接控制服务启停，热生效通过 IPC ReloadConfig 完成。

---

## 1) 服务模式暂停按钮禁用

**问题**
服务模式点击“暂停”会在 IPC 读取时抛异常并崩溃。

**设计**
- 初始化时检测 `RuntimeKind == "service"`：
  - `PauseResumeButton.IsEnabled = false`
  - 文案为 `"暂停（服务模式不可用）"`
- `OnPauseResumeClick` 添加服务模式防卫返回。
- `SwitchToDefaultModeAsync` 与模式切换后同步更新按钮启用状态与文案。

**测试**
新增 UI source/行为测试 `PauseButton_InServiceMode_ShouldBeDisabled`。

---

## 2) 主题切换即时生效

**问题**
ThemeVariant 仅为 VM 字段，未触发 Avalonia 主题切换；启动也未读取配置。

**设计**
- `App.axaml.cs` 启动时读取 config 并设置 `RequestedThemeVariant`。
- `MainWindowViewModel.ThemeVariant` setter 内部同步 `Application.Current.RequestedThemeVariant`。
- UI Theme 选择控件双向绑定 `ThemeVariant`，选项值严格 `system|light|dark`。
  - 通过 `ComboBoxItem.Tag` 传递值，`ThemeVariant` 为 string。

**测试**
新增测试 `ThemeVariant_Change_ShouldUpdateAvaloniaTheme`（验证 setter 行为/应用状态更新）。

---

## 3) 备份逻辑修复 + 备份目录

**问题**
`.bak` 变成了清理后的文件；备份目录缺失且不创建。

**设计**
- 备份策略：启用后先备份原文件，再执行清理。
- 新增 `BackupOptions.Directory`：为空时使用 `Path.Combine(HotFolder, "bak")`。
- 备份目录保持相对结构：`D:\hot\sub\a.jpg → D:\hot\bak\sub\a.jpg.bak`。
- 默认启用备份（`Backup.Enabled = true`）。
- 当 `Backup.Directory` 为空时，ApplyConfig 写回解析后的 `HotFolder\bak`。
- 若目录不存在，提前创建。
- RuleEngine 决策时保证 `BackupPath != OutputPath`（否则直接抛错或 audit）。

**测试**
新增 `Backup_ShouldContainOriginalMetadata_AfterPipelineRun` + RuleEngine 断言测试。

---

## 4) 路径选择器（StorageProvider）

**范围**
ExifTool 路径、监控目录、备份目录、日志目录。

**设计**
- 每行路径输入添加 📁 浏览按钮（`Button.ghost`，宽 36）。
- 使用 Avalonia `StorageProvider`：
  - ExifTool 使用 `OpenFilePickerAsync`（过滤 `exiftool.exe`）。
  - 目录使用 `OpenFolderPickerAsync`。
- `MainWindow` 新增可注入 `IStorageProvider` 以便测试。

**测试**
新增 `FolderPicker_ShouldUpdateViewModel_WhenFolderSelected`（Mock StorageProvider）。

---

## 5) 监控目录自动创建

**设计**
- Worker 启动 FileSystemWatcher 前确保 `Watch.HotFolder` 目录存在。
- 若文件路径不存在则跳过处理，必要时写入 audit 事件 `directory_created`。

---

## 6) 服务模式“应用配置”失败（热重载）

**问题**
服务模式错误调用 `ServiceManager.Start()` 导致 1056。

**设计**
- 统一为 IPC 热重载：
  - `WorkerIpcMethods.ReloadConfig`
  - UI `WorkerIpcClient.ReloadConfigAsync`
  - Worker 端处理 ReloadConfig 并触发配置重新加载
- ApplyConfig：
  1) 写入 config
  2) `ReloadConfigAsync`
  3) `SaveStatus = "✓ 已应用"`（失败显示 `✗ 应用失败：...`，3 秒清空）
 - 删除 “保存更改” 按钮，仅保留 “应用配置”。

---

## 7) 日志设置区域

**设计**
- 新增“日志设置”卡片：
  - LogLevel（info/debug）
  - AuditLogDirectory（路径 + 浏览按钮）
- Config：`Audit.LogDirectory` + `Audit.DiagnosticMode`，VM 以 `LogLevel` 封装。
- 保留“详细事件”CheckBox，但增加说明文字。

---

## 8) ExifTool 自动检测

**设计**
- 检测顺序：便携版路径优先，其次 Program Files，再 PATH。
- 结果显示在 ExifTool 输入框下方提示（灰色文字）。
- 同步更新 DefaultPaths.ExifToolPath（便携版优先）。

---

## 9) 统一窗口/托盘图标

**设计**
- 使用 `Assets/tray-dot-16.png.base64` 解码为 WindowIcon。
- App 启动时设置 `desktop.MainWindow.Icon`。
- `PhotoPrivacy.Ui.csproj` 添加 `ApplicationIcon` 指向统一图标。

---

## 验收
- 服务模式暂停按钮禁用且不崩溃
- 主题切换即时生效
- 备份包含原始元数据，目录自动创建
- 路径选择器可用（文件/文件夹）
- 服务模式应用配置不再 1056，显示成功提示
- 图标一致
- `dotnet test PhotoPrivacy.sln` 全绿
