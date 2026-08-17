namespace PhotoPrivacy.Core.Constants;

public static class DefaultPaths
{
    public static string ExifToolPath => ResolveExifToolPath();

    /// <summary>
    /// Cross-platform default hot folder. Returns a user-accessible photos directory.
    /// AppConfigValidator rejects non-existent directories, so users get a clear "not configured" error.
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
    /// Default backup directory: <hotFolder>/.pp_backup. Empty when hotFolder is empty.
    /// ADR 0052 A6: Backup before metadata cleaning.
    /// </summary>
    public static string DefaultBackupDirectory => string.IsNullOrEmpty(DefaultHotFolder) ? string.Empty : System.IO.Path.Combine(DefaultHotFolder, ".pp_backup");

    private static string ResolveDefaultHotFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrEmpty(pictures) ? string.Empty : pictures;
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
