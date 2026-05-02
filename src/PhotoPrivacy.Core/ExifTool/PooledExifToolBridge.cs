using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class PooledExifToolBridge : IExifToolBridge
{
    private readonly IExifToolBridge[] _bridges;
    private readonly SemaphoreSlim _parallelGate;
    private int _nextIndex;

    public PooledExifToolBridge(IReadOnlyList<IExifToolBridge> bridges, int maxParallelism = 0)
    {
        if (bridges.Count == 0)
        {
            throw new ArgumentException("bridges must not be empty", nameof(bridges));
        }

        _bridges = bridges.ToArray();
        var cap = maxParallelism > 0 ? maxParallelism : _bridges.Length;
        _parallelGate = new SemaphoreSlim(Math.Min(cap, _bridges.Length));
    }

    public string VersionText => _bridges[0].VersionText;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var bridge in _bridges)
        {
            await bridge.StartAsync(cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var bridge in _bridges)
        {
            try
            {
                await bridge.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(failures);
        }
    }

    public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await _parallelGate.WaitAsync(cancellationToken);
        try
        {
            var index = (uint)Interlocked.Increment(ref _nextIndex);
            var selected = _bridges[index % (uint)_bridges.Length];
            return await selected.WipeMetadataAsync(targetPath, cancellationToken);
        }
        finally
        {
            _parallelGate.Release();
        }
    }
}

public static class PooledExifToolBridgeFactory
{
    public static IExifToolBridge BuildFromConfig(AppConfig config, IAuditLogger audit)
    {
        if (config.ExifTool.DryRun)
        {
            return new DryRunExifToolBridge(audit);
        }

        var poolSize = Math.Max(1, config.ExifTool.StayOpenPoolSize);
        if (poolSize == 1)
        {
            return BuildSingle(config, audit);
        }

        var bridges = new List<IExifToolBridge>(poolSize);
        for (var i = 0; i < poolSize; i++)
        {
            bridges.Add(BuildSingle(config, audit));
        }

        return new PooledExifToolBridge(bridges, config.ExifTool.MaxParallelDrain);
    }

    private static IExifToolBridge BuildSingle(AppConfig config, IAuditLogger audit)
    {
        return new ExifToolBridge(
            process: new ProcessExifToolProcess(),
            config: config,
            logger: null,
            lifecycleSink: async (lifecycleEvent, cancellationToken) =>
            {
                await audit.WriteAsync(
                    new AuditEvent(
                        EventType: lifecycleEvent.EventType,
                        Level: AuditLevel.Info,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        TaskId: Guid.NewGuid().ToString("N"),
                        SourcePath: lifecycleEvent.SourcePath,
                        Message: lifecycleEvent.Message,
                        Data: lifecycleEvent.Data),
                    cancellationToken);
            });
    }
}
