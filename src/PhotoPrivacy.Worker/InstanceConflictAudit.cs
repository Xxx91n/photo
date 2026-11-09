using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using Serilog;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 重复启动 / 单写者准入门让位时的冲突留痕（#127）。
/// 这是「真正写审计」的路径，是「配置加载期零目录副作用」（#124 / #153）的显式例外：
/// 目录只在这里创建，且创建前有界确认可写；只读 / 不可写安装目录下不改写冲突审计落点
/// （不另择 %ProgramData% 或 ~/.local/state），只降级为可读记录且不阻断退出，保留冲突事实：
/// stderr 恒写，Serilog 已初始化时再补一路文件日志（服务会话下 stderr 不落盘）。
/// 写入方式（#211 用户裁决）：不起 JsonLineAuditLogger 的后台队列，退出前直接同步 append
/// 到当天 audit 文件；3 次×300 ms 重试包在真写文件外面，落盘失败当场可见并降级，
/// 不再出现「重试抓不到异常、后台吞掉 IOException」的静默丢行。
/// </summary>
internal static class InstanceConflictAudit
{
    private const int WriteAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 留痕结果：DegradationRecord 非空即表示降级（可读记录已由 Degrade 写 stderr + Serilog）；
    /// 为空表示没有发生降级：同步写入已成功，或事件等级低于配置阈值被有意过滤。
    /// 生产调用方可丢弃返回值：两路可观测面都在 Degrade 内落地；返回值供测试与后续调用方判读。
    /// </summary>
    internal sealed record ConflictAuditOutcome(string? DegradationRecord);

    public static Task<ConflictAuditOutcome> TryWriteAsync(string[] args, RuntimeMode requestedMode, string mutexName)
    {
        return WriteAsync(
            args,
            requestedMode,
            message: "检测到重复启动并已拒绝",
            data: new Dictionary<string, string>
            {
                ["mutex_name"] = mutexName
            });
    }

    /// <summary>单写者准入门（#162）让位留痕：后启动 Worker 对同 config 审计目录干净退出前写一条。</summary>
    public static Task<ConflictAuditOutcome> TryWriteSingleWriterYieldAsync(string[] args, RuntimeMode requestedMode, string auditDirectory)
    {
        return WriteAsync(
            args,
            requestedMode,
            message: "检测到另一 Worker 正在使用同一审计目录，本进程让位退出",
            data: new Dictionary<string, string>
            {
                ["reason"] = "single_writer_gate",
                ["audit_directory"] = auditDirectory
            });
    }

    private static async Task<ConflictAuditOutcome> WriteAsync(
        string[] args,
        RuntimeMode requestedMode,
        string message,
        IReadOnlyDictionary<string, string> data)
    {
        // 降级记录与审计事件共用同一份 payload：降级时也必须带上 requested_mode 等冲突上下文，
        // 否则「留痕为什么没落盘」之外连冲突形态都看不出（#127 AC1）。
        var payload = new Dictionary<string, string>
        {
            ["requested_mode"] = requestedMode.ToString().ToLowerInvariant()
        };
        foreach (var (key, value) in data)
        {
            payload[key] = value;
        }

        AppConfig config;
        try
        {
            // 配置解析必须纯读（#124 / #153）：Program 每次非 CLI 启动都会经此解析单写者门身份，
            // 目录创建只能发生在下面的「真正写审计」路径。
            config = LoadConfigFromArgs(args);
        }
        catch (Exception ex)
        {
            return Degrade("<unresolved>", ex, message, payload, "配置解析失败");
        }

        // 与 JsonLineAuditLogger 的等级过滤保持一致：低于配置阈值的事件直接不写，也不算降级。
        // 过滤放在建目录之前：既然不写，就不该为这条事件碰审计目录（#124 / #153 的零副作用规则）。
        if (AuditLevel.Warn < AuditLevelParser.Parse(config.Audit.LogLevel))
        {
            return new ConflictAuditOutcome(null);
        }

        var auditDirectory = config.Audit.LogDirectory;
        var directoryFailure = TryEnsureWritableAuditDirectory(auditDirectory);
        if (directoryFailure is not null)
        {
            return Degrade(auditDirectory, directoryFailure, message, payload, "审计目录不可写");
        }

        var auditEvent = new AuditEvent(
            EventType: "instance_conflict",
            Level: AuditLevel.Warn,
            TimestampUtc: DateTimeOffset.UtcNow,
            TaskId: Guid.NewGuid().ToString("N"),
            SourcePath: config.Watch.HotFolder,
            Message: message,
            Data: payload);

        try
        {
            await AppendSyncWithRetryAsync(auditDirectory, auditEvent, config.Audit.DiagnosticMode)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Degrade(auditDirectory, ex, message, payload, "冲突留痕写入失败");
        }

        return new ConflictAuditOutcome(null);
    }

    /// <summary>
    /// 冲突留痕直接同步 append 到当天审计文件（#211）：这条路径只在退出门前写一行，
    /// 不起后台队列，失败当场看得见。3 次×300 ms 重试包在真写文件外面：
    /// 常驻 Worker 也写同一个 audit-*.jsonl 时，它开文件只是一小会儿，隔 300 ms 再试通常能过；
    /// 最后一次仍失败就把异常交给调用方降级，不再保留「看起来有重试」的死代码。
    /// 文件共享方式与 JsonLineAuditLogger 一致（FileShare.Read）：Windows 上两边看到同一把共享锁，
    /// POSIX 上 FileShare 只是协作式建议锁。
    /// 不套 100 MB 分卷：冲突行固定写当天基础名，基础名已满时只多出一行超限记录，
    /// 写进 UI 日志页真正尾随的那个文件；下一批批量写仍按既有规则落到 _N（#41）。
    /// </summary>
    private static async Task AppendSyncWithRetryAsync(
        string auditDirectory,
        AuditEvent auditEvent,
        bool diagnosticMode)
    {
        var line = AuditLineFormatter.Format(auditEvent, diagnosticMode);
        var target = Path.Combine(auditDirectory, AuditLogFileNames.BaseName(DateTime.Now) + ".jsonl");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < WriteAttempts)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 冲突留痕的目录创建只在这条写路径做有界尝试：建目录 + DeleteOnClose 探针确认当前账户真能写。
    /// 「目录存在」不等于「当前账户可写」（与 Program.TryPrepareWritableDirectory 同一判断标准）。
    /// 只读 / 不可写安装目录（Program Files、受限账户）下返回异常而不外抛，由调用方降级为可读记录。
    /// </summary>
    private static Exception? TryEnsureWritableAuditDirectory(string auditDirectory)
    {
        if (string.IsNullOrWhiteSpace(auditDirectory))
        {
            return new ArgumentException("审计目录为空", nameof(auditDirectory));
        }

        try
        {
            Directory.CreateDirectory(auditDirectory);
            using var probe = new FileStream(
                Path.Combine(auditDirectory, ".write-probe-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return null;
        }
        catch (Exception ex)
        {
            // 全捕获是有意的：这条路径在进程退出前执行，任何异常都不得阻断退出（#127）。
            return ex;
        }
    }

    /// <summary>
    /// 降级记录必须自洽可读：既写清失败原因与审计目录，也带上冲突事实本身，
    /// 避免只读安装目录下「留痕消失且无痕迹可查」的静默失败。
    /// 双通道：stderr 恒写；Serilog 已初始化（单写者让位路径）时补一路文件日志，
    /// 未初始化（重复启动路径）时静态 Log 是默认静默 logger，自然空转、不新增落点。
    /// </summary>
    private static ConflictAuditOutcome Degrade(
        string auditDirectory,
        Exception failure,
        string message,
        IReadOnlyDictionary<string, string> data,
        string stage)
    {
        var details = string.Join(", ", data.Select(pair => $"{pair.Key}={pair.Value}"));
        var record =
            $"[PhotoPrivacyWorker] conflict audit degraded / 冲突审计留痕降级（{stage}）：" +
            $"{failure.GetType().Name}: {failure.Message}；" +
            $"configured_audit_directory={auditDirectory}；conflict={message}；{details}。" +
            " 冲突事实以本行 conflict= 字段为准；留痕失败不阻断退出（#127）。";

        try
        {
            Console.Error.WriteLine(record);
        }
        catch
        {
            // stderr 本身不可写时也不得阻断退出。
        }

        // 服务会话下 stderr 不落盘：Serilog 已就绪时再补一路文件日志。
        // 按 #85 规则吞掉 provider 故障，日志不得反过来阻断让位退出。
        SafeLog.Try(() => Log.Warning("conflict audit degraded: {DegradationRecord}", record));

        return new ConflictAuditOutcome(record);
    }

    internal static AppConfig LoadConfigFromArgs(string[] args)
    {
        var configPath = ResolveOptionValue(args, "--config")
            ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");

        var config = File.Exists(configPath)
            ? AppConfigLoader.Load(configPath)
            : AppConfig.Default;

        var hotFolder = ResolveOptionValue(args, "--hot-folder");
        if (!string.IsNullOrWhiteSpace(hotFolder))
        {
            config = config with { Watch = config.Watch with { HotFolder = hotFolder } };
        }

        var auditFolder = ResolveOptionValue(args, "--audit-folder");
        if (!string.IsNullOrWhiteSpace(auditFolder))
        {
            config = config with { Audit = config.Audit with { LogDirectory = auditFolder } };
       }

        // 配置解析必须纯读（#153）：Program 每次非 CLI 启动都会调用本方法解析单写者门身份，
        // 目录缺失时不得在这里创建 hot/_audit；真正写冲突留痕时由 WriteAsync 的有界尝试创建。
        return config;
    }

    private static string? ResolveOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(optionName.Length + 1)..].Trim();
            }

            if (!string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length)
            {
                return args[i + 1].Trim();
            }
        }

        return null;
    }
}
