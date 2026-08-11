# UI Fixes + Config Reload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 UI 功能性 Bug（暂停崩溃、主题切换无效、备份逻辑错误、路径选择缺失、监控目录自动创建、服务模式应用配置失败），并完善 UX（仅应用配置、日志设置区、ExifTool 自动检测、统一图标）。

**Architecture:** GUI 侧所有配置变更统一走「写配置 + IPC ReloadConfig」热重载；Worker 侧重建配置与管线，Watcher、审计与 ExifTool Bridge 依据新配置更新；备份路径由 RuleEngine 决策，FileTaskPipeline 负责顺序保证与目录创建。

**Tech Stack:** .NET 10, Avalonia 11, xUnit, NamedPipe IPC

---

## File structure mapping

- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Modify: `src/PhotoPrivacy.Ui/ConfigEditCommand.cs`
- Modify: `src/PhotoPrivacy.Ui/ConfigEditor.cs`
- Modify: `src/PhotoPrivacy.Ui/App.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj`
- Modify: `src/PhotoPrivacy.Ui/WorkerIpcClient.cs`
- Modify: `src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs`
- Modify: `src/PhotoPrivacy.Ipc/WorkerIpcJsonContext.cs`
- Modify: `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs`
- Modify: `src/PhotoPrivacy.Worker/WorkerRuntimeContext.cs`
- Modify: `src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs`
- Modify: `src/PhotoPrivacy.Core/Rules/RuleEngine.cs`
- Modify: `src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs`
- Modify: `src/PhotoPrivacy.Core/Constants/DefaultPaths.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs`
- Modify: `config/config.sample.json`
- Modify: `README.md`
- Create: `tests/PhotoPrivacy.Core.Tests/Rules/RuleEngineTests.cs`
- Modify: `tests/PhotoPrivacy.Core.Tests/Pipeline/FileTaskPipelineTests.cs`
- Modify: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs`
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantBehaviorSourceTests.cs`
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/PauseButtonPolicySourceTests.cs`
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/FolderPickerIntegrationTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs`

---

### Task 1: Pause 按钮在服务模式禁用

**Files:**
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/PauseButtonPolicySourceTests.cs`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Write failing test (RED)**

```csharp
using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class PauseButtonPolicySourceTests
{
    [Fact]
    public void PauseButton_InServiceMode_ShouldBeDisabled()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("PauseResumeButton.IsEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("暂停（服务模式不可用）", source, StringComparison.Ordinal);
        Assert.Contains("if (string.Equals(_options.RuntimeKind, \"service\"", source, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run:
```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~PauseButtonPolicySourceTests"
```
Expected: FAIL (strings not found).

- [ ] **Step 3: Implement minimal code (GREEN)**

In `MainWindow.InitializeRuntime`, after setting `viewModel`:
```csharp
if (string.Equals(options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase))
{
    PauseResumeButton.IsEnabled = false;
    PauseResumeButton.Content = "暂停（服务模式不可用）";
}
else
{
    PauseResumeButton.IsEnabled = true;
}
```

In `OnPauseResumeClick` add guard:
```csharp
if (_options is not null && string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase))
{
    return;
}
```

In `SwitchToDefaultModeAsync`, after updating `_options.RuntimeKind`:
```csharp
PauseResumeButton.IsEnabled = !string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase);
PauseResumeButton.Content = PauseResumeButton.IsEnabled ? PauseResumeButton.Content : "暂停（服务模式不可用）";
```

- [ ] **Step 4: Run test to verify pass**

Run same test, expect PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/PhotoPrivacy.IntegrationTests/Ui/PauseButtonPolicySourceTests.cs src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs
git commit -m "fix(ui): disable pause button in service mode"
```

---

### Task 2: ThemeVariant 即时切换生效

**Files:**
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantBehaviorSourceTests.cs`
- Modify: `src/PhotoPrivacy.Ui/App.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`

- [ ] **Step 1: Write failing test (RED)**

```csharp
using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ThemeVariantBehaviorSourceTests
{
    [Fact]
    public void ThemeVariant_Change_ShouldUpdateAvaloniaTheme()
    {
        var vmPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs");
        var vmSource = File.ReadAllText(vmPath, Encoding.UTF8);
        Assert.Contains("RequestedThemeVariant", vmSource, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Dark", vmSource, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Light", vmSource, StringComparison.Ordinal);

        var appPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml.cs");
        var appSource = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("RequestedThemeVariant", appSource, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test (expect FAIL)**

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ThemeVariantBehaviorSourceTests"
```

- [ ] **Step 3: Implement minimal code (GREEN)**

In `MainWindowViewModel.ThemeVariant` setter:
```csharp
if (SetField(ref _themeVariant, value))
{
    var normalized = value?.ToLowerInvariant();
    if (Avalonia.Application.Current is not null)
    {
        Avalonia.Application.Current.RequestedThemeVariant = normalized switch
        {
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            "light" => Avalonia.Styling.ThemeVariant.Light,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }
}
```

In `App.axaml.cs` (after config load):
```csharp
Application.Current!.RequestedThemeVariant = config.Ui.ThemeVariant switch
{
    "dark" => Avalonia.Styling.ThemeVariant.Dark,
    "light" => Avalonia.Styling.ThemeVariant.Light,
    _ => Avalonia.Styling.ThemeVariant.Default
};
```

In `MainWindow.axaml` theme combo:
```xml
<ComboBox SelectedValue="{Binding ThemeVariant}" SelectedValuePath="Tag">
  <ComboBoxItem Content="system" Tag="system" />
  <ComboBoxItem Content="light" Tag="light" />
  <ComboBoxItem Content="dark" Tag="dark" />
</ComboBox>
```

- [ ] **Step 4: Run test (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantBehaviorSourceTests.cs src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs src/PhotoPrivacy.Ui/App.axaml.cs src/PhotoPrivacy.Ui/Views/MainWindow.axaml
git commit -m "fix(ui): apply theme variant changes immediately"
```

---

### Task 3: 备份目录与备份顺序修复

**Files:**
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs`
- Modify: `config/config.sample.json`
- Create: `tests/PhotoPrivacy.Core.Tests/Rules/RuleEngineTests.cs`
- Modify: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs`

- [ ] **Step 1: Write failing tests (RED)**

Add to `AppConfigConcurrencyOptionsTests.cs`:
```csharp
[Fact]
public void Load_Should_Map_Backup_Directory_Field()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, """
        {
          "schema_version": 1,
          "backup": {
            "enabled": true,
            "directory": "D:\\hot\\bak",
            "suffix": ".bak",
            "retention": "keep"
          }
        }
        """);

        var cfg = AppConfigLoader.Load(configPath);
        Assert.Equal("D:\\hot\\bak", cfg.Backup.Directory);
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}
```

New `RuleEngineTests.cs`:
```csharp
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    [Fact]
    public void Decide_Should_Build_BackupPath_Under_BackupDirectory()
    {
        var cfg = AppConfig.Default with
        {
            Backup = AppConfig.Default.Backup with
            {
                Enabled = true,
                Directory = @"D:\hot\bak",
                Suffix = ".bak"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = @"D:\hot" }
        };

        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\sub\a.jpg");

        Assert.True(decision.CreateBackup);
        Assert.Equal(@"D:\hot\bak\sub\a.jpg.bak", decision.BackupPath);
        Assert.NotEqual(decision.OutputPath, decision.BackupPath, ignoreCase: true);
    }
}
```

- [ ] **Step 2: Run tests (expect FAIL)**

```bash
dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter "FullyQualifiedName~AppConfigConcurrencyOptionsTests|FullyQualifiedName~RuleEngineTests"
```

- [ ] **Step 3: Implement minimal code (GREEN)**

Update `BackupOptions` signature:
```csharp
public sealed record BackupOptions(bool Enabled, string Directory, string Suffix, string Retention);
```

Set defaults in `AppConfig.Default`:
```csharp
Backup = new BackupOptions(
    Enabled: true,
    Directory: string.Empty,
    Suffix: ".bak",
    Retention: "keep"),
```

Update loader/json DTOs to include `backup.directory`.

Update `config.sample.json`:
```json
"backup": {
  "enabled": true,
  "directory": "",
  "suffix": ".bak",
  "retention": "keep"
}
```

In `RuleEngine.Decide`:
```csharp
var backupDir = string.IsNullOrWhiteSpace(_config.Backup.Directory)
    ? Path.Combine(_config.Watch.HotFolder, "bak")
    : _config.Backup.Directory;
var relative = Path.GetRelativePath(_config.Watch.HotFolder, sourcePath);
var backupPath = createBackup
    ? Path.Combine(backupDir, relative) + _config.Backup.Suffix
    : null;

if (createBackup && string.Equals(outputPath, backupPath, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("backup path must differ from output path");
}
```

- [ ] **Step 4: Run tests (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Core/Configuration/AppConfig.cs src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs config/config.sample.json tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs tests/PhotoPrivacy.Core.Tests/Rules/RuleEngineTests.cs src/PhotoPrivacy.Core/Rules/RuleEngine.cs
git commit -m "feat(config): add backup directory and rule-based backup path"
```

---

### Task 4: Pipeline 备份顺序与目录创建

**Files:**
- Modify: `src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs`
- Modify: `tests/PhotoPrivacy.Core.Tests/Pipeline/FileTaskPipelineTests.cs`

- [ ] **Step 1: Write failing test (RED)**

Add to `FileTaskPipelineTests.cs`:
```csharp
[Fact]
public async Task HandleAsync_Should_Copy_Backup_Before_Wipe()
{
    var cfg = AppConfig.Default with
    {
        Backup = AppConfig.Default.Backup with { Enabled = true, Directory = @"D:\hot\bak" },
        Watch = AppConfig.Default.Watch with { HotFolder = @"D:\hot" },
        Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
    };

    var ruleEngine = new RuleEngine(cfg);
    var bridge = new CaptureTargetBridge();
    var fileOps = new InMemoryFileOperations();
    var audit = new InMemoryAuditLogger();
    var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit);

    await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

    Assert.Contains(fileOps.Copies, c => c.Destination == @"D:\hot\bak\a.jpg.bak");
    Assert.Equal(@"D:\hot\a.jpg", bridge.LastTargetPath);
}
```

- [ ] **Step 2: Run test (expect FAIL)**

```bash
dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter "FullyQualifiedName~HandleAsync_Should_Copy_Backup_Before_Wipe"
```

- [ ] **Step 3: Implement minimal code (GREEN)**

In `FileTaskPipeline.HandleAsync`:
```csharp
if (!File.Exists(sourcePath))
{
    await _audit.WriteAsync(new AuditEvent("file_skipped", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, "source_missing", null), cancellationToken);
    return;
}

if (decision.CreateBackup && decision.BackupPath is not null)
{
    var backupDir = Path.GetDirectoryName(decision.BackupPath);
    if (!string.IsNullOrWhiteSpace(backupDir))
    {
        _fileOperations.EnsureDirectory(backupDir);
    }
    _fileOperations.Copy(sourcePath, decision.BackupPath, overwrite: true);
}
```

- [ ] **Step 4: Run test (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs tests/PhotoPrivacy.Core.Tests/Pipeline/FileTaskPipelineTests.cs
git commit -m "fix(pipeline): backup original file before wipe"
```

---

### Task 5: 配置命令与 UI ViewModel 扩展

**Files:**
- Modify: `src/PhotoPrivacy.Ui/ConfigEditCommand.cs`
- Modify: `src/PhotoPrivacy.Ui/ConfigEditor.cs`
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write failing tests (RED)**

Add to `ConfigEditorRoundTripTests.cs`:
```csharp
Assert.Equal(command.BackupDirectory, reloaded.Backup.Directory);
Assert.Equal(command.AuditLogDirectory, reloaded.Audit.LogDirectory);
Assert.Equal(command.LogLevel, reloaded.Audit.DiagnosticMode ? "debug" : "info");
```

Add to `MainWindowViewModelTests.cs`:
```csharp
vm.BackupDirectory = @"D:\hot\bak";
vm.AuditLogDirectory = @"D:\hot\_audit";
vm.LogLevel = "debug";
Assert.Contains(nameof(MainWindowViewModel.BackupDirectory), raised);
Assert.Contains(nameof(MainWindowViewModel.AuditLogDirectory), raised);
Assert.Contains(nameof(MainWindowViewModel.LogLevel), raised);
```

- [ ] **Step 2: Run tests (expect FAIL)**

- [ ] **Step 3: Implement minimal code (GREEN)**

Extend `ConfigEditCommand` with:
```csharp
string BackupDirectory,
string AuditLogDirectory,
string LogLevel
```

Update `ConfigEditor.UpdateConfig`:
```csharp
var resolvedBackupDirectory = string.IsNullOrWhiteSpace(command.BackupDirectory)
    ? Path.Combine(command.HotFolderPath, "bak")
    : command.BackupDirectory;

var diagnosticMode = string.Equals(command.LogLevel, "debug", StringComparison.OrdinalIgnoreCase);

Backup = config.Backup with
{
    Enabled = command.BackupEnabled,
    Directory = resolvedBackupDirectory
},
Audit = config.Audit with
{
    DiagnosticMode = diagnosticMode,
    LogDirectory = command.AuditLogDirectory
},
```

Add to `MainWindowViewModel`:
```csharp
private string _backupDirectory = string.Empty;
private string _auditLogDirectory = string.Empty;
private string _logLevel = "info";

public string BackupDirectory { get => _backupDirectory; set => SetField(ref _backupDirectory, value); }
public string AuditLogDirectory { get => _auditLogDirectory; set => SetField(ref _auditLogDirectory, value); }
public string LogLevel { get => _logLevel; set => SetField(ref _logLevel, value); }
```

- [ ] **Step 4: Run tests (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/ConfigEditCommand.cs src/PhotoPrivacy.Ui/ConfigEditor.cs src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs
git commit -m "feat(ui-config): add backup and audit log fields"
```

---

### Task 6: UI 配置页结构与路径浏览

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs`
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/FolderPickerIntegrationTests.cs`

- [ ] **Step 1: Write failing tests (RED)**

Add to `MainWindowSourceDiagnosticTests.cs`:
```csharp
Assert.DoesNotContain("SaveConfigButton", source, StringComparison.Ordinal);
Assert.Contains("ApplyConfigButton", source, StringComparison.Ordinal);
Assert.Contains("日志设置", source, StringComparison.Ordinal);
Assert.Contains("Browse", source, StringComparison.OrdinalIgnoreCase);
```

Create `FolderPickerIntegrationTests.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class FolderPickerIntegrationTests
{
    [Fact]
    public async Task FolderPicker_ShouldUpdateViewModel_WhenFolderSelected()
    {
        var vm = new PhotoPrivacy.Ui.ViewModels.MainWindowViewModel();
        var window = new PhotoPrivacy.Ui.Views.MainWindow();
        window.DataContext = vm;
        window.TestStorageProvider = new FakeStorageProvider(@"D:\hot");

        await window.TestPickHotFolderAsync();

        Assert.Equal(@"D:\hot", vm.HotFolderPath);
    }
}

internal sealed class FakeStorageProvider : IStorageProvider
{
    private readonly string _folderPath;
    public FakeStorageProvider(string folderPath) => _folderPath = folderPath;
    public bool CanOpen => true;
    public bool CanSave => false;
    public Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
        => Task.FromResult<IReadOnlyList<IStorageFolder>>([new FakeStorageFolder(_folderPath)]);
    public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        => Task.FromResult<IReadOnlyList<IStorageFile>>([]);
    public Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options) => Task.FromResult<IStorageFile?>(null);
    public Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder) => Task.FromResult<IStorageFolder?>(null);
}

internal sealed class FakeStorageFolder : IStorageFolder
{
    public FakeStorageFolder(string path) => Path = new Uri(path);
    public Uri Path { get; }
    public string Name => System.IO.Path.GetFileName(Path.LocalPath);
    public Task DeleteAsync() => Task.CompletedTask;
    public Task<IStorageItem?> MoveAsync(Uri destination) => Task.FromResult<IStorageItem?>(null);
    public Task<IStorageFolder?> CreateFolderAsync(string name) => Task.FromResult<IStorageFolder?>(null);
    public Task<IStorageFile?> CreateFileAsync(string name) => Task.FromResult<IStorageFile?>(null);
    public IAsyncEnumerable<IStorageItem> GetItemsAsync() => AsyncEnumerable.Empty<IStorageItem>();
    public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());
}
```

- [ ] **Step 2: Run tests (expect FAIL)**

- [ ] **Step 3: Implement minimal code (GREEN)**

Update `MainWindow.axaml`:
- 删除 `SaveConfigButton`，只保留 `ApplyConfigButton`。
- 路径行右侧增加浏览按钮（ghost，宽 36，Content="浏览"）。
- 增加“日志设置”卡片（LogLevel、AuditLogDirectory）。
- 备份目录行仅在 `BackupEnabled` 时显示。
- 主题与日志级别 ComboBox 使用 `SelectedValue` + `SelectedValuePath="Tag"`。

Update `MainWindow.axaml.cs`:
- 增加 `internal IStorageProvider? TestStorageProvider { get; set; }`。
- 增加 `internal Task TestPickHotFolderAsync()` 用于测试调用。
- 实现各 Browse 按钮点击，使用 `StorageProvider`。

- [ ] **Step 4: Run tests (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Views/MainWindow.axaml src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs tests/PhotoPrivacy.IntegrationTests/Ui/FolderPickerIntegrationTests.cs
git commit -m "feat(ui): add apply-only config and path pickers"
```

---

### Task 7: IPC ReloadConfig 贯通

**Files:**
- Modify: `src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs`
- Modify: `src/PhotoPrivacy.Ui/WorkerIpcClient.cs`
- Modify: `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs`
- Modify: `src/PhotoPrivacy.Worker/WorkerRuntimeContext.cs`
- Modify: `src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs`

- [ ] **Step 1: Write failing test (RED)**

In `MainWindowConfigHotReloadSourceTests.cs`:
```csharp
Assert.Contains("ReloadConfig", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run test (expect FAIL)**

- [ ] **Step 3: Implement minimal code (GREEN)**

`WorkerIpcMethods`:
```csharp
public const string ReloadConfig = "ReloadConfig";
```

`WorkerIpcClient`:
```csharp
public Task<WorkerIpcResponse?> ReloadConfigAsync(string pipeName, CancellationToken token)
    => SendAsync(pipeName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), token);
```

`WorkerRuntimeContext`:
```csharp
public Task ReloadConfigAsync() => _worker.ReloadConfigAsync();
```

`WorkerIpcServerHostedService.HandleRequest`:
```csharp
case WorkerIpcMethods.ReloadConfig:
    _runtime.ReloadConfigAsync().GetAwaiter().GetResult();
    return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);
```

`MetadataCleanerWorker`:
```csharp
private readonly SemaphoreSlim _reloadGate = new(1,1);
private AppConfig _currentConfig = AppConfig.Default;

public async Task ReloadConfigAsync()
{
    await _reloadGate.WaitAsync();
    try
    {
        var config = LoadEffectiveConfig();
        AppConfigValidator.Validate(config);
        await ApplyConfigAsync(config, CancellationToken.None);
    }
    finally
    {
        _reloadGate.Release();
    }
}
```

Add `ApplyConfigAsync` method to rebuild audit, bridge, pipeline, watcher.

- [ ] **Step 4: Run test (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs src/PhotoPrivacy.Ui/WorkerIpcClient.cs src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs src/PhotoPrivacy.Worker/WorkerRuntimeContext.cs src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs
git commit -m "feat(ipc): add reload-config and worker hot reload"
```

---

### Task 8: ExifTool 自动检测 + 图标统一

**Files:**
- Modify: `src/PhotoPrivacy.Core/Constants/DefaultPaths.cs`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Modify: `src/PhotoPrivacy.Ui/App.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj`

- [ ] **Step 1: Write failing tests (RED)**

Add to `MainWindowSourceDiagnosticTests.cs`:
```csharp
Assert.Contains("已自动检测到", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run test (expect FAIL)**

- [ ] **Step 3: Implement minimal code (GREEN)**

`DefaultPaths`:
```csharp
public static string ExifToolPath => ResolveExifToolPath();
private static string ResolveExifToolPath()
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool.exe"),
        Path.Combine(AppContext.BaseDirectory, "exiftool.exe"),
        @"C:\Program Files\ExifTool\exiftool.exe",
        @"C:\Program Files (x86)\ExifTool\exiftool.exe",
        @"C:\Windows\exiftool.exe"
    };
    foreach (var path in candidates)
    {
        if (File.Exists(path)) return path;
    }
    return @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe";
}
```

`MainWindow.axaml.cs`:
```csharp
private static readonly string[] WellKnownExifToolPaths = [...];
private string _exifToolHint = string.Empty;
```
Set hint when auto-detected and bind to `MainWindowViewModel.ExifToolPathHint`.

`MainWindow.axaml`:
Add hint TextBlock under ExifTool path row:
```xml
<TextBlock Text="{Binding ExifToolPathHint}" FontSize="11" Foreground="{DynamicResource TextTertiaryBrush}"/>
```

`App.axaml.cs`:
```csharp
var iconBytes = Convert.FromBase64String(File.ReadAllText(iconPath).Trim());
desktop.MainWindow.Icon = new WindowIcon(new MemoryStream(iconBytes));
```

`PhotoPrivacy.Ui.csproj`:
```xml
<ApplicationIcon>Assets\tray-dot-16.png.base64</ApplicationIcon>
```

- [ ] **Step 4: Run tests (expect PASS)**

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Core/Constants/DefaultPaths.cs src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs src/PhotoPrivacy.Ui/Views/MainWindow.axaml src/PhotoPrivacy.Ui/App.axaml.cs src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs
git commit -m "feat(ui): add exiftool auto-detect and unified icon"
```

---

### Task 9: 全量回归

- [ ] **Step 1: Run targeted tests**
```bash
dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter "FullyQualifiedName~AppConfig|FullyQualifiedName~RuleEngine|FullyQualifiedName~FileTaskPipeline"
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~Ui"
```

- [ ] **Step 2: Run full suite**
```bash
dotnet test PhotoPrivacy.sln
```

- [ ] **Step 3: Commit docs (if needed)**
```bash
git add README.md
git commit -m "docs(ui): document apply-config and backup defaults"
```
