using System.Text.Json;
using System.Text.Json.Serialization;

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
        var (legacyExifToolPath, hasNestedExifToolPath) = ReadLegacyHints(json);

        var dto = JsonSerializer.Deserialize<AppConfigDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto is null)
        {
            return AppConfig.Default;
        }

        return new AppConfig(
            SchemaVersion: Math.Max(1, dto.SchemaVersion),
            ExifTool: new ExifToolOptions(
                Path: ResolveExifToolPath(dto.ExifTool.Path, legacyExifToolPath, hasNestedExifToolPath),
                EnableWindowsLongPath: dto.ExifTool.EnableWindowsLongPath,
                EnableLargeFileSupport: dto.ExifTool.EnableLargeFileSupport,
                DryRun: dto.ExifTool.DryRun,
                ExtraExifToolArgs: dto.ExifTool.ExtraExifToolArgs ?? []),
            Watch: new WatchOptions(
                HotFolder: dto.Watch.HotFolder,
                IncludeSubdirectories: dto.Watch.IncludeSubdirectories,
                DebounceMs: dto.Watch.DebounceMs,
                InternalBufferSize: dto.Watch.InternalBufferSize),
            Rules: new RuleOptions(
                AllowedExtensions: dto.Rules.AllowedExtensions ?? [],
                ExcludedPatterns: dto.Rules.ExcludedPatterns ?? [],
                OutputMode: dto.Rules.OutputMode,
                OutputDirectory: dto.Rules.OutputDirectory),
            Retry: new RetryOptions(
                MaxAttempts: dto.Retry.MaxAttempts,
                BackoffSeconds: dto.Retry.BackoffSeconds ?? [1]),
            Backup: new BackupOptions(
                Enabled: dto.Backup.Enabled,
                Suffix: dto.Backup.Suffix,
                Retention: dto.Backup.Retention),
            Quarantine: new QuarantineOptions(
                Enabled: dto.Quarantine.Enabled,
                Directory: dto.Quarantine.Directory),
            Audit: new AuditOptions(
                LogDirectory: dto.Audit.LogDirectory,
                RetainDays: dto.Audit.RetainDays,
                DiagnosticMode: dto.Audit.DiagnosticMode),
            Ui: new UiOptions(
                HideMainWindowOnStartup: dto.Ui.HideMainWindowOnStartup,
                HideTrayIcon: dto.Ui.HideTrayIcon));
    }

    private static string ResolveExifToolPath(string nestedPath, string? legacyPath, bool hasNestedExifToolPath)
    {
        if (hasNestedExifToolPath)
        {
            return nestedPath;
        }

        return !string.IsNullOrWhiteSpace(legacyPath)
            ? legacyPath
            : nestedPath;
    }

    private static (string? LegacyExifToolPath, bool HasNestedExifToolPath) ReadLegacyHints(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? legacyExifToolPath = null;
        var hasNestedExifToolPath = false;

        if (root.TryGetProperty("exiftool_path", out var legacyPathElement)
            && legacyPathElement.ValueKind == JsonValueKind.String)
        {
            legacyExifToolPath = legacyPathElement.GetString();
        }

        if (root.TryGetProperty("exiftool", out var exiftoolElement)
            && exiftoolElement.ValueKind == JsonValueKind.Object
            && exiftoolElement.TryGetProperty("path", out var nestedPathElement)
            && nestedPathElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(nestedPathElement.GetString()))
        {
            hasNestedExifToolPath = true;
        }

        return (legacyExifToolPath, hasNestedExifToolPath);
    }

    private sealed class AppConfigDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; } = AppConfig.Default.SchemaVersion;

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
        public string Path { get; init; } = AppConfig.Default.ExifTool.Path;

        [JsonPropertyName("enable_windows_long_path")]
        public bool EnableWindowsLongPath { get; init; } = AppConfig.Default.ExifTool.EnableWindowsLongPath;

        [JsonPropertyName("enable_large_file_support")]
        public bool EnableLargeFileSupport { get; init; } = AppConfig.Default.ExifTool.EnableLargeFileSupport;

        [JsonPropertyName("dry_run")]
        public bool DryRun { get; init; } = AppConfig.Default.ExifTool.DryRun;

        [JsonPropertyName("extra_exiftool_args")]
        public string[]? ExtraExifToolArgs { get; init; } = [];
    }

    private sealed class WatchDto
    {
        [JsonPropertyName("hot_folder")]
        public string HotFolder { get; init; } = AppConfig.Default.Watch.HotFolder;

        [JsonPropertyName("include_subdirectories")]
        public bool IncludeSubdirectories { get; init; } = AppConfig.Default.Watch.IncludeSubdirectories;

        [JsonPropertyName("debounce_ms")]
        public int DebounceMs { get; init; } = AppConfig.Default.Watch.DebounceMs;

        [JsonPropertyName("internal_buffer_size")]
        public int InternalBufferSize { get; init; } = AppConfig.Default.Watch.InternalBufferSize;
    }

    private sealed class RulesDto
    {
        [JsonPropertyName("allowed_extensions")]
        public string[]? AllowedExtensions { get; init; } = AppConfig.Default.Rules.AllowedExtensions;

        [JsonPropertyName("excluded_patterns")]
        public string[]? ExcludedPatterns { get; init; } = AppConfig.Default.Rules.ExcludedPatterns;

        [JsonPropertyName("output_mode")]
        public string OutputMode { get; init; } = AppConfig.Default.Rules.OutputMode;

        [JsonPropertyName("output_directory")]
        public string OutputDirectory { get; init; } = AppConfig.Default.Rules.OutputDirectory;
    }

    private sealed class RetryDto
    {
        [JsonPropertyName("max_attempts")]
        public int MaxAttempts { get; init; } = AppConfig.Default.Retry.MaxAttempts;

        [JsonPropertyName("backoff_seconds")]
        public int[]? BackoffSeconds { get; init; } = AppConfig.Default.Retry.BackoffSeconds;
    }

    private sealed class BackupDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; } = AppConfig.Default.Backup.Enabled;

        [JsonPropertyName("suffix")]
        public string Suffix { get; init; } = AppConfig.Default.Backup.Suffix;

        [JsonPropertyName("retention")]
        public string Retention { get; init; } = AppConfig.Default.Backup.Retention;
    }

    private sealed class QuarantineDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; } = AppConfig.Default.Quarantine.Enabled;

        [JsonPropertyName("directory")]
        public string Directory { get; init; } = AppConfig.Default.Quarantine.Directory;
    }

    private sealed class AuditDto
    {
        [JsonPropertyName("log_directory")]
        public string LogDirectory { get; init; } = AppConfig.Default.Audit.LogDirectory;

        [JsonPropertyName("retain_days")]
        public int RetainDays { get; init; } = AppConfig.Default.Audit.RetainDays;

        [JsonPropertyName("diagnostic_mode")]
        public bool DiagnosticMode { get; init; } = AppConfig.Default.Audit.DiagnosticMode;
    }

    private sealed class UiDto
    {
        [JsonPropertyName("hide_main_window_on_startup")]
        public bool HideMainWindowOnStartup { get; init; } = AppConfig.Default.Ui.HideMainWindowOnStartup;

        [JsonPropertyName("hide_tray_icon")]
        public bool HideTrayIcon { get; init; } = AppConfig.Default.Ui.HideTrayIcon;
    }
}
