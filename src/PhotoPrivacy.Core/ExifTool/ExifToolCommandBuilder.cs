using System.Text;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public static class ExifToolCommandBuilder
{
    public static string[] BuildStartArguments(AppConfig config)
    {
        var args = new List<string>
        {
            "-stay_open", "True",
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

    public static string BuildProbeTaskBlock(string targetPath, string id)
    {
        ValidatePathForExifToolProtocol(targetPath);

        return $"-fast\n-json\n{targetPath}\n-echo1\nPROBE_DONE_{id}\n-execute\n";
    }

    /// <summary>
    /// ADR 0053 M6a: Build wipe task block using per-format safe defaults via WipeStrategyResolver.
    /// Unknown extensions are rejected (audit wipe_skipped_unknown).
    /// </summary>
    public static string BuildWipeTaskBlock(string targetPath, string taskId)
    {
        ValidatePathForExifToolProtocol(targetPath);

        var wipe = WipeStrategyResolver.Resolve(targetPath);
        if (wipe.SkipReason is not null)
        {
            // Unknown format — return a no-op probe block so the task completes cleanly
            // with a warning echo. The caller should audit wipe_skipped_unknown.
            var noOp = new StringBuilder();
            noOp.Append("-echo1\n");
            noOp.Append($"SKIP_{taskId}\n");
            noOp.Append("-execute\n");
            return noOp.ToString();
        }

        var sb = new StringBuilder();
        // Per-format effective args (e.g. "-all= --icc_profile:all -tagsfromfile @ -colorspacetags" for JPEG)
        foreach (var arg in wipe.EffectiveArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(arg + "\n");
        }
        sb.Append("-overwrite_original\n");
        sb.Append(targetPath + "\n");
        sb.Append("-echo1\n");
        sb.Append($"TASK_DONE_{taskId}\n");
        sb.Append("-execute\n");
        return sb.ToString();
    }

    /// <summary>
    /// 验证文件路径在 ExifTool stay_open 协议中的安全性。
    /// 防止通过注入换行符或 ExifTool 命令标志来执行非预期命令。
    /// </summary>
    private static void ValidatePathForExifToolProtocol(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空", nameof(path));
        }

        // 检测换行符注入
        if (path.Contains('\n') || path.Contains('\r'))
        {
            throw new ArgumentException("路径包含换行符，存在 ExifTool 命令注入风险", nameof(path));
        }

        // 检测 ExifTool 命令标志注入（以 - 开头的路径可能是注入的命令）
        // 合法文件路径可能以 - 开头（如 "-myfile.jpg"），但在 stay_open 模式下
        // 这会被解释为 ExifTool 参数，所以也应拒绝。
        if (path.StartsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException("路径以 '-' 开头，在 ExifTool stay_open 模式下会被解释为命令参数", nameof(path));
        }

        // 检测空字节注入
        if (path.Contains('\0'))
        {
            throw new ArgumentException("路径包含空字节", nameof(path));
        }

        // 检测路径遍历
        if (path.Contains("..\\") || path.Contains("../") || path.Contains("..\\\\"))
        {
            throw new ArgumentException("路径包含目录遍历序列 '..'", nameof(path));
        }

        // 验证路径是完全限定的（绝对路径）
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("路径必须是绝对路径", nameof(path));
        }
    }
}
