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

 public sealed record UiOptions(bool HideMainWindowOnStartup, bool HideTrayIcon, string ThemeVariant, string Locale = "zh-CN", double SidebarWidth = 200.0);

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
            AllowedExtensions: [
            // JPEG family
            ".jpg", ".jpeg", ".jpe", ".jps", ".jph", ".jpf", ".j2k", ".jp2", ".jng",
            ".jxl", ".jpm", ".jph", ".jpx", ".jxr",
            // RAW
            ".cr2", ".cr3", ".crw", ".crm", ".arw", ".nef", ".nrw", ".orf", ".ori", ".pef",
            ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srw", ".mef", ".mos", ".erf",
            ".mrw", ".dcp", ".dng", ".fff", ".gpr", ".lrf", ".lrv", ".iiq",
            // TIFF
            ".tif", ".tiff",
            // PNG / generic bitmap
            ".png", ".apng", ".pbm", ".pgm", ".ppm",
            // HEIF / AVIF
            ".heic", ".heif", ".hif", ".avif",
            // Adobe / DTP
            ".psd", ".psdt", ".psb", ".ps", ".ps2", ".ps3", ".eps", ".eps2",
            ".eps3", ".epsf", ".ai", ".ait", ".ind", ".indd", ".indt", ".insp",
            // PDF
            ".pdf",
            // Video
            ".mp4", ".m4a", ".m4b", ".m4p", ".m4v", ".mov", ".qt", ".3g2",
            ".3gp", ".3gp2", ".3gpp", ".f4a", ".f4b", ".f4p", ".f4v", ".mqv",
            // Others
            ".360", ".aax", ".arq", ".ciff", ".cs1", ".dr4", ".dvb", ".exif",
            ".exv", ".flif", ".gif", ".glv", ".hdp", ".icc", ".icm", ".mie", ".mng",
            ".mpo", ".nksc", ".thm", ".vrd", ".wdp", ".webp", ".x3f", ".xmp",
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
           SidebarWidth: 200.0));
}
