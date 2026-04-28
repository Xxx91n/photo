namespace PhotoPrivacy.Core.Constants;

public static class DefaultPaths
{
    public static string ExifToolPath => ResolveExifToolPath();

    private static string ResolveExifToolPath()
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
            if (System.IO.File.Exists(path))
            {
                return path;
            }
        }

        return @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe";
    }
}
