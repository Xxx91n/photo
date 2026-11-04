using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Worker;

internal static class InstanceConflictAudit
{
    public static async Task TryWriteAsync(string[] args, RuntimeMode requestedMode, string mutexName)
    {
        await TryWriteAsync(
            args,
            requestedMode,
            message: "检测到重复启动并已拒绝",
            data: new Dictionary<string, string>
            {
                ["mutex_name"] = mutexName
            }).ConfigureAwait(false);
    }

    /// <summary>单写者准入门（#162）让位留痕：后启动 Worker 对同 config 审计目录干净退出前写一条。</summary>
    public static async Task TryWriteSingleWriterYieldAsync(string[] args, RuntimeMode requestedMode, string auditDirectory)
    {
        await TryWriteAsync(
            args,
            requestedMode,
            message: "检测到另一 Worker 正在使用同一审计目录，本进程让位退出",
            data: new Dictionary<string, string>
            {
                ["reason"] = "single_writer_gate",
                ["audit_directory"] = auditDirectory
            }).ConfigureAwait(false);
    }

    private static async Task TryWriteAsync(
        string[] args,
        RuntimeMode requestedMode,
        string message,
        IReadOnlyDictionary<string, string> data)
    {
        try
        {
            var config = LoadConfigFromArgs(args);
            var logger = new JsonLineAuditLogger(
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

            try
            {
                // 有界重试：常驻 Worker 的 JsonLineAuditLogger 按次打开 audit-*.jsonl（FileShare.Read），
                // 让位留痕/冲突留痕与其短暂撞上会被共享冲突打掉——best-effort 留痕值得 3 次机会，
                // 仍失败就吞（留痕永不阻断退出；stderr 与 Serilog 文件日志是另外两路可观测面）。
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
            finally
            {
                logger.Dispose();
            }
        }
        catch
        {
            // conflict audit must never block process exit
        }
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
        // 目录缺失时不得在这里创建 hot/_audit；真正写冲突留痕时由 JsonLineAuditLogger 构造创建。
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
