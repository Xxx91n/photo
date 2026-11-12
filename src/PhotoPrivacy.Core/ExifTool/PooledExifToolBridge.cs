using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// 上报「首个已解析出的版本」：池内任一子桥解析出版本即可，全部未知时仍是 unknown 哨兵。
    /// </summary>
    // 原来只看 _bridges[0]，但派发从 index 1 起轮转，第一个文件往往落在 _bridges[1]，
    // bridge[0] 可能整个生命周期都没启动过 → 恒报 unknown。版本对同一 ExifTool 二进制
    // 是同一值，取任一非 unknown 的子桥值语义等价。
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

    /// <summary>
    /// 逐个释放子桥；<c>_parallelGate</c> 有意不 Dispose（#126，与 <see cref="ExifToolBridge.Dispose"/>
    /// 同一族）：在途 <see cref="WipeMetadataAsync"/> 的 finally Release 可能跨过停机/重载对旧池的
    /// Dispose，先 Dispose 会命中 Release 抛 ObjectDisposedException，或把挂在 WaitAsync 上的
    /// 等待者永久吊住。信号量无非托管资源，随 GC 与进程退出收尾。
    /// </summary>
    public void Dispose()
    {
        foreach (var bridge in _bridges)
        {
            (bridge as IDisposable)?.Dispose();
        }
    }
}

public static class PooledExifToolBridgeFactory
{
    /// <param name="processLogger">
    /// 透传给每个 <see cref="ProcessExifToolProcess"/> 的日志器（#37）：停机期 stdin 管道已断的
    /// 预期异常降 Debug、非预期失败留 Warning 痕——该类自身不持有审计/生命周期缝，只能走日志。
    /// </param>
    public static IExifToolBridge BuildFromConfig(AppConfig config, IAuditLogger audit, IReadOnlyDictionary<string, bool>? wipeRules = null, ILogger? processLogger = null)
    {
        if (config.ExifTool.DryRun)
        {
            return new DryRunExifToolBridge(audit);
        }

        var poolSize = Math.Max(1, config.ExifTool.StayOpenPoolSize);
        if (poolSize == 1)
        {
            return BuildSingle(config, audit, wipeRules, processLogger);
        }

        var bridges = new List<IExifToolBridge>(poolSize);
        for (var i = 0; i < poolSize; i++)
        {
            bridges.Add(BuildSingle(config, audit, wipeRules, processLogger));
        }

        return new PooledExifToolBridge(bridges, config.ExifTool.MaxParallelDrain);
    }

    private static IExifToolBridge BuildSingle(AppConfig config, IAuditLogger audit, IReadOnlyDictionary<string, bool>? wipeRules, ILogger? processLogger)
    {
        return new ExifToolBridge(
            process: new ProcessExifToolProcess(logger: processLogger),
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
