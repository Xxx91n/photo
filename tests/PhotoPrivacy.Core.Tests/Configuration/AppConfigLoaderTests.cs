using PhotoPrivacy.Core.Configuration;

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
                "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
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
                "suffix": ".bak",
                "retention": "keep"
              },
              "quarantine": {
                "enabled": true,
                "directory": "D:\\hot\\_quarantine"
              },
              "audit": {
                "log_directory": "D:\\hot\\_audit",
                "retain_days": 30,
                "diagnostic_mode": true
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
                "suffix": ".bak",
                "retention": "keep"
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

            Assert.Equal(@"D:\legacy\ExifTool.exe", cfg.ExifTool.Path);
            Assert.Equal(1, cfg.SchemaVersion);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
