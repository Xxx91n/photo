# Photo Hotfolder Metadata Cleaner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个 Windows 热文件夹元数据清理工具，使用单一 ExifTool `stay_open` 进程，支持白名单规则、重试后隔离、可选 `.bak` 备份、审计日志，以及 CLI + Windows Service 双宿主。

**Architecture:** 采用单引擎双宿主架构。`PhotoPrivacy.Core` 实现所有业务能力（配置、FSW 监听、去抖、规则判定、管道编排、ExifToolBridge、审计），`PhotoPrivacy.Cli` 与 `PhotoPrivacy.Service` 仅负责宿主生命周期和依赖注入。所有高风险行为（ExifTool 参数、输出同目录、防重复、错误恢复）通过配置校验和测试先行锁定。

**Tech Stack:** .NET 10, C#, xUnit, Microsoft.Extensions.Hosting, FileSystemWatcher, System.Text.Json, ExifTool (`-stay_open true -@ -`).

---

## Planned File Structure

### Solution and projects
- Create: `PhotoPrivacy.sln`
- Create: `src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj`
- Create: `src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj`
- Create: `src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj`
- Create: `tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj`
- Create: `tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj`

### Core files
- Create: `src/PhotoPrivacy.Core/Constants/DefaultPaths.cs` - 固定默认 ExifTool 路径。
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs` - 配置对象与默认值。
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs` - JSON 配置读取。
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfigValidator.cs` - 约束校验（绝对路径、关键参数保护、目录存在性）。
- Create: `src/PhotoPrivacy.Core/Audit/AuditEvent.cs` - 审计事件模型。
- Create: `src/PhotoPrivacy.Core/Audit/IAuditLogger.cs` - 审计接口。
- Create: `src/PhotoPrivacy.Core/Audit/JsonLineAuditLogger.cs` - JSONL 输出与保留策略。
- Create: `src/PhotoPrivacy.Core/Audit/PathMasker.cs` - 路径掩码与稳定哈希。
- Create: `src/PhotoPrivacy.Core/Rules/RuleDecision.cs` - 规则判定结果。
- Create: `src/PhotoPrivacy.Core/Rules/RuleEngine.cs` - 白名单/排除/输出/备份决策。
- Create: `src/PhotoPrivacy.Core/Queue/FileFingerprint.cs` - 文件指纹模型。
- Create: `src/PhotoPrivacy.Core/Queue/InFlightRegistry.cs` - 在途集合。
- Create: `src/PhotoPrivacy.Core/Queue/RecentFingerprintCache.cs` - 防重复缓存。
- Create: `src/PhotoPrivacy.Core/Queue/DebounceQueue.cs` - 去抖队列。
- Create: `src/PhotoPrivacy.Core/ExifTool/ExifToolCommandBuilder.cs` - 启动参数与任务块构造。
- Create: `src/PhotoPrivacy.Core/ExifTool/IExifToolBridge.cs` - Bridge 接口。
- Create: `src/PhotoPrivacy.Core/ExifTool/ExifToolBridge.cs` - `stay_open` 进程管理与 `TASK_DONE` 协议。
- Create: `src/PhotoPrivacy.Core/Pipeline/IFileOperations.cs` - 文件操作抽象（便于测试）。
- Create: `src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs` - 重试/隔离/备份/审计编排。
- Create: `src/PhotoPrivacy.Core/Watcher/IFolderWatcher.cs` - 监听抽象。
- Create: `src/PhotoPrivacy.Core/Watcher/FswFolderWatcher.cs` - FSW 封装与 Error 恢复。
- Create: `src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs` - 启动核心流程。

### Host files
- Create: `src/PhotoPrivacy.Cli/Program.cs` - 前台常驻宿主。
- Create: `src/PhotoPrivacy.Service/Program.cs` - Windows Service 宿主。

### Test files
- Create: `tests/PhotoPrivacy.Core.Tests/Constants/DefaultPathsTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigValidatorTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Audit/JsonLineAuditLoggerTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Rules/RuleEngineTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Queue/DebounceQueueTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/ExifTool/ExifToolCommandBuilderTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/ExifTool/ExifToolBridgeTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Pipeline/FileTaskPipelineTests.cs`
- Create: `tests/PhotoPrivacy.Core.Tests/Watcher/FswFolderWatcherTests.cs`
- Create: `tests/PhotoPrivacy.IntegrationTests/Smoke/EndToEndSmokeTests.cs`

### Docs and config files
- Create: `config/config.sample.json`
- Create: `README.md`
- Create: `scripts/smoke.ps1`
- Create: `scripts/benchmark.ps1`

---

### Task 1: 初始化解决方案与基础常量

**Files:**
- Create: `PhotoPrivacy.sln`
- Create: `src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj`
- Create: `src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj`
- Create: `src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj`
- Create: `tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj`
- Create: `tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj`
- Create: `src/PhotoPrivacy.Core/Constants/DefaultPaths.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Constants/DefaultPathsTests.cs`

- [ ] **Step 1: 创建解决方案与项目骨架**

Run:
```bash
dotnet new sln -n PhotoPrivacy
dotnet new classlib -n PhotoPrivacy.Core -o src/PhotoPrivacy.Core
dotnet new console -n PhotoPrivacy.Cli -o src/PhotoPrivacy.Cli
dotnet new worker -n PhotoPrivacy.Service -o src/PhotoPrivacy.Service
dotnet new xunit -n PhotoPrivacy.Core.Tests -o tests/PhotoPrivacy.Core.Tests
dotnet new xunit -n PhotoPrivacy.IntegrationTests -o tests/PhotoPrivacy.IntegrationTests
dotnet sln PhotoPrivacy.sln add src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj
dotnet add src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj reference src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj
dotnet add src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj reference src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj
dotnet add tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj reference src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj
dotnet add tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj reference src/PhotoPrivacy.Core/PhotoPrivacy.Core.csproj src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj
dotnet add src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj package Microsoft.Extensions.Hosting.WindowsServices
```

- [ ] **Step 2: 写失败测试（固定 ExifTool 路径）**

```csharp
using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Tests.Constants;

public sealed class DefaultPathsTests
{
    [Fact]
    public void ExifToolPath_Should_Be_The_Required_Absolute_Path()
    {
        Assert.Equal(
            @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe",
            DefaultPaths.ExifToolPath);
    }
}
```

- [ ] **Step 3: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter DefaultPathsTests`
Expected: FAIL，提示 `DefaultPaths` 不存在。

- [ ] **Step 4: 实现最小代码让测试通过**

```csharp
namespace PhotoPrivacy.Core.Constants;

public static class DefaultPaths
{
    public const string ExifToolPath = @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe";
}
```

- [ ] **Step 5: 再次运行测试**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter DefaultPathsTests`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add PhotoPrivacy.sln src/PhotoPrivacy.Core src/PhotoPrivacy.Cli src/PhotoPrivacy.Service tests/PhotoPrivacy.Core.Tests tests/PhotoPrivacy.IntegrationTests
git commit -m "chore: bootstrap solution and enforce default exiftool path"
```

---

### Task 2: 配置模型、加载与强校验

**Files:**
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfig.cs`
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfigLoader.cs`
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfigValidator.cs`
- Create: `src/PhotoPrivacy.Core/Configuration/AppConfigValidationException.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Configuration/AppConfigValidatorTests.cs`

- [ ] **Step 1: 写失败测试（路径必须绝对、关键参数不可覆盖、必须存在 exiftool_files）**

```csharp
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

public sealed class AppConfigValidatorTests
{
    [Fact]
    public void Validate_Should_Throw_When_ExifToolPath_Is_Not_Absolute()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { Path = "ExifTool.exe" }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Validate_Should_Throw_When_ExtraArgs_Contain_StayOpen()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { ExtraExifToolArgs = new[] { "-stay_open", "true" } }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Validate_Should_Throw_When_ExiftoolFiles_Directory_Missing()
    {
        var tempExe = Path.Combine(Path.GetTempPath(), "fake_exiftool.exe");
        File.WriteAllText(tempExe, "x");

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with { Path = tempExe }
            };

            Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter AppConfigValidatorTests`
Expected: FAIL，提示 `AppConfig` 与 `AppConfigValidator` 未定义。

- [ ] **Step 3: 实现配置模型**

```csharp
using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Configuration;

public sealed record ExifToolOptions(
    string Path,
    bool EnableWindowsLongPath,
    bool EnableLargeFileSupport,
    string[] ExtraExifToolArgs);

public sealed record WatchOptions(
    string HotFolder,
    bool IncludeSubdirectories,
    int DebounceMs,
    int InternalBufferSize);

public sealed record RuleOptions(
    string[] AllowedExtensions,
    string[] ExcludedPatterns,
    string OutputMode,
    string OutputDirectory);

public sealed record RetryOptions(int MaxAttempts, int[] BackoffSeconds);
public sealed record BackupOptions(bool Enabled, string Suffix, string Retention);
public sealed record QuarantineOptions(bool Enabled, string Directory);
public sealed record AuditOptions(string LogDirectory, int RetainDays, bool DiagnosticMode);

public sealed record AppConfig(
    int SchemaVersion,
    ExifToolOptions ExifTool,
    WatchOptions Watch,
    RuleOptions Rules,
    RetryOptions Retry,
    BackupOptions Backup,
    QuarantineOptions Quarantine,
    AuditOptions Audit)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        ExifTool: new ExifToolOptions(
            Path: DefaultPaths.ExifToolPath,
            EnableWindowsLongPath: true,
            EnableLargeFileSupport: true,
            ExtraExifToolArgs: Array.Empty<string>()),
        Watch: new WatchOptions(
            HotFolder: @"D:\hot",
            IncludeSubdirectories: true,
            DebounceMs: 800,
            InternalBufferSize: 65536),
        Rules: new RuleOptions(
            AllowedExtensions: new[] { ".jpg", ".jpeg", ".png", ".heic", ".mp4", ".pdf", ".docx" },
            ExcludedPatterns: new[] { "~$*", "*.tmp" },
            OutputMode: "same_as_source",
            OutputDirectory: string.Empty),
        Retry: new RetryOptions(MaxAttempts: 3, BackoffSeconds: new[] { 1, 3, 10 }),
        Backup: new BackupOptions(Enabled: false, Suffix: ".bak", Retention: "keep"),
        Quarantine: new QuarantineOptions(Enabled: true, Directory: @"D:\hot\_quarantine"),
        Audit: new AuditOptions(LogDirectory: @"D:\hot\_audit", RetainDays: 30, DiagnosticMode: false));
}
```

- [ ] **Step 4: 实现加载器与校验器**

```csharp
using System.Text.Json;

namespace PhotoPrivacy.Core.Configuration;

public static class AppConfigLoader
{
    public static AppConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("config.json not found", configPath);
        }

        var json = File.ReadAllText(configPath);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return cfg ?? AppConfig.Default;
    }
}

public sealed class AppConfigValidationException : Exception
{
    public AppConfigValidationException(string message) : base(message) { }
}

public static class AppConfigValidator
{
    private static readonly string[] ForbiddenExtraArgs =
    {
        "-stay_open", "-@", "-execute", "-echo1"
    };

    public static void Validate(AppConfig cfg)
    {
        if (!Path.IsPathFullyQualified(cfg.ExifTool.Path))
        {
            throw new AppConfigValidationException("exiftool.path must be absolute");
        }

        if (!File.Exists(cfg.ExifTool.Path))
        {
            throw new AppConfigValidationException("exiftool.path not found");
        }

        var exiftoolDir = Path.GetDirectoryName(cfg.ExifTool.Path)!;
        var exiftoolFilesDir = Path.Combine(exiftoolDir, "exiftool_files");
        if (!Directory.Exists(exiftoolFilesDir))
        {
            throw new AppConfigValidationException("exiftool_files directory missing");
        }

        if (cfg.ExifTool.ExtraExifToolArgs.Any(arg => ForbiddenExtraArgs.Contains(arg, StringComparer.OrdinalIgnoreCase)))
        {
            throw new AppConfigValidationException("extra_exiftool_args contains forbidden argument");
        }
    }
}
```

- [ ] **Step 5: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter AppConfigValidatorTests`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/PhotoPrivacy.Core/Configuration tests/PhotoPrivacy.Core.Tests/Configuration
git commit -m "feat(config): add config model, loader, and hard safety validation"
```

---

### Task 3: 审计日志（JSONL + 路径脱敏 + 保留策略）

**Files:**
- Create: `src/PhotoPrivacy.Core/Audit/AuditEvent.cs`
- Create: `src/PhotoPrivacy.Core/Audit/IAuditLogger.cs`
- Create: `src/PhotoPrivacy.Core/Audit/PathMasker.cs`
- Create: `src/PhotoPrivacy.Core/Audit/JsonLineAuditLogger.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Audit/JsonLineAuditLoggerTests.cs`

- [ ] **Step 1: 写失败测试（UTC、掩码、可写入）**

```csharp
using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.Tests.Audit;

public sealed class JsonLineAuditLoggerTests
{
    [Fact]
    public async Task WriteAsync_Should_Write_One_Json_Line_With_Utc_Timestamp_And_Masked_Path()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false);
            var ev = new AuditEvent(
                EventType: "file_processing_succeeded",
                TimestampUtc: DateTimeOffset.UtcNow,
                TaskId: "t-1",
                SourcePath: @"C:\Users\alice\Pictures\a.jpg",
                Message: "ok",
                Data: null);

            await logger.WriteAsync(ev, CancellationToken.None);

            var file = Directory.GetFiles(dir, "audit-*.jsonl").Single();
            var line = File.ReadLines(file).Single();
            Assert.Contains("file_processing_succeeded", line);
            Assert.Contains("<redacted>", line);
            Assert.DoesNotContain("alice", line, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter JsonLineAuditLoggerTests`
Expected: FAIL，提示 `JsonLineAuditLogger`/`AuditEvent` 未定义。

- [ ] **Step 3: 实现审计事件与掩码工具**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace PhotoPrivacy.Core.Audit;

public sealed record AuditEvent(
    string EventType,
    DateTimeOffset TimestampUtc,
    string TaskId,
    string SourcePath,
    string Message,
    IReadOnlyDictionary<string, string>? Data);

public interface IAuditLogger
{
    ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public static class PathMasker
{
    public static string Mask(string sourcePath)
    {
        var masked = sourcePath.Replace(@"\Users\", @"\Users\<redacted>\", StringComparison.OrdinalIgnoreCase);
        return masked;
    }

    public static string StableHash(string sourcePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath));
        return Convert.ToHexString(bytes[..8]);
    }
}
```

- [ ] **Step 4: 实现 JSONL 审计日志器**

```csharp
using System.Text.Json;

namespace PhotoPrivacy.Core.Audit;

public sealed class JsonLineAuditLogger : IAuditLogger
{
    private readonly string _logDirectory;
    private readonly int _retainDays;
    private readonly bool _diagnosticMode;

    public JsonLineAuditLogger(string logDirectory, int retainDays, bool diagnosticMode)
    {
        _logDirectory = logDirectory;
        _retainDays = retainDays;
        _diagnosticMode = diagnosticMode;
        Directory.CreateDirectory(_logDirectory);
    }

    public async ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
        var target = Path.Combine(_logDirectory, fileName);
        var payload = new
        {
            event_type = auditEvent.EventType,
            timestamp_utc = auditEvent.TimestampUtc.ToUniversalTime().ToString("O"),
            task_id = auditEvent.TaskId,
            source_path_masked = PathMasker.Mask(auditEvent.SourcePath),
            source_path_hash = PathMasker.StableHash(auditEvent.SourcePath),
            message = auditEvent.Message,
            data = auditEvent.Data,
            diagnostic_mode = _diagnosticMode
        };

        var json = JsonSerializer.Serialize(payload);
        await File.AppendAllTextAsync(target, json + Environment.NewLine, cancellationToken);
    }

    public void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retainDays);
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "audit-*.jsonl"))
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
```

- [ ] **Step 5: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter JsonLineAuditLoggerTests`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/PhotoPrivacy.Core/Audit tests/PhotoPrivacy.Core.Tests/Audit
git commit -m "feat(audit): add JSONL audit logger with path masking"
```

---

### Task 4: 规则引擎（白名单、输出策略、备份决策）

**Files:**
- Create: `src/PhotoPrivacy.Core/Rules/RuleDecision.cs`
- Create: `src/PhotoPrivacy.Core/Rules/RuleEngine.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Rules/RuleEngineTests.cs`

- [ ] **Step 1: 写失败测试（白名单命中、固定输出目录、.bak 备份路径）**

```csharp
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    [Fact]
    public void Decide_Should_Process_When_Extension_Is_Allowed()
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\a.jpg");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(@"D:\hot\a.jpg", decision.OutputPath);
    }

    [Fact]
    public void Decide_Should_Use_Fixed_Output_Directory_When_Configured()
    {
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = @"D:\clean"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = @"D:\hot" }
        };
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\album\a.jpg");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(@"D:\clean\album\a.jpg", decision.OutputPath);
    }

    [Fact]
    public void Decide_Should_Create_Bak_Path_When_Backup_Enabled()
    {
        var cfg = AppConfig.Default with { Backup = AppConfig.Default.Backup with { Enabled = true, Suffix = ".bak" } };
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\a.jpg");

        Assert.True(decision.CreateBackup);
        Assert.Equal(@"D:\hot\a.jpg.bak", decision.BackupPath);
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter RuleEngineTests`
Expected: FAIL，提示 `RuleEngine`/`RuleDecision` 未定义。

- [ ] **Step 3: 实现规则结果与规则引擎**

```csharp
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Rules;

public sealed record RuleDecision(
    bool ShouldProcess,
    string Reason,
    string? OutputPath,
    bool CreateBackup,
    string? BackupPath);

public sealed class RuleEngine
{
    private readonly AppConfig _config;

    public RuleEngine(AppConfig config)
    {
        _config = config;
    }

    public RuleDecision Decide(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (!_config.Rules.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return new RuleDecision(false, "extension_not_allowed", null, false, null);
        }

        foreach (var pattern in _config.Rules.ExcludedPatterns)
        {
            if (MatchesPattern(Path.GetFileName(sourcePath), pattern))
            {
                return new RuleDecision(false, "excluded_pattern", null, false, null);
            }
        }

        var outputPath = ResolveOutputPath(sourcePath);
        var createBackup = _config.Backup.Enabled;
        var backupPath = createBackup ? sourcePath + _config.Backup.Suffix : null;

        return new RuleDecision(true, "eligible", outputPath, createBackup, backupPath);
    }

    private string ResolveOutputPath(string sourcePath)
    {
        if (!string.Equals(_config.Rules.OutputMode, "fixed_directory", StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        var relative = Path.GetRelativePath(_config.Watch.HotFolder, sourcePath);
        return Path.Combine(_config.Rules.OutputDirectory, relative);
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "~$*") return fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase);
        if (pattern == "*.tmp") return fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
```

- [ ] **Step 4: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter RuleEngineTests`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/PhotoPrivacy.Core/Rules tests/PhotoPrivacy.Core.Tests/Rules
git commit -m "feat(rules): add whitelist output and backup decision engine"
```

---

### Task 5: 去抖队列 + 在途追踪 + 防重复缓存

**Files:**
- Create: `src/PhotoPrivacy.Core/Queue/FileFingerprint.cs`
- Create: `src/PhotoPrivacy.Core/Queue/InFlightRegistry.cs`
- Create: `src/PhotoPrivacy.Core/Queue/RecentFingerprintCache.cs`
- Create: `src/PhotoPrivacy.Core/Queue/DebounceQueue.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Queue/DebounceQueueTests.cs`

- [ ] **Step 1: 写失败测试（同一路径去抖只出队一次 + 完成后短期抑制）**

```csharp
using PhotoPrivacy.Core.Queue;

namespace PhotoPrivacy.Core.Tests.Queue;

public sealed class DebounceQueueTests
{
    [Fact]
    public void PopReady_Should_Return_Only_One_Item_For_Burst_Events()
    {
        var now = DateTimeOffset.UtcNow;
        var fakeNow = now;
        var queue = new DebounceQueue(TimeSpan.FromMilliseconds(800), () => fakeNow);

        queue.Enqueue(@"D:\hot\a.jpg");
        queue.Enqueue(@"D:\hot\a.jpg");
        queue.Enqueue(@"D:\hot\a.jpg");

        fakeNow = now.AddMilliseconds(900);
        var ready = queue.PopReady();

        Assert.Single(ready);
        Assert.Equal(@"D:\hot\a.jpg", ready[0]);
    }

    [Fact]
    public void RecentFingerprintCache_Should_Suppress_Duplicate_Output_Within_Ttl()
    {
        var cache = new RecentFingerprintCache(() => DateTimeOffset.UtcNow);
        var fp = new FileFingerprint(1024, new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc));

        cache.Remember(@"D:\hot\a.jpg", fp);
        var suppress = cache.ShouldSuppress(@"D:\hot\a.jpg", fp, TimeSpan.FromSeconds(5));

        Assert.True(suppress);
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter DebounceQueueTests`
Expected: FAIL，提示 `DebounceQueue` 等类型未定义。

- [ ] **Step 3: 实现去抖与去重组件**

```csharp
namespace PhotoPrivacy.Core.Queue;

public sealed record FileFingerprint(long Size, DateTime LastWriteUtc);

public sealed class InFlightRegistry
{
    private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool TryEnter(string path)
    {
        lock (_gate)
        {
            return _set.Add(path);
        }
    }

    public void Exit(string path)
    {
        lock (_gate)
        {
            _set.Remove(path);
        }
    }
}

public sealed class DebounceQueue
{
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public DebounceQueue(TimeSpan window, Func<DateTimeOffset> now)
    {
        _window = window;
        _now = now;
    }

    public void Enqueue(string path)
    {
        lock (_gate)
        {
            _events[path] = _now();
        }
    }

    public IReadOnlyList<string> PopReady()
    {
        var now = _now();
        var ready = new List<string>();

        lock (_gate)
        {
            foreach (var kvp in _events.ToArray())
            {
                if (now - kvp.Value >= _window)
                {
                    ready.Add(kvp.Key);
                    _events.Remove(kvp.Key);
                }
            }
        }

        return ready;
    }
}

public sealed class RecentFingerprintCache
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (FileFingerprint fp, DateTimeOffset seenAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public RecentFingerprintCache(Func<DateTimeOffset> now)
    {
        _now = now;
    }

    public void Remember(string path, FileFingerprint fp)
    {
        lock (_gate)
        {
            _cache[path] = (fp, _now());
        }
    }

    public bool ShouldSuppress(string path, FileFingerprint fp, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(path, out var entry))
            {
                return false;
            }

            if (_now() - entry.seenAt > ttl)
            {
                _cache.Remove(path);
                return false;
            }

            return entry.fp == fp;
        }
    }
}
```

- [ ] **Step 4: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter DebounceQueueTests`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/PhotoPrivacy.Core/Queue tests/PhotoPrivacy.Core.Tests/Queue
git commit -m "feat(queue): add debounce queue, in-flight registry, and dedupe cache"
```

---

### Task 6: ExifTool 命令构建与 Bridge（stay_open + TASK_DONE）

**Files:**
- Create: `src/PhotoPrivacy.Core/ExifTool/ExifToolCommandBuilder.cs`
- Create: `src/PhotoPrivacy.Core/ExifTool/IExifToolBridge.cs`
- Create: `src/PhotoPrivacy.Core/ExifTool/ExifToolBridge.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/ExifTool/ExifToolCommandBuilderTests.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/ExifTool/ExifToolBridgeTests.cs`

- [ ] **Step 1: 写失败测试（启动参数必须包含 stay_open / API；任务块必须包含 TASK_DONE）**

```csharp
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolCommandBuilderTests
{
    [Fact]
    public void BuildStartArguments_Should_Contain_StayOpen_And_Api_Flags()
    {
        var args = ExifToolCommandBuilder.BuildStartArguments(AppConfig.Default);

        Assert.Contains("-stay_open", args);
        Assert.Contains("true", args);
        Assert.Contains("-@", args);
        Assert.Contains("-", args);
        Assert.Contains("-API", args);
        Assert.Contains("WindowsLongPath=1", args);
        Assert.Contains("LargeFileSupport=1", args);
    }

    [Fact]
    public void BuildWipeTaskBlock_Should_Contain_TaskDone_And_Execute()
    {
        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(@"D:\hot\a.jpg", "123");

        Assert.Contains("-all=", block);
        Assert.Contains("-overwrite_original", block);
        Assert.Contains("-echo1 TASK_DONE_123", block);
        Assert.Contains("-execute", block);
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter ExifToolCommandBuilderTests`
Expected: FAIL，提示 `ExifToolCommandBuilder` 未定义。

- [ ] **Step 3: 实现命令构建器**

```csharp
using System.Text;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public static class ExifToolCommandBuilder
{
    public static string[] BuildStartArguments(AppConfig config)
    {
        var args = new List<string>
        {
            "-stay_open", "true",
            "-@", "-",
            "-q", "-q"
        };

        if (config.ExifTool.EnableWindowsLongPath)
        {
            args.AddRange(new[] { "-API", "WindowsLongPath=1" });
        }

        if (config.ExifTool.EnableLargeFileSupport)
        {
            args.AddRange(new[] { "-API", "LargeFileSupport=1" });
        }

        args.AddRange(config.ExifTool.ExtraExifToolArgs);
        return args.ToArray();
    }

    public static string BuildWipeTaskBlock(string targetPath, string taskId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-all=");
        sb.AppendLine("-overwrite_original");
        sb.AppendLine(targetPath);
        sb.AppendLine($"-echo1 TASK_DONE_{taskId}");
        sb.AppendLine("-execute");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: 写失败测试（Bridge 按 TASK_DONE 完成）**

```csharp
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolBridgeTests
{
    [Fact]
    public async Task WipeMetadataAsync_Should_Complete_When_TaskDone_Line_Arrives()
    {
        var process = new FakeExifToolProcess();
        var bridge = new ExifToolBridge(process, AppConfig.Default);
        await bridge.StartAsync(CancellationToken.None);

        var task = bridge.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);
        process.EmitStdout("TASK_DONE_1");

        await task;
    }
}
```

- [ ] **Step 5: 实现 Bridge 最小版本**

```csharp
using System.Collections.Concurrent;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public interface IExifToolBridge
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken);
}

public interface IExifToolProcess
{
    event Action<string>? StdoutLine;
    Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken);
    Task WriteStdinAsync(string text, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class ExifToolBridge : IExifToolBridge
{
    private readonly IExifToolProcess _process;
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private int _taskId;

    public ExifToolBridge(IExifToolProcess process, AppConfig config)
    {
        _process = process;
        _config = config;
        _process.StdoutLine += OnStdoutLine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _process.StartAsync(_config.ExifTool.Path, ExifToolCommandBuilder.BuildStartArguments(_config), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => _process.StopAsync(cancellationToken);

    public async Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _taskId).ToString();
        var doneMarker = $"TASK_DONE_{id}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[doneMarker] = tcs;

        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(targetPath, id);
        await _process.WriteStdinAsync(block, cancellationToken);
        await tcs.Task.WaitAsync(cancellationToken);
    }

    private void OnStdoutLine(string line)
    {
        foreach (var key in _pending.Keys)
        {
            if (line.Contains(key, StringComparison.Ordinal))
            {
                if (_pending.TryRemove(key, out var tcs))
                {
                    tcs.TrySetResult(true);
                }
            }
        }
    }
}
```

- [ ] **Step 6: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter ExifTool`
Expected: PASS。

- [ ] **Step 7: 提交**

```bash
git add src/PhotoPrivacy.Core/ExifTool tests/PhotoPrivacy.Core.Tests/ExifTool
git commit -m "feat(exiftool): add stay_open command builder and TASK_DONE bridge"
```

---

### Task 7: Pipeline 编排（重试、隔离、备份、审计）

**Files:**
- Create: `src/PhotoPrivacy.Core/Pipeline/IFileOperations.cs`
- Create: `src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Pipeline/FileTaskPipelineTests.cs`

- [ ] **Step 1: 写失败测试（首次失败重试，超过上限隔离）**

```csharp
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class FileTaskPipelineTests
{
    [Fact]
    public async Task HandleAsync_Should_Move_To_Quarantine_After_Max_Retries()
    {
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = new[] { 0, 0 } },
            Quarantine = AppConfig.Default.Quarantine with { Directory = @"D:\hot\_quarantine" }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit);

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == @"D:\hot\a.jpg" && m.Destination == @"D:\hot\_quarantine\a.jpg");
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter FileTaskPipelineTests`
Expected: FAIL，提示 `FileTaskPipeline` 未定义。

- [ ] **Step 3: 实现 Pipeline 与文件操作抽象**

```csharp
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Pipeline;

public interface IFileOperations
{
    void EnsureDirectory(string path);
    void Move(string source, string destination);
    void Copy(string source, string destination, bool overwrite);
}

public sealed class FileTaskPipeline
{
    private readonly AppConfig _config;
    private readonly RuleEngine _ruleEngine;
    private readonly IExifToolBridge _bridge;
    private readonly IFileOperations _fileOps;
    private readonly IAuditLogger _audit;

    public FileTaskPipeline(
        AppConfig config,
        RuleEngine ruleEngine,
        IExifToolBridge bridge,
        IFileOperations fileOps,
        IAuditLogger audit)
    {
        _config = config;
        _ruleEngine = ruleEngine;
        _bridge = bridge;
        _fileOps = fileOps;
        _audit = audit;
    }

    public async Task HandleAsync(string sourcePath, CancellationToken ct)
    {
        var decision = _ruleEngine.Decide(sourcePath);
        if (!decision.ShouldProcess || decision.OutputPath is null)
        {
            await _audit.WriteAsync(new AuditEvent("file_skipped", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, decision.Reason, null), ct);
            return;
        }

        if (decision.CreateBackup && decision.BackupPath is not null)
        {
            _fileOps.Copy(sourcePath, decision.BackupPath, overwrite: true);
        }

        var target = decision.OutputPath;
        var targetDir = Path.GetDirectoryName(target)!;
        _fileOps.EnsureDirectory(targetDir);

        for (var attempt = 1; attempt <= _config.Retry.MaxAttempts; attempt++)
        {
            try
            {
                await _bridge.WipeMetadataAsync(target, ct);
                await _audit.WriteAsync(new AuditEvent("file_processing_succeeded", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, $"attempt={attempt}", null), ct);
                return;
            }
            catch (Exception ex)
            {
                await _audit.WriteAsync(new AuditEvent("file_processing_failed", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, ex.Message, null), ct);
                if (attempt < _config.Retry.MaxAttempts)
                {
                    var delaySeconds = _config.Retry.BackoffSeconds[Math.Min(attempt - 1, _config.Retry.BackoffSeconds.Length - 1)];
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                    continue;
                }
            }
        }

        if (_config.Quarantine.Enabled)
        {
            var dst = Path.Combine(_config.Quarantine.Directory, Path.GetFileName(sourcePath));
            _fileOps.EnsureDirectory(_config.Quarantine.Directory);
            _fileOps.Move(sourcePath, dst);
            await _audit.WriteAsync(new AuditEvent("file_quarantined", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, dst, null), ct);
        }
    }
}
```

- [ ] **Step 4: 增加同目录输出防重复测试并实现（inflight + fingerprint）**

```csharp
[Fact]
public async Task HandleAsync_Should_Skip_When_File_Is_InFlight()
{
    var cfg = AppConfig.Default;
    var ruleEngine = new RuleEngine(cfg);
    var bridge = new SlowSuccessBridge();
    var fileOps = new InMemoryFileOperations();
    var audit = new InMemoryAuditLogger();
    var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit);

    var t1 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);
    var t2 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

    await Task.WhenAll(t1, t2);
    Assert.Equal(1, bridge.Calls);
}
```

实现点（插入 `FileTaskPipeline`）：

```csharp
private readonly HashSet<string> _inflight = new(StringComparer.OrdinalIgnoreCase);
private readonly object _gate = new();

private bool TryEnterInflight(string path)
{
    lock (_gate)
    {
        return _inflight.Add(path);
    }
}

private void ExitInflight(string path)
{
    lock (_gate)
    {
        _inflight.Remove(path);
    }
}
```

调用方式：`HandleAsync` 开头 `if (!TryEnterInflight(sourcePath)) return;`，`finally` 中 `ExitInflight(sourcePath)`。

- [ ] **Step 5: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter FileTaskPipelineTests`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/PhotoPrivacy.Core/Pipeline tests/PhotoPrivacy.Core.Tests/Pipeline
git commit -m "feat(pipeline): add retry quarantine backup and inflight safety"
```

---

### Task 8: FSW 监听器与 Error 恢复闭环

**Files:**
- Create: `src/PhotoPrivacy.Core/Watcher/IFolderWatcher.cs`
- Create: `src/PhotoPrivacy.Core/Watcher/FswFolderWatcher.cs`
- Test: `tests/PhotoPrivacy.Core.Tests/Watcher/FswFolderWatcherTests.cs`

- [ ] **Step 1: 写失败测试（Error 触发重建并补偿扫描）**

```csharp
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Tests.Watcher;

public sealed class FswFolderWatcherTests
{
    [Fact]
    public async Task Error_Should_Rebuild_And_Run_Full_Recovery_Scan()
    {
        var cfg = AppConfig.Default;
        var fakeFactory = new FakeWatcherFactory();
        var fakeScanner = new FakeRecoveryScanner(new[] { @"D:\hot\lost1.jpg", @"D:\hot\lost2.jpg" });
        var audit = new InMemoryAuditLogger();
        var collected = new List<string>();

        var watcher = new FswFolderWatcher(cfg, fakeFactory, fakeScanner, audit, path =>
        {
            collected.Add(path);
            return Task.CompletedTask;
        });

        watcher.Start();
        fakeFactory.Current!.RaiseError(new InternalBufferOverflowException("overflow"));

        await Task.Delay(50);

        Assert.Contains(@"D:\hot\lost1.jpg", collected);
        Assert.Contains(@"D:\hot\lost2.jpg", collected);
        Assert.Contains(audit.Events, e => e.EventType == "fsw_recovered");
        watcher.Stop();
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter FswFolderWatcherTests`
Expected: FAIL，提示 `FswFolderWatcher` 未定义。

- [ ] **Step 3: 实现监听接口与 FSW 包装器**

```csharp
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Watcher;

public interface IFolderWatcher
{
    void Start();
    void Stop();
}

public interface IRecoveryScanner
{
    IReadOnlyList<string> ScanAll(string rootPath);
}

public sealed class FswFolderWatcher : IFolderWatcher
{
    private readonly AppConfig _config;
    private readonly IRecoveryScanner _scanner;
    private readonly IAuditLogger _audit;
    private readonly Func<string, Task> _onPath;
    private FileSystemWatcher? _fsw;

    public FswFolderWatcher(
        AppConfig config,
        IRecoveryScanner scanner,
        IAuditLogger audit,
        Func<string, Task> onPath)
    {
        _config = config;
        _scanner = scanner;
        _audit = audit;
        _onPath = onPath;
    }

    public void Start()
    {
        _fsw = BuildWatcher();
        _fsw.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _fsw?.Dispose();
        _fsw = null;
    }

    private FileSystemWatcher BuildWatcher()
    {
        var fsw = new FileSystemWatcher(_config.Watch.HotFolder)
        {
            IncludeSubdirectories = _config.Watch.IncludeSubdirectories,
            InternalBufferSize = _config.Watch.InternalBufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName
        };

        fsw.Created += (_, e) => _ = _onPath(e.FullPath);
        fsw.Changed += (_, e) => _ = _onPath(e.FullPath);
        fsw.Renamed += (_, e) => _ = _onPath(e.FullPath);
        fsw.Error += async (_, e) => await RecoverAsync(e.GetException());

        return fsw;
    }

    private async Task RecoverAsync(Exception ex)
    {
        await _audit.WriteAsync(new AuditEvent("fsw_error", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, ex.Message, null), CancellationToken.None);

        Stop();
        Start();

        foreach (var path in _scanner.ScanAll(_config.Watch.HotFolder))
        {
            await _onPath(path);
        }

        await _audit.WriteAsync(new AuditEvent("fsw_recovered", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, "recreated_watcher", null), CancellationToken.None);
    }
}
```

- [ ] **Step 4: 运行测试并确认通过**

Run: `dotnet test tests/PhotoPrivacy.Core.Tests/PhotoPrivacy.Core.Tests.csproj --filter FswFolderWatcherTests`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/PhotoPrivacy.Core/Watcher tests/PhotoPrivacy.Core.Tests/Watcher
git commit -m "feat(watcher): add fsw event watcher with overflow recovery scan"
```

---

### Task 9: CLI/Service 宿主接线、样例配置与验收脚本

**Files:**
- Create: `src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs`
- Create: `src/PhotoPrivacy.Cli/Program.cs`
- Create: `src/PhotoPrivacy.Service/Program.cs`
- Create: `config/config.sample.json`
- Create: `README.md`
- Create: `scripts/smoke.ps1`
- Create: `scripts/benchmark.ps1`
- Test: `tests/PhotoPrivacy.IntegrationTests/Smoke/EndToEndSmokeTests.cs`

- [ ] **Step 1: 写失败集成测试（CLI 启动后可处理一份样例文件）**

```csharp
namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class EndToEndSmokeTests
{
    [Fact]
    public async Task CliHost_Should_Process_One_File_And_Write_Audit_Event()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var hot = Path.Combine(root, "hot");
            var audit = Path.Combine(root, "audit");
            Directory.CreateDirectory(hot);
            Directory.CreateDirectory(audit);
            File.WriteAllText(Path.Combine(hot, "a.jpg"), "dummy");

            // 这里通过后续实现的 smoke.ps1 启动 CLI 并等待一次处理
            var script = Path.GetFullPath("scripts/smoke.ps1");
            var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{script}\" -HotFolder \"{hot}\" -AuditFolder \"{audit}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var lines = File.ReadAllLines(auditFile);
            Assert.Contains(lines, l => l.Contains("file_processing_succeeded", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/PhotoPrivacy.IntegrationTests/PhotoPrivacy.IntegrationTests.csproj --filter EndToEndSmokeTests`
Expected: FAIL，提示 `scripts/smoke.ps1` 或宿主未实现。

- [ ] **Step 3: 实现 Worker 与 CLI 宿主**

`src/PhotoPrivacy.Cli/Program.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<MetadataCleanerWorker>();
var app = builder.Build();
await app.RunAsync();
```

`src/PhotoPrivacy.Service/Program.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "PhotoPrivacyCleaner");
builder.Services.AddHostedService<MetadataCleanerWorker>();
var app = builder.Build();
await app.RunAsync();
```

- [ ] **Step 4: 提供样例配置和脚本**

`config/config.sample.json`
```json
{
  "schema_version": 1,
  "exiftool": {
    "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
    "enable_windows_long_path": true,
    "enable_large_file_support": true,
    "extra_exiftool_args": []
  },
  "watch": {
    "hot_folder": "D:\\hot",
    "include_subdirectories": true,
    "debounce_ms": 800,
    "internal_buffer_size": 65536
  },
  "rules": {
    "allowed_extensions": [".jpg", ".jpeg", ".png", ".heic", ".mp4", ".pdf", ".docx"],
    "excluded_patterns": ["~$*", "*.tmp"],
    "output_mode": "same_as_source",
    "output_directory": ""
  },
  "retry": { "max_attempts": 3, "backoff_seconds": [1, 3, 10] },
  "backup": { "enabled": false, "suffix": ".bak", "retention": "keep" },
  "quarantine": { "enabled": true, "directory": "D:\\hot\\_quarantine" },
  "audit": { "log_directory": "D:\\hot\\_audit", "retain_days": 30, "diagnostic_mode": false }
}
```

`scripts/smoke.ps1`
```powershell
param(
  [Parameter(Mandatory=$true)][string]$HotFolder,
  [Parameter(Mandatory=$true)][string]$AuditFolder
)

Write-Host "Smoke test start"
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --hot-folder "$HotFolder" --audit-folder "$AuditFolder" --once
if ($LASTEXITCODE -ne 0) { throw "CLI failed" }
Write-Host "Smoke test done"
```

`scripts/benchmark.ps1`
```powershell
param(
  [string]$HotFolder = "D:\hot",
  [int]$SmallCount = 1000,
  [int]$SmallKb = 100
)

New-Item -ItemType Directory -Force -Path $HotFolder | Out-Null
for ($i=0; $i -lt $SmallCount; $i++) {
  $path = Join-Path $HotFolder ("small-{0:D4}.jpg" -f $i)
  $bytes = New-Object byte[] ($SmallKb * 1024)
  [System.IO.File]::WriteAllBytes($path, $bytes)
}
Write-Host "Generated $SmallCount files in $HotFolder"
```

- [ ] **Step 5: 编写 README 运行说明与验收命令**

`README.md`
```markdown
# PhotoPrivacy Cleaner (MVP-1)

## Quick start
1. 复制 `config/config.sample.json` 为 `config/config.json` 并修改热目录。
2. 确认 `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe` 和同级 `exiftool_files` 存在。
3. 运行 CLI：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj
```

4. 运行 Service（管理员）：

```bash
sc create PhotoPrivacyCleaner binPath= "<publish_path>\\PhotoPrivacy.Service.exe"
sc start PhotoPrivacyCleaner
```

## Verification
```bash
dotnet test PhotoPrivacy.sln
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -HotFolder D:\hot -AuditFolder D:\hot\_audit
```
```

- [ ] **Step 6: 运行全集测试**

Run: `dotnet test PhotoPrivacy.sln`
Expected: PASS（Core + Integration）。

- [ ] **Step 7: 提交**

```bash
git add src/PhotoPrivacy.Core/Worker src/PhotoPrivacy.Cli/Program.cs src/PhotoPrivacy.Service/Program.cs config/config.sample.json scripts README.md tests/PhotoPrivacy.IntegrationTests
git commit -m "feat(host): wire cli and windows service with smoke and benchmark docs"
```

---

## Final Verification Checklist

- [ ] `dotnet build PhotoPrivacy.sln`
- [ ] `dotnet test PhotoPrivacy.sln`
- [ ] `dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --once`
- [ ] 确认只有单个 ExifTool 持久进程（不得每文件 spawn）
- [ ] 制造 FSW overflow 场景并验证 `fsw_recovered` 审计事件
- [ ] 验证同目录输出不会循环重复处理
- [ ] 验证失败重试后隔离并写二次审计

## Spec Coverage Map

- 架构分层（Watcher -> DebounceQueue -> Pipeline -> ExifToolBridge）：Task 4/5/6/7/8/9
- 固定 ExifTool 路径 + 可配置覆写 + 启动校验：Task 1/2
- `stay_open` + `TASK_DONE` 协议：Task 6
- FSW Error 恢复 + 全量补偿扫描：Task 8
- 写入策略、重试后隔离、`.bak` 备份：Task 7
- JSONL 审计 + UTC + 路径脱敏：Task 3
- 双宿主（CLI + Service）：Task 9
- 验收与基准脚本：Task 9 + Final Verification Checklist
