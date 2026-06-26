using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class WorkerProcessManagerTests
{
    [Fact]
    public void BuildBackgroundLaunchStartInfo_Should_Hide_Worker_Console_Window()
    {
        var psi = WorkerProcessManager.BuildBackgroundLaunchStartInfo(@"D:\app\PhotoPrivacyWorker.exe");

        Assert.Equal(@"D:\app\PhotoPrivacyWorker.exe", psi.FileName);
        // 使用 ArgumentList 安全传递参数，不再使用 Arguments 字符串拼接
        Assert.Contains("--mode", psi.ArgumentList, StringComparer.Ordinal);
        Assert.Contains("background", psi.ArgumentList, StringComparer.Ordinal);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, psi.WindowStyle);
    }

    [Fact]
    public void BuildBackgroundLaunchStartInfo_Should_Include_Config_Path_When_Provided()
    {
        var psi = WorkerProcessManager.BuildBackgroundLaunchStartInfo(
            @"D:\app\PhotoPrivacyWorker.exe",
            @"D:\app\config\config.json");

        // 使用 ArgumentList 安全传递参数
        Assert.Contains("--mode", psi.ArgumentList, StringComparer.Ordinal);
        Assert.Contains("background", psi.ArgumentList, StringComparer.Ordinal);
        Assert.Contains("--config", psi.ArgumentList, StringComparer.Ordinal);
        Assert.Contains(@"D:\app\config\config.json", psi.ArgumentList, StringComparer.Ordinal);
    }
}
