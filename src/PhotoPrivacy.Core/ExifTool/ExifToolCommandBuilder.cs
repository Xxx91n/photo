using System.Text;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public static class ExifToolCommandBuilder
{
    public static string[] BuildStartArguments(AppConfig config)
    {
        var args = new List<string>
        {
            "-stay_open", "true",
            "-@", "-",
            "-q", "-q"
        };

        if (config.ExifTool.EnableWindowsLongPath)
        {
            args.AddRange(["-API", "WindowsLongPath=1"]);
        }

        if (config.ExifTool.EnableLargeFileSupport)
        {
            args.AddRange(["-API", "LargeFileSupport=1"]);
        }

        if (config.ExifTool.ExtraExifToolArgs is { Length: > 0 })
        {
            args.AddRange(config.ExifTool.ExtraExifToolArgs);
        }

        return args.ToArray();
    }

    public static string BuildWipeTaskBlock(string targetPath, string taskId)
    {
        var sb = new StringBuilder();
        sb.Append("-all=\n");
        sb.Append("-overwrite_original\n");
        sb.Append(targetPath + "\n");
        sb.Append($"-echo1 TASK_DONE_{taskId}\n");
        sb.Append("-execute\n");
        return sb.ToString();
    }
}
