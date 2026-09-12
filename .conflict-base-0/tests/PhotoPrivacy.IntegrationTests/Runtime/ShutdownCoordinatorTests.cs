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
        coordinator.RequestStop();
        coordinator.RequestStop();

        await coordinator.WaitForFirstRequestAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(100);

        Assert.Equal(1, calls);
    }

}
