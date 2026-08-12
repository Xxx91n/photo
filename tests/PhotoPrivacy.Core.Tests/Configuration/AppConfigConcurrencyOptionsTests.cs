using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

public sealed class AppConfigConcurrencyOptionsTests
{
    [Fact]
    public void Load_Should_Map_ExifTool_Concurrency_Fields()
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
                "extra_exiftool_args": [],
                "stay_open_pool_size": 4,
                "max_parallel_drain": 6
              }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);

            Assert.Equal(4, cfg.ExifTool.StayOpenPoolSize);
            Assert.Equal(6, cfg.ExifTool.MaxParallelDrain);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ToIndentedJson_Should_Emit_ExifTool_Concurrency_Fields()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                StayOpenPoolSize = 3,
                MaxParallelDrain = 5
            }
        };

        var json = AppConfigJson.ToIndentedJson(cfg);

        Assert.Contains("\"stay_open_pool_size\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"max_parallel_drain\": 5", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Should_Map_Ui_ThemeVariant_Field()
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
                "extra_exiftool_args": [],
                "stay_open_pool_size": 1,
                "max_parallel_drain": 1
              },
              "ui": {
                "hide_main_window_on_startup": false,
                "hide_tray_icon": false,
                "theme_variant": "dark"
              }
            }
            """);

            var cfg = AppConfigLoader.Load(configPath);
            Assert.Equal("dark", cfg.Ui.ThemeVariant);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ToIndentedJson_Should_Emit_Ui_ThemeVariant_Field()
    {
        var cfg = AppConfig.Default with
        {
            Ui = AppConfig.Default.Ui with { ThemeVariant = "light" }
        };

        var json = AppConfigJson.ToIndentedJson(cfg);
        Assert.Contains("\"theme_variant\": \"light\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Validate_Should_Throw_When_Concurrency_Options_Are_Invalid(int poolSize, int maxParallelDrain)
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = true,
                StayOpenPoolSize = poolSize,
                MaxParallelDrain = maxParallelDrain
            }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }
}
