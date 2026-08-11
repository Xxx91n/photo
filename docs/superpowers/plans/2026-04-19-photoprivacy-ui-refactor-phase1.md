# PhotoPrivacy UI Refactor Phase-1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 引入 Avalonia UI 项目并完成第一阶段运行骨架，让 Windows/Linux 都可通过同一 CLI 路由进入新的 UI 后台模式，为后续托盘与服务管理器重构打底。

**Architecture:** 维持现有三层结构（Core/CLI/UI）：Core 保持纯业务，CLI 负责模式路由与进程治理，UI 承载 Avalonia 窗口与托盘交互。Phase-1 不做完整 UI 功能替换，只先搭建可编译、可运行、可测试的项目骨架，并把文档与发布流程对齐。

**Tech Stack:** .NET 10, Avalonia 11 (Desktop + FluentTheme), xUnit, PowerShell.

---

## File structure mapping

- Create: `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj`（Avalonia 项目，`net10.0`）
- Create: `src/PhotoPrivacy.Ui/App.axaml`
- Create: `src/PhotoPrivacy.Ui/App.axaml.cs`
- Create: `src/PhotoPrivacy.Ui/Program.cs`
- Create: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Create: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Create: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `PhotoPrivacy.sln`（加入 `PhotoPrivacy.Ui`）
- Modify: `src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj`（引用 UI 项目，准备 background 路由替换）
- Modify: `src/PhotoPrivacy.Cli/Program.cs`（`--mode background` 进入 Avalonia 后台入口）
- Modify: `src/PhotoPrivacy.Cli/TrayApplicationContext.cs`（标记废弃）
- Modify: `tests/PhotoPrivacy.IntegrationTests/*.cs`（补 background 路由行为测试）
- Modify: `README.md`（补 UI 重构阶段说明与运行方式）

---

### Task 1: Release Gate Integration

**Files:**
- Modify: `scripts/release-readiness.ps1`
- Test: `scripts/verify-force-kill-cleanup.ps1`

- [ ] **Step 1: Add failing gate invocation test scenario (manual script-level RED)**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64 -IncludeForceKillCheck true
```

Expected (RED before implementation): no `[4/4] force-kill cleanup gate` stage.

- [ ] **Step 2: Implement optional stage-4 force-kill gate**

Add parsing of `-IncludeForceKillCheck` and invoke `scripts/verify-force-kill-cleanup.ps1` only when enabled and runtime is `win-*`.

- [ ] **Step 3: Verify gate works**

Run same command and expect stage-4 output + PASS.

- [ ] **Step 4: Commit**

```bash
git add scripts/release-readiness.ps1
git commit -m "ci: add optional force-kill cleanup gate to release readiness"
```

### Task 2: Documentation Consolidation

**Files:**
- Modify: `README.md`
- Modify: `docs/release-roadmap-2026-04-17.md`
- Delete: `release-roadmap-2026-04-17.md` (duplicate at repo root)

- [ ] **Step 1: Write failing doc-check (manual RED)**

Confirm there are duplicated roadmap docs and README lacks unified win/linux commands + release gate usage.

- [ ] **Step 2: Update README build/release section**

Include:
- Win publish command (`-Framework net10.0-windows`)
- Linux publish command (`-Framework net10.0`)
- release-readiness with `-IncludeForceKillCheck true`
- standalone force-kill verification command

- [ ] **Step 3: Normalize roadmap doc and remove duplicate**

Keep canonical file under `docs/` and remove root duplicate.

- [ ] **Step 4: Commit**

```bash
git add README.md docs/release-roadmap-2026-04-17.md release-roadmap-2026-04-17.md
git commit -m "docs: consolidate release instructions and roadmap location"
```

### Task 3: Add Avalonia UI Project Skeleton

**Files:**
- Create: `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj`
- Create: `src/PhotoPrivacy.Ui/App.axaml`
- Create: `src/PhotoPrivacy.Ui/App.axaml.cs`
- Create: `src/PhotoPrivacy.Ui/Program.cs`
- Create: `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- Create: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- Create: `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- Modify: `PhotoPrivacy.sln`

- [ ] **Step 1: Write failing solution compile check (RED)**

Run:

```bash
dotnet build PhotoPrivacy.sln
```

Expected: missing `PhotoPrivacy.Ui` project and references.

- [ ] **Step 2: Create Avalonia project with minimum runnable shell**

`PhotoPrivacy.Ui.csproj` should include:

```xml
<TargetFramework>net10.0</TargetFramework>
<PackageReference Include="Avalonia" Version="11.*" />
<PackageReference Include="Avalonia.Desktop" Version="11.*" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
<ProjectReference Include="..\PhotoPrivacy.Core\PhotoPrivacy.Core.csproj" />
```

And minimal `MainWindow` + `App` bootstrap.

- [ ] **Step 3: Add project into solution and verify build**

Run:

```bash
dotnet build PhotoPrivacy.sln
```

Expected: build success.

- [ ] **Step 4: Commit**

```bash
git add PhotoPrivacy.sln src/PhotoPrivacy.Ui
git commit -m "feat(ui): add Avalonia UI project skeleton"
```

### Task 4: CLI Route Preparation for Avalonia Background Mode

**Files:**
- Modify: `src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj`
- Modify: `src/PhotoPrivacy.Cli/Program.cs`
- Modify: `src/PhotoPrivacy.Cli/TrayApplicationContext.cs`
- Test: `tests/PhotoPrivacy.IntegrationTests/RuntimeModeResolverTests.cs`

- [ ] **Step 1: Add failing route test (RED)**

Add/adjust integration test that asserts background mode route no longer depends on WinForms-only tray context.

- [ ] **Step 2: Wire CLI to invoke Avalonia host for background mode**

Keep existing behavior as fallback during migration, but make main path call UI entrypoint.

- [ ] **Step 3: Mark WinForms tray context as deprecated**

Add `[Obsolete]` and comments indicating temporary compatibility only.

- [ ] **Step 4: Verify tests**

```bash
dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/PhotoPrivacy.Cli tests/PhotoPrivacy.IntegrationTests
git commit -m "refactor(cli): prepare background mode routing for Avalonia"
```

### Task 5: Phase-1 Verification + Next-phase Backlog Freeze

**Files:**
- Modify: `README.md`
- Modify: `docs/release-roadmap-2026-04-17.md`

- [ ] **Step 1: Run full verification**

```bash
dotnet test PhotoPrivacy.sln
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64 -IncludeForceKillCheck true
```

- [ ] **Step 2: Record phase status in docs**

Update roadmap with:
- done: force-kill gate + job object cleanup
- in-progress: Avalonia migration
- next: service manager tab + Linux background tray parity

- [ ] **Step 3: Commit**

```bash
git add README.md docs/release-roadmap-2026-04-17.md
git commit -m "docs: record phase-1 status and next-stage GUI backlog"
```

---

## Phase-2 preview backlog (not in this implementation batch)

1. Avalonia TrayIcon full replacement for WinForms tray
2. Windows Service Manager tab (Install/Uninstall/Start/Stop + UAC elevate)
3. Audit tailing log panel with live append
4. Linux background mode support through Avalonia tray (no service manager tab)
5. CLI csproj convergence to single `net10.0` once WinForms dependency fully removed
