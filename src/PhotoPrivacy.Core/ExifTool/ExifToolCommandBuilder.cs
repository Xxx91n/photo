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

        args.AddRange(config.ExifTool.ExtraExifToolArgs);
        return args.ToArray();
    }

    public static string BuildWipeTaskBlock(string targetPath, string taskId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-all=");
        sb.AppendLine("-overwrite_original");
        sb.AppendLine(targetPath);
        sb.AppendLine($"-echo1 TASK_DONE_{taskId}");
        sb.AppendLine("-execute");
        return sb.ToString();
    }
}
