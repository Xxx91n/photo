# ADR 0039: 日志级别后端对接 + audit logger 始终构造 + 资源释放

**状态**: 已接受
**日期**: 2026-08-13

## 背景

ADR 0038 修复了 LogLevel 的 UI 往返持久化，但遗留一个语义断裂点：
日志级别筛选只在 GUI 层生效，未对接后端 audit logger 构造。

### 旧问题

```csharp
// 旧代码 — NDR 0038 引入后仍是 bool 开关
_audit = config.Audit.DiagnosticMode
    ? new JsonLineAuditLogger(..., AuditLevelParser.Parse(config.Audit.LogLevel))
    : null;
```

`ConfigEditor.ResolveDiagnosticMode` 把 LogLevel 压成 bool：
- all/debug → true → 构造 logger
- **info/warn/error → false → _audit = null → NoopAuditLogger → 不写任何审计日志**

结果：用户选 WARN/ERROR 期望写更高级别审计日志，实际完全不写。
日志级别只浮于 GUI 显示，未对接后端 Worker/Service 生命周期。

### 行业标准对照（Microsoft.Extensions.Logging）

.NET 官方做法：logger 始终构造，用 minimum level `IsEnabled(logLevel)` 筛选，
低于最低级别的不传给 provider。完全禁用用 `LogLevel.None`(=6)，
而非"不构造 logger"。禁用所有是 opt-in 的极端情况，不与级别筛选耦合。

## 决策

### 1. audit logger 始终构造（不再用 DiagnosticMode 开关）

```csharp
// 新代码
_audit = new JsonLineAuditLogger(
    config.Audit.LogDirectory, config.Audit.RetainDays,
    config.Audit.DiagnosticMode,  // 仅保留为 audit JSON payload 字段标记
    AuditLevelParser.Parse(config.Audit.LogLevel));  // 真正的级别筛选
```

`DiagnosticMode` 字段保留为 audit JSON payload 的标记字段（`diagnostic_mode`），
及 UI LogEnabled 显示，但**不再控制 logger 构造**。
`LogLevel` 成为后端审计写入的权威筛选字段。

### 2. InstanceConflictAudit 也传入 minimumWriteLevel

`InstanceConflictAudit.TryWriteAsync` 构造 logger 时补传
`AuditLevelParser.Parse(config.Audit.LogLevel)`，
让级别筛选在进程冲突审计路径也生效。

### 3. AppConfig.Default 自洽

Default 原为 `DiagnosticMode: true` + 我上个提交加的 `LogLevel: "info"` —— 不自洽
（ResolveDiagnosticMode("info")=false 但 DiagnosticMode=true）。
修正为 `DiagnosticMode: false, LogLevel: "info"` 自洽。

config.sample.json 同步加 `log_level: "debug"` 与 `diagnostic_mode: true` 自洽
（debug → true，写全含 Debug）。

### 4. Worker StopAsync 释放资源（修复 CA2213）

audit logger 始终构造后，`_audit` (JsonLineAuditLogger 持有 Channel 后台任务
+ CancellationTokenSource) 从未 Dispose 是真实资源泄漏。
StopAsync 在写完 service_stopped 审计、调用 base.StopAsync 之前补
`_audit?.Dispose()`（最长 5s 等待后台 flush）和 `_reloadGate.Dispose()`。

### 5. 打包流程复核（Grill 问题 1）

实测 publish-app.ps1 -Runtime win-x64 -Zip false:
最终 release/win-x64/ 落盘结构为平铺：
```
release/win-x64/
  PhotoPrivacy.exe       95.9 MB (单文件)
  PhotoPrivacyWorker.exe 73.1 MB (单文件)
  Assets/  config/  scripts/  README.md
```
无 ui/ worker/ 临时目录残留 — 脚本先 publish 到中转目录、复制 apphost、
再删除中转的流程正确。首次解压即为最终平铺结构。

## 后果

- WARN/ERROR 级别真正生效：选 warn 只写 Warn 及以上审计；选 error 只写 Error
- info 级别不再静默：选 info 时 Info 级审计日志正常写入
  （旧行为 DiagnosticMode=false → _audit=null → 不写任何审计，已修正）
- 旧 config 缺 log_level 字段时用 Default 的 "info" 填充 → logger 构造 + minimumInfo
  行为变化：从"不写审计"变为"写 info 及以上"。这是更符合用户预期的修正
- audit logger 后台任务在 StopAsync 优雅 Dispose，无 Channel/CTS 泄漏
- DiagnosticMode 字段保留向后兼容（不删，UI LogEnabled 与 audit JSON 字段仍用它）
