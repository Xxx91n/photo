# 报告 — 票 17 配置 round-trip 对称性止血（架构恢复第五轮）

日期：2026-09-03　窗口：票 17 专属　版本控制：WORKFLOW §4.2（GitButler 虚拟分支，未 push）

## 声明 → 证据 → 结论对照

### 检查点 A — sidebar_width 两侧 DTO 补齐 + 构造传参

- **声明**：`AppConfigLoader` 与 `AppConfigJson` 两侧 DTO 补齐 `ui.sidebar_width`（读+写），构造处传参；拖拽宽度重启可恢复。
- **证据**：
  - `src/PhotoPrivacy.Core/Configuration/AppConfigJson.cs`：`UiDto` 新增 `[JsonPropertyName("sidebar_width")] double SidebarWidth = 200.0`；`ToIndentedJson` 的 `Ui` 段新增 `SidebarWidth = config.Ui.SidebarWidth`。
  - `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`：`UiDto` 同名属性（默认 `AppConfig.Default.Ui.SidebarWidth`）；`Load` 构造 `UiOptions(... SidebarWidth: dto.Ui.SidebarWidth, ...)`。
  - UI 链路现状核实（未改动，原本已接线）：`MainWindow.axaml.cs` 拖拽完成 → `vm.SidebarWidth` → `ScheduleDebouncedConfigApply`（ADR 0037 防抖 500ms）→ `ConfigEditCommand.SidebarWidth` → `ConfigEditor.UpdateConfig` 已赋 `Ui.SidebarWidth`——此前仅因写侧 DTO 无该属性而存盘即丢；读侧同理，`RestoreSidebarWidth(effectiveConfig.Ui.SidebarWidth)` 因读侧 DTO 无该属性永远回落 200.0。两侧 DTO 补齐后链路即通。
  - 测试：`RoundTrip_Should_Preserve_Modified_SidebarWidth_And_BackupRetainDays`（356.5 往返还原）、`RoundTrip_Should_Preserve_SidebarWidth_When_Only_Key_Present`（仅含 `sidebar_width` 键的最小 JSON 读回 312.0，验证 init 默认值兜底路径）、`ConfigEditorRoundTripTests` 增补 `command.SidebarWidth = 321.5` 落盘+读回断言。
- **结论**：✅ 通过。拖拽宽度经防抖保存后落盘，重启读回恢复。

### 检查点 B — Json DTO 补 retain_days，保存不回落

- **声明**：`AppConfigJson.BackupDto` 补齐 `backup.retain_days`（写），保存后不回落默认 30。
- **证据**：`AppConfigJson.cs` `BackupDto` 新增 `[JsonPropertyName("retain_days")] int RetainDays = 30`；`ToIndentedJson` 的 `Backup` 段新增 `RetainDays = config.Backup.RetainDays`。此前 UI 保存任意配置即把该键从 config.json 抹掉，读侧 init 默认 30 兜底，用户设置丢失。
- **结论**：✅ 通过。`RoundTrip_Should_Preserve_Modified_SidebarWidth_And_BackupRetainDays` 断言 `RetainDays=7` 往返还原。

### 附带项 1 — 移除死字段 backup.retention

- **声明**：移除遗留死字段 `backup.retention` 及 config.json 中的对应键。
- **证据**：`config/config.json` backup 段移除 `"retention": "keep"` 并补齐 `max_size_mb`/`retain_days`（与写侧新输出形态一致）；测试夹具同步清理（`AppConfigLoaderTests` 3 处、`DryRunOutputFlowTests` 5 处、`InstanceConflictAuditTests`、`SmokeScriptDiagnosticsTests` 各 1 处）；全库 `rg '"retention"'` 仅剩新测试中的反向断言一处。代码侧无 `retention` 属性（`BackupOptions` 从无此参数，ADR 0006 已裁决改用 `max_size_mb`），无需代码改动。
- **结论**：✅ 通过。

### 附带项 2 — sample audit 段收敛

- **声明**：收敛 `config.sample.json` 的 `audit.diagnostic_mode/log_level` 与 `AppConfig.Default` 取值一致。
- **证据**：sample 由 `diagnostic_mode: true / log_level: "debug"` 改为 `false / "info"`，与 `AppConfig.Default.Audit`（`DiagnosticMode: false, LogLevel: "info"`）一致。锁定测试 `Sample_Should_Match_Default_On_Key_Configurable_Values` 覆盖 sample 全部段的全部键 vs `AppConfig.Default`（schema_version、exiftool 6 键、watch 5 键、rules.output_mode、retry.max_attempts、backup 4 键、quarantine.enabled、audit 3 键、ui 5 键），并断言 sample 不含死键 `backup.retention`。
- **结论**：✅ 通过。未来 sample 与 Default 再发散即测试红灯。

### 检查点 C — round-trip 测试全字段 + 门禁

- **声明**：round-trip 测试覆盖全部配置字段（含 sidebar_width/retain_days），单槽绿；build 0 错 0 SCS。
- **证据**：
  - 新增 `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigRoundTripTests.cs` 6 个测试：Default 全字段往返逐字段断言（含数组的段用序列相等——record 相等对数组按引用比较，round-trip 后是新实例，首轮曾因此红灯并已修正断言方式）、修改值往返、最小 JSON 兜底、写侧键存在、死键不存在、sample 对齐。
  - 单槽串行（test.runsettings）：Core.Tests 151/151 绿；IntegrationTests 276/276 绿（含 ConfigEditorRoundTripTests 增补断言）。
  - `dotnet build PhotoPrivacy.sln --no-incremental`：0 错误，grep `SCS` 零命中（1012 个警告均为既有 CA 风格警告，未新增）。
  - semgrep：`p/csharp` 全 src 0 发现（3 条为 MainWindow.axaml.cs 既有 taint 规则超时噪声，与本票无关）；`p/csharp + p/security-audit` 双配置扫描两个改动文件 0 发现 0 错误。
- **结论**：✅ 通过。

## 版本控制

- GitButler 虚拟分支 `ticket-17-config-roundtrip-stopbleed`（but slug rqv），commit 信息「票17: 配置 round-trip 对称性止血 — …」，13 文件 +400/−25；未 push。报告副本随该 commit 提交，属自指内容故不写死哈希——物理哈希以 git log 实物为准（WORKFLOW §7 教训 3）。
- 收口操作仅 `but commit`（新建分支提交），未执行 §4.4 触发清单内的历史改写/丢弃操作；仍按稳妥原则先跑了仓库外快照（manifest 校验 ZERO-LOSS）。
- 工作区内其他票（18/21 等）在途改动未触碰、未圈入本票提交。

## 遗留与移交

- 本票为最小止血，double-DTO 架构重构（单 DTO/源生成器）按 spec 归中远期 backlog，不在本轮。
- `AppConfigJson`（写侧 DTO 无默认值兜底，靠 AppConfig 值填充）与 `AppConfigLoader`（读侧 DTO 带默认值兜底）的双 DTO 漂移温床仍在，票 17 测试已锁对称性兜底。
- 完成定义逐条满足：DTO 补齐+构造传参 ✅；round-trip 全字段单槽绿 ✅；build 0 错 0 SCS ✅；本报告落 .scratch 并沉淀 docs/process/reports/ ✅。
