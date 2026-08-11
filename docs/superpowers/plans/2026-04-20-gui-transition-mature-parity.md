# GUI 过渡版成熟交互收口 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保持三进程架构不回退的前提下，恢复成熟 GUI 核心交互（1:1 优先），清理用户可见开发痕迹，达到可落地交付标准。

**Architecture:** 以历史成熟 Avalonia GUI 实现为行为基线，优先复用现有 UI 代码，只在边界层适配 Worker IPC 与服务路径约束。保持 `PhotoPrivacy.exe`（GUI）与 `PhotoPrivacyWorker.exe`（Background/Service）解耦，不重做视觉系统，只做过渡收口。

**Tech Stack:** .NET 10, Avalonia 11, xUnit, PowerShell scripts, Worker IPC named pipes.

---

## File structure mapping

- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`（恢复成熟控件结构，收敛过渡噪音）
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`（复用成熟交互逻辑，保留新架构适配）
- Modify: `src/PhotoPrivacy.Ui/Program.cs`（发布态去开发痕迹）
- Modify: `src/PhotoPrivacy.Ui/TrayHost.cs`（托盘文案与行为一致性）
- Modify: `src/PhotoPrivacy.Ui/BackgroundUiOptions.cs`（移除过渡冗余字段）
- Modify: `src/PhotoPrivacy.Ui/ServiceManager.cs`（保持 Worker 路径硬约束）
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`（状态文案与成熟行为对齐）
- Modify: `README.md`（用户交付视角，弱化开发态内容）
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/*.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessScriptValidationTests.cs`

---

### Task 1: MainWindow 交互壳恢复到成熟基线

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/ModeLabelMappingTests.cs`

- [ ] **Step 1: 写失败测试，锁定成熟模式标签输出（RED）**

在 `tests/PhotoPrivacy.IntegrationTests/Ui/ModeLabelMappingTests.cs` 增加（或调整）用例，断言：

```csharp
[Theory]
[InlineData("tray", "🟢 托盘模式")]
[InlineData("service", "🔵 服务模式")]
[InlineData("unknown", "unknown")]
public void MapModeLabel_Should_Follow_Mature_Labels(string raw, string expected)
{
    var actual = MainWindowModeLabel.Map(raw);
    Assert.Equal(expected, actual);
}
```

> 若当前项目无 `MainWindowModeLabel`，先按现有静态方法封装计划到 Task 1 Step 3。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ModeLabelMappingTests"
```

Expected: FAIL（缺少封装或标签不匹配）。

- [ ] **Step 3: 最小实现通过测试并恢复成熟交互骨架**

实现要点：

1. `MainWindow.axaml`：以成熟版控件组织为准（状态条 + 工具条 + 配置/日志/服务 Tab），避免过渡期多余入口；
2. `MainWindow.axaml.cs`：
   - 保留成熟行为：暂停/恢复、清空日志、打开配置目录、服务管理动作；
   - 保留新架构能力：`ConnectOrLaunchWorkerAsync` 与 IPC 状态拉取；
   - 状态文案统一用户导向（例如 `Worker 运行中` / `服务运行中`）；
3. 为 `MapModeLabel` 抽出可测方法（可新建小型 helper 文件）。

- [ ] **Step 4: 运行 UI 相关测试**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~Ui"
```

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Views/MainWindow.axaml src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs tests/PhotoPrivacy.IntegrationTests/Ui
git commit -m "refactor(ui): restore mature main window interaction shell"
```

### Task 2: 托盘行为与运行时状态文案收口

**Files:**
- Modify: `src/PhotoPrivacy.Ui/TrayHost.cs`
- Modify: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/TrayPolicyTests.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/MainWindowViewModelTests.cs`

- [ ] **Step 1: 写失败测试，锁定托盘菜单文案与暂停切换（RED）**

在托盘/VM 测试中断言：

- 暂停状态下菜单为“恢复”，运行状态下为“暂停”；
- 默认运行状态文本符合成熟过渡口径（非技术术语泄露）。

- [ ] **Step 2: 运行目标测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~TrayPolicyTests|FullyQualifiedName~MainWindowViewModelTests"
```

Expected: FAIL（文案或行为不一致）。

- [ ] **Step 3: 最小实现**

1. `TrayHost.cs`：
   - 保持“暂停/恢复、打开主窗口、退出”三项稳定菜单；
   - 点击托盘恢复主窗口行为不变；
2. `MainWindowViewModel.cs`：
   - 默认状态文案与成熟版一致；
   - 不暴露内部实现术语。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/TrayHost.cs src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs tests/PhotoPrivacy.IntegrationTests/Ui
git commit -m "refactor(ui): align tray behavior and runtime wording for transition release"
```

### Task 3: 去除发布态开发痕迹（UI Program）

**Files:**
- Modify: `src/PhotoPrivacy.Ui/Program.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/UiAvailabilityTests.cs`

- [ ] **Step 1: 写失败测试，约束发布态不启用开发 trace（RED）**

增加测试（可基于字符串扫描 Program 源码）断言：

- Debug 构建可保留 trace；
- Release 路径不默认输出开发调试 trace。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~UiAvailabilityTests"
```

Expected: FAIL（当前仍是全局 trace）。

- [ ] **Step 3: 最小实现**

将 `BuildAvaloniaApp()` 改为条件 trace：

```csharp
public static AppBuilder BuildAvaloniaApp()
{
    var builder = AppBuilder.Configure<App>().UsePlatformDetect();
#if DEBUG
    builder = builder.LogToTrace();
#endif
    return builder;
}
```

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/Program.cs tests/PhotoPrivacy.IntegrationTests/Ui/UiAvailabilityTests.cs
git commit -m "chore(ui): disable dev trace in release runtime"
```

### Task 4: 清理过渡冗余字段并保持 Worker 服务路径硬约束

**Files:**
- Modify: `src/PhotoPrivacy.Ui/BackgroundUiOptions.cs`
- Modify: `src/PhotoPrivacy.Ui/Program.cs`
- Modify: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `src/PhotoPrivacy.Ui/ServiceManager.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/Ui/ServiceManagerTests.cs`

- [ ] **Step 1: 写失败测试，防止使用 GUI 可执行路径作为服务目标（RED）**

在 `ServiceManagerTests` 增加断言：

- 传入 `PhotoPrivacy.exe` 时 `Install/Start` 必须失败；
- 错误消息包含 `PhotoPrivacyWorker.exe`。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ServiceManagerTests"
```

Expected: FAIL。

- [ ] **Step 3: 最小实现并清理冗余字段**

1. `BackgroundUiOptions` 去掉 `SelfExecutablePath`（若仅为过渡残留）；
2. `Program.cs` 与 `MainWindow.axaml.cs` 改为统一使用 `WorkerExecutablePath`；
3. `ServiceManager.cs` 保持并强化 worker-only 校验。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Ui/BackgroundUiOptions.cs src/PhotoPrivacy.Ui/Program.cs src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs src/PhotoPrivacy.Ui/ServiceManager.cs tests/PhotoPrivacy.IntegrationTests/Ui/ServiceManagerTests.cs
git commit -m "fix(ui): enforce worker-only service target and remove transition residue"
```

### Task 5: README 用户交付收口（弱化开发态）

**Files:**
- Modify: `README.md`
- Test: `tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessScriptValidationTests.cs`

- [ ] **Step 1: 写失败测试（RED）**

在脚本/文档验证测试中增加断言：

- README 不出现 `src/PhotoPrivacy.Cli`、`PhotoPrivacy.Cli` 旧入口；
- 用户主路径命令优先围绕 `PhotoPrivacy.exe` 与 `PhotoPrivacyWorker.exe`。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter "FullyQualifiedName~ReleaseReadinessScriptValidationTests"
```

Expected: FAIL（若仍有开发态描述）。

- [ ] **Step 3: 最小实现**

`README.md` 收口策略：

1. 前半段仅保留用户视角：安装、启动、服务安装、故障验证；
2. 调试命令保留但后置到“开发者附录”；
3. 主发布入口统一 `scripts/publish-app.ps1`，旧入口仅兼容提示。

- [ ] **Step 4: 运行测试确认通过**

Run 同 Step 2。

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add README.md tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessScriptValidationTests.cs
git commit -m "docs: polish README for end-user transition release"
```

### Task 6: 全量回归与交付验收

**Files:**
- Modify: `docs/release-roadmap-2026-04-17.md`（记录过渡收口完成状态）

- [ ] **Step 1: 运行全量测试**

Run:

```bash
dotnet test PhotoPrivacy.sln
```

Expected: PASS。

- [ ] **Step 2: 运行发布门禁**

Run:

```bash
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64
```

Expected: PASS。

- [ ] **Step 3: 运行强杀清理验收**

Run:

```bash
powershell -ExecutionPolicy Bypass -File scripts/verify-force-kill-cleanup.ps1 -Version 0.1.0-preview
```

Expected: PASS（`CHILD_STILL_ALIVE=False`）。

- [ ] **Step 4: 更新路线图状态说明**

在 `docs/release-roadmap-2026-04-17.md` 增加一段：

- GUI 过渡收口已完成；
- 当前为成熟过渡版；
- 全新 GUI 重构列入下一阶段。

- [ ] **Step 5: Commit**

```bash
git add docs/release-roadmap-2026-04-17.md
git commit -m "docs: mark transition GUI maturity milestone complete"
```
