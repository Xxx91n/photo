using System.Text.Json;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

/// <summary>
/// 票 17 — 配置 round-trip 对称性锁定：AppConfig.Default → AppConfigJson.ToIndentedJson
/// → AppConfigLoader.Load 必须逐字段还原（含此前为死字段的 ui.sidebar_width 与
/// backup.retain_days），并断言 config.sample.json 关键取值与 AppConfig.Default 一致，
/// 防止未来新增字段再悄悄变成死字段。
/// </summary>
public sealed class AppConfigRoundTripTests
{
    private static string RepoRoot => FindRepoRoot();

    [Fact]
    public void RoundTrip_Should_Preserve_Every_Field_Of_Default()
    {
        var cfg = AppConfig.Default;
        var json = AppConfigJson.ToIndentedJson(cfg);
        var path = WriteTempConfig(json);

        try
        {
            var reloaded = AppConfigLoader.Load(path);

            Assert.Equal(cfg.SchemaVersion, reloaded.SchemaVersion);

            // 含数组的段：record 相等对数组按引用比较，round-trip 后是新实例，须逐字段断言
            Assert.Equal(cfg.ExifTool.Path, reloaded.ExifTool.Path);
            Assert.Equal(cfg.ExifTool.EnableWindowsLongPath, reloaded.ExifTool.EnableWindowsLongPath);
            Assert.Equal(cfg.ExifTool.EnableLargeFileSupport, reloaded.ExifTool.EnableLargeFileSupport);
            Assert.Equal(cfg.ExifTool.DryRun, reloaded.ExifTool.DryRun);
            Assert.Equal(cfg.ExifTool.ExtraExifToolArgs, reloaded.ExifTool.ExtraExifToolArgs);
            Assert.Equal(cfg.ExifTool.StayOpenPoolSize, reloaded.ExifTool.StayOpenPoolSize);
            Assert.Equal(cfg.ExifTool.MaxParallelDrain, reloaded.ExifTool.MaxParallelDrain);

            Assert.Equal(cfg.Watch.HotFolder, reloaded.Watch.HotFolder);
            Assert.Equal(cfg.Watch.IncludeSubdirectories, reloaded.Watch.IncludeSubdirectories);
            Assert.Equal(cfg.Watch.DebounceMs, reloaded.Watch.DebounceMs);
            Assert.Equal(cfg.Watch.InternalBufferSize, reloaded.Watch.InternalBufferSize);
            Assert.Equal(cfg.Watch.AutoExcludedDirectories, reloaded.Watch.AutoExcludedDirectories);
            Assert.Equal(cfg.Watch.PollingIntervalSeconds, reloaded.Watch.PollingIntervalSeconds);

            Assert.Equal(cfg.Rules.AllowedExtensions, reloaded.Rules.AllowedExtensions);
            Assert.Equal(cfg.Rules.ExcludedPatterns, reloaded.Rules.ExcludedPatterns);
            Assert.Equal(cfg.Rules.OutputMode, reloaded.Rules.OutputMode);
            Assert.Equal(cfg.Rules.OutputDirectory, reloaded.Rules.OutputDirectory);

            Assert.Equal(cfg.Retry.MaxAttempts, reloaded.Retry.MaxAttempts);
            Assert.Equal(cfg.Retry.BackoffSeconds, reloaded.Retry.BackoffSeconds);

            Assert.Equal(cfg.Backup, reloaded.Backup);
            Assert.Equal(cfg.Quarantine, reloaded.Quarantine);
            Assert.Equal(cfg.Audit, reloaded.Audit);
            Assert.Equal(cfg.Ui, reloaded.Ui);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void RoundTrip_Should_Preserve_Modified_SidebarWidth_And_BackupRetainDays()
    {
        var cfg = AppConfig.Default with
        {
            Backup = AppConfig.Default.Backup with { RetainDays = 7 },
            Ui = AppConfig.Default.Ui with { SidebarWidth = 356.5 }
        };
        var json = AppConfigJson.ToIndentedJson(cfg);
        var path = WriteTempConfig(json);

        try
        {
            var reloaded = AppConfigLoader.Load(path);

            Assert.Equal(7, reloaded.Backup.RetainDays);
            Assert.Equal(356.5, reloaded.Ui.SidebarWidth);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void RoundTrip_Should_Preserve_SidebarWidth_When_Only_Key_Present()
    {
        var json = """
        {
          "schema_version": 1,
          "ui": {
            "sidebar_width": 312.0
          }
        }
        """;
        var path = WriteTempConfig(json);

        try
        {
            var reloaded = AppConfigLoader.Load(path);

            Assert.Equal(312.0, reloaded.Ui.SidebarWidth);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ToIndentedJson_Should_Emit_SidebarWidth_And_BackupRetainDays_Keys()
    {
        var json = AppConfigJson.ToIndentedJson(AppConfig.Default);

        Assert.Contains("\"sidebar_width\": 200", json, StringComparison.Ordinal);
        Assert.Contains("\"retain_days\": 30", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToIndentedJson_Should_Not_Emit_Dead_Retention_Key()
    {
        var json = AppConfigJson.ToIndentedJson(AppConfig.Default);

        Assert.DoesNotContain("\"retention\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 票 27 B 检查点 — 收敛 3 处双 DTO 漂移点:Backup.Suffix / Ui.ThemeVariant / Ui.Locale
    /// 写侧默认值必须等于读侧 AppConfigLoader 默认值,确保磁盘 JSON 永远带语义值(不出现空串).
    /// </summary>
    [Theory]
    [InlineData("\"suffix\"", "\".bak\"")]
    [InlineData("\"theme_variant\"", "\"system\"")]
    [InlineData("\"locale\"", "\"zh-CN\"")]
    [InlineData("\"theme_id\"", "\"catppuccin\"")]
    [InlineData("\"sidebar_width\"", "200")]
    public void ToIndentedJson_Should_Emit_Aligned_Defaults_For_Triple_Drift_Points(string keyFragment, string valueFragment)
    {
        var json = AppConfigJson.ToIndentedJson(AppConfig.Default);

        Assert.Contains(keyFragment, json, StringComparison.Ordinal);
        Assert.Contains(valueFragment, json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 票 27 B — 读侧空白兜底:手工编辑漏填关键字段后,AppConfigLoader 必须回填到 AppConfig.Default
    /// 语义值,而不是传空串到下游路径拼接或主题切换逻辑里.
    /// </summary>
    [Theory]
    [InlineData("\"suffix\"", "\".bak\"")]
    [InlineData("\"theme_variant\"", "\"system\"")]
    [InlineData("\"locale\"", "\"zh-CN\"")]
    public void Loader_Should_Fallback_To_Default_When_Field_Is_Missing(string droppedKey, string expectedValueFragment)
    {
        var json = AppConfigJson.ToIndentedJson(AppConfig.Default);
        // 从 JSON 里删掉指定的关键字段,模拟"手工编辑漏填"
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            json,
            $"\\s*\"{droppedKey.Trim('"')}\"\\s*:\\s*\"[^\"]*\",?",
            string.Empty);
        var path = WriteTempConfig(stripped);

        try
        {
            var reloaded = AppConfigLoader.Load(path);
            var roundTripJson = AppConfigJson.ToIndentedJson(reloaded);
            Assert.Contains(expectedValueFragment, roundTripJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    /// <summary>
    /// spec Testing Decisions：断言 sample 与 AppConfig.Default 关键取值一致
    /// （audit.diagnostic_mode/log_level 此前与代码默认相反）。
    /// </summary>
    [Fact]
    public void Sample_Should_Match_Default_On_Key_Configurable_Values()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "config", "config.sample.json")));
        var root = doc.RootElement;

        Assert.Equal(AppConfig.Default.SchemaVersion, root.GetProperty("schema_version").GetInt32());

        var exiftool = root.GetProperty("exiftool");
        Assert.Equal(AppConfig.Default.ExifTool.EnableWindowsLongPath, exiftool.GetProperty("enable_windows_long_path").GetBoolean());
        Assert.Equal(AppConfig.Default.ExifTool.EnableLargeFileSupport, exiftool.GetProperty("enable_large_file_support").GetBoolean());
        Assert.Equal(AppConfig.Default.ExifTool.DryRun, exiftool.GetProperty("dry_run").GetBoolean());
        Assert.Equal(AppConfig.Default.ExifTool.StayOpenPoolSize, exiftool.GetProperty("stay_open_pool_size").GetInt32());
        Assert.Equal(AppConfig.Default.ExifTool.MaxParallelDrain, exiftool.GetProperty("max_parallel_drain").GetInt32());

        var watch = root.GetProperty("watch");
        Assert.Equal(AppConfig.Default.Watch.IncludeSubdirectories, watch.GetProperty("include_subdirectories").GetBoolean());
        Assert.Equal(AppConfig.Default.Watch.DebounceMs, watch.GetProperty("debounce_ms").GetInt32());
        Assert.Equal(AppConfig.Default.Watch.InternalBufferSize, watch.GetProperty("internal_buffer_size").GetInt32());
        Assert.Equal(AppConfig.Default.Watch.PollingIntervalSeconds, watch.GetProperty("polling_interval_seconds").GetInt32());

        var rules = root.GetProperty("rules");
        Assert.Equal(AppConfig.Default.Rules.OutputMode, rules.GetProperty("output_mode").GetString());

        var retry = root.GetProperty("retry");
        Assert.Equal(AppConfig.Default.Retry.MaxAttempts, retry.GetProperty("max_attempts").GetInt32());

        var backup = root.GetProperty("backup");
        Assert.Equal(AppConfig.Default.Backup.Enabled, backup.GetProperty("enabled").GetBoolean());
        Assert.Equal(AppConfig.Default.Backup.Suffix, backup.GetProperty("suffix").GetString());
        Assert.Equal(AppConfig.Default.Backup.MaxSizeMb, backup.GetProperty("max_size_mb").GetInt32());
        Assert.Equal(AppConfig.Default.Backup.RetainDays, backup.GetProperty("retain_days").GetInt32());

        var quarantine = root.GetProperty("quarantine");
        Assert.Equal(AppConfig.Default.Quarantine.Enabled, quarantine.GetProperty("enabled").GetBoolean());

        var audit = root.GetProperty("audit");
        Assert.Equal(AppConfig.Default.Audit.RetainDays, audit.GetProperty("retain_days").GetInt32());
        Assert.False(backup.TryGetProperty("retention", out _), "config.sample.json 不应再含死键 backup.retention");
        Assert.Equal(AppConfig.Default.Audit.DiagnosticMode, audit.GetProperty("diagnostic_mode").GetBoolean());
        Assert.Equal(AppConfig.Default.Audit.LogLevel, audit.GetProperty("log_level").GetString());

        var ui = root.GetProperty("ui");
        Assert.Equal(AppConfig.Default.Ui.HideMainWindowOnStartup, ui.GetProperty("hide_main_window_on_startup").GetBoolean());
        Assert.Equal(AppConfig.Default.Ui.HideTrayIcon, ui.GetProperty("hide_tray_icon").GetBoolean());
        Assert.Equal(AppConfig.Default.Ui.ThemeVariant, ui.GetProperty("theme_variant").GetString());
        Assert.Equal(AppConfig.Default.Ui.ThemeId, ui.GetProperty("theme_id").GetString());
        Assert.Equal(AppConfig.Default.Ui.SidebarWidth, ui.GetProperty("sidebar_width").GetDouble());
    }

    private static string WriteTempConfig(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PhotoPrivacy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("PhotoPrivacy.sln not found upward from " + AppContext.BaseDirectory);
    }
}
