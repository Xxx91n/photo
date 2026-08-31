using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: 卸载流程源断言迁移到 ServiceModeController。
/// </summary>
public sealed class MainWindowUninstallFlowSourceTests
{
    [Fact]
    public void ServiceModeController_Source_Should_Run_Uninstall_Off_Ui_Thread()
    {
        var source = ReadSource("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("public async Task UninstallAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => _serviceManager.Uninstall()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Pin_Background_Endpoint_After_Service_Uninstall()
    {
        var source = ReadSource("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Force_Background_Reconnect_After_Uninstall()
    {
        var source = ReadSource("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("getServiceRuntimeStateOverride: () => ServiceRuntimeState.NotInstalled", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            current = Path.GetFullPath(Path.Combine(current, ".."));
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, "PhotoPrivacy.sln")))
            {
                return File.ReadAllText(Path.Combine(new[] { current }.Concat(segments).ToArray()), Encoding.UTF8);
            }
        }

        throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
    }
}