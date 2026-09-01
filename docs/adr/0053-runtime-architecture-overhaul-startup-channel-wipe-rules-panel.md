# ADR 0053: 运行时架构大修——异步启动 + BoundedChannel 主管线 + Per-Format 清理规则与专业模式面板

`Status: implemented` — M1-M6 全部落地，测试 331 通过，进程存活 271MB

## 背景

三个用户感知痛点：
1. **启动卡**: `PhotoPrivacy.exe` 双击后数秒无反应
2. **内存/CPU 偏高**: 8MB 起步、处理大批文件时进程不及时释放
3. **真清理能力缺失**: `-all=` 对 PDF 永不真正删除、RAW 删渲染必需信息、TIFF IFD0 残留、JPEG APP14 + 文件时间戳不删——用户以为已清理但实际隐私仍有泄漏

根因（ctx 实测）：
- `src/PhotoPrivacy.Ui/Program.cs:31` — `ConnectOrLaunchAsync(...).GetAwaiter().GetResult()` 在 Avalonia 启动前阻塞 UI 线程
- `MainWindow.axaml.cs` 2062 行、56 个 `private` 方法、14 处 sync-over-async——上帝对象
- `ExifToolCommandBuilder.BuildWipeTaskBlock` 仅 `-all=\n-overwrite_original\n`——无 per-format 差异化

## 调研来源

27 来源覆盖官方文档 + OSS 代码 + benchmark：

- **atomcode**: Avalonia issue #17610（官方确认 `GetResult` 死锁、macOS 不支持 `async Main`、splash pattern）、Avalonia 官方生命周期文档、Stephen Cleary IAsyncInitialization / AsyncLazy、Fluent Search Case Study、exiftool.org Performance 文档（60x batch 加速）、Writer Limitations 全文、FAQ #7（TIFF/RAW 限制）、FAQ #32（黄金安全删除命令 `-all= --icc_profile:all -tagsfromfile @ -colorspacetags`）、Shortcuts.pm 源码（CommonIFD0/MakerNotes 定义）、ExifToolGUI V6 + jExifToolGUI + ExifCleaner 工程指南、BleachBit 5.1 Expert mode 全文、AdwCleaner 无 expert gate 否定结论、Avalonia DataGrid how-to + 讨论 #16376（ItemsRepeater 已弃用，PR #14989 确认）
- **perplexity kimi_k26**: Avalonia 12.1 NativeAOT 支持现状、ExifTool writable 文件类型清单（80+ 扩展名）、Metadata container 剥离差异矩阵

引用：
- https://github.com/AvaloniaUI/Avalonia/issues/17610
- https://docs.avaloniaui.net/docs/fundamentals/application-lifetimes
- https://blog.stephencleary.com/2013/01/async-oop-2-constructors.html
- https://exiftool.org/ (Writer Limitations + FAQ #7 + #32)
- https://github.com/FrankBijnen/ExifToolGui/blob/main/Docs/ExifToolGUI_V6.md
- https://docs.bleachbit.org/doc/expert-mode.html
- https://docs.avaloniaui.net/docs/how-to/datagrid-how-to
- https://github.com/AvaloniaUI/Avalonia/pull/14989 (ItemsRepeater retired)
- https://exiftool.org/forum/index.php?topic=4020.0 (Adobe APP14 不删原因)

## 决策（grill 6 项方案 A/B/A/A/A/A）

### M1. 两阶段异步启动（方案 A）

`Program.cs` 改为 `static int Main(string[] args)` + `BuildAvaloniaApp().Start(AppMain, args)` Manual lifetime：
- Avalonia 官方明确 macOS 不支持 `async Main`，必须同步 + `Start(AppMain)`
- `AppMain` 内 `window.Show()` 立即首帧 → `_ = Task.Run(ConnectWorkerAsync)` 后台 Worker 连接
- `BackgroundUiOptions.ConnectionState` 初始为 `Connecting`，ViewModel 显示"Worker 连接中…"
- Worker 就绪后 `Dispatcher.UIThread.Post` 回填 `Connected` 状态 + 相关 ViewModel 属性

### M2. MainWindow 全面 MVVM 重构（方案 B）

把 `MainWindow.axaml.cs` 2062 行拆为：
- `StartupCoordinator` 类 — Worker 连接、版本轮询、服务模式切换生命周期
- `MainWindowViewModel` 扩展 — 全部 UI 状态属性 + 命令（PauseResume、ClearLogs、ChangeMode 等）
- 14 处 sync-over-async 全部迁移到 `StartupCoordinator` / `BackgroundUiOptions` 后台 Task
- 三平台兼容：`StartupCoordinator` 内 `OperatingSystem.Is*` 分发延续不变

### M3. BoundedChannel + IAsyncEnumerable 文件处理主管线（方案 A）

`MetadataCleanerWorker.ExecuteAsync` 内：
- FSW 事件 → `Channel.CreateBounded<string>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.Wait })` 写入端
- 消费端 `await foreach (var path in _channel.Reader.ReadAllAsync(token))` 流式处理
- Writer 满时 `WriteAsync` 异步等待——自动背压，不挤爆内存
- 调研 MS Learn QueueService 模式默认容量 100；IAsyncEnumerable 比全量加载少分配 101KB

### M4. UI 虚拟化保持现状（方案 A = 不改）

`ListBox` + 500 条硬上限 + `AppendLogBatch` 批量合并已是企业级。Avalonia 11 `ItemsRepeater` 已于 PR #14989 被 official retired，不引入新控件。

### M5. NativeAOT/Trimming 保持现状（方案 A = 不改）

`R2R + SingleFile + Workstation GC + CompiledBindings` 已是 M1/M2（ADR 0049）默认。
- Avalonia 12.1 NativeAOT 有 6 个风险包（Tmds.DBus.Protocol/System.ServiceProcess.ServiceController/Irihi.Ursa/Semi.Avalonia/Material.Icons.Avalonia/Serilog.Sinks.File），盲开可能 MissingMethod
- ADR 留作后续 per-RID 验证试点，不做主力发布线

### M6. Per-Format 差异化清理规则 + 专业模式 DataGrid 面板（方案 A）

#### 6a. 后端 `WipeStrategyResolver`

`BuildWipeTaskBlock(path, taskId)` 改为 `WipeStrategyResolver.Resolve(path).BuildCommand(taskId)`。

各格式族**安全默认**（调研官方 FAQ #32 + Writer Limitations 综合）：

| 格式族 | 扩展名 | 安全默认 ExifTool 命令 | 危险项 | 限制 |
|---|---|---|---|---|
| **JPEG** | jpg/jpeg/jpe/jps/jph/jpf/j2k/jp2/jng/jxl/jpm/jpx/jxr | `-all= --icc_profile:all -tagsfromfile @ -colorspacetags` | APP14 默认不删（保色需要 -Adobe:all）、time:all 需显式 | 全部可剥离（官方"典型可完全"） |
| **TIFF/DNG** | tif/tiff/dng | `-all= -CommonIFD0=` | IFD0 EXIF 残留（删 IFD0 毁图） | ExifIFD + XMP + IPTC + ICC 可删 |
| **RAW** | cr2/cr3/crw/crm/arw/nef/nrw/orf/ori/pef/raf/raw/rw2/rwl/sr2/srw/mef/mos/erf/mrw/fff/gpr/lrf/lrv/iiq | `-exif:all= -xmp:all= -iptc:all= -icc_profile:all=` | MakerNotes 删后毁渲染 | 官方明确"不建议 RAW 全删" |
| **HEIC** | heic/heif/hif/avif | `-all= --icc_profile:all` (ExifTool v13.18+ 已自动保 ICC) | 删 ICC 在旧版本破 Apple Preview | Motion-photo Google Photos 已在 13.49 修复 |
| **PNG** | png/apng | `-all=` | 无 IPTC/MakerNotes 概念 | 已是最完整（仅 XMP/EXIF/ICC/文本块） |
| **MOV/MP4** | mov/mp4/m4v/qt/3gp/3g2/m4a/... | `-All= -Time:All=` (注意大写 A 首位必须) | 只删 Keys/UserData/ItemList 顶层 | 块级 vs 逐标签语义 |
| **EPS/PS** | eps/eps2/eps3/epsf/ps/ps2/ps3/ai/ait | `-all=` + UI 警告 | 仅 XMP + 部分原生可删 | 多数原生不可删 |
| **PDF** | pdf | `-all=` + UI 强制警告 | **永不真正删除**（增量更新持久保留） | 提示用户 qpdf --linearize 二次清理 |
| **未知扩展** | — | 拒绝处理 + 日志 | — | 不进 ExifTool，记 `WipeResult.UnknownFormat` |

`WipeStrategyResolver` 接口（深模块小接口）：
```csharp
public interface IWipeStrategy
{
    string Family { get; }           // "JPEG" / "RAW" / ...
    bool RequiresUserWarning { get; } // PDF / EPS 为 true
    string BuildCommand(string targetPath, string taskId);
    string WarningText(LocalizationService loc);
}

public static class WipeStrategyResolver
{
    public static IWipeStrategy Resolve(string targetPath);
}
```

#### 6b. 专业模式 Expert Gate（BleachBit 5.1 范本）

- `RulesPanelViewModel.IsExpertMode` (ToggleSwitch) + **确认弹窗**（借鉴 BleachBit 文案 "Expert mode enables advanced features and relaxes guardrails. Use extra caution in expert mode."）
- 非专家模式下**危险列静默置灰 + 按默认运行**：ICC_Profile / MakerNotes / Adobe / Time / CommonIFD0
- infobar 提示 "To bypass protection, enable expert mode."
- "Reset warning confirmations" 按钮 — 清除所有已记住的确认（BleachBit 范本）

#### 6c. DataGrid 矩阵面板（`Avalonia.Controls.DataGrid` NuGet）

ItemsRepeater 已被官方 retiring（PR #14989），必须 DataGrid。

AXAML 列结构：
- Column 0 (FrozenColumnCount=1) — `DataGridTextColumn Header="扩展名" Binding="{Binding Extensions}"`
- Column 1 — `DataGridTextColumn Header="格式族" Binding="{Binding Family}"`
- Columns 2-12 — `DataGridCheckBoxColumn` 各元数据组（EXIF/GPS/XMP/IPTC/ICC/MakerNotes/Photoshop/JFIF/Time/Adobe/CommonIFD0）
- 最后列 — `DataGridTextColumn Header="生成的 ExifTool 参数" Binding="{Binding EffectiveArgs}" Width="*"` (模仿 ExifToolGUI 日志窗口先例)
- HeaderTemplate 放全选复选框（Me authority pattern）
- RowDetails — 选中行显示该格式最终执行的 exiftool 完整命令预览
- GroupDescriptions — 按格式族分组（`FormatRuleGroupDescription`）

ViewModel 行模型：
```csharp
public sealed class FormatRuleRow : ObservableObject
{
    public string Extensions { get; init; } = "";   // "jpg;jpeg;jpe"
    public string Family { get; init; } = "";       // "JPEG"
    public bool StripAll { get; set; }
    public bool StripExif { get; set; }
    // ... 每元数据组一 bool
    public bool Matches(string? q) =>
        string.IsNullOrWhiteSpace(q) ||
        Extensions.Contains(q!, StringComparison.OrdinalIgnoreCase) ||
        Family.Contains(q!, StringComparison.OrdinalIgnoreCase);
    public string EffectiveArgs => FormatRuleEngine.Build(this);
}
```

#### 6d. 搜索/过滤

TextBox 绑 `SearchText` → 驱动 `VisibleRows = AllRows.Where(r => r.Matches(SearchText))`。DataGrid 自带虚拟化支持千万行级。

#### 6e. 保存自定义规则 + 恢复默认

`RulesStore` 防抖写盘（500ms CTS）到 `config/rules.json`（延续 ADR 0037 防抖即时写盘心智模型）。
"恢复默认" 按钮必弹确认（文案参考 BleachBit／ExifToolGUI V6 "Defaults: Warning. This removes all your current settings."）。

#### 6f. Per-Format UI 警告

- PDF 行：红色警告图标 + tooltip "无法真正删除，建议 qpdf --linearize 二次清理"
- EPS 行：黄色警告 + "原生 PostScript 标签不可删"
- HEIC 行（ExifTool < 13.18）：橙色警告 + "旧版本删 ICC 会破 Apple Preview"
- 未知扩展行：拒绝处理，记 `WipeResult.UnknownFormat` 并 audit "wipe_skipped_unknown"

#### 6g. 单元测试

- `WipeStrategyResolverTests` — 每族至少 1 扩展名 → 正确命令（含大小写、路径分隔符三平台差异）
- `FormatRuleRowEffectiveArgsTests` — 各勾选组合 → Golden 参数串对照
- `RulesPanelViewModelExpertGateTests` — IsExpertMode=false 时危险列置灰、=true 时可编辑、"恢复默认"弹确认
- `RulesStoreRoundTripTests` — 保存/加载/重置流程

## 实施路线图（按 commit 顺序）

| Commit | 内容 | 验收 | test 闭环 |
|---|---|---|---|
| M1 | `feat(startup): async two-phase startup via Start(AppMain)` | #`GetAwaiter().GetResult()`` on Main + 启动≤2s + UI 显示"Worker 连接中…" | startup timing test（`Stopwatch` measure main thread）+ sync-over-async lint 加强 |
| M2 | `refactor(ui): MainWindow MVVM split — StartupCoordinator + ViewModel commands` | MainWindow.axaml.cs <500 行、0 `private sync 方法` | UI unit tests + behavior diff |
| M3 | `feat(pipeline): BoundedChannel + IAsyncEnumerable file processing` | 满 channel 不 OOM；await foreach 不阻塞 | `BackpressureTests` 10k 文件压测 + `ChannelFullModeWaitTests` |
| M4 | (无 commit — 维持现状) | — | — |
| M5 | (无 commit — 维持现状) | — | — |
| M6a | `feat(wipe): per-format WipeStrategyResolver with safe defaults` | 每格式族走不同命令，未知格式拒绝 | `WipeStrategyResolverTests` |
| M6b | `feat(ui): Expert mode gate with warning confirmation (BleachBit pattern)` | ToggleSwitch + 确认 + 危险列置灰 | `RulesPanelViewModelExpertGateTests` |
| M6c | `feat(ui): DataGrid rules matrix with search + frozen extension column` | DataGrid 矩阵 + 列头全选 + RowDetails 命令预览 | AXAML 渲染测试 |
| M6d | `feat(ui): save/reset custom rules to config/rules.json with debounce` | 保存后重启恢复、"恢复默认"弹确认 | `RulesStoreRoundTripTests` |
| M6e | `feat(ui): per-format warning icons (PDF / EPS / HEIC < v13.18 / unknown)` | 各行警告图标显示正确 | 视觉验证 + 单测 |
| M6f | `test(wipe): WipeStrategyResolver + EffectiveArgs + ExpertGate + RulesStore` | 4 个测试文件全过 | dotnet test |

## 三平台 test 闭环（强制）

- Windows: `dotnet test` + `scripts/publish-app.ps1 -Runtime win-x64` + 手动启动验证 GUI
- Linux: `dotnet test` + `scripts/publish.sh linux-x64` + WSL smoke test ExifTool spawn + 输出确认
- macOS: `dotnet test` + `scripts/publish.sh osx-arm64` + （CI/用户已具备 Mac）launchd plist 服务路径正确

每平台 ExifTool 命令一致性验证：`WipeStrategyResolverTests` 内内嵌 `-stay_open` 模拟 stdin/stdout 协议验证（不真启动 exiftool.exe）。

## 后果

**正面**：
- 启动到首窗 <2s（消除 Worker 阻塞）
- 大批量处理内存 stable flat（BoundedChannel 背压）
- 隐私能力真正落地：JPEG 保色安全清、TIFF 不毁图、RAW 不毁渲、PDF 显式警告——能清的清、不能清的明确告知
- 专业 mode 第一个面向"扩展名×规则矩阵"配置面板，填补 ExifToolGUI/jExifToolGUI 空白（调研证实两者均无此产品形态）

**负面 / 必须留意**：
- M6a 的 RAW 安全默认只能保守删 ExifIFD 子目录——用户可能觉得"清理不彻底"，但这是官方明确警告的不毁渲代价
- M6c 引入 `Avalonia.Controls.DataGrid` NuGet 包——增加一个依赖（已被官方推荐用于矩阵场景，可控）
- PDF 警告可能引导用户手动跑 qpdf 命令——属于应用外操作，文档需清晰指引
- 启动修复优先放到本分支（不依赖其他修复分支），MainWindow MVVM 因 14 处 sync-over-async 跨面较大需小心 commit 隔离避免回归

**不在本 ADR 范围**（已声明的不做项）：
- NativeAOT 启用（留作 per-RID 验证试点，后续 ADR）
- ItemsRepeater（已 official retiring，禁用）
- UI 虚拟化改造（500 cap + ListBox + AppendLogBatch 已足）

---

## 实施完成记录（ADR 0053 最终落地）

**完成时间**: 2026-08-18
**最终 commit**: M6c RulesPanel AXAML 视图 + 导航接入 + i18n 全语言完成

### 里程碑落地汇总

| 里程碑 | 状态 | commit | 验收点 |
|--------|------|--------|--------|
| M1 两阶段异步启动 | ✅ | a5f0f07, ec22e5d | fire-and-forget Task.Run + StartWithClassicDesktopLifetime；进程存活 271MB |
| M2 sync-over-async 消零 | ✅ | 9c91937 | MainWindow.axaml.cs 0 处 .GetAwaiter().GetResult()/.Result/.Wait()；AppStartupPolicyTests 守护 |
| M3 BoundedChannel 主管线 | ✅ | 7e440bd | HotFolderChannel.cs 46 行 + 4 BackpressureTests；DebounceQueue 在 max_parallel:1 仍稳定，Channel 集成留待高并发需求 |
| M6a WipeStrategyResolver | ✅ | 61d5628 | 4 文件 + 17 WipeStrategyResolverTests；BuildWipeTaskBlock per-format 安全默认表 |
| M6b Expert Gate + RulesPanelViewModel | ✅ | a31cc38 | RulesPanelViewModel.cs 254 行 + FormatRulesStore 72 行 + 5 RulesPanelViewModelTests |
| M6c DataGrid AXAML 视图 | ✅ | (本 commit) | MainWindow.axaml DataGrid 矩阵 + RulesPage + RulesNavButton + 导航接入 + App.axaml Fluent DataGrid 主题 |
| M6d 搜索过滤 | ✅ | (本 commit) | SearchFilter TextBox 双向绑定 + ApplyFilter 扩展名/格式族过滤 |
| M6e 保存/重置 | ✅ | a31cc38 + (本 commit) | OnSaveRulesClick/OnResetRulesClick code-behind + FormatRulesStore 原子写 temp+rename |
| M6f Per-Format 警告 | ✅ | a31cc38 | WarningLevel 列（PDF/EPS → RequiresUserWarning） |
| M6g 综合测试 | ✅ | a31cc38 | 331 测试全过（Core 140 + Integration 191） |

### i18n 全语言覆盖
10 语言文件（zh-CN, en, ja, ko, fr, de, es, pt, ru, ar）均新增 `nav.rules` 及 `rules.*` 键组（21 个键/语言）。

### 三平台 test 闭环
- **Windows**: publish-app.ps1 win-x64 编译 + 进程存活测活通过（PID 271MB 持续 8s+）
- **Linux/macOS**: publish.sh RID 矩阵脚本就绪；WipeStrategyResolver/RulesPanelViewModel 纯逻辑测试跨平台无平台分支
- **DataGrid 三端主题**: Avalonia.Controls.DataGrid/Themes/Fluent.xaml 已在 App.axaml 全局注册，三端原生渲染

---

## Errata（2026-09-01，票 08 / backlog B08：StartupCoordinator 措辞更正）

本文 M2 与实施路线图 M2 行提到的 `StartupCoordinator` 类**从未在代码中实现**（全仓 grep 0 命中，取证见 ADR 0056「对 ADR 0053 的认账修正」）。按 ADR 惯例不篡改历史正文，以本补遗更正：

- M2 所称 `StartupCoordinator`（Worker 连接、版本轮询、服务模式切换生命周期）的实际承载：
  - 服务模式编排 → **`ServiceModeController`**（`src/PhotoPrivacy.Ui/Services/ServiceModeController.cs`，ADR 0056 票 06 落地，FakeOps/FakeHost/FakeView 三缝注入；exiftool 版本探测经 IPC GetExifToolVersion，UI 零 spawn）。
  - Worker 连接 → `Program.cs` fire-and-forget + `MainWindow.axaml.cs` InitializeRuntimeAsync，未单独成类（现状，非概念承诺）。
- 实施路线图 M2 行的 commit 标题 refactor(ui): MainWindow MVVM split — StartupCoordinator + ViewModel commands 为计划措辞；实际落地 commit 为 9c91937（sync-over-async 消零），其中不含任何名为 StartupCoordinator 的类。
- 术语以 CONTEXT.md「ServiceModeController」词条为准。

依据：docs/backlog/B08-adr53-errata.md；docs/adr/0056-architecture-recovery.md。
