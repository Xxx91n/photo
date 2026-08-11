using Serilog.Core;
using Serilog.Events;

namespace PhotoPrivacy.Core.Audit;

/// <summary>
/// ADR 0023: Serilog Enricher that masks path-related properties in log events.
/// Registered via LoggerConfiguration.Enrich.With{PathMaskingEnricher}().
/// </summary>
public sealed class PathMaskingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> PathProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "FilePath", "SourcePath", "path", "WatchDirectory", "HotFolder",
        "QuarantineDirectory", "BackupDirectory", "ExePath", "ConfigPath"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var prop in logEvent.Properties.ToList())
        {
            if (PathProperties.Contains(prop.Key) && prop.Value is ScalarValue { Value: string s })
            {
                var masked = PathMasker.Mask(s);
                if (masked != s)
                {
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(prop.Key, masked));
                }
            }
        }
    }
}
