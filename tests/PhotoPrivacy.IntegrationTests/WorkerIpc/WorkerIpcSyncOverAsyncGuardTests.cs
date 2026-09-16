namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 票 05（A-005）回潮守卫。
///
/// 职责分工（D-003）：行为测试（<see cref="WorkerIpcResidentStabilityTests"/>）防「程序做错事」，
/// 本守卫防「措辞/结构回潮」——防止 sync-over-async 与串行 accept 结构被重新引入。
///
/// 范围限定为本票触面（宿主 / 连接循环 / 请求处理）。
/// 不覆盖 <c>Program.cs</c> 的 POSIX 信号回调——该处同形态 sync-over-async 属本轮范围外，
/// 已在票 05 报告「离轨/张力」节登记，不纳入本守卫（避免守卫与实际不符）。
/// </summary>
public sealed class WorkerIpcSyncOverAsyncGuardTests
{
    private static readonly string[][] GuardedFiles =
    [
        ["src", "PhotoPrivacy.Worker", "WorkerIpcServerHostedService.cs"],
        ["src", "PhotoPrivacy.Worker", "WorkerIpcServerLoop.cs"],
        ["src", "PhotoPrivacy.Worker", "WorkerIpcRequestHandler.cs"]
    ];

    private static readonly string[] SyncOverAsyncTokens =
    [
        "GetAwaiter().GetResult()",
        ".Result",
        ".Wait()"
    ];

    [Fact]
    public void Worker_Ipc_Surface_Should_Not_Contain_Sync_Over_Async()
    {
        var violations = new List<string>();

        foreach (var file in GuardedFiles)
        {
            var source = SourceLint.ReadStripped(file);
            foreach (var token in SyncOverAsyncTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add(string.Join("/", file) + " -> " + token);
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Worker IPC 面出现 sync-over-async 回潮：" + string.Join("; ", violations));
    }

    [Fact]
    public void Worker_Ipc_Loop_Should_Not_Await_Request_Handling_Inline()
    {
        // 结构守卫：连接处理必须以独立任务驱动（accept 循环不得内联 await 处理体），
        // 且读必须走缓冲读实现。
        var loop = SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "WorkerIpcServerLoop.cs");

        Assert.Contains("HandleClientAsync", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("await HandleClientAsync", loop, StringComparison.Ordinal);
        Assert.Contains("IpcLineReader.ReadBoundedLineAsync", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void Ipc_Line_Reader_Should_Be_The_Single_Buffered_Implementation()
    {
        // 回潮守卫：逐字节读循环（new byte[1]）不得重新出现在服务端/客户端。
        var loop = SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "WorkerIpcServerLoop.cs");
        var client = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "WorkerIpcClient.cs");

        Assert.DoesNotContain("new byte[1]", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("new byte[1]", client, StringComparison.Ordinal);
        Assert.Contains("IpcLineReader.ReadBoundedLineAsync", client, StringComparison.Ordinal);
    }
}
