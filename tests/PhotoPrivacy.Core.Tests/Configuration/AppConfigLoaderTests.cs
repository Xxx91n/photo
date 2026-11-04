using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Tests.Configuration;

public sealed class AppConfigLoaderTests
{
    [Fact]
    public void Load_Should_Map_SnakeCase_Json_Fields()
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
                "path": "C:\\Program Files\\ExifTool\\exiftool.exe",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": true,
                "extra_exiftool_args": ["-charset", "filename=utf8"]
              },
              "watch": {
                "hot_folder": "D:\\hot",
                "include_subdirectories": true,
                "debounce_ms": 900,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": ["*.tmp"],
                "output_mode": "same_as_source",
                "output_directory": ""
              },
              "retry": {
                "max_attempts": 2,
                "backoff_seconds": [1, 2]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak"
              },
              "quarantine": {
                "enabled": true,
                "directory": "D:\\hot\\_quarantine"
              },
              "audit": {
                "log_directory": "D:\\hot\\_audit",
                "retain_days": 30,
                "diagnostic_mode": true
              },
              "ui": {
                "hide_main_window_on_startup": true
              }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.True(cfg.ExifTool.EnableWindowsLongPath);
            Assert.True(cfg.ExifTool.EnableLargeFileSupport);
            Assert.True(cfg.ExifTool.DryRun);
            Assert.Equal(2, cfg.ExifTool.ExtraExifToolArgs.Length);
            Assert.Equal(900, cfg.Watch.DebounceMs);
            Assert.True(cfg.Audit.DiagnosticMode);
            Assert.True(cfg.Ui.HideMainWindowOnStartup);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // #33: exiftool.path 给一个已安装的 ExifTool 目录（ExifToolGUI 的 ETOverrideDir 口径），
    // 加载后应归一到该目录内的 exiftool/exiftool.exe 绝对路径，供校验器与 Worker 直接使用。
    [Fact]
    public void Load_Should_Resolve_Directory_ExifToolPath_To_Contained_Executable()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var overrideDir = Path.Combine(dir, "custom-exiftool");
        Directory.CreateDirectory(overrideDir);
        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        File.WriteAllText(Path.Combine(overrideDir, exeName), "stub");
        // Windows 临时路径含反斜杠，直接插入 JSON 会破坏字符串；交给序列化器生成带引号的合法字面量。
        var overridePathJson = System.Text.Json.JsonSerializer.Serialize(overrideDir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": { "path": {{overridePathJson}} },
              "watch": { "hot_folder": "D:\\hot" },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "same_as_source",
                "output_directory": ""
              },
              "retry": { "max_attempts": 1, "backoff_seconds": [0] },
              "backup": { "enabled": false, "suffix": ".bak" },
              "quarantine": { "enabled": true, "directory": "D:\\hot\\_quarantine" },
              "audit": { "log_directory": "D:\\hot\\_audit" }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(Path.Combine(overrideDir, exeName), cfg.ExifTool.Path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_Should_Migrate_Legacy_Root_ExiftoolPath_Field()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
            {
              "schema_version": 0,
              "exiftool_path": "D:\\legacy\\ExifTool.exe",
              "watch": {
                "hot_folder": "D:\\hot",
                "include_subdirectories": true,
                "debounce_ms": 800,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "same_as_source",
                "output_directory": ""
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak"
              },
              "quarantine": {
                "enabled": true,
                "directory": "D:\\hot\\_quarantine"
              },
              "audit": {
                "log_directory": "D:\\hot\\_audit",
                "retain_days": 30,
                "diagnostic_mode": false
              },
              "ui": {
                "hide_main_window_on_startup": false
              }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(@"D:\legacy\ExifTool.exe", cfg.ExifTool.Path);
            Assert.Equal(1, cfg.SchemaVersion);
            Assert.False(cfg.Ui.HideMainWindowOnStartup);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_Should_Default_HideMainWindowOnStartup_To_False_When_Ui_Section_Missing()
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
                "path": "C:\\Program Files\\ExifTool\\exiftool.exe",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": false,
                "extra_exiftool_args": []
              },
              "watch": {
                "hot_folder": "D:\\hot",
                "include_subdirectories": true,
                "debounce_ms": 800,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "same_as_source",
                "output_directory": ""
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak"
              },
              "quarantine": {
                "enabled": true,
                "directory": "D:\\hot\\_quarantine"
              },
              "audit": {
                "log_directory": "D:\\hot\\_audit",
                "retain_days": 30,
                "diagnostic_mode": false
              }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.False(cfg.Ui.HideMainWindowOnStartup);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // exiftool.path 空串回落运行时探测（Design(#108)）：
    // 发布包内置 ExifTool 后模板不再写死绝对路径，留空 = 优先 <BaseDirectory>/ExifTool/。
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Load_Should_Fall_Back_To_Runtime_Probe_When_ExifTool_Path_Is_Empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
            {
              "schema_version": 1,
              "exiftool": { "path": "" },
              "watch": { "hot_folder": "D:\\hot" }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(DefaultPaths.ExifToolPath, cfg.ExifTool.Path);
            Assert.False(string.IsNullOrWhiteSpace(cfg.ExifTool.Path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_Should_Keep_Explicit_ExifTool_Path()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            var explicitPath = Path.Combine(dir, "custom", "exiftool.exe");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": { "path": "{{explicitPath.Replace("\\", "\\\\")}}" },
              "watch": { "hot_folder": "D:\\hot" }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(explicitPath, cfg.ExifTool.Path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 空配置兜底 —— quarantine.directory / audit.log_directory 空串回落 DefaultPaths。
    // 原 bug：空串直传 → Path.Combine("", 文件名) 退化成相对路径 → 隔离/审计文件落进进程 CWD
    //（config.sample.json 这两项就是空串，跟着 README 第一步走即命中）。
    // 断言口径：回落值必须是 DefaultPaths 的绝对路径 —— 原 bug 下是空串（非绝对路径）。
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Load_Should_Fall_Back_To_DefaultPaths_When_Quarantine_And_Audit_Directories_Are_Empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
            {
              "schema_version": 1,
              "watch": { "hot_folder": "D:\\hot" },
              "quarantine": { "enabled": true, "directory": "" },
              "audit": { "log_directory": "", "retain_days": 30 }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(DefaultPaths.DefaultQuarantineDirectory, cfg.Quarantine.Directory);
            Assert.Equal(DefaultPaths.DefaultAuditLogDirectory, cfg.Audit.LogDirectory);

            Assert.True(Path.IsPathFullyQualified(cfg.Quarantine.Directory),
                $"quarantine.directory 必须是绝对路径，实际：'{cfg.Quarantine.Directory}'");
            Assert.True(Path.IsPathFullyQualified(cfg.Audit.LogDirectory),
                $"audit.log_directory 必须是绝对路径，实际：'{cfg.Audit.LogDirectory}'");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_Should_Fall_Back_To_DefaultPaths_When_Directories_Are_Whitespace()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
            {
              "schema_version": 1,
              "quarantine": { "directory": "   " },
              "audit": { "log_directory": "\t" }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(DefaultPaths.DefaultQuarantineDirectory, cfg.Quarantine.Directory);
            Assert.Equal(DefaultPaths.DefaultAuditLogDirectory, cfg.Audit.LogDirectory);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_Should_Keep_Explicit_Quarantine_And_Audit_Directories()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
            {
              "schema_version": 1,
              "quarantine": { "directory": "D:\\hot\\_quarantine" },
              "audit": { "log_directory": "D:\\hot\\_audit" }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(@"D:\hot\_quarantine", cfg.Quarantine.Directory);
            Assert.Equal(@"D:\hot\_audit", cfg.Audit.LogDirectory);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
