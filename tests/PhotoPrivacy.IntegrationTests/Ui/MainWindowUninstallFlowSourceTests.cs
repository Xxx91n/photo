
namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: 卸载流程源断言迁移到 ServiceModeController。
/// </summary>
public sealed class MainWindowUninstallFlowSourceTests
{
    [Fact]
    public void ServiceModeController_Source_Should_Run_Uninstall_Off_Ui_Thread()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("public async Task UninstallAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => _serviceManager.Uninstall()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Pin_Background_Endpoint_After_Service_Uninstall()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Force_Background_Reconnect_After_Uninstall()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("getServiceRuntimeStateOverride: () => ServiceRuntimeState.NotInstalled", source, StringComparison.Ordinal);
    }

}