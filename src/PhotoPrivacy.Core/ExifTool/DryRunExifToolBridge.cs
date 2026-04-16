using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class DryRunExifToolBridge : IExifToolBridge
{
    private readonly IAuditLogger _audit;

    public DryRunExifToolBridge(IAuditLogger audit)
    {
        _audit = audit;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(
            new AuditEvent(
                EventType: "dry_run_wipe_skipped",
                TimestampUtc: DateTimeOffset.UtcNow,
                TaskId: Guid.NewGuid().ToString("N"),
                SourcePath: targetPath,
                Message: "dry-run mode enabled, exiftool execution skipped",
                Data: null),
            cancellationToken);
    }
}
