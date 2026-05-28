using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Watcher;

public sealed class FswFolderWatcher : IFolderWatcher
{
    private readonly AppConfig _config;
    private readonly IRecoveryScanner _scanner;
    private readonly IAuditLogger _audit;
    private readonly Func<string, Task> _onPath;
    private readonly IFileSystemWatcherFactory _factory;
    private readonly IReadOnlyList<string> _autoExcludedSubdirectories;
    private readonly ILogger _logger;

    private FileSystemWatcher? _fsw;
    private PollingFallbackScanner? _poller;

    public FswFolderWatcher(
        AppConfig config,
        IRecoveryScanner scanner,
        IAuditLogger audit,
        Func<string, Task> onPath,
        IFileSystemWatcherFactory? factory = null,
        ILogger? logger = null)
    {
        _config = config;
        _scanner = scanner;
        _audit = audit;
        _onPath = onPath;
        _factory = factory ?? (IFileSystemWatcherFactory)new DefaultFileSystemWatcherFactory();
        _autoExcludedSubdirectories = WatchPathFilter.ResolveAutoExcludedSubdirectories(config);
        _logger = logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        _fsw = BuildWatcher();
        _fsw.EnableRaisingEvents = true;

        // Start polling fallback if configured (polling_interval_seconds > 0)
        if (_config.Watch.PollingIntervalSeconds > 0)
        {
            var interval = TimeSpan.FromSeconds(_config.Watch.PollingIntervalSeconds);
            _poller = new PollingFallbackScanner(
                _config.Watch.HotFolder,
                _onPath,
                path => WatchPathFilter.ShouldSkipPath(path, _autoExcludedSubdirectories),
                interval,
                _logger);
            _poller.Start();
        }
    }

    public void Stop()
    {
        _poller?.Dispose();
        _poller = null;
        _fsw?.Dispose();
        _fsw = null;
    }

    public void RecoverFromError(Exception ex)
    {
        _ = RecoverAsync(ex);
    }

    public Task RecoverFromErrorAsync(Exception ex)
    {
        return RecoverAsync(ex);
    }

    private FileSystemWatcher BuildWatcher()
    {
        var watcher = _factory.Create(_config.Watch.HotFolder);
        watcher.IncludeSubdirectories = _config.Watch.IncludeSubdirectories;
        watcher.InternalBufferSize = _config.Watch.InternalBufferSize;
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName;

        watcher.Created += (_, e) => _ = OnFsEventAsync(e.FullPath);
        watcher.Changed += (_, e) => _ = OnFsEventAsync(e.FullPath);
        watcher.Renamed += (_, e) => _ = OnFsEventAsync(e.FullPath);
        watcher.Error += (_, e) => _ = RecoverAsync(e.GetException() ?? new IOException("FileSystemWatcher error"));

        return watcher;
    }

    private async Task RecoverAsync(Exception ex)
    {
        await _audit.WriteAsync(
            new AuditEvent("fsw_error", AuditLevel.Warn, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, ex.Message, null),
            CancellationToken.None);

        Stop();
        Start();

        // Force polling rescan immediately after FSW recovery to catch anything missed
        if (_poller is not null)
        {
            await _poller.ForceRescanAsync();
        }

        foreach (var path in _scanner.ScanAll(_config.Watch.HotFolder))
        {
            if (WatchPathFilter.ShouldSkipPath(path, _autoExcludedSubdirectories))
            {
                continue;
            }

            await _onPath(path);
        }

        await _audit.WriteAsync(
            new AuditEvent("fsw_recovered", AuditLevel.Info, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, "recreated_watcher", null),
            CancellationToken.None);
    }

    public IReadOnlyList<string> GetAutoExcludedSubdirectories()
    {
        return _autoExcludedSubdirectories;
    }

    private Task OnFsEventAsync(string fullPath)
    {
        if (WatchPathFilter.ShouldSkipPath(fullPath, _autoExcludedSubdirectories))
        {
            return Task.CompletedTask;
        }

        return _onPath(fullPath);
    }

    private sealed class DefaultFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public FileSystemWatcher Create(string path)
        {
            return new FileSystemWatcher(path);
        }
    }
}
