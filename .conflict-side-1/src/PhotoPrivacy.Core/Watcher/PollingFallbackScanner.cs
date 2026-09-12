using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PhotoPrivacy.Core.Watcher;

/// <summary>
/// Periodic polling scanner that compensates for FileSystemWatcher missed events.
/// Compares file snapshots (last-write + size) at a configurable interval and
/// enqueues any changed or newly-discovered files.
/// </summary>
public sealed class PollingFallbackScanner : IDisposable
{
    private readonly string _rootPath;
    private readonly Func<string, Task> _onPath;
    private readonly Func<string, bool> _shouldSkip;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;

    private Timer? _timer;
    private Dictionary<string, FileSnapshot> _previousSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _snapshotLock = new();

    public PollingFallbackScanner(
        string rootPath,
        Func<string, Task> onPath,
        Func<string, bool> shouldSkip,
        TimeSpan interval,
        ILogger? logger = null)
    {
        _rootPath = rootPath;
        _onPath = onPath;
        _shouldSkip = shouldSkip;
        _interval = interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
        _logger = logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        lock (_snapshotLock)
        {
            _previousSnapshot = TakeSnapshot();
        }

        _timer = new Timer(OnTick, null, _interval, _interval);
        _logger.LogInformation("Polling fallback started. Interval={Interval}s, Root={Root}", _interval.TotalSeconds, _rootPath);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Force a full rescan now (e.g. after FileSystemWatcher error recovery).
    /// </summary>
    public async Task ForceRescanAsync()
    {
        var changes = DetectChanges();
        foreach (var path in changes)
        {
            await _onPath(path);
        }
    }

    private async void OnTick(object? state)
    {
        try
        {
            var changes = DetectChanges();
            foreach (var path in changes)
            {
                await _onPath(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polling fallback scan failed.");
        }
    }

    private List<string> DetectChanges()
    {
        var current = TakeSnapshot();
        var changes = new List<string>();

        lock (_snapshotLock)
        {
            foreach (var (path, snap) in current)
            {
                if (_shouldSkip(path))
                    continue;

                if (!_previousSnapshot.TryGetValue(path, out var prev))
                {
                    // New file
                    changes.Add(path);
                }
                else if (prev.LastWriteUtc != snap.LastWriteUtc || prev.Size != snap.Size)
                {
                    // Modified file
                    changes.Add(path);
                }
            }

            _previousSnapshot = current;
        }

        return changes;
    }

    private Dictionary<string, FileSnapshot> TakeSnapshot()
    {
        var snapshot = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_rootPath))
            return snapshot;

        try
        {
            foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
            {
                if (_shouldSkip(file))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists)
                    {
                        snapshot[file] = new FileSnapshot(info.LastWriteTimeUtc, info.Length);
                    }
                }
                catch
                {
                    // File may have been deleted between EnumerateFiles and FileInfo — skip.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polling snapshot failed for {Root}", _rootPath);
        }

        return snapshot;
    }

    private readonly record struct FileSnapshot(DateTime LastWriteUtc, long Size);
}
