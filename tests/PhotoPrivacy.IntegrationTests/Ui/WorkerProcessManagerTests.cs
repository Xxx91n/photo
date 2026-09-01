﻿using PhotoPrivacy.Ui;

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
    public void WorkerProcessManager_Source_Should_Not_Declare_Ipc_Methods()
    {
        // issue 04 checkpoint A: WorkerProcessManager keeps process-launch duty only.
        // Every protocol method name (WorkerIpcMethods list) must be absent from the class —
        // all IPC calls live in WorkerIpcClient (single typed entry point).
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "WorkerProcessManager.cs");
        var source = File.ReadAllText(sourcePath, System.Text.Encoding.UTF8);

        var ipcMethodNames = new[]
        {
            "Ping", "GetStatus", "Pause", "Resume", "Shutdown", "ReloadConfig", "GetRecentLogs", "GetExifToolVersion"
        };

        foreach (var method in ipcMethodNames)
        {
            Assert.DoesNotContain(method, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConnectOrLaunchAsync_Status_Probe_Must_Stay_IO_Resilient_Via_Client()
    {
        // ponytail: regression guard for first-launch crash (release UI log 02:04:43 [FATAL]
        // Pipe is broken). The freshly launched Worker can bind the pipe (IsAlive(Ping)
        // succeeds) then exit mid-GetStatus — SendAsync throws IOException("Pipe is broken").
        // The resilient downgrade now lives in WorkerIpcClient.ProbeStatusSafeAsync (issue 04
        // moved it out of WorkerProcessManager); ConnectOrLaunchAsync must route all status
        // probes through it and declare no direct IPC sends of its own.
        var wpmSource = File.ReadAllText(
            Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "WorkerProcessManager.cs"),
            System.Text.Encoding.UTF8);
        var clientSource = File.ReadAllText(
            Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "WorkerIpcClient.cs"),
            System.Text.Encoding.UTF8);

        // The client owns the resilient probe: IO/Socket/Timeout degrade to null, never crash.
        Assert.Contains("public async Task<WorkerIpcResponse?> ProbeStatusSafeAsync", clientSource, StringComparison.Ordinal);
        Assert.Contains("catch (System.IO.IOException)", clientSource, StringComparison.Ordinal);
        Assert.Contains("catch (System.Net.Sockets.SocketException)", clientSource, StringComparison.Ordinal);
        Assert.Contains("catch (TimeoutException)", clientSource, StringComparison.Ordinal);

        // All three status probes in ConnectOrLaunchAsync (service pipe, background pipe
        // pre-launch, background pipe post-launch retry loop) go through the safe helper.
        var safeCallCount = System.Text.RegularExpressions.Regex.Matches(
            wpmSource,
            @"ProbeStatusSafeAsync\(",
            System.Text.RegularExpressions.RegexOptions.None,
            System.TimeSpan.FromSeconds(5)).Count;
        Assert.True(safeCallCount >= 3, $"ProbeStatusSafeAsync should appear >=3 times in WorkerProcessManager (3 call sites), found {safeCallCount}");

        // No direct IPC sends remain in WorkerProcessManager.
        Assert.DoesNotContain("SendAsync(", wpmSource, StringComparison.Ordinal);
    }
}
