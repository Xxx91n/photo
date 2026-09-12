using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.Runtime;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public async Task RequestStop_Should_Invoke_StopHost_Only_Once()
    {
        var calls = 0;
        var coordinator = new ShutdownCoordinator(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1));

        coordinator.RequestStop();

        // 票 28 返修三轮：SUT 的 stopHost 经 Task.Run 调度，与 WaitForFirstRequestAsync 返回无先后
        // 保证（CI 两次实锤 calls==0 调度竞态）——轮询至回调确已执行再验证 exactly-once。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref calls) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        coordinator.RequestStop();
        coordinator.RequestStop();
        await Task.Delay(100);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

}
