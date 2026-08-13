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

### 6. 打包流程全平台兼容（Grill 问题 2）

**调研来源**: pwm pro gpt56 调研 × 3 次 + exa 广范围搜索，引用 dotnet/runtime .gitattributes、Microsoft single-file 文档、Avalonia 部署指南。

#### 6.1 问题：Unix 二进制缺执行位（破坏性）

Windows NTFS 无 POSIX 权限语义。当 `publish-app.ps1` 在 Windows 主机执行
`dotnet publish -r linux-x64 --self-contained /p:PublishSingleFile=true` 时，
生成的 ELF/Mach-O apphost 二进制在 NTFS 上不携带 Unix 执行位。
`tar -czf` 打包时记录的 mode bits 不含 `+x`，
Linux/macOS 用户解压 `tar.gz` 后 `./PhotoPrivacy` 报 `Permission denied`。
pwm pro gpt56 确认：`.NET SDK cross-publish` 不保证在非 POSIX 文件系统上
设置 `+x`，需 publish 脚本手动 `chmod +x`。

**修复**: `publish-app.ps1` 在复制二进制到 `targetDir` 之后、`tar -czf` 之前，
对 `linux-*`/`osx-*` runtime 执行 `chmod +x` 给三个文件：
`PhotoPrivacy`、`PhotoPrivacyWorker`、`scripts/install-*.sh`。
`chmod` 在 `pwsh` 跨平台原生可用（不依赖 WSL）。

#### 6.2 问题：硬编码反斜杠路径（非跨平台）

`publish-app.ps1` 原用 `"$repoRoot\src\..."` 反斜杠拼接路径，
在 Linux/macOS 的 `pwsh` 上 `DotNet publish` 路径解析失败。

**修复**: 全部 8 处硬编码 `\` 路径改为 `Join-Path` cmdlet，
`pwsh` 原生跨平台路径拼接（`Join-Path $repoRoot "src" | Join-Path -ChildPath "..."`）。
`publish.sh` 已用 `$(pwd)` + `/` 正确处理，无需改动。

#### 6.3 问题：`.gitattributes` 缺 binary 标记 + .sh eol 规则（CRLF 损坏风险）

原有 `.gitattributes` 仅 4 条规则（`* text=auto eol=lf` + 3 条 PS CRLF）。
二进制资产（`.png/.ico/.dll/.exe/.so/.dylib`）含 `0x0D 0x0A` 字节会被
`text=auto` 误判为文本，导致 CRLF 注入损坏二进制。
`.sh` 脚本缺显式 `eol=lf` 规则（依赖 `auto` 兜底不可靠）。

**修复**: 采用 dotnet/runtime 风格完整模板（方案 B），补全：
- 5 条 Windows 脚本 CRLF 规则（`.bat/.cmd/.ps1/.psm1/.psd1`）
- 1 条 Unix 脚本 LF 规则（`.sh`）
- 8 条 .NET 源文件 `text` 规则（`.cs/.csproj/.sln/.fs/.fsproj/.vb/.vbproj/.csx`）
- 8 条配置标记规则（`.json eol=lf/.xml/.config/.axaml/.xaml/.yaml/.yml/.toml`）
- 2 条文档规则（`.md/.txt`）
- 13 条 binary 资产（图片/字体）
- 6 条 native 二进制（`.dll/.exe/.so/.dylib/.pdb/.mdb`）
- 5 条归档格式（`.zip/.gz/.tar/.7z/.bz2`）

#### 6.4 验证

- `git add --renormalize .` 固化规则
- `git diff --check` 无 CRLF 冲突
- `publish-app.ps1 -Runtime win-x64 -Zip false` 落盘结构不变
- `publish-app.ps1 -Runtime linux-x64` 生成 `tar.gz` 内二进制含执行位
  （需在 Linux 主机或 WSL 上解压验证 `ls -l PhotoPrivacy` 有 `-rwxr-xr-x`）
