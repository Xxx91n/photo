using Serilog.Core;
using Serilog.Events;

namespace PhotoPrivacy.Core.Audit;

/// <summary>
/// ADR 0023: Serilog IDestructuringPolicy that masks path-related properties during
/// structured destructuring of objects. Registered via LoggerConfiguration.Destructure.With().
/// Handles {@obj} destructured objects; PathMaskingEnricher handles named scalar properties.
/// Together both enrollment paths cover all log-event paths uniformly.
/// </summary>
public sealed class PathMaskingDestructuringPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> PathProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "FilePath", "SourcePath", "path", "WatchDirectory", "HotFolder",
        "QuarantineDirectory", "BackupDirectory", "ExePath", "ConfigPath"
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyFactory, out LogEventPropertyValue result)
    {
        // Only handle structured objects (e.g. anonymous/record types); skip primitives/strings.
        if (value is null || value is string || value.GetType().IsPrimitive)
        {
            result = null!;
            return false;
        }

        // Reflect over public readable properties looking for named path fields.
        var props = value.GetType().GetProperties();
        var matched = false;
        var mutated = new Dictionary<string, LogEventPropertyValue?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanRead || !PathProperties.Contains(p.Name))
            {
                continue;
            }

            var current = p.GetValue(value);
            if (current is string s)
            {
                var masked = PathMasker.Mask(s);
                if (masked != s)
                {
                    mutated[p.Name] = new ScalarValue(masked);
                    matched = true;
                }
            }
        }

        if (!matched)
        {
            result = null!;
            return false;
        }

        // Build a StructureValue copy of the original with masked path properties replaced.
        // Use the default destructuring for the object, then overlay our masked values.
        var defaultValue = propertyFactory.CreatePropertyValue(value, true);
        if (defaultValue is StructureValue structure)
        {
            var newProps = new List<LogEventProperty>(structure.Properties.Count);
            foreach (var prop in structure.Properties)
            {
                if (mutated.TryGetValue(prop.Name, out var replacement) && replacement is not null)
                {
                    newProps.Add(new LogEventProperty(prop.Name, replacement));
                }
                else
                {
                    newProps.Add(prop);
                }
            }
            result = new StructureValue(newProps, structure.TypeTag);
            return true;
        }

        result = null!;
        return false;
    }
}
