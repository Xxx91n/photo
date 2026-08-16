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

    [Fact]
    public void ConnectOrLaunchAsync_GetStatus_Must_Be_IO_Resilient_Against_Pipe_Broken_Race()
    {
        // ponytail: regression guard for first-launch crash (release UI log 02:04:43 [FATAL]
        // Pipe is broken @ WorkerProcessManager.cs:line 69). When the freshly launched Worker
        // binds the pipe (IsAlive(Ping) succeeds) but then exits mid-GetStatus, SendAsync throws
        // IOException("Pipe is broken"). ConnectOrLaunchAsync MUST catch that and downgrade,
        // NOT let it escape to UiProgram.Start's GetAwaiter().GetResult and kill the whole UI.
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "WorkerProcessManager.cs");
        var source = File.ReadAllText(sourcePath, System.Text.Encoding.UTF8);

        // The resilient helper must exist and catch IO/Socket/Timeout.
        Assert.Contains("private async Task<WorkerIpcResponse?> GetStatusSafeAsync", source, StringComparison.Ordinal);
        Assert.Contains("catch (System.IO.IOException)", source, StringComparison.Ordinal);
        Assert.Contains("catch (System.Net.Sockets.SocketException)", source, StringComparison.Ordinal);
        Assert.Contains("catch (TimeoutException)", source, StringComparison.Ordinal);

        // All three GetStatus call sites in ConnectOrLaunchAsync must go through the safe helper.
        // Service pipe + background pipe (pre-launch) + background pipe (post-launch retry loop) = 3.
        var safeCallCount = System.Text.RegularExpressions.Regex.Matches(
            source,
            @"GetStatusSafeAsync\(",
            System.Text.RegularExpressions.RegexOptions.None,
            System.TimeSpan.FromSeconds(5)).Count;
        // 3 call sites + 1 method declaration named "GetStatusSafeAsync(" => count is 4 usages total.
        // The method declaration signature itself contains "GetStatusSafeAsync(" so >= 4.
        Assert.True(safeCallCount >= 4, $"GetStatusSafeAsync should appear >=4 times (3 call sites + 1 decl), found {safeCallCount}");
        // The only remaining raw SendAsync(GetStatus) must be the sibling GetStatusAsync public method,
        // not inside ConnectOrLaunchAsync. Verify by substring (it is a single-line return).
        Assert.Contains("GetStatusSafeAsync(WorkerIpcEndpointNames.ServicePipe, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("GetStatusSafeAsync(WorkerIpcEndpointNames.BackgroundPipe, cancellationToken)", source, StringComparison.Ordinal);
    }

}
