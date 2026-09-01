
namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: 模式切换源断言迁移到 ServiceModeController。
/// </summary>
public sealed class MainWindowModeSwitchSourceTests
{
    [Fact]
    public void ServiceModeController_Source_Should_Not_Shutdown_Tray_Worker_During_Mode_Switch()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.DoesNotContain("await _workerIpc.ShutdownAsync(previousEndpoint, token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Use_Explicit_Tray_Shutdown_Helper_For_Service_Switch()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("public async Task<bool> ShutdownTrayWorkerForServiceSwitchAsync", source, StringComparison.Ordinal);
        Assert.Contains("await _workerIpc.ShutdownAsync(endpoint, token).ConfigureAwait(false);", source, StringComparison.Ordinal);
    }

}