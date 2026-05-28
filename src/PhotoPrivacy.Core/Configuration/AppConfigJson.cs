using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoPrivacy.Core.Configuration;

public static class AppConfigJson
{
    public static string ToIndentedJson(AppConfig config)
    {
        var dto = new AppConfigDto
        {
            SchemaVersion = config.SchemaVersion,
            ExifTool = new ExifToolDto
            {
                Path = config.ExifTool.Path,
                EnableWindowsLongPath = config.ExifTool.EnableWindowsLongPath,
                EnableLargeFileSupport = config.ExifTool.EnableLargeFileSupport,
                DryRun = config.ExifTool.DryRun,
                ExtraExifToolArgs = config.ExifTool.ExtraExifToolArgs ?? [],
                StayOpenPoolSize = config.ExifTool.StayOpenPoolSize,
                MaxParallelDrain = config.ExifTool.MaxParallelDrain
            },
            Watch = new WatchDto
            {
                HotFolder = config.Watch.HotFolder,
                IncludeSubdirectories = config.Watch.IncludeSubdirectories,
                DebounceMs = config.Watch.DebounceMs,
                InternalBufferSize = config.Watch.InternalBufferSize,
                AutoExcludedDirectories = config.Watch.AutoExcludedDirectories ?? [],
                PollingIntervalSeconds = config.Watch.PollingIntervalSeconds
            },
            Rules = new RulesDto
            {
                AllowedExtensions = config.Rules.AllowedExtensions ?? [],
                ExcludedPatterns = config.Rules.ExcludedPatterns ?? [],
                OutputMode = config.Rules.OutputMode,
                OutputDirectory = config.Rules.OutputDirectory
            },
            Retry = new RetryDto
            {
                MaxAttempts = config.Retry.MaxAttempts,
                BackoffSeconds = config.Retry.BackoffSeconds ?? []
            },
            Backup = new BackupDto
            {
                Enabled = config.Backup.Enabled,
                Directory = config.Backup.Directory,
                Suffix = config.Backup.Suffix,
                Retention = config.Backup.Retention
            },
            Quarantine = new QuarantineDto
            {
                Enabled = config.Quarantine.Enabled,
                Directory = config.Quarantine.Directory
            },
            Audit = new AuditDto
            {
                LogDirectory = config.Audit.LogDirectory,
                RetainDays = config.Audit.RetainDays,
                DiagnosticMode = config.Audit.DiagnosticMode
            },
            Ui = new UiDto
            {
                HideMainWindowOnStartup = config.Ui.HideMainWindowOnStartup,
                HideTrayIcon = config.Ui.HideTrayIcon,
                ThemeVariant = config.Ui.ThemeVariant
            }
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private sealed class AppConfigDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("exiftool")]
        public ExifToolDto ExifTool { get; init; } = new();

        [JsonPropertyName("watch")]
        public WatchDto Watch { get; init; } = new();

        [JsonPropertyName("rules")]
        public RulesDto Rules { get; init; } = new();

        [JsonPropertyName("retry")]
        public RetryDto Retry { get; init; } = new();

        [JsonPropertyName("backup")]
        public BackupDto Backup { get; init; } = new();

        [JsonPropertyName("quarantine")]
        public QuarantineDto Quarantine { get; init; } = new();

        [JsonPropertyName("audit")]
        public AuditDto Audit { get; init; } = new();

        [JsonPropertyName("ui")]
        public UiDto Ui { get; init; } = new();
    }

    private sealed class ExifToolDto
    {
        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        [JsonPropertyName("enable_windows_long_path")]
        public bool EnableWindowsLongPath { get; init; }

        [JsonPropertyName("enable_large_file_support")]
        public bool EnableLargeFileSupport { get; init; }

        [JsonPropertyName("dry_run")]
        public bool DryRun { get; init; }

        [JsonPropertyName("extra_exiftool_args")]
        public string[] ExtraExifToolArgs { get; init; } = [];

        [JsonPropertyName("stay_open_pool_size")]
        public int StayOpenPoolSize { get; init; }

        [JsonPropertyName("max_parallel_drain")]
        public int MaxParallelDrain { get; init; }
    }

    private sealed class WatchDto
    {
        [JsonPropertyName("hot_folder")]
        public string HotFolder { get; init; } = string.Empty;

        [JsonPropertyName("include_subdirectories")]
        public bool IncludeSubdirectories { get; init; }

        [JsonPropertyName("debounce_ms")]
        public int DebounceMs { get; init; }

        [JsonPropertyName("internal_buffer_size")]
        public int InternalBufferSize { get; init; }

        [JsonPropertyName("auto_excluded_directories")]
        public string[] AutoExcludedDirectories { get; init; } = [];

        [JsonPropertyName("polling_interval_seconds")]
        public int PollingIntervalSeconds { get; init; }
    }

    private sealed class RulesDto
    {
        [JsonPropertyName("allowed_extensions")]
        public string[] AllowedExtensions { get; init; } = [];

        [JsonPropertyName("excluded_patterns")]
        public string[] ExcludedPatterns { get; init; } = [];

        [JsonPropertyName("output_mode")]
        public string OutputMode { get; init; } = string.Empty;

        [JsonPropertyName("output_directory")]
        public string OutputDirectory { get; init; } = string.Empty;
    }

    private sealed class RetryDto
    {
        [JsonPropertyName("max_attempts")]
        public int MaxAttempts { get; init; }

        [JsonPropertyName("backoff_seconds")]
        public int[] BackoffSeconds { get; init; } = [];
    }

    private sealed class BackupDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("directory")]
        public string Directory { get; init; } = string.Empty;

        [JsonPropertyName("suffix")]
        public string Suffix { get; init; } = string.Empty;

        [JsonPropertyName("retention")]
        public string Retention { get; init; } = string.Empty;
    }

    private sealed class QuarantineDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("directory")]
        public string Directory { get; init; } = string.Empty;
    }

    private sealed class AuditDto
    {
        [JsonPropertyName("log_directory")]
        public string LogDirectory { get; init; } = string.Empty;

        [JsonPropertyName("retain_days")]
        public int RetainDays { get; init; }

        [JsonPropertyName("diagnostic_mode")]
        public bool DiagnosticMode { get; init; }
    }

    private sealed class UiDto
    {
        [JsonPropertyName("hide_main_window_on_startup")]
        public bool HideMainWindowOnStartup { get; init; }

        [JsonPropertyName("hide_tray_icon")]
        public bool HideTrayIcon { get; init; }

        [JsonPropertyName("theme_variant")]
        public string ThemeVariant { get; init; } = string.Empty;
    }
}
