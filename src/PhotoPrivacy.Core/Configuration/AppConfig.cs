using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Configuration;

public sealed record ExifToolOptions(
    string Path,
    bool EnableWindowsLongPath,
    bool EnableLargeFileSupport,
    bool DryRun,
    string[] ExtraExifToolArgs,
    int StayOpenPoolSize,
    int MaxParallelDrain);

public sealed record WatchOptions(
    string HotFolder,
    bool IncludeSubdirectories,
    int DebounceMs,
    int InternalBufferSize,
    string[] AutoExcludedDirectories,
    int PollingIntervalSeconds);

public sealed record RuleOptions(
    string[] AllowedExtensions,
    string[] ExcludedPatterns,
    string OutputMode,
    string OutputDirectory);

public sealed record RetryOptions(int MaxAttempts, int[] BackoffSeconds);

public sealed record BackupOptions(bool Enabled, string Directory, string Suffix, int MaxSizeMb, int RetainDays = 30);

public sealed record QuarantineOptions(bool Enabled, string Directory);

public sealed record AuditOptions(string LogDirectory, int RetainDays, bool DiagnosticMode, string LogLevel = "info");

 public sealed record UiOptions(bool HideMainWindowOnStartup, bool HideTrayIcon, string ThemeVariant, string Locale = "zh-CN", double SidebarWidth = 200.0, string ThemeId = "catppuccin");

public sealed record AppConfig(
    int SchemaVersion,
    ExifToolOptions ExifTool,
    WatchOptions Watch,
    RuleOptions Rules,
    RetryOptions Retry,
    BackupOptions Backup,
    QuarantineOptions Quarantine,
    AuditOptions Audit,
    UiOptions Ui)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        ExifTool: new ExifToolOptions(
            Path: DefaultPaths.ExifToolPath,
            EnableWindowsLongPath: true,
            EnableLargeFileSupport: true,
            DryRun: false,
            ExtraExifToolArgs: [],
            StayOpenPoolSize: 1,
            MaxParallelDrain: 1),
       Watch: new WatchOptions(
            HotFolder: DefaultPaths.DefaultHotFolder,
           IncludeSubdirectories: true,
           DebounceMs: 800,
           InternalBufferSize: 65536,
           AutoExcludedDirectories: [],
           PollingIntervalSeconds: 0),
        Rules: new RuleOptions(
            // 票 01 宣称收敛（D-005.1）：allowed_extensions 与 WipeStrategyResolver.ExtensionFamilyMap
            // 映射面一一对应（集合差=0，由 FormatCoverageReconciliationTests 入 CI 钉死）。
            // 映射面之外的格式（webp/gif/PSD/长尾 RAW 变体等）明确“不支持”：立即跳过并记
            // wipe_skipped_unknown 审计事件，绝不静默处理。
            AllowedExtensions: [
            // JPEG family
            ".jpg", ".jpeg", ".jpe", ".jps", ".jph",
            // TIFF / DNG
            ".tif", ".tiff", ".dng",
            // RAW
            ".cr2", ".cr3", ".arw", ".nef", ".orf", ".raf",
            ".rw2", ".pef", ".srw", ".sr2",
            // HEIF / AVIF
            ".heic", ".heif", ".hif", ".avif",
            // PNG
            ".png", ".apng",
            // Video (MOV/MP4)
            ".mov", ".mp4", ".m4v", ".qt", ".3gp", ".3g2",
            // PDF
            ".pdf",
            // EPS / PS / AI
            ".eps", ".ps", ".ai",
        ],
            ExcludedPatterns: ["~$*", "*.tmp"],
            OutputMode: "same_as_source",
            OutputDirectory: string.Empty),
        Retry: new RetryOptions(
            MaxAttempts: 3,
            BackoffSeconds: [1, 3, 10]),
       Backup: new BackupOptions(
           Enabled: true,
           Directory: string.Empty,
           Suffix: ".bak",
           MaxSizeMb: 5000,
           RetainDays: 30),
       Quarantine: new QuarantineOptions(
           Enabled: true,
            Directory: DefaultPaths.DefaultQuarantineDirectory),
       Audit: new AuditOptions(
            LogDirectory: DefaultPaths.DefaultAuditLogDirectory,
           RetainDays: 30,
           DiagnosticMode: false,
           LogLevel: "info"),
       Ui: new UiOptions(
           HideMainWindowOnStartup: false,
           HideTrayIcon: false,
           ThemeVariant: "system",
           Locale: "zh-CN",
           SidebarWidth: 200.0,
           ThemeId: "catppuccin"));
}
