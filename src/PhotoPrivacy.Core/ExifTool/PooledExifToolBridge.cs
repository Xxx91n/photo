using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class PooledExifToolBridge : IExifToolBridge, IDisposable
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

    // 原来只看 _bridges[0]，但派发从 index 1 起轮转，第一个文件往往落在 _bridges[1]，
    // bridge[0] 可能整个生命周期都没启动过 → 恒报 unknown。版本对同一 ExifTool 二进制
    // 是同一值，取任一非 unknown 的子桥值语义等价。
    // Design(#86): 池化桥上报「首个已解析出的版本」，不再锚定 _bridges[0]。
    public string VersionText
    {
        get
        {
            foreach (var bridge in _bridges)
            {
                var version = bridge.VersionText;
                if (!ExifToolVersionText.IsUnknown(version))
                {
                    return version;
                }
            }

            return ExifToolVersionText.Unknown;
        }
    }

    /// <summary>
    /// 版本解析只拉起池里的第一个子桥（#86）——版本对同一 ExifTool 二进制是同一值，
    /// 没有必要为一次状态查询把整个池的 stay_open 进程全部启动。
    /// 已知单点：若子桥 0 的可执行文件路径本身失效，其余子桥同样起不来（配置共享同一路径），
    /// 因此不为这一情形扩散到整池。
    /// </summary>
    // Design(#86): 版本解析只启动 _bridges[0]，不为一次状态查询拉起整池。
    public Task EnsureVersionAsync(CancellationToken cancellationToken)
        => _bridges[0].EnsureVersionAsync(cancellationToken);

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

    public void Dispose()
    {
        foreach (var bridge in _bridges)
        {
            (bridge as IDisposable)?.Dispose();
        }
        _parallelGate.Dispose();
    }
}

public static class PooledExifToolBridgeFactory
{
    public static IExifToolBridge BuildFromConfig(AppConfig config, IAuditLogger audit, IReadOnlyDictionary<string, bool>? wipeRules = null)
    {
        if (config.ExifTool.DryRun)
        {
            return new DryRunExifToolBridge(audit);
        }

        var poolSize = Math.Max(1, config.ExifTool.StayOpenPoolSize);
        if (poolSize == 1)
        {
            return BuildSingle(config, audit, wipeRules);
        }

        var bridges = new List<IExifToolBridge>(poolSize);
        for (var i = 0; i < poolSize; i++)
        {
            bridges.Add(BuildSingle(config, audit, wipeRules));
        }

        return new PooledExifToolBridge(bridges, config.ExifTool.MaxParallelDrain);
    }

    private static IExifToolBridge BuildSingle(AppConfig config, IAuditLogger audit, IReadOnlyDictionary<string, bool>? wipeRules)
    {
        return new ExifToolBridge(
            process: new ProcessExifToolProcess(),
            config: config,
            wipeRules: wipeRules,
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
