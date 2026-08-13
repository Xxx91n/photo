using System;

namespace PhotoPrivacy.Core.Audit;

public enum AuditLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

public static class AuditLevelParser
{
    public static AuditLevel Parse(string? logLevel)
    {
        return logLevel?.ToLowerInvariant() switch
        {
            "all" or "debug" => AuditLevel.Debug,
            "info" => AuditLevel.Info,
            "warn" or "warning" => AuditLevel.Warn,
            "error" => AuditLevel.Error,
            _ => AuditLevel.Info
        };
    }
}
