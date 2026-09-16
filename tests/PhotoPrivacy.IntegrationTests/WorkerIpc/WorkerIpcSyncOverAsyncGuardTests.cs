namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 票 05（A-005）回潮守卫。
///
/// 职责分工（D-003）：行为测试（<see cref="WorkerIpcResidentStabilityTests"/>）防「程序做错事」，
/// 本守卫防「措辞/结构回潮」——防止 sync-over-async 与串行 accept 结构被重新引入。
///
/// 范围 = Worker IPC 触面（宿主 / 连接循环 / 请求处理）+ 入口信号面。
/// 票 06 承接票 05 报告张力 T1：Program.cs 的 SIGHUP onReload 回调同形态
/// sync-over-async 已修复（PosixSignalHooks 改 Func<Task> + Task.Run 编组），
/// Program.cs 与 PosixSignalHooks.cs 自此纳入守卫面。
/// </summary>
public sealed class WorkerIpcSyncOverAsyncGuardTests
{
    private static readonly string[][] GuardedFiles =
    [
        ["src", "PhotoPrivacy.Worker", "WorkerIpcServerHostedService.cs"],
        ["src", "PhotoPrivacy.Worker", "WorkerIpcServerLoop.cs"],
        ["src", "PhotoPrivacy.Worker", "WorkerIpcRequestHandler.cs"],
        ["src", "PhotoPrivacy.Worker", "Program.cs"],
        ["src", "PhotoPrivacy.Worker", "PosixSignalHooks.cs"]
    ];

    private static readonly string[] SyncOverAsyncTokens =
    [
        "GetAwaiter().GetResult()",
        ".Result",
        ".Wait()"
    ];

    internal static List<string> ClassifySyncOverAsync(string strippedSource, string fileLabel)
    {
        var violations = new List<string>();
        foreach (var token in SyncOverAsyncTokens)
        {
            if (strippedSource.Contains(token, StringComparison.Ordinal))
            {
                violations.Add(fileLabel + " -> " + token);
            }
        }

        return violations;
    }

    [Fact]
    public void Worker_Ipc_Surface_Should_Not_Contain_Sync_Over_Async()
    {
        var violations = new List<string>();

        foreach (var file in GuardedFiles)
        {
            violations.AddRange(
                ClassifySyncOverAsync(SourceLint.ReadStripped(file), string.Join("/", file)));
        }

        Assert.True(
            violations.Count == 0,
            "Worker IPC 面出现 sync-over-async 回潮：" + string.Join("; ", violations));
    }

    [Fact]
    public void Sync_Over_Async_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：基线必须零违规；向绿态样本注入各 token 形态，分类器必须报红。
        var green = SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "Program.cs");
        var baseline = ClassifySyncOverAsync(green, "Program.cs");
        Assert.True(baseline.Count == 0, "baseline Program.cs not green: " + string.Join("; ", baseline));

        Assert.NotEmpty(ClassifySyncOverAsync(
            green + Environment.NewLine + "x.ReloadConfigAsync().GetAwaiter().GetResult();", "t"));
        Assert.NotEmpty(ClassifySyncOverAsync(
            green + Environment.NewLine + "var r = t.Result;", "t"));
        Assert.NotEmpty(ClassifySyncOverAsync(
            green + Environment.NewLine + "t.Wait();", "t"));
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
