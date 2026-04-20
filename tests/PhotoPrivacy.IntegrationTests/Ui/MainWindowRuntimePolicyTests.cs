using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowRuntimePolicyTests
{
    [Fact]
    public void ShouldHideOnStartup_Should_Return_False_When_Tray_Not_Visible()
    {
        var hide = MainWindowRuntimePolicy.ShouldHideOnStartup(
            hideMainWindowOnStartup: true,
            useTrayIcon: false,
            hideTrayIcon: false);

        Assert.False(hide);
    }

    [Fact]
    public void ShouldHideOnStartup_Should_Return_True_When_Tray_Is_Visible_And_Config_Requests_Hide()
    {
        var hide = MainWindowRuntimePolicy.ShouldHideOnStartup(
            hideMainWindowOnStartup: true,
            useTrayIcon: true,
            hideTrayIcon: false);

        Assert.True(hide);
    }
}
