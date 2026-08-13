# ADR 0038: 日志级别 LogLevel 完整往返持久化

**状态**: 已接受
**日期**: 2026-08-13

## 背景

日志级别 ComboBox 提供 5 个选项（ALL/INFO/DEBUG/WARN/ERROR），但只有 INFO 和 DEBUG 能持久化——
其他三个选项（ALL/WARN/ERROR）点击后弹回之前选中的值。

### 根因

`AuditOptions` record 只有 `bool DiagnosticMode` 字段，没有独立的 `LogLevel` 字段。
`ConfigEditor.ResolveDiagnosticMode` 把 5 个级别压成一个 bool（all/debug→true, info/warn/error→false），
然后 `ApplyRuntimeConfigToUiState` 从 bool 反推 LogLevel 时只能得到 `"debug"` 或 `"info"`：

```csharp
// 旧代码 — 丢失原始级别
vm.LogLevel = cfg.Audit.DiagnosticMode ? "debug" : "info";
```

当用户选 ALL/WARN/ERROR → `OnLogLevelSelectionChanged` 设 `vm.LogLevel="all"` →
防抖 500ms 触发 `ApplyConfigImmediatelyAsync` 写 config（被压成 bool）→
`ApplyRuntimeConfigToUiState` 反读 → `vm.LogLevel` 变回 debug/info →
`SyncLogLevelComboSelection` 让 ComboBox 弹回。

### 行业标准对照（Serilog Settings Configuration）

Serilog 官方 `serilog-settings-configuration` 用字符串直接存储最小级别：
```json
{ "Serilog": { "MinimumLevel": "Debug" } }
```
不压缩为 bool，支持 reloadOnChange 动态切换。本修复对齐该行业模式。

## 决策

给 `AuditOptions` 新增 `string LogLevel` 字段（默认 `"info"`），完整往返持久化。
`DiagnosticMode` 保留为派生字段（向后兼容 Worker 使用它做 logger 开关）。

### 变更

1. **AuditOptions**（AppConfig.cs）: positional record 末尾加 `string LogLevel = "info"`
2. **AppConfigJson.AuditDto** + **AppConfigLoader.AuditDto**: 加 `log_level` JSON 属性，
   `init` + 默认值，旧 config 缺该字段自动填充（System.Text.Json 默认行为，向后兼容）
3. **ConfigEditor.UpdateConfig**: 写入 `LogLevel = command.LogLevel`，
   `DiagnosticMode` 仍从 LogLevel 派生（保持 Worker 向后兼容）
4. **ApplyRuntimeConfigToUiState** + **Initialize**: 从 `cfg.Audit.LogLevel` 直接读取
   （不再从 bool 反推）
5. **AuditLevelParser.Parse**: 新增工具方法，字符串→AuditLevel 映射
   （all→Debug, info→Info, warn→Warn, error→Error）
6. **MetadataCleanerWorker**: 构造 `JsonLineAuditLogger` 时传入
   `AuditLevelParser.Parse(config.Audit.LogLevel)` 作为 `minimumWriteLevel`，
   让日志级别真正生效于审计日志筛选

### 向后兼容

- 旧 config.json 没有 `log_level` 字段 → `AuditDto.LogLevel` 用 `= AppConfig.Default.Audit.LogLevel` 默认值
  （即 `"info"`），行为与旧行为一致
- `DiagnosticMode` 字段保留不变，Worker 仍用它做 logger 构造开关
- `AuditLevel` enum 不变（无 All 值，"all" 映射为 Debug=写全部）

## 后果

- 5 个日志级别全部能往返持久化，ComboBox 不再弹回
- 审计日志按级别筛选（minimumWriteLevel），而非仅做 DiagnosticMode 开关
- 新增 `log_level` 字段到 config.json schema（SchemaVersion 不变，字段可选）
