using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: 模式切换源断言迁移到 ServiceModeController。
/// </summary>
public sealed class MainWindowModeSwitchSourceTests
{
    [Fact]
    public void ServiceModeController_Source_Should_Not_Shutdown_Tray_Worker_During_Mode_Switch()
    {
        var source = ReadSource("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.DoesNotContain("await _workerIpc.ShutdownAsync(previousEndpoint, token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceModeController_Source_Should_Use_Explicit_Tray_Shutdown_Helper_For_Service_Switch()
    {
        var source = ReadSource("src", "PhotoPrivacy.Ui", "Services", "ServiceModeController.cs");

        Assert.Contains("public async Task<bool> ShutdownTrayWorkerForServiceSwitchAsync", source, StringComparison.Ordinal);
        Assert.Contains("await _workerIpc.ShutdownAsync(endpoint, token).ConfigureAwait(false);", source, StringComparison.Ordinal);
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