namespace PhotoPrivacy.Core.Constants;

public static class DefaultPaths
{
    public static string ExifToolPath => ResolveExifToolPath();

    /// <summary>
    /// App-local default hot folder: <BaseDirectory>/hot, three-platform compatible.
    /// An explicitly empty hot_folder is allowed as a first-run standby state; the UI guides users to configure it.
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
    /// Default backup directory: <hotFolder>/bak — aligned with BackupPathResolver.DefaultBackupDirName.
    /// Previously .pp_backup mismatched the actual backup dir "bak" (the UI watermark misled users).
    /// Backup before metadata cleaning.
    /// </summary>
    public static string DefaultBackupDirectory => string.IsNullOrEmpty(DefaultHotFolder) ? string.Empty : System.IO.Path.Combine(DefaultHotFolder, "bak");

    // App-local hot folder (BaseDirectory/hot) — three-platform compatible,
    // does NOT select user's system pictures (prevents accidental damage to user photos).
    // AppConfig.Default.Watch.HotFolder references DefaultPaths.DefaultHotFolder (BaseDirectory/hot);
    // Backup.Directory stays string.Empty (dynamic fallback to <hotFolder>/bak).
    private static string ResolveDefaultHotFolder()
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "hot");
    }

    /// <summary>
    /// 把配置里的 <c>exiftool.path</c> 归一到「可执行文件绝对路径」，对齐 ExifToolGUI 的
    /// <c>ETOverrideDir</c> 口径（用户给一个 ExifTool 安装目录，启动时拼成 <c>目录/exiftool</c>）：
    /// - 空串：原样返回，交给调用方回落到运行时探测（<see cref="ExifToolPath"/>）；
    /// - 已存在的文件：原样返回（用户给的是 exiftool 可执行文件本身的绝对路径）；
    /// - 已存在的目录：拼出该目录内的 <c>exiftool</c> (Unix) / <c>exiftool.exe</c> (Windows)；
    /// - 其余：原样返回，交由 <see cref="AppConfigValidator"/> 报「not found」。
    /// 集中在此处理，避免 Worker 与 UI 各自拼目录后分叉（#33）。
    /// </summary>
    /// <remarks>
    /// 决策本体随 #33 关闭已实现（ExifTool 下载链验证走通）；#33 关闭后标记改指仍存续的治理票 #82（死指针清理），
    /// 决策的原始来历保留在上述 <c>#33</c> 引用里，不做删改。
    /// </remarks>
    // Design(#82): exiftool.path 支持目录式覆盖，目录内解析出 exiftool/exiftool.exe。
    internal static string ResolveExifToolExecutable(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        if (System.IO.File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (System.IO.Directory.Exists(configuredPath))
        {
            var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
            return System.IO.Path.Combine(configuredPath, exeName);
        }

        return configuredPath;
    }

    private static string ResolveExifToolPath()
    {
        var candidates = OperatingSystem.IsWindows() ? WindowsCandidates() : UnixCandidates();

        foreach (var path in candidates)
        {
            if (System.IO.File.Exists(path)) return path;
        }

        // 只装在 PATH 里的 ExifTool 也算可用——与 UI 的 resolve_exiftool_from_path 同口径，
        // 否则 UI 显示「已自动检测到」而 Worker 把监控配置拒掉，同一台机器给出两处矛盾的结论。
        // Design(#82): 固定候选全落空后按 PATH 解析 exiftool，与 UI 的探测规则一致。
        var fromPath = FindExifToolOnPathVariable();
        if (fromPath is not null) return fromPath;

        return OperatingSystem.IsWindows()
            ? @"C:\Program Files\ExifTool\exiftool.exe"
            : "exiftool";
    }

    internal static string[] WindowsCandidates() =>
    [
        System.IO.Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool.exe"),
        System.IO.Path.Combine(AppContext.BaseDirectory, "exiftool.exe"),
        @"C:\Program Files\ExifTool\exiftool.exe",
        @"C:\Program Files (x86)\ExifTool\exiftool.exe",
        @"C:\Windows\exiftool.exe",
    ];

    /// <summary>
    /// Unix 候选顺序与 UI 的 <c>resolve_default_exiftool_path</c> 一致：先包内 <c>ExifTool/</c>，
    /// 再包根裸 <c>exiftool</c>，最后系统安装位置。
    /// 顺序不能反：包内那份自带同级 <c>lib/</c>，包根裸文件通常没有；包根优先会选中一个
    /// 缺运行时目录的副本，<c>AppConfigValidator</c> 随即拒绝启用监控（#108）。
    /// </summary>
    internal static string[] UnixCandidates() =>
    [
        System.IO.Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool"),
        System.IO.Path.Combine(AppContext.BaseDirectory, "exiftool"),
        "/usr/bin/exiftool",
        "/usr/local/bin/exiftool",
        "/opt/homebrew/bin/exiftool",
    ];

    /// <summary>
    /// 扫 PATH 找 exiftool。只接受绝对路径片段：相对片段（如 <c>.</c>）解析结果依赖进程工作目录，
    /// 而 Worker 的启动目录不确定，且 <c>AppConfigValidator</c> 要求绝对路径。
    /// 这里有意的取舍是：PATH 解析结果可能被同机同用户篡改，但配置里的显式路径始终优先，
    /// 且 UI 侧早已按同一规则探测，两边一致比单边收紧更重要。
    /// </summary>
    private static string? FindExifToolOnPathVariable()
        => FindExifToolInPathValue(
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool");

    /// <summary>
    /// PATH 里可用的绝对路径片段（去引号、丢弃非绝对片段）。PATH 的解析口径只在这一个地方定义，
    /// 避免 Worker 与 UI 各自实现一套后分叉；<see cref="FindExifToolInPathValue"/> 与它之上的
    /// 探测都从这里取片段。
    /// </summary>
    internal static IEnumerable<string> EnumerateAbsolutePathEntries(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (var entry in pathValue.Split(
                     System.IO.Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim('"');
            if (System.IO.Path.IsPathFullyQualified(directory))
            {
                yield return directory;
            }
        }
    }

    /// <summary>在给定 PATH 值里找可执行文件；抽成纯函数便于按固定 PATH 值做回归（不碰进程环境）。</summary>
    internal static string? FindExifToolInPathValue(string? pathValue, string fileName)
    {
        foreach (var directory in EnumerateAbsolutePathEntries(pathValue))
        {
            var candidate = System.IO.Path.Combine(directory, fileName);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
