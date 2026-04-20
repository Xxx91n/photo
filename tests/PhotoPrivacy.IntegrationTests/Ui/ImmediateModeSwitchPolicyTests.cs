using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ImmediateModeSwitchPolicyTests
{
    [Fact]
    public void ShouldSwitchAfterInstallSuccess_Should_Return_True()
    {
        var should = ImmediateModeSwitchPolicy.ShouldSwitchAfterInstall(
            new ServiceCommandResult(ServiceCommandStatus.Success, "ok", 0));

        Assert.True(should);
    }

    [Fact]
    public void ShouldSwitchAfterUninstallSuccess_Should_Return_True()
    {
        var should = ImmediateModeSwitchPolicy.ShouldSwitchAfterUninstall(
            new ServiceCommandResult(ServiceCommandStatus.Success, "ok", 0));

        Assert.True(should);
    }

    [Fact]
    public void ShouldSwitchAfterInstallFailure_Should_Return_False()
    {
        var should = ImmediateModeSwitchPolicy.ShouldSwitchAfterInstall(
            new ServiceCommandResult(ServiceCommandStatus.Failed, "no", 5));

        Assert.False(should);
    }
}
