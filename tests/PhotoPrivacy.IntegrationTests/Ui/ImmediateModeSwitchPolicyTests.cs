using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ImmediateModeSwitchPolicyTests
{
    [Fact]
    public void ShouldRestartAfterInstallSuccess_Should_Return_True()
    {
        var should = ImmediateModeSwitchPolicy.ShouldRestartAfterInstall(
            new ServiceCommandResult(ServiceCommandStatus.Success, "ok", 0));

        Assert.True(should);
    }

    [Fact]
    public void ShouldRestartAfterUninstallSuccess_Should_Return_True()
    {
        var should = ImmediateModeSwitchPolicy.ShouldRestartAfterUninstall(
            new ServiceCommandResult(ServiceCommandStatus.Success, "ok", 0));

        Assert.True(should);
    }

    [Fact]
    public void ShouldRestartAfterInstallFailure_Should_Return_False()
    {
        var should = ImmediateModeSwitchPolicy.ShouldRestartAfterInstall(
            new ServiceCommandResult(ServiceCommandStatus.Failed, "no", 5));

        Assert.False(should);
    }
}
