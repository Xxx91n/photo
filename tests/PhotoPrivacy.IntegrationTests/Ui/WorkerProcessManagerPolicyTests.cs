using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class WorkerProcessManagerPolicyTests
{
    [Fact]
    public async Task ConnectOrLaunchAsync_Should_Return_Service_Mode_When_Service_Installed_But_Pipe_Not_Alive()
    {
        var manager = new WorkerProcessManager(new WorkerIpcClient());

        var result = await manager.ConnectOrLaunchAsync(
            workerExecutablePath: null,
            cancellationToken: CancellationToken.None,
            getServiceRuntimeState: () => ServiceRuntimeState.Stopped);

        Assert.Equal("service", result.RuntimeKind);
        Assert.Equal("PhotoPrivacyCleaner.Service", result.EndpointName, ignoreCase: false);
        Assert.False(result.ShouldShowTrayIcon);
    }
}
