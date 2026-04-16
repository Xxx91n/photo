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

    private FileSystemWatcher? _fsw;

    public FswFolderWatcher(
        AppConfig config,
        IRecoveryScanner scanner,
        IAuditLogger audit,
        Func<string, Task> onPath,
        IFileSystemWatcherFactory? factory = null)
    {
        _config = config;
        _scanner = scanner;
        _audit = audit;
        _onPath = onPath;
        _factory = factory ?? (IFileSystemWatcherFactory)new DefaultFileSystemWatcherFactory();
    }

    public void Start()
    {
        _fsw = BuildWatcher();
        _fsw.EnableRaisingEvents = true;
    }

    public void Stop()
    {
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

        watcher.Created += (_, e) => _ = _onPath(e.FullPath);
        watcher.Changed += (_, e) => _ = _onPath(e.FullPath);
        watcher.Renamed += (_, e) => _ = _onPath(e.FullPath);
        watcher.Error += (_, e) => _ = RecoverAsync(e.GetException() ?? new IOException("FileSystemWatcher error"));

        return watcher;
    }

    private async Task RecoverAsync(Exception ex)
    {
        await _audit.WriteAsync(
            new AuditEvent("fsw_error", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, ex.Message, null),
            CancellationToken.None);

        Stop();
        Start();

        foreach (var path in _scanner.ScanAll(_config.Watch.HotFolder))
        {
            await _onPath(path);
        }

        await _audit.WriteAsync(
            new AuditEvent("fsw_recovered", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), _config.Watch.HotFolder, "recreated_watcher", null),
            CancellationToken.None);
    }

    private sealed class DefaultFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public FileSystemWatcher Create(string path)
        {
            return new FileSystemWatcher(path);
        }
    }
}
