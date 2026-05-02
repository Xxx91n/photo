namespace PhotoPrivacy.Core.Configuration;

public static class AppConfigValidator
{
    private static readonly string[] ForbiddenExtraArgs =
    [
        "-stay_open",
        "-@",
        "-execute",
        "-echo1"
    ];

    public static void Validate(AppConfig config)
    {
        if (config.ExifTool.StayOpenPoolSize < 1)
        {
            throw new AppConfigValidationException("exiftool.stay_open_pool_size must be >= 1");
        }

        if (config.ExifTool.MaxParallelDrain < 1)
        {
            throw new AppConfigValidationException("exiftool.max_parallel_drain must be >= 1");
        }

        if (config.ExifTool.DryRun)
        {
            return;
        }

        if (!Path.IsPathFullyQualified(config.ExifTool.Path))
        {
            throw new AppConfigValidationException("exiftool.path must be absolute");
        }

        if (!File.Exists(config.ExifTool.Path))
        {
            throw new AppConfigValidationException("exiftool.path not found");
        }

        var exifToolDirectory = Path.GetDirectoryName(config.ExifTool.Path)
            ?? throw new AppConfigValidationException("exiftool.path parent directory missing");
        var exifToolFilesDirectory = Path.Combine(exifToolDirectory, "exiftool_files");

        if (!Directory.Exists(exifToolFilesDirectory))
        {
            throw new AppConfigValidationException("exiftool_files directory missing");
        }

        if (config.ExifTool.ExtraExifToolArgs.Any(arg => ForbiddenExtraArgs.Contains(arg, StringComparer.OrdinalIgnoreCase)))
        {
            throw new AppConfigValidationException("extra_exiftool_args contains forbidden argument");
        }

        if (config.Backup.Enabled)
        {
            var hot = Path.GetFullPath(config.Watch.HotFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var backupDir = !string.IsNullOrWhiteSpace(config.Backup.Directory)
                ? Path.GetFullPath(config.Backup.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.Combine(hot, "bak");

            if (string.Equals(hot, backupDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppConfigValidationException("备份目录不能与监控目录相同，请指定监控目录的子目录(如 bak)或其他路径。");
            }
        }
    }
}
