namespace PhotoPrivacy.IntegrationTests;

internal static class ExifToolLocator
{
    private const string EnvVar = "PHOTO_EXIFTOOL_PATH";
    private const string DevMachineFallback = @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe";

    public static string TryResolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        var fromPath = FindInPath(exeName);
        if (fromPath is not null)
            return fromPath;

        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                DevMachineFallback,
                @"C:\Windows\exiftool.exe",
                @"C:\tools\exiftool.exe",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "ExifTool", "exiftool.exe"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }

        return DevMachineFallback;
    }

    private static string? FindInPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(full))
                return full;
        }
        return null;
    }
}
