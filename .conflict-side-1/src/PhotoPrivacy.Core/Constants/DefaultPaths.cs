namespace PhotoPrivacy.Core.Constants;

public static class DefaultPaths
{
    public static string ExifToolPath => ResolveExifToolPath();

    /// <summary>
    /// App-local default hot folder: <BaseDirectory>/hot (ADR 0055 A4), three-platform compatible.
    /// AppConfigValidator rejects an empty hot_folder (non-dry-run), so users get a clear "not configured" error.
    /// </summary>
    public static string DefaultHotFolder => ResolveDefaultHotFolder();

    /// <summary>
    /// Default quarantine directory: <hotFolder>/_quarantine. Empty when hotFolder is empty.
    /// </summary>
    public static string DefaultQuarantineDirectory => string.IsNullOrEmpty(DefaultHotFolder) ? string.Empty : System.IO.Path.Combine(DefaultHotFolder, "_quarantine");

    /// <summary>
    /// Default audit log directory: <hotFolder>/_audit. Empty when hotFolder is empty.
    /// </summary>
    public static string DefaultAuditLogDirectory => string.IsNullOrEmpty(DefaultHotFolder) ? string.Empty : System.IO.Path.Combine(DefaultHotFolder, "_audit");

    /// <summary>
    /// Default backup directory: <hotFolder>/bak — aligned with BackupPathResolver.DefaultBackupDirName.
    /// ADR 0055 A4: Previously .pp_backup which mismatched actual backup dir "bak" (UI Watermark misled users).
    /// ADR 0052 A6: Backup before metadata cleaning.
    /// </summary>
    public static string DefaultBackupDirectory => string.IsNullOrEmpty(DefaultHotFolder) ? string.Empty : System.IO.Path.Combine(DefaultHotFolder, "bak");

    // ADR 0055 A4: App-local hot folder (BaseDirectory/hot) — three-platform compatible,
    // does NOT select user's system pictures (prevents accidental damage to user photos).
    // AppConfig.Default.Watch.HotFolder references DefaultPaths.DefaultHotFolder (BaseDirectory/hot);
    // Backup.Directory stays string.Empty (ADR 0045 dynamic fallback to <hotFolder>/bak).
    private static string ResolveDefaultHotFolder()
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "hot");
    }

    private static string ResolveExifToolPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool.exe"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "exiftool.exe"),
                @"C:\Program Files\ExifTool\exiftool.exe",
                @"C:\Program Files (x86)\ExifTool\exiftool.exe",
                @"C:\Windows\exiftool.exe",
            };

            foreach (var path in candidates)
            {
                if (System.IO.File.Exists(path)) return path;
            }

            return @"C:\Program Files\ExifTool\exiftool.exe";
        }

        var unixCandidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "exiftool"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool"),
            "/usr/bin/exiftool",
            "/usr/local/bin/exiftool",
            "/opt/homebrew/bin/exiftool",
        };

        foreach (var path in unixCandidates)
        {
            if (System.IO.File.Exists(path)) return path;
        }

        return "exiftool";
    }
}
