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
    }
}
