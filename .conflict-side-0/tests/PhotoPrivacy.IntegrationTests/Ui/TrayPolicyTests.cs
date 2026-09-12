using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class TrayPolicyTests
{
    [Fact]
    public void ShouldShowTrayIcon_Should_Return_False_When_Service_Exists()
    {
        var show = TrayPolicy.ShouldShowTrayIcon(isBackgroundMode: true, isServiceInstalled: true);

        Assert.False(show);
    }

    [Fact]
    public void ShouldShowTrayIcon_Should_Return_True_When_Background_Without_Service()
    {
        var show = TrayPolicy.ShouldShowTrayIcon(isBackgroundMode: true, isServiceInstalled: false);

        Assert.True(show);
    }

    [Fact]
    public void ShouldShowTrayIcon_Should_Return_False_When_Not_Background_Mode()
    {
        var show = TrayPolicy.ShouldShowTrayIcon(isBackgroundMode: false, isServiceInstalled: false);

        Assert.False(show);
    }
}
