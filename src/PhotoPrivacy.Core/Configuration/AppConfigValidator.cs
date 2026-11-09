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
    /// 包管理器管理的安装前缀（Unix）：命中即认为该安装能自行解析 <c>lib/</c>。
    /// 只列真实的包管理器根，不用「在 PATH 里」代替——见 <see cref="IsPackageManagedDirectory"/>。
    /// </summary>
    private static readonly string[] PackageManagedPrefixes =
    [
        "/usr/",
        "/bin/",
        "/sbin/",
        "/opt/",
        "/snap/",
        "/nix/",
        "/home/linuxbrew/",
    ];

    /// <summary>
    /// 当前 MatchesPattern 未实现的通配语法字符——出现即按字面量匹配，
    /// 用户按 gitignore/rsync 习惯写的模式会静默失效，故必须告警（不识别≠静默）。
    /// </summary>
    private static readonly char[] UnsupportedPatternSyntaxChars = ['[', ']', '{', '}'];

    /// <summary>
    /// 当前平台文件名不允许的字符（去掉 <c>*</c> / <c>?</c> 两个通配符，
    /// 它们在本项目语义里是通配而非字面量）。含这些字符的模式永远匹配不到文件名。
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        Path.GetInvalidFileNameChars().Where(c => c != '*' && c != '?').ToArray();

    /// <summary>
    /// 配置校验分三层（决策记录见 #108）：
    /// <list type="number">
    /// <item><b>无条件层</b>——纯值不变量（连接池大小、禁用参数）。既不依赖文件系统，
    /// 也不依赖是否启用监控，因此任何形态（含待机、含 dry-run）都必须通过。</item>
    /// <item><b>dry-run 层</b>——不调用 ExifTool、不落盘，资源存在性无意义，直接放过。</item>
    /// <item><b>启用监控前置层</b>——ExifTool 可执行文件与伴随目录、隔离/审计目录、
    /// 备份目录冲突。这些资源只在监控真正运行时被使用，故只在启用监控时 fail-closed。</item>
    /// </list>
    /// 空监控目录是首运行待机态（#100）：以前用一条 <c>return</c> 整体跳过，
    /// 连带跳过了与监控无关的禁用参数校验，且使备份分支里的空目录判断变成不可达死代码；
    /// 现在改为按「是否启用监控」显式分层，跳过范围仅限监控资源本身。
    /// 代价：缺 ExifTool 的裸机在保存监控目录时才会被拒——该拒绝原因由 UI 常驻显示，
    /// 不随 3 s 过期消失（同票 UI 侧）。
    /// </summary>
    public static void Validate(AppConfig config)
    {
        // —— 无条件层 ——
        if (config.ExifTool.StayOpenPoolSize < 1)
        {
            throw new AppConfigValidationException("exiftool.stay_open_pool_size must be >= 1");
        }

        if (config.ExifTool.MaxParallelDrain < 1)
        {
            throw new AppConfigValidationException("exiftool.max_parallel_drain must be >= 1");
        }

        // 禁用参数会破坏 stay_open 长驻协议，与是否启用监控、是否 dry-run 无关，故前置。
        if (config.ExifTool.ExtraExifToolArgs.Any(arg => ForbiddenExtraArgs.Contains(arg, StringComparer.OrdinalIgnoreCase)))
        {
            throw new AppConfigValidationException("extra_exiftool_args contains forbidden argument");
        }

        // —— dry-run 层 ——
        if (config.ExifTool.DryRun)
        {
            return;
        }

        // —— 启用监控前置层 ——
        // An empty hot folder is a valid first-run standby state. The worker
        // remains available over IPC until the user chooses a directory.
        if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            return;
        }

        // 空串回落是 loader 层的责任（AppConfigLoader 已补默认），
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
            // 前缀保留英文说法（smoke 断言与文档按此定位），后缀给出可执行指引：
            // 发布包内置 ExifTool 后，用户不需要自己找路径，报错必须说清两条出路。
            throw new AppConfigValidationException(
                $"exiftool.path must be absolute（当前值：\"{config.ExifTool.Path}\"）。" +
                "exiftool.path 留空表示按运行时探测（优先发布包内置的 ExifTool 目录）；" +
                "内置文件缺失时请重新解压发布包，或安装 ExifTool 后在配置页填写可执行文件的完整路径。");
        }

        if (!File.Exists(config.ExifTool.Path))
        {
            throw new AppConfigValidationException(
                $"exiftool.path not found（当前值：\"{config.ExifTool.Path}\"）。" +
                "发布包内置的 ExifTool 缺失时请重新解压发布包；或安装 ExifTool 后在配置页指定 exiftool 可执行文件的完整路径。");
        }

        // 符号链接要先解到最终目标：`~/bin/exiftool -> ~/ExifTool/exiftool` 这类安装的 lib/
        // 在目标旁边而不在链接旁边，按链接所在目录查运行时目录会把可用安装误判成装坏（#108 审查）。
        var exifToolPath = ResolveFinalTarget(config.ExifTool.Path);
        var exifToolDirectory = Path.GetDirectoryName(exifToolPath)
            ?? throw new AppConfigValidationException("exiftool.path parent directory missing");

        // 伴随运行时目录：Windows 包是 exiftool_files（官方要求与 exiftool.exe 同级），
        // Perl 发行版是同级 lib/。缺了它 exiftool 起不来，而失败点会晚到 stay_open 桥接启动期，
        // 所以在配置校验就拦住。
        if (OperatingSystem.IsWindows())
        {
            var exifToolFilesDirectory = Path.Combine(exifToolDirectory, "exiftool_files");

            if (!Directory.Exists(exifToolFilesDirectory))
            {
                throw new AppConfigValidationException(
                    $"exiftool_files directory missing（{exifToolDirectory} 下缺少 exiftool_files 目录）。" +
                    "Windows 版 ExifTool 的 exiftool.exe 必须与 exiftool_files 同级；请重新解压发布包，或重新安装 ExifTool。");
            }
        }
        else if (!IsPackageManagedDirectory(exifToolDirectory)
                 && !Directory.Exists(Path.Combine(exifToolDirectory, "lib")))
        {
            // 包管理器管理的安装（/usr、/opt/homebrew、/nix/store 等）由包自身登记 lib 搜索路径，
            // 不要求同级目录；其余位置（含发布包内置的 ExifTool/、自定义目录）必须是自包含的一份。
            throw new AppConfigValidationException(
                $"exiftool_files directory missing（{exifToolDirectory} 下既没有 exiftool_files 也没有 lib 目录）。" +
                "随包内置的 ExifTool 需与 lib/ 同级；请重新解压发布包，或安装系统级 ExifTool 后在配置页指定 /usr/bin/exiftool。");
        }

        // 本块只在启用监控时到达，故「备份启用但监控目录为空」不可能成立——
        // 那条判断曾经是不可达死代码（空目录在方法入口就 return 了），已随分层删除。
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

    /// <summary>
    /// 解到符号链接的最终目标；不是链接或解析失败时原样返回。
    /// 用于「运行时目录在链接目标旁边」的安装（Unix 上 <c>~/bin/exiftool -> ~/ExifTool/exiftool</c>）。
    /// </summary>
    private static string ResolveFinalTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>
    /// 包管理器管理的安装目录：这些位置的 exiftool 由包自身登记 lib 搜索路径（发行版 <c>/usr</c>、
    /// Homebrew <c>/opt/homebrew</c>、Nix <c>/nix/store</c>、Linuxbrew 等），不要求同级的 <c>lib/</c>。
    ///
    /// 判定不采信「目录在 PATH 里」：PATH 只证明文件能被发现，自定义目录里可能只是一个缺 <c>lib/</c>
    /// 的脚本副本——那种副本过了校验也会在首个文件上起不来，等于把 fail-closed 换成延迟失败（#108 审查）。
    /// </summary>
    private static bool IsPackageManagedDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))
            + Path.DirectorySeparatorChar;

        foreach (var prefix in PackageManagedPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 不识别≠静默、降级必须留下可见记录：排除清单的可见性告警。
    /// <see cref="Validate"/> 承载 fail-closed 的硬拒路径；本方法承载「不阻断但必须可见」的软路径，
    /// 由 Worker 落结构化日志与 <c>config_warning</c> 审计事件。
    ///
    /// 判定规则（匹配语义 = 纯文件名 glob，仅支持 <c>*</c> 与 <c>?</c>）：
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
