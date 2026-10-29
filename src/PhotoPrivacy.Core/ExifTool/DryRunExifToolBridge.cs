using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class DryRunExifToolBridge : IExifToolBridge
{
    private readonly IAuditLogger _audit;

    public string VersionText => "dry-run";

    public DryRunExifToolBridge(IAuditLogger audit)
    {
        _audit = audit;
    }

    /// <summary>dry-run 不上真实进程，版本恒为 <c>dry-run</c>（非 unknown），无探针可跑。</summary>
    public Task EnsureVersionAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(
            new AuditEvent(
                EventType: "dry_run_wipe_skipped",
                Level: AuditLevel.Info,
                TimestampUtc: DateTimeOffset.UtcNow,
                TaskId: Guid.NewGuid().ToString("N"),
                SourcePath: targetPath,
                Message: "dry-run mode enabled, exiftool execution skipped",
                Data: null),
            cancellationToken);
        return WipeResult.Cleaned_Modified;
    }
}
