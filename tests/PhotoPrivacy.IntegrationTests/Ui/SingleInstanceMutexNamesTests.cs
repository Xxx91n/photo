namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class SingleInstanceMutexNamesTests
{
    [Fact]
    public void ServiceMutexName_Should_Be_Unified_Global_Instance()
    {
        Assert.Equal(@"Global\PhotoPrivacyCleaner_ServiceHost", PhotoPrivacy.Cli.AppInstanceMutexNames.ServiceHost);
    }

    [Fact]
    public void UiMutexName_Should_Be_Unified_Global_Instance()
    {
        Assert.Equal(@"Global\PhotoPrivacyCleaner_Instance", PhotoPrivacy.Cli.AppInstanceMutexNames.ForUiMode);
    }

    [Fact]
    public void BackgroundMutexName_Should_Be_Unified_Global_Instance()
    {
        Assert.Equal(@"Global\PhotoPrivacyCleaner_Instance", PhotoPrivacy.Cli.AppInstanceMutexNames.ForBackgroundMode);
    }
}
