using PhotoPrivacy.Ui;
using Xunit;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

public class ServiceStateProbeAndWaitTests
{
    [Fact]
    public void SystemdStateProbe_Should_Return_NotInstalled_On_NonLinux()
    {
        var probe = new SystemdStateProbe();
        if (!OperatingSystem.IsLinux())
        {
            Assert.Equal(ServiceRuntimeState.NotInstalled, probe.GetState("photoprivacy"));
            Assert.False(probe.ServiceExists("photoprivacy"));
        }
    }

    [Fact]
    public void LaunchdStateProbe_Should_Return_NotInstalled_On_NonMacOS()
    {
        var probe = new LaunchdStateProbe();
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Equal(ServiceRuntimeState.NotInstalled, probe.GetState("photoprivacy"));
            Assert.False(probe.ServiceExists("photoprivacy"));
        }
    }

    [Fact]
    public async Task WaitForStateAsync_Should_Return_Immediately_When_Already_In_Desired_State()
    {
        var probe = new FakeStateProbe(ServiceRuntimeState.Running);
        var manager = new ServiceManager(new FakeExecutor(), probe);

        var result = await manager.WaitForStateAsync(ServiceRuntimeState.Running);

        Assert.Equal(ServiceRuntimeState.Running, result);
        Assert.Equal(1, probe.CallCount); // returns on first check, no delay
    }

    [Fact]
    public async Task WaitForStateAsync_Should_Retry_And_Return_Final_State_When_Never_Matches()
    {
        var probe = new FakeStateProbe(ServiceRuntimeState.Stopped);
        // We want desired=Running but probe always returns Stopped
        var manager = new ServiceManager(new FakeExecutor(), probe);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await manager.WaitForStateAsync(ServiceRuntimeState.Running, cts.Token);

        Assert.Equal(ServiceRuntimeState.Stopped, result);
        // Should have retried multiple times
        Assert.True(probe.CallCount > 1);
    }

    [Fact]
    public void ServiceManager_IsAvailable_Should_Be_True_On_Supported_Platforms()
    {
        var manager = new ServiceManager(new FakeExecutor(), new FakeStateProbe(ServiceRuntimeState.NotInstalled));
        Assert.True(manager.IsAvailable);
    }

    private sealed class FakeStateProbe : IServiceStateProbe
    {
        private readonly ServiceRuntimeState _state;
        public int CallCount;

        public FakeStateProbe(ServiceRuntimeState state) => _state = state;

        public ServiceRuntimeState GetState(string serviceName)
        {
            CallCount++;
            return _state;
        }

        public bool ServiceExists(string serviceName) => _state != ServiceRuntimeState.NotInstalled;
    }

    private sealed class FakeExecutor : IScCommandExecutor
    {
        public ServiceCommandResult Execute(System.Diagnostics.ProcessStartInfo startInfo)
            => ServiceCommandResult.Success();
    }
}