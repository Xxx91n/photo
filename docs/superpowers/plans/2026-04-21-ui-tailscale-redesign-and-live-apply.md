# PhotoPrivacy GUI Tailscale-Style Redesign and Live-Apply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 一次性完成产品化 GUI 重构（Tailscale 风格）、统一配置生效边界、显式“应用配置”机制、主题自由切换（含黑暗模式），并保持三进程架构稳定。

**Architecture:** 保留现有 `PhotoPrivacy.exe` + `PhotoPrivacyWorker.exe`（background/service）架构与 IPC 通道，重构 UI 为“左导航 + 右内容页”的单窗口壳。配置采用“保存写盘 + 显式应用”双阶段：保存不改进程，应用触发当前模式的重连/重启流程并验证回执。主题系统采用 Avalonia ThemeVariant + ResourceDictionary，覆盖亮/暗双主题。

**Tech Stack:** .NET 10, Avalonia 11, xUnit, PowerShell, Worker IPC Named Pipes.

---

## File structure mapping

- Create: `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml`（亮/暗主题资源、颜色变量、按钮与页面样式）
- Modify: `src/PhotoPrivacy.Ui/App.axaml`（引入主题资源字典）
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs`（新增 UI 主题配置字段）
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`（加载新 UI 字段）
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs`（序列化新 UI 字段）
- Modify: `src/PhotoPrivacy.Ui/ConfigEditCommand.cs`（新增主题字段）
- Modify: `src/PhotoPrivacy.Ui/ConfigEditor.cs`（持久化主题字段）
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`（导航/状态点/主题与应用状态绑定）
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`（重建为左导航 + 三页内容）
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`（页面导航、保存/应用配置、主题切换、即时 UI 刷新）
- Modify: `src/PhotoPrivacy.Ui/Program.cs`（二次启动激活窗口稳定性）
- Modify: `src/PhotoPrivacy.Ui/WorkerProcessManager.cs`（background 启动始终携带 configPath）
- Modify: `config/config.sample.json`（新增 ui.theme_variant）
- Modify: `config/config.json`（新增 ui.theme_variant）
- Test: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs`（扩展断言新 UI 配置字段）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs`（新增 VM 属性与通知测试）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs`（新增主题字段 round-trip）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs`（保存+应用配置调用链）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowServiceSwitchSourceTests.cs`（服务启动前托盘清场保护逻辑）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/UiProgramSourceSingleInstanceTests.cs`（二次启动激活窗口稳定性）
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantSourceTests.cs`（主题切换与 ResourceDictionary 接入）

---

### Task 1: 配置模型扩展（主题字段）

**Files:**
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`
- Modify: `src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs`
- Modify: `config/config.sample.json`
- Modify: `config/config.json`
- Modify: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs`

- [ ] **Step 1: 写失败测试（RED）— 新增 UI 主题字段序列化与加载断言**

在 `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs` 增加：

```csharp
[Fact]
public void Load_Should_Map_Ui_ThemeVariant_Field()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, """
        {
          "schema_version": 1,
          "exiftool": {
            "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
            "enable_windows_long_path": true,
            "enable_large_file_support": true,
            "dry_run": false,
            "extra_exiftool_args": [],
            "stay_open_pool_size": 1,
            "max_parallel_drain": 1
          },
          "ui": {
            "hide_main_window_on_startup": false,
            "hide_tray_icon": false,
            "theme_variant": "dark"
          }
        }
        """);

        var cfg = AppConfigLoader.Load(configPath);
        Assert.Equal("dark", cfg.Ui.ThemeVariant);
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}

[Fact]
public void ToIndentedJson_Should_Emit_Ui_ThemeVariant_Field()
{
    var cfg = AppConfig.Default with
    {
        Ui = AppConfig.Default.Ui with { ThemeVariant = "light" }
    };

    var json = AppConfigJson.ToIndentedJson(cfg);
    Assert.Contains("\"theme_variant\": \"light\"", json, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter "FullyQualifiedName~AppConfigConcurrencyOptionsTests"
```

Expected: FAIL（`ThemeVariant` 字段尚未存在）。

- [ ] **Step 3: 最小实现通过测试**

实现：

1. `UiOptions` 增加 `string ThemeVariant`（默认 `system`）；
2. Loader DTO 增加 `theme_variant`；
3. Json DTO 输出 `theme_variant`；
4. `config.sample.json` 与 `config.json` 增加 `ui.theme_variant`。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Core/Configuration/AppConfig.cs src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs config/config.sample.json config/config.json tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigConcurrencyOptionsTests.cs
git commit -m "feat(config): add ui theme variant schema and serialization"
```

### Task 2: 配置编辑与持久化支持主题字段

**Files:**
- Modify: `src/PhotoPrivacy.Ui/ConfigEditCommand.cs`
- Modify: `src/PhotoPrivacy.Ui/ConfigEditor.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs`

- [ ] **Step 1: 写失败测试（RED）— round-trip 断言主题字段写回**

在 `tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs` 增加：

```csharp
Assert.Equal("dark", reloaded.Ui.ThemeVariant);
Assert.True(uiEl.TryGetProperty("theme_variant", out _));
```

并在 `ConfigEditCommand` 构造中传入 `ThemeVariant: "dark"`。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ConfigEditorRoundTripTests"
```

Expected: FAIL（命令与写入逻辑尚未支持）。

- [ ] **Step 3: 最小实现通过测试**

实现：

1. `ConfigEditCommand` 增加 `ThemeVariant`；
2. `ConfigEditor.UpdateConfig` 写入 `Ui.ThemeVariant`。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/ConfigEditCommand.cs src/PhotoPrivacy.Ui/ConfigEditor.cs tests/PhotoPrivacy.IntegrationTests/Ui/ConfigEditorRoundTripTests.cs
git commit -m "feat(ui-config): persist theme variant through config editor"
```

### Task 3: 主题资源与样式系统（亮/暗）

**Files:**
- Create: `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml`
- Modify: `src/PhotoPrivacy.Ui/App.axaml`
- Create: `tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantSourceTests.cs`

- [ ] **Step 1: 写失败测试（RED）— 主题资源和 App 引用存在性**

新建 `tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantSourceTests.cs`：

```csharp
using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ThemeVariantSourceTests
{
    [Fact]
    public void App_Source_Should_Include_AppTheme_ResourceDictionary()
    {
        var appPath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml");
        var source = File.ReadAllText(appPath, Encoding.UTF8);
        Assert.Contains("Styling/AppTheme.axaml", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_Source_Should_Define_Light_And_Dark_Color_Tokens()
    {
        var themePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Styling", "AppTheme.axaml");
        var source = File.ReadAllText(themePath, Encoding.UTF8);
        Assert.Contains("AccentBlue", source, StringComparison.Ordinal);
        Assert.Contains("BtnPrimaryBg", source, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant=\"Dark\"", source, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ThemeVariantSourceTests"
```

Expected: FAIL（文件/引用尚不存在）。

- [ ] **Step 3: 最小实现通过测试**

实现：

1. 新建 `Styling/AppTheme.axaml`：
   - 定义亮色 token（AppBg/SidebarBg/CardBorder/TextPrimary/AccentBlue/BtnPrimaryBg 等）；
   - 定义暗色 token（使用 `ThemeVariant="Dark"` 的样式覆盖）；
   - 定义 `Button.primary` / `Button.ghost` / `Button.danger` / `Button.nav` / `Border.settings-card` 等类样式。
2. `App.axaml` 将该资源字典加入 `<Application.Styles>`。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Styling/AppTheme.axaml src/PhotoPrivacy.Ui/App.axaml tests/PhotoPrivacy.IntegrationTests/Ui/ThemeVariantSourceTests.cs
git commit -m "feat(ui-theme): add light/dark tokenized theme resources"
```

### Task 4: ViewModel 扩展（导航、状态点、主题、应用状态）

**Files:**
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs`

- [ ] **Step 1: 写失败测试（RED）— 新属性通知与默认值**

在 `MainWindowViewModelTests.cs` 增加：

```csharp
[Fact]
public void NewUiProperties_Should_Raise_PropertyChanged()
{
    var vm = new MainWindowViewModel();
    var raised = new List<string>();
    vm.PropertyChanged += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
        {
            raised.Add(e.PropertyName!);
        }
    };

    vm.CurrentPage = "log";
    vm.ThemeVariant = "dark";
    vm.SaveStatus = "已保存，待应用";

    Assert.Contains(nameof(MainWindowViewModel.CurrentPage), raised);
    Assert.Contains(nameof(MainWindowViewModel.ThemeVariant), raised);
    Assert.Contains(nameof(MainWindowViewModel.SaveStatus), raised);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected: FAIL（属性尚未定义）。

- [ ] **Step 3: 最小实现通过测试**

实现新增属性：

1. `CurrentPage`（`config`/`log`/`service`）；
2. `ThemeVariant`（`system`/`light`/`dark`）；
3. `SaveStatus`；
4. 只读派生：`ModeColor`、`ModeLabel`、`StatusDotColor`、`PauseResumeLabel`、`ServiceStatusDotColor`。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs
git commit -m "feat(ui-vm): add navigation, theme, and status presentation properties"
```

### Task 5: MainWindow 一次性结构重建（Tailscale 风格）

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs`

- [ ] **Step 1: 写失败测试（RED）— 页面骨架断言**

在 `MainWindowSourceDiagnosticTests.cs` 增加：

```csharp
Assert.Contains("Width=\"920\"", source, StringComparison.Ordinal);
Assert.Contains("MinWidth=\"740\"", source, StringComparison.Ordinal);
Assert.Contains("CurrentPage", source, StringComparison.Ordinal);
Assert.DoesNotContain("<TabControl", source, StringComparison.Ordinal);
Assert.Contains("Classes=\"settings-card\"", source, StringComparison.Ordinal);
Assert.Contains("ToggleSwitch", source, StringComparison.Ordinal);
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~MainWindowSourceDiagnosticTests"
```

Expected: FAIL（当前仍为 TabControl 结构）。

- [ ] **Step 3: 最小实现通过测试并重构 UI**

重写 `MainWindow.axaml`：

1. 根布局改为左栏 200px + 右侧内容区；
2. 导航按钮切换 `CurrentPage`；
3. 配置页改 settings-card + settings-row；
4. 服务页改状态卡 + 操作卡；
5. 日志页改工具条 + 紧凑 list；
6. `SaveConfigButton` 与新 `ApplyConfigButton` 放置于配置页操作区；
7. 新增主题切换控件（ComboBox 或 segmented buttons）。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Views/MainWindow.axaml tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowSourceDiagnosticTests.cs
git commit -m "refactor(ui): rebuild main window with tailscale-style navigation shell"
```

### Task 6: MainWindow 交互层改造（导航、主题切换、保存/应用分离）

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowServiceSwitchSourceTests.cs`

- [ ] **Step 1: 写失败测试（RED）— 应用配置动作链与服务前置保护**

在 `MainWindowConfigHotReloadSourceTests.cs` 调整为：

```csharp
Assert.Contains("private async void OnApplyConfigClick", source, StringComparison.Ordinal);
Assert.Contains("vm.SaveStatus = \"已保存，待应用\"", source, StringComparison.Ordinal);
Assert.Contains("ApplyRuntimeConfigToUiState();", source, StringComparison.Ordinal);
Assert.Contains("ApplyConfigForCurrentModeAsync", source, StringComparison.Ordinal);
```

在 `MainWindowServiceSwitchSourceTests.cs` 保留并强化：

```csharp
Assert.Contains("if (requiresTrayShutdown)", source, StringComparison.Ordinal);
Assert.Contains("托盘 Worker 仍在运行，已取消服务启动，请稍后重试", source, StringComparison.Ordinal);
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~MainWindowConfigHotReloadSourceTests|FullyQualifiedName~MainWindowServiceSwitchSourceTests"
```

Expected: FAIL（尚无显式 ApplyConfig 入口与完整文案流程）。

- [ ] **Step 3: 最小实现通过测试**

实现 `MainWindow.axaml.cs`：

1. 导航点击设置 `vm.CurrentPage`；
2. 主题切换事件：
   - `ThemeVariant=system/light/dark` 写回 VM；
   - 立即设置 `Application.Current.RequestedThemeVariant`；
3. `OnSaveConfigClick` 仅写盘 + `vm.SaveStatus="已保存，待应用"`；
4. 新增 `OnApplyConfigClick`：
   - 读取最新 config -> `ApplyRuntimeConfigToUiState()`；
   - 根据当前模式执行 `ApplyConfigForCurrentModeAsync`：
     - tray: shutdown+relaunch background（同 configPath）
     - service: stop+reconfig+start
   - 成功后 `vm.SaveStatus="配置已应用"`，失败后写失败信息
5. 保持已有服务切换防呆。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowConfigHotReloadSourceTests.cs tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowServiceSwitchSourceTests.cs
git commit -m "feat(ui): add explicit apply-config flow with mode-aware live reload"
```

### Task 7: Program/WorkerManager 连通性补强（统一 config 与二次启动唤起）

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Program.cs`
- Modify: `src/PhotoPrivacy.Ui/WorkerProcessManager.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/UiProgramSourceSingleInstanceTests.cs`
- Modify: `tests/PhotoPrivacy.IntegrationTests/Ui/WorkerProcessManagerTests.cs`

- [ ] **Step 1: 写失败测试（RED）— background 启动参数包含 configPath**

在 `WorkerProcessManagerTests.cs` 增加：

```csharp
[Fact]
public void BuildBackgroundLaunchStartInfo_Should_Include_Config_Path_When_Provided()
{
    var psi = WorkerProcessManager.BuildBackgroundLaunchStartInfo(@"D:\app\PhotoPrivacyWorker.exe", @"D:\app\config\config.json");
    Assert.Contains("--mode background", psi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--config \"D:\\app\\config\\config.json\"", psi.Arguments, StringComparison.Ordinal);
}
```

在 `UiProgramSourceSingleInstanceTests.cs` 保持对二次启动 notify + 短等待的断言。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~WorkerProcessManagerTests|FullyQualifiedName~UiProgramSourceSingleInstanceTests"
```

Expected: FAIL（若签名/参数逻辑未完全对齐）。

- [ ] **Step 3: 最小实现通过测试**

实现：

1. `BuildBackgroundLaunchStartInfo` 始终允许携带 configPath；
2. `ConnectOrLaunchAsync` 传递 configPath 启动参数；
3. `Program` 在非 owner 路径继续 notify existing instance，并保留短等待确保前台激活稳定。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Program.cs src/PhotoPrivacy.Ui/WorkerProcessManager.cs tests/PhotoPrivacy.IntegrationTests/Ui/WorkerProcessManagerTests.cs tests/PhotoPrivacy.IntegrationTests/Ui/UiProgramSourceSingleInstanceTests.cs
git commit -m "fix(ui-runtime): enforce shared config path and stable second-launch activation"
```

### Task 8: 全量回归与验收

**Files:**
- Modify: `README.md`（若需补“保存后应用配置”和主题切换说明）

- [ ] **Step 1: 运行核心测试组**

Run:

```bash
dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter "FullyQualifiedName~AppConfig|FullyQualifiedName~ExifTool|FullyQualifiedName~Pipeline"
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~Ui"
```

Expected: PASS。

- [ ] **Step 2: 运行全量测试**

Run:

```bash
dotnet test PhotoPrivacy.sln
```

Expected: PASS。

- [ ] **Step 3: 手工冒烟（本地）**

操作：

1. 启动 UI，切换 Light/Dark/System；
2. 改配置点击“保存更改”，确认只显示“待应用”；
3. 点击“应用配置”，确认当前模式生效且状态刷新；
4. 重复双击 `PhotoPrivacy.exe`，确认现有窗口前置。

Expected: 全部符合。

- [ ] **Step 4: 文档补充（如有必要）**

在 README 增加：

- 主题切换入口；
- 保存/应用配置的行为区别；
- 模式共享同一 config 的说明。

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs(ui): document apply-config workflow and theme switching"
```
