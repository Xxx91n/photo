# 路线图：运行时架构大修（ADR 0053 实施计划）

> 本路线图固化 grill 确认的 6 大项 + 6 子项任务，按 commit 顺序排列。每项含验收标准 + 三平台 test 闭环 + 平台兼容性说明。
> 决策依据：ADR 0053 + atomcode 27 来源调研 + perplexity kimi_k26 补充。

## 启动顺序与验证逻辑

```
M1 启动修复 → 必须先落地
  └─ M2 MainWindow MVVM 重构 → 紧接，依赖 M1 的 async 入口
       └─ M3 BoundedChannel 主管线 → 独立可在 M2 后任意点插入
            └─ M6a 后端 WipeStrategyResolver → 可与 M3 并行（不同文件）
                 └─ M6b Expert gate + M6c DataGrid + M6d 搜索过滤 + M6e 保存/重置 + M6f 警告
                      └─ M6g 测试 → 全部收尾
```

## M1. 两阶段异步启动

**改动**: `src/PhotoPrivacy.Ui/Program.cs`（214 行→约 250 行）
- 移除 `ConnectOrLaunchAsync(...).GetAwaiter().GetResult()` 与其他 sync 调用
- 改为 `BuildAvaloniaApp().Start(AppMain, args)` Manual lifetime
- 新增 `static async Task AppMain(Application app, string[] args)`：`window.Show()` 立即首帧 → `_ = Task.Run(ConnectWorkerAsync)`
- `BackgroundUiOptions.ConnectionState` 加 `Connecting` 枚举值；UI 显示 i18n "Worker 连接中…"
- Worker 就绪后 `Dispatcher.UIThread.Post` 回填 `Connected` + 触发 `MainWindowRefreshStateAsync`

**验收标准**:
- 双击 `PhotoPrivacy.exe` 到首窗可见 ≤ 2 秒
- `grep -rn "GetAwaiter().GetResult()\|\.Result\|\.Wait()" src/PhotoPrivacy.Ui/Program.cs` 返回 0
- UI 首窗显示 "Worker 连接中…"，Worker 就绪后状态刷新

**test 闭环**:
- `StartupPhaseTimingTests` — `Stopwatch` 测 `Program.Main` 内 Avalonia 首帧到 `window.Show()` 时延（目标 <800ms）
- 强化 `CultureChanged_Handler_Does_Not_Contain_SyncOverAsync` lint（已存在）扫描 `Program.cs`
- 三平台 smoke：Linux WSL 启动 `./PhotoPrivacy` + macOS 启动 `./PhotoPrivacy`（CI 不跑，本地或 user 验证）

## M2. MainWindow 全面 MVVM 重构

**改动文件**:
- `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs` 2062 行 → 约 500 行
- 新增 `src/PhotoPrivacy.Ui/Startup/StartupCoordinator.cs`（约 400 行，承接 14 处 sync-over-async 后台 Task）
- `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs` 373 行扩展至约 700 行（PA/Resume/ClearLogs/ChangeMode 等全部命令化）
- 删除 `BackgroundUiOp...` 相关 `private` 字段引用（迁 `StartupCoordinator`）

**验收标准**:
- `wc -l src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs` ≤ 600
- `grep -c "private.*GetAwaiter().GetResult()\|\.Result\|\.Wait()" src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs` == 0
- 三种启动方式（启动器 / CLI / 服务模式）都能进入 MainWindow 初始化
- ViewModel 完整单元测试覆盖；Behavior diff 对齐 M0 基线

**test 闭环**:
- 新增 `MainWindowViewModelCommandsTests` — PauseResume / ClearLogs / ChangeMode 命令
- 旧 `MainWindowBehaviorTests` 迁到 ViewModel 测试
- `StartupCoordinatorTests` — Worker 连接失败/超时/重试路径
- 三平台 IPC transport 兼容：`IpcTransportFactory` 不改动

**冲突规避**：不触碰 IPC 修复分支在 `WorkerProcessManager.ConnectOrLaunchAsync:69` 的 IOException 处理；仅迁出 Main 里该路径的 sync 调用。

## M3. BoundedChannel + IAsyncEnumerable 文件处理主管线

**改动文件**:
- `src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs`（712 行 → 约 800 行）
- 新增 `src/PhotoPrivacy.Core/Pipeline/HotFolderChannel.cs`（约 100 行）

**改动语法**:
```csharp
private Channel<string>? _fileChannel;   // BoundedChannel(4096, FullMode=Wait)

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _fileChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.Wait
    });

    var watcher = StartFileWatcher(stoppingToken);   // FSW 事件 -> _fileChannel.Writer.WriteAsync

    await foreach (var path in _fileChannel.Reader.ReadAllAsync(stoppingToken))
    {
        await ProcessOneAsync(path, stoppingToken);
    }
}

private async Task OnFileCreated(string path, CancellationToken token)
{
    await _fileChannel!.Writer.WriteAsync(path, token);   // 满则异步等待
}
```

**验收标准**:
- 压 10000 文件 hot folder，进程内存稳定不超 baseline +20MB（监控 `Process.WorkingSet64`）
- FSW 后台线程不阻塞 Worker 主循环
- 取消时 `Channel.Writer.Complete()` 干净退出，不卡 pending WriteAsync

**test 闭环**:
- `BackpressureTests` — 模拟 10k 文件压测（dry run，不实启动 ExifTool）
- `ChannelFullModeWaitTests` — 写满后 producer 异步挂起、consumer drain 后恢复
- 三平台：Windows FSW 内部 buffer 64KB / Linux inotify / macOS FSEvents 全部分发后走同一 Channel 接口（不平台分支）

## M4. UI 虚拟化维持现状 — 不改

**改动**: 0 行
**理由**: `ListBox + ObservableCollection + 500 cap + AppendLogBatch` 已是企业级。Avalonia 11 的 `ItemsRepeater` 已 retired（PR #14989）。ItemsRepeater 是 10000+ 项场景的需求，本项目 500 上限远在阈值下，迁移反而失去 ListBox 内置选择/键盘导航。

## M5. NativeAOT/Trimming 维持现状 — 不改

**改动**: 0 行
**理由**: `PublishReadyToRun=true + PublishSingleFile=true + Workstation GC + CompiledBindings` 已是 ADR 0049 默认。Avalonia 12.1 NativeAOT 在 6 个依赖包未验证下盲开风险高。留作后续 per-RID 验证试点，不开主力发布线。

## M6. Per-Format 差异化清理规则 + 专业模式 DataGrid 面板

### M6a. 后端 `WipeStrategyResolver`

**改动**:
- 新增 `src/PhotoPrivacy.Core/ExifTool/WipeStrategyResolver.cs`（约 150 行）
- 新增 `src/PhotoPrivacy.Core/ExifTool/IWipeStrategy.cs`（接口约 20 行）
- 新增 `src/PhotoPrivacy.Core/ExifTool/Strategies/JpegWipeStrategy.cs` / `TiffWipeStrategy.cs` / `RawWipeStrategy.cs` / `HeicWipeStrategy.cs` / `PngWipeStrategy.cs` / `MovWipeStrategy.cs` / `PdfWipeStrategy.cs` / `EpsWipeStrategy.cs` / `UnknownFormatStrategy.cs`（每文件 ~30-50 行）
- 改 `src/PhotoPrivacy.Core/ExifTool/ExifToolBridge.cs` L223 `BuildWipeTaskBlock(path, id)` → `WipeStrategyResolver.Resolve(path).BuildCommand(path, id)`

**验收标准**:
- 扩展名 `photo.jpg` → JPEG 策略命令 `-all=\n--icc_profile:all\n-tagsfromfile @\n-colorspacetags\n-overwrite_original\n<file>\n...`
- 扩展名 `photo.cr3` → RAW 策略 `-exif:all=\n-xmp:all=\n-iptc:all=\n-icc_profile:all=\n-overwrite_original\n<file>\n...`
- 扩展名 `photo.mov` → MOV 策略 `-All=\n-Time:All=\n-overwrite_original\n<file>\n...` （注意大写 A）
- 扩展名 `doc.pdf` → PDF 策略 `-all=\n-overwrite_original\n<file>\n...` + `RequiresUserWarning=true`
- 未识别扩展 → `WipeResult.UnknownFormat`，audit `wipe_skipped_unknown`，不传 ExifTool

**test 闭环**:
- `WipeStrategyResolverTests` — 每族至少 3 扩展名（覆盖大小写混写、路径分隔符）
- `.jpg` / `.JPG` / `.jpeg` / `.jpe` 同走 JPEG 策略
- `.CR3` / `.cr3` 同走 RAW 策略
- 三平台：`Path.GetFileName` 取扩展名跨平台一致

### M6b. 专业模式 Expert Gate

**改动文件**:
- 新增 `src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs`（约 200 行）
- 新增 `src/PhotoPrivacy.Ui/Views/RulesPanel.axaml`（约 250 行）

**验收标准**:
- `IsExpertMode` 默认 false，ToggleSwitch 切换时弹确认对话框
- 非专家模式：ICC / MakerNotes / Adobe / Time / CommonIFD0 列 `IsEnabled=False`，且值强制回到对应格式族安全默认
- 显示 infobar "To bypass protection, enable expert mode."（i18n key: `expert_mode_infobar`）
- "Reset warning confirmations" 按钮重置所有已记住的确认状态（清空 `config/remembered_confirmations.json`）

**test 闭环**:
- `RulesPanelViewModelExpertGateTests` — IsExpertMode=false 时危险列 `StripIcc` getter 返回 false 即使 setter 设 true
- "启用专家模式" 点击 → 弹确认 → 取消不切换状态、确认才切
- 三平台：Avalonia 确认对话框三端原生

### M6c. DataGrid 矩阵面板

**改动**:
- 新增 NuGet: `Avalonia.Controls.DataGrid` (版本 12.1.1, 对齐 Avalonia 主包)
- 新增 `src/PhotoPrivacy.Ui/Views/RulesPanel.axaml` DataGrid AXAML（约 150 行）
- 新增 `src/PhotoPrivacy.Ui/ViewModels/FormatRuleRow.cs`（约 80 行）

**验收标准**:
- DataGrid 列：扩展名（FrozenColumnCount=1）、格式族、11 个元数据组 CheckBoxColumn、生成参数列
- HeaderTemplate 放全选/全清复选框
- RowDetails 展开显示该格式最终 ExifTool 命令预览（可复制）
- 按"格式族"分组（DataGridCollectionView.GroupDescriptions）

**test 闭环**:
- AXAML 编译通过（`dotnet build` 无 XAML 错误）
- 视觉：3 平台截图对照（不强制自动化）
- `FormatRuleRowEffectiveArgsTests` — 各勾选组合 → Golden 参数串对照（用 inline snapshot）

### M6d. 搜索过滤

**改动**: `RulesPanelViewModel` 加 `SearchText` 属性 + `VisibleRows` 计算属性（约 30 行）

**验收标准**:
- 搜索 `jpg` → 仅 JPEG 行
- 搜索 `raw` → 命中 CR2/CR3/NEF/ORF/ARW 所有 RAW 行
- 空搜索 → 全部行
- 搜索不区分大小写

### M6e. 保存自定义规则 + 重置默认

**改动文件**:
- 新增 `src/PhotoPrivacy.Core/Configuration/FormatRulesStore.cs`（约 120 行）
- 新增 `config/rules.json` 配置文件（首次运行生成）

**验收标准**:
- 勾选改动后 500ms 内自动防抖写盘（`CTS` 取消重置）
- "保存自定义规则" 按钮立即写盘（不必等防抖）
- "恢复默认" 按钮弹二次确认对话框，确认后才恢复
- 重启后加载 `config/rules.json` 已保存的自定义配置
- 配置文件读写原子（temp + rename，遵循 ADR 0005 的原子写模式）

**test 闭环**:
- `RulesStoreRoundTripTests` — 写入 → 加载 → 值对照
- `RulesStoreResetTests` — 重置 → 默认值断言
- 原子写：模拟写中途卡死，旧文件保持可用

### M6f. Per-Format 警告图标

**改动**: DataGrid 行 CellTemplate 加警告列（约 40 行）

**验收标准**:
- PDF 行显示红色 warning 图标 + tooltip
- EPS 行显示黄色 warning 图标 + tooltip
- HEIC 行（配置 ExifTool 版本 < 13.18）橙色 warning
- 未知扩展行灰色 "unknown" 标记
- 已知安全格式（PNG/JPEG/TIFF/HEIC 13.18+/MOV/MOV）无警告图标

**test 闭环**:
- `WipeStrategyRequiresUserWarningTests` — 各族 flag 正确
- 视觉验证（截图对照）

### M6g. 综合测试与集成

**改动**: 新增 4 个测试文件（约 200 行代码）
- `tests/PhotoPrivacy.Core.Tests/ExifTool/WipeStrategyResolverTests.cs`
- `tests/PhotoPrivacy.Core.Tests/ExifTool/FormatRuleRowEffectiveArgsTests.cs`
- `tests/PhotoPrivacy.IntegrationTests/Ui/RulesPanelViewModelExpertGateTests.cs`
- `tests/PhotoPrivacy.IntegrationTests/Core/FormatRulesStoreRoundTripTests.cs`

**验收标准**:
- `dotnet test` Core 项目全过
- `dotnet test` Integration 项目全过（排除 env-only 5 个测试）
- 无新增 SCS 安全警告
- ExifTool 协议测试模拟stdin/stdout（不真启动 exiftool.exe）

## 平台兼容性贯穿所有里程碑

| 项 | Windows | Linux | macOS |
|---|---|---|---|
| M1 启动 | `StartWithClassicDesktopLifetime` | 同 | 同（不支持 `async Main`） |
| M2 MVVM | `Dispatcher.UIThread.Post` | 同 | 同 |
| M3 Channel | stdin/stdout 协议一致 | 同 | 同 |
| M6a WipeStrategy | `Path.GetFileName` 跨平台一致 | 同 | 同 |
| M6c DataGrid | Avalonia.Controls.DataGrid 三端原生 | 同 | 同 |
| M6e RulesStore | `config/rules.json` 遵循 `DefaultPaths` 三端分发 | 同 | 同 |

## Git 规范（AGENTS.md §4 + §7）

每个里程碑独立 commit；整个 ADR 完成后 `git push origin main`：

```bash
# M1 完成
git add src/PhotoPrivacy.Ui/Program.cs tests/...
git commit -m "feat(startup): async two-phase startup via Start(AppMain) — ADR 0053 M1"

# M2 完成
git commit -m "refactor(ui): MainWindow MVVM split — StartupCoordinator + commands, 2062→500 lines — ADR 0053 M2"

# M3 完成
git commit -m "feat(pipeline): BoundedChannel + IAsyncEnumerable file processing pipeline — ADR 0053 M3"

# M6a-M6f 每项独立 commit
git commit -m "feat(wipe): WipeStrategyResolver with per-format safe defaults — ADR 0053 M6a"
# ...

# 最后整体推送
git push origin main
```

## 后果（ADR 0053 已记录的负面项）

- **RAW 保守策略**：用户可能觉得 "清理不彻底"，但这是不毁渲的官方代价——文档明确告知
- **新增依赖 `Avalonia.Controls.DataGrid`**：已被官方推荐用于矩阵场景，可控
- **PDF 警告**：引导用户跑 qpdf 命令属应用外操作，文档需清晰指引路径
- **MainWindow 大 implicated commit**：14 处 sync-over-async 跨面大，commit 隔离不慎会回归——建议分两 commit：先 M2-backend（迁逻辑），再 M2-axaml（仅引用更新）

## 不在本路线图范围（已声明）

- NativeAOT（per-RID 验证试点，后续 ADR）
- ItemsRepeater（已 official retired，禁用）
- UI 虚拟化改造（500 cap + ListBox + AppendLogBatch 已足）
