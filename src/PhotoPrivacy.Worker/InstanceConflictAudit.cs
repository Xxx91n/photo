using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using Serilog;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 重复启动 / 单写者准入门让位时的冲突留痕（#127）。
/// 这是「真正写审计」的路径，是「配置加载期零目录副作用」（#124 / #153）的显式例外：
/// 目录只在这里创建，且创建前有界确认可写；只读 / 不可写安装目录下不改写冲突审计落点
/// （不另择 %ProgramData% 或 ~/.local/state），只降级为可读记录并保留冲突事实：
/// stderr 恒写，Serilog 已初始化时再补一路文件日志（服务会话下 stderr 不落盘）。
/// 残余：留痕本身仍是 best-effort，后台落盘失败由 JsonLineAuditLogger 内部吞掉；
/// 下方那 3 次 IOException 重试对当前 WriteAsync 实现是死代码（只入队、不碰文件，不抛 IOException），
/// 删除或改造成能感知落盘失败的重试由 #211 裁定。两条残余均继承 #162 / PR #163，由 #211 承接。
/// </summary>
// Design(#82): 配置解析与待机路径不得建目录；冲突留痕只在写路径有界建目录，失败降级为可读记录且不阻断退出，只读安装目录不改写落点（后台 flush 残余见 #211）。
internal static class InstanceConflictAudit
{
    /// <summary>
    /// 留痕结果：DegradationRecord 非空即表示降级（可读记录已由 Degrade 写 stderr + Serilog）；
    /// 为空只表示「未观察到同步失败」，不等于落盘成功——后台 flush 仍是 best-effort（残余见类注释）。
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
        AppConfig config;
        try
        {
            // 配置解析必须纯读（#124 / #153）：Program 每次非 CLI 启动都会经此解析单写者门身份，
            // 目录创建只能发生在下面的「真正写审计」路径。
            config = LoadConfigFromArgs(args);
        }
        catch (Exception ex)
        {
            return Degrade("<unresolved>", ex, message, data, "配置解析失败");
        }

        var auditDirectory = config.Audit.LogDirectory;
        var directoryFailure = TryEnsureWritableAuditDirectory(auditDirectory);
        if (directoryFailure is not null)
        {
            return Degrade(auditDirectory, directoryFailure, message, data, "审计目录不可写");
        }

        JsonLineAuditLogger? logger = null;
        Exception? writeFailure = null;
        try
        {
            logger = new JsonLineAuditLogger(
                config.Audit.LogDirectory,
                config.Audit.RetainDays,
                config.Audit.DiagnosticMode,
                AuditLevelParser.Parse(config.Audit.LogLevel));

            var payload = new Dictionary<string, string>
            {
                ["requested_mode"] = requestedMode.ToString().ToLowerInvariant()
            };
            foreach (var (key, value) in data)
            {
                payload[key] = value;
            }

            // 有界重试（#162 引入）：对当前 JsonLineAuditLogger.WriteAsync 实现是死代码——
            // 它只做 Serialize + Channel.TryWrite（满即 DropOldest），不碰文件、不抛 IOException。
            // 后台 flush 与常驻 Worker 抢 audit-*.jsonl 的共享冲突同样不由这里感知；
            // 删除或改造成能感知落盘失败的重试由 #211 裁定（见类注释）。
            // 构造/入队阶段抛出的其他异常由外层降级为可读记录（留痕永不阻断退出）。
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await logger.WriteAsync(
                        new AuditEvent(
                            EventType: "instance_conflict",
                            Level: AuditLevel.Warn,
                            TimestampUtc: DateTimeOffset.UtcNow,
                            TaskId: Guid.NewGuid().ToString("N"),
                            SourcePath: config.Watch.HotFolder,
                            Message: message,
                            Data: payload),
                        CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                catch (IOException) when (attempt < 3)
                {
                    await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            writeFailure = ex;
        }
        finally
        {
            if (logger is not null)
            {
                // Dispose 会等待后台落盘通道排空（<=5s）；自身吞掉后台异常，这里只兜住意外逃逸。
                try
                {
                    logger.Dispose();
                }
                catch (Exception ex)
                {
                    writeFailure ??= ex;
                }
            }
        }

        return writeFailure is null
            ? new ConflictAuditOutcome(null)
            : Degrade(auditDirectory, writeFailure, message, data, "冲突留痕写入失败");
    }

    /// <summary>
    /// 冲突留痕的目录创建只在这条写路径做有界尝试：建目录 + DeleteOnClose 探针确认当前账户真能写。
    /// 「目录存在」不等于「当前账户可写」（与 Program.TryPrepareWritableDirectory 同判据）。
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
        // 按 #85 口径吞掉 provider 故障，日志不得反过来阻断让位退出。
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
