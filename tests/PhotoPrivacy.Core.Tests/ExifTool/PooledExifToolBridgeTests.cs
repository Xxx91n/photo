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

        public int StopCalls { get; private set; }

        public int WipeCalls { get; private set; }

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
