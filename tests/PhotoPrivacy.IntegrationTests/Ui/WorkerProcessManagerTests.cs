using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class WorkerProcessManagerTests
{
    [Fact]
    public void BuildBackgroundLaunchStartInfo_Should_Hide_Worker_Console_Window()
    {
        var psi = WorkerProcessManager.BuildBackgroundLaunchStartInfo(@"D:\app\PhotoPrivacyWorker.exe");

        Assert.Equal(@"D:\app\PhotoPrivacyWorker.exe", psi.FileName);
        Assert.Equal("--mode background", psi.Arguments);
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

        Assert.Contains("--mode background", psi.Arguments, StringComparison.Ordinal);
        Assert.Contains("--config \"D:\\app\\config\\config.json\"", psi.Arguments, StringComparison.Ordinal);
    }
}
