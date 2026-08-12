using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Worker;

internal static class InstanceConflictAudit
{
    public static async Task TryWriteAsync(string[] args, RuntimeMode requestedMode, string mutexName)
    {
        try
        {
            var config = LoadConfigFromArgs(args);
            var logger = new JsonLineAuditLogger(
                config.Audit.LogDirectory,
                config.Audit.RetainDays,
                config.Audit.DiagnosticMode);

            try
            {
                await logger.WriteAsync(
                    new AuditEvent(
                        EventType: "instance_conflict",
                        Level: AuditLevel.Warn,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        TaskId: Guid.NewGuid().ToString("N"),
                        SourcePath: config.Watch.HotFolder,
                        Message: "检测到重复启动并已拒绝",
                        Data: new Dictionary<string, string>
                        {
                            ["requested_mode"] = requestedMode.ToString().ToLowerInvariant(),
                            ["mutex_name"] = mutexName
                        }),
                    CancellationToken.None).ConfigureAwait(false);
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

    private static AppConfig LoadConfigFromArgs(string[] args)
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

        Directory.CreateDirectory(config.Audit.LogDirectory);
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
