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
    string[] AutoExcludedDirectories);

public sealed record RuleOptions(
    string[] AllowedExtensions,
    string[] ExcludedPatterns,
    string OutputMode,
    string OutputDirectory);

public sealed record RetryOptions(int MaxAttempts, int[] BackoffSeconds);

public sealed record BackupOptions(bool Enabled, string Directory, string Suffix, string Retention);

public sealed record QuarantineOptions(bool Enabled, string Directory);

public sealed record AuditOptions(string LogDirectory, int RetainDays, bool DiagnosticMode);

public sealed record UiOptions(bool HideMainWindowOnStartup, bool HideTrayIcon, string ThemeVariant);

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
            HotFolder: @"D:\hot",
            IncludeSubdirectories: true,
            DebounceMs: 800,
            InternalBufferSize: 65536,
            AutoExcludedDirectories: []),
        Rules: new RuleOptions(
            AllowedExtensions: [".jpg", ".jpeg", ".png", ".heic", ".mp4", ".pdf", ".docx"],
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
            Retention: "keep"),
        Quarantine: new QuarantineOptions(
            Enabled: true,
            Directory: @"D:\hot\_quarantine"),
        Audit: new AuditOptions(
            LogDirectory: @"D:\hot\_audit",
            RetainDays: 30,
            DiagnosticMode: true),
        Ui: new UiOptions(
            HideMainWindowOnStartup: false,
            HideTrayIcon: false,
            ThemeVariant: "system"));
}
