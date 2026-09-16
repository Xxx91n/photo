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

    /// <summary>
    /// 票 02（A-003）：当前 MatchesPattern 未实现的通配语法字符——出现即按字面量匹配，
    /// 用户按 gitignore/rsync 习惯写的模式会静默失效，故必须告警（不识别≠静默）。
    /// </summary>
    private static readonly char[] UnsupportedPatternSyntaxChars = ['[', ']', '{', '}'];

    /// <summary>
    /// 票 02（A-003）：当前平台文件名不允许的字符（去掉 <c>*</c> / <c>?</c> 两个通配符，
    /// 它们在本项目语义里是通配而非字面量）。含这些字符的模式永远匹配不到文件名。
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        Path.GetInvalidFileNameChars().Where(c => c != '*' && c != '?').ToArray();

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

        // ADR 0045: Hot folder must be configured for non-dry-run mode, otherwise
        // the worker cannot scan/enqueue files and silently does nothing.
        if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            throw new AppConfigValidationException("watch.hot_folder 未配置。请先设置监控目录。");
        }

        // 票 02（A-002）：空串回落是 loader 层的责任（AppConfigLoader 已兜底），
        // 这里做第二层保险并给出可读错误——空串一旦流到使用点，
        // Path.Combine("", 文件名) 会退化成相对路径，隔离/审计落进进程 CWD。
        if (string.IsNullOrWhiteSpace(config.Quarantine.Directory))
        {
            throw new AppConfigValidationException(
                "quarantine.directory 未配置。请指定隔离目录，或留空让运行时按默认路径解析（空值会让隔离文件落进进程工作目录）。");
        }

        if (string.IsNullOrWhiteSpace(config.Audit.LogDirectory))
        {
            throw new AppConfigValidationException(
                "audit.log_directory 未配置。请指定审计日志目录，或留空让运行时按默认路径解析（空值会让审计日志落进进程工作目录）。");
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
            if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
            {
                throw new AppConfigValidationException("watch.hot_folder 未配置，无法启用备份。请先设置监控目录。");
            }

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

    /// <summary>
    /// 票 02（A-003 / 不变量①④）：排除清单的可见性告警——「不识别≠静默」。
    /// <see cref="Validate"/> 承载 fail-closed 的硬拒路径；本方法承载「不阻断但必须可见」的软路径，
    /// 由 Worker 落结构化日志与 <c>config_warning</c> 审计事件。
    ///
    /// 判定口径（匹配语义 = 纯文件名 glob，仅支持 <c>*</c> 与 <c>?</c>）：
    /// 1) 空串/空白——永远匹配不到任何非空文件名；
    /// 2) 含路径分隔符（<c>/</c> 或 <c>\</c>）——匹配对象是 <see cref="Path.GetFileName(string)"/>，
    ///    文件名里永远不含分隔符，用户按路径写排除规则必然静默失效；
    /// 3) 含未实现的通配语法字符（<c>[ ] { }</c>）——按字面量匹配，与 gitignore/rsync 习惯不符；
    /// 4) 含当前平台非法文件名字符——永远匹配不到文件名。
    /// 每个模式最多产出一条告警（按上述顺序取首个命中），返回空列表表示无告警。
    /// </summary>
    public static IReadOnlyList<string> CollectWarnings(AppConfig config)
    {
        var patterns = config.Rules.ExcludedPatterns ?? [];
        if (patterns.Length == 0)
        {
            return Array.Empty<string>();
        }

        var warnings = new List<string>();

        for (var i = 0; i < patterns.Length; i++)
        {
            var pattern = patterns[i];

            if (string.IsNullOrWhiteSpace(pattern))
            {
                warnings.Add(
                    $"rules.excluded_patterns[{i}] 是空串/空白，永远匹配不到任何文件名，该条目不会生效。");
                continue;
            }

            if (pattern.IndexOf('/') >= 0 || pattern.IndexOf('\\') >= 0)
            {
                warnings.Add(
                    $"rules.excluded_patterns[{i}]=\"{pattern}\" 含路径分隔符，但排除清单只按文件名匹配，该模式永远匹配不到任何文件。");
                continue;
            }

            var unsupportedIndex = pattern.IndexOfAny(UnsupportedPatternSyntaxChars);
            if (unsupportedIndex >= 0)
            {
                warnings.Add(
                    $"rules.excluded_patterns[{i}]=\"{pattern}\" 含未支持的匹配语法 '{pattern[unsupportedIndex]}'，将按字面量字符匹配（当前仅支持 * 与 ?）。");
                continue;
            }

            var invalidIndex = pattern.IndexOfAny(InvalidFileNameChars);
            if (invalidIndex >= 0)
            {
                warnings.Add(
                    $"rules.excluded_patterns[{i}]=\"{pattern}\" 含当前平台文件名不允许的字符，该模式永远匹配不到任何文件。");
            }
        }

        return warnings;
    }
}
