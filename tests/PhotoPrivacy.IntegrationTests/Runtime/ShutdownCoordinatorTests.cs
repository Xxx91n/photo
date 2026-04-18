using PhotoPrivacy.Cli;

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

    [Fact]
    public void ShouldShowInteractivePrompt_Should_Be_True_For_Service_Mode_When_UserInteractive()
    {
        var shouldShow = InstanceConflictUiPolicy.ShouldShowInteractivePrompt(RuntimeMode.Service, isUserInteractive: true);

        Assert.True(shouldShow);
    }

    [Fact]
    public void ShouldShowInteractivePrompt_Should_Be_False_For_Cli_Mode()
    {
        var shouldShow = InstanceConflictUiPolicy.ShouldShowInteractivePrompt(RuntimeMode.Cli, isUserInteractive: true);

        Assert.False(shouldShow);
    }

    [Fact]
    public void ShouldShowInteractivePrompt_Should_Be_False_When_Not_UserInteractive()
    {
        var shouldShow = InstanceConflictUiPolicy.ShouldShowInteractivePrompt(RuntimeMode.Service, isUserInteractive: false);

        Assert.False(shouldShow);
    }
}
