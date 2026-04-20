using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ServiceUiPolicyTests
{
    [Fact]
    public void BuildButtonState_Should_Enable_Only_Install_When_NotInstalled()
    {
        var state = ServiceUiPolicy.BuildButtonState(ServiceRuntimeState.NotInstalled);

        Assert.True(state.InstallEnabled);
        Assert.False(state.UninstallEnabled);
        Assert.False(state.StartEnabled);
        Assert.False(state.StopEnabled);
    }

    [Fact]
    public void BuildButtonState_Should_Enable_Start_And_Uninstall_When_Installed_But_NotRunning()
    {
        var state = ServiceUiPolicy.BuildButtonState(ServiceRuntimeState.Stopped);

        Assert.False(state.InstallEnabled);
        Assert.True(state.UninstallEnabled);
        Assert.True(state.StartEnabled);
        Assert.False(state.StopEnabled);
    }

    [Fact]
    public void BuildButtonState_Should_Enable_Only_Stop_When_Running()
    {
        var state = ServiceUiPolicy.BuildButtonState(ServiceRuntimeState.Running);

        Assert.False(state.InstallEnabled);
        Assert.False(state.UninstallEnabled);
        Assert.False(state.StartEnabled);
        Assert.True(state.StopEnabled);
    }

    [Fact]
    public void ShouldSwitchFromTrayToServiceShell_Should_Return_True_When_Service_Running()
    {
        var shouldSwitch = ServiceUiPolicy.ShouldSwitchFromTrayToServiceShell(ServiceRuntimeState.Running);

        Assert.True(shouldSwitch);
    }

    [Fact]
    public void ShouldSwitchFromTrayToServiceShell_Should_Return_True_When_Service_Installed_But_Stopped()
    {
        var shouldSwitch = ServiceUiPolicy.ShouldSwitchFromTrayToServiceShell(ServiceRuntimeState.Stopped);

        Assert.True(shouldSwitch);
    }

    [Fact]
    public void ShouldSwitchFromServiceShellToTray_Should_Return_True_When_Service_Uninstalled()
    {
        var shouldSwitch = ServiceUiPolicy.ShouldSwitchFromServiceShellToTray(ServiceRuntimeState.NotInstalled);

        Assert.True(shouldSwitch);
    }
}
