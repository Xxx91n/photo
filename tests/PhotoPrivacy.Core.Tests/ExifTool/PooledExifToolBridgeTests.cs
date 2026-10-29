using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class PooledExifToolBridgeTests
{
    [Fact]
    public async Task WipeMetadataAsync_Should_Distribute_Across_Multiple_Bridges()
    {
        var bridgeA = new FakeBridge("A");
        var bridgeB = new FakeBridge("B");
        var pooled = new PooledExifToolBridge([bridgeA, bridgeB]);

        await pooled.StartAsync(CancellationToken.None);

        await pooled.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);
        await pooled.WipeMetadataAsync(@"D:\hot\b.jpg", CancellationToken.None);
        await pooled.WipeMetadataAsync(@"D:\hot\c.jpg", CancellationToken.None);
        await pooled.WipeMetadataAsync(@"D:\hot\d.jpg", CancellationToken.None);

        await pooled.StopAsync(CancellationToken.None);

        Assert.Equal(2, bridgeA.WipeCalls);
        Assert.Equal(2, bridgeB.WipeCalls);
        Assert.Equal(1, bridgeA.StartCalls);
        Assert.Equal(1, bridgeB.StartCalls);
        Assert.Equal(1, bridgeA.StopCalls);
        Assert.Equal(1, bridgeB.StopCalls);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Use_Effective_Parallelism_Cap()
    {
        var gate = new SemaphoreSlim(0);
        var bridgeA = new BlockingBridge(gate);
        var bridgeB = new BlockingBridge(gate);
        var pooled = new PooledExifToolBridge([bridgeA, bridgeB], maxParallelism: 2);

        await pooled.StartAsync(CancellationToken.None);

        var t1 = pooled.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);
        var t2 = pooled.WipeMetadataAsync(@"D:\hot\b.jpg", CancellationToken.None);
        var t3 = pooled.WipeMetadataAsync(@"D:\hot\c.jpg", CancellationToken.None);

        await Task.Delay(80);
        Assert.Equal(2, BlockingBridge.Inflight);

        gate.Release(3);
        await Task.WhenAll(t1, t2, t3);
        await pooled.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void VersionText_Should_Report_First_Known_Version_Across_Bridges()
    {
        // #86 回归：派发从 index 1 起轮转，pool>1 时首个文件通常落在 bridge[1..]，
        // bridge[0] 可能整个生命周期都没启动过——只看 bridge[0] 会恒报 unknown。
        var bridgeA = new FakeBridge(ExifToolVersionText.Unknown);
        var bridgeB = new FakeBridge("13.20");
        var pooled = new PooledExifToolBridge([bridgeA, bridgeB]);

        Assert.Equal("13.20", pooled.VersionText);
    }

    [Fact]
    public void VersionText_Should_Fall_Back_To_Unknown_When_No_Bridge_Resolved_Version()
    {
        var pooled = new PooledExifToolBridge(
            [new FakeBridge(ExifToolVersionText.Unknown), new FakeBridge(ExifToolVersionText.Unknown)]);

        Assert.Equal(ExifToolVersionText.Unknown, pooled.VersionText);
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Only_Start_The_First_Bridge()
    {
        // #86：一次状态查询没有理由把整个池的 stay_open 进程全部拉起——
        // 版本对同一 ExifTool 二进制是同一值，只解析第一个子桥。
        var bridgeA = new FakeBridge(ExifToolVersionText.Unknown);
        var bridgeB = new FakeBridge(ExifToolVersionText.Unknown);
        var pooled = new PooledExifToolBridge([bridgeA, bridgeB]);

        await pooled.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal(1, bridgeA.EnsureVersionCalls);
        Assert.Equal(0, bridgeB.EnsureVersionCalls);
    }

    [Fact]
    public async Task BuildFromConfig_Should_Create_Single_Bridge_When_Pool_Size_Is_One()
    {
        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = true,
                StayOpenPoolSize = 1,
                MaxParallelDrain = 8
            }
        };

        var bridge = PooledExifToolBridgeFactory.BuildFromConfig(config, new PhotoPrivacy.Core.Audit.NoopAuditLogger());
        await bridge.StartAsync(CancellationToken.None);
        await bridge.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);
        await bridge.StopAsync(CancellationToken.None);

        Assert.IsNotType<PooledExifToolBridge>(bridge);
    }

    private sealed class FakeBridge : IExifToolBridge
    {
        public FakeBridge(string version)
        {
            VersionText = version;
        }

        public string VersionText { get; }

        public int StartCalls { get; private set; }

        public int EnsureVersionCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int WipeCalls { get; private set; }

        public Task EnsureVersionAsync(CancellationToken cancellationToken)
        {
            EnsureVersionCalls++;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            WipeCalls++;
            return Task.FromResult(WipeResult.Cleaned_NoOp);
        }
    }

    private sealed class BlockingBridge : IExifToolBridge
    {
        private readonly SemaphoreSlim _gate;

        public BlockingBridge(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public static int Inflight => Volatile.Read(ref _inflight);

        public string VersionText => "blocking";

        private static int _inflight;

        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _inflight);
            try
            {
                await _gate.WaitAsync(cancellationToken);
                return WipeResult.Cleaned_NoOp;
            }
            finally
            {
                Interlocked.Decrement(ref _inflight);
            }
        }
    }
}
