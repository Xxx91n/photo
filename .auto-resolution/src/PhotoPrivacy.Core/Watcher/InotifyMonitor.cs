using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PhotoPrivacy.Core.Watcher;

/// <summary>
/// Cross-platform inotify instance monitor.
/// On Linux, reads /proc/sys/fs/inotify/{max_user_watches,max_user_instances}
/// and logs warnings when usage approaches the limit.
/// On other platforms, this is a no-op.
/// </summary>
public static class InotifyMonitor
{
    private const string MaxWatchesPath = "/proc/sys/fs/inotify/max_user_watches";
    private const string MaxInstancesPath = "/proc/sys/fs/inotify/max_user_instances";
    private const string WatchCountPath = "/proc/sys/fs/inotify/max_queued_events";

    /// <summary>
    /// Checks inotify limits on Linux and logs warnings if approaching the limit.
    /// Returns a diagnostic string for audit logging, or null if not applicable.
    /// </summary>
    public static string? CheckAndWarn(ILogger? logger = null)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        logger ??= NullLogger.Instance;

        try
        {
            var maxWatches = ReadProcValue(MaxWatchesPath);
            var maxInstances = ReadProcValue(MaxInstancesPath);

            if (maxWatches is null && maxInstances is null)
                return null;

            var currentWatches = GetCurrentWatchCount();
            var currentInstances = GetCurrentInstanceCount();

            var parts = new List<string>();

            if (maxWatches is not null)
            {
                var usagePct = currentWatches > 0 && maxWatches > 0
                    ? (int)(currentWatches * 100 / maxWatches)
                    : 0;

                parts.Add($"watches: {currentWatches}/{maxWatches} ({usagePct}%)");

                if (usagePct > 80)
                {
                    logger.LogWarning(
                        "inotify watch usage is at {Pct}% ({Current}/{Max}). " +
                        "Consider increasing: echo {NewMax} | sudo tee {Path}",
                        usagePct, currentWatches, maxWatches,
                        maxWatches * 2, MaxWatchesPath);
                }
            }

            if (maxInstances is not null)
            {
                parts.Add($"instances: {currentInstances}/{maxInstances}");

                if (currentInstances > maxInstances * 0.8)
                {
                    logger.LogWarning(
                        "inotify instance usage is high ({Current}/{Max}). " +
                        ".NET 11+ shares a single instance per process; consider upgrading.",
                        currentInstances, maxInstances);
                }
            }

            return $"inotify: {string.Join(", ", parts)}";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "inotify limit check failed (non-fatal).");
            return null;
        }
    }

    private static long? ReadProcValue(string path)
    {
        if (!File.Exists(path))
            return null;

        var text = File.ReadAllText(path).Trim();
        return long.TryParse(text, out var value) ? value : null;
    }

    private static long GetCurrentWatchCount()
    {
        // Count inotify watches by listing /proc/*/fd/* symlinks pointing to inotify
        // This is an approximation; the exact count requires parsing /proc/sys/fs/inotify/
        // For simplicity, we count directories under the watched path.
        // A more precise approach would parse /proc/*/fdinfo/* for inotify entries.
        try
        {
            var fdDir = "/proc/self/fd";
            if (!Directory.Exists(fdDir))
                return 0;

            var count = 0;
            foreach (var entry in Directory.EnumerateFiles(fdDir))
            {
                try
                {
                    var link = File.ResolveLinkTarget(entry, returnFinalTarget: false);
                    if (link is not null && link.FullName.Contains("inotify", StringComparison.Ordinal))
                        count++;
                }
                catch
                {
                    // Permission denied or fd closed — skip
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetCurrentInstanceCount()
    {
        // Count inotify instances by counting inotify file descriptors across all processes.
        // Simplified: just count our own process's inotify fds.
        try
        {
            var fdDir = "/proc/self/fd";
            if (!Directory.Exists(fdDir))
                return 0;

            var count = 0;
            foreach (var entry in Directory.EnumerateFiles(fdDir))
            {
                try
                {
                    var link = File.ResolveLinkTarget(entry, returnFinalTarget: false);
                    if (link is not null && link.FullName.Contains("inotify", StringComparison.Ordinal))
                        count++;
                }
                catch
                {
                    // skip
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
