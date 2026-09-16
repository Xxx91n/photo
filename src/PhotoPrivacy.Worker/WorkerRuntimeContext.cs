using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;

namespace PhotoPrivacy.Worker;

public sealed class WorkerRuntimeContext : IWorkerIpcRuntime
{
    private readonly IRuntimeControl _runtimeControl;
    private readonly MetadataCleanerWorker _worker;
    private readonly Func<string> _watchDirectoryAccessor;
    private readonly Func<string?> _auditLogDirectoryAccessor;

    public WorkerRuntimeContext(
        IRuntimeControl runtimeControl,
        MetadataCleanerWorker worker,
        Func<string> watchDirectoryAccessor,
        RuntimeMode mode,
        Func<string?>? auditLogDirectoryAccessor = null)
    {
        _runtimeControl = runtimeControl;
        _worker = worker;
        _watchDirectoryAccessor = watchDirectoryAccessor;
        _auditLogDirectoryAccessor = auditLogDirectoryAccessor ?? (() => null);
        Mode = mode;
    }

    public RuntimeMode Mode { get; }

    public bool IsPaused => _runtimeControl.IsPaused;

    public string ExifToolVersion => _worker.CurrentExifToolVersion;

    public string WatchDirectory => _watchDirectoryAccessor();

    /// <summary>
    /// ADR 0046: audit log directory for GetRecentLogs IPC channel.
    /// </summary>
    public string? AuditLogDirectory => _auditLogDirectoryAccessor();

    public void Pause() => _runtimeControl.Pause();

    public void Resume() => _runtimeControl.Resume();

    public Task ReloadConfigAsync() => _worker.ReloadConfigAsync();

    /// <summary>
    /// ADR 0033: Validate config without applying. Returns (valid, errorMessage).
    /// </summary>
    public (bool valid, string? error) TryValidateConfig()
    {
        return _worker.TryValidateConfig();
    }
}
