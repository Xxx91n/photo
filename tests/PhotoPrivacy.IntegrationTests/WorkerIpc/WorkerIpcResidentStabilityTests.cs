using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 行为测试：驻守稳定性。
///
/// 覆盖 issue 05 Acceptance criteria 第 2 条「accept 循环无 sync-over-async
/// （重启重载不阻塞后续 accept）」：
/// 用内存剧本传输 + 可控假运行时直接驱动连接循环，造出「ReloadConfig 挂住数秒」的场景，
/// 断言心跳探针仍被服务。原实现（同步 HandleRequest + GetAwaiter().GetResult()
/// + 严格串行 accept）在本套用例下必红。
/// </summary>
public sealed class WorkerIpcResidentStabilityTests
{
    [Fact]
    public async Task Loop_Should_Keep_Serving_New_Connections_While_Reload_Is_In_Progress()
    {
        var reloadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var transport = new ScriptedIpcTransport();
        var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
        using var cts = new CancellationTokenSource();

        var loopTask = loop.RunAsync(
            transport,
            async (request, _) =>
            {
                if (request?.Method == WorkerIpcMethods.ReloadConfig)
                {
                    reloadEntered.TrySetResult(true);
                    await reloadGate.Task;
                }

                return (new WorkerIpcResponse(true, Id: request?.Id, V: 1), false);
            },
            () => { },
            cts.Token);

        // 1) 长重载连接先入场并挂住。
        var slow = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.ReloadConfig, "slow"));
        transport.PushConnection(slow);
        await reloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 2) 重载仍在途时投递心跳探针——必须在预算内被服务。
        var ping = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.Ping, "ping"));
        transport.PushConnection(ping);

        var served = await WaitForResponseAsync(ping, TimeSpan.FromSeconds(3));

        Assert.True(served, "重载在途时后续连接（心跳探针）必须在预算内被服务");
        Assert.Contains("\"ok\":true", ping.ReadResponse(), StringComparison.Ordinal);
        Assert.False(reloadGate.Task.IsCompleted, "本断言保证 Ping 是在重载尚未完成时被服务的");

        reloadGate.TrySetResult(true);
        Assert.True(await WaitForResponseAsync(slow, TimeSpan.FromSeconds(5)));

        cts.Cancel();
        await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Loop_Should_Discard_Oversized_Request_Without_Responding()
    {
        var transport = new ScriptedIpcTransport();
        var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
        using var cts = new CancellationTokenSource();
        var handled = 0;

        var loopTask = loop.RunAsync(
            transport,
            (request, _) =>
            {
                Interlocked.Increment(ref handled);
                return Task.FromResult((new WorkerIpcResponse(true, Id: request?.Id, V: 1), false));
            },
            () => { },
            cts.Token);

        // 64 KB 上限 + 1：超长消息必须被丢弃（不进入处理，不回包）。
        var oversized = new ScriptedIpcConnection(new string('a', (64 * 1024) + 1));
        transport.PushConnection(oversized);

        var small = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.Ping, "ok"));
        transport.PushConnection(small);

        Assert.True(await WaitForResponseAsync(small, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref handled));
        Assert.Equal(0, oversized.ReadResponse().Length);

        cts.Cancel();
        await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Handler_ReloadConfig_Should_Await_Asynchronously_Instead_Of_Blocking_The_Caller()
    {
        var reloadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeWorkerIpcRuntime { ReloadTask = reloadGate.Task };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        // 旧实现的 sync-over-async 会阻塞调用线程直到重载结束（返回的 Task 在返回时已完成）；
        // 这里让门在后台 300ms 后打开，使旧实现快速红而不是挂死。
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            reloadGate.TrySetResult(true);
        });

        var pending = handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), CancellationToken.None);

        Assert.False(pending.IsCompleted, "ReloadConfig 处理必须真 await；sync-over-async 会让返回的 Task 已完成");

        var (response, shouldShutdown) = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(response.Ok);
        Assert.False(shouldShutdown);
        Assert.Equal(1, runtime.ReloadCalls);
    }

    [Fact]
    public async Task Handler_Ping_Should_Not_Be_Blocked_By_InFlight_Reload()
    {
        var reloadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeWorkerIpcRuntime { ReloadTask = reloadGate.Task };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        var reload = handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), CancellationToken.None);

        var (pingResponse, _) = await handler
            .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Ping), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(pingResponse.Ok);
        Assert.False(reload.IsCompleted);

        reloadGate.TrySetResult(true);
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handler_Should_Preserve_Non_Reload_Method_Semantics()
    {
        var runtime = new FakeWorkerIpcRuntime();
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        var (ping, pingShutdown) = await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Ping), CancellationToken.None);
        Assert.True(ping.Ok);
        Assert.False(pingShutdown);
        Assert.Equal(1, ping.V);
        Assert.False(ping.Data!.IsPaused);

        var (pause, _) = await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Pause), CancellationToken.None);
        Assert.True(pause.Ok);
        Assert.Equal(1, runtime.PauseCalls);

        var (resume, _) = await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Resume), CancellationToken.None);
        Assert.True(resume.Ok);
        Assert.Equal(1, runtime.ResumeCalls);

        // 非 CLI 模式拒绝 Shutdown。
        var (shutdown, shutdownFlag) = await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Shutdown), CancellationToken.None);
        Assert.False(shutdown.Ok);
        Assert.False(shutdownFlag);

        // 校验失败时返回可读错误且不应用配置。
        runtime.ValidationResult = (false, "bad config");
        var (invalid, _) = await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), CancellationToken.None);
        Assert.False(invalid.Ok);
        Assert.Contains("bad config", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(0, runtime.ReloadCalls);

        // 未知方法 / 空请求。
        var (unknown, _) = await handler.HandleAsync(new WorkerIpcRequest("NoSuchMethod"), CancellationToken.None);
        Assert.False(unknown.Ok);

        var (nullRequest, _) = await handler.HandleAsync(null, CancellationToken.None);
        Assert.False(nullRequest.Ok);
    }

    /// <summary>状态快照为 unknown 时必须驱动一次版本补探（#86 回归）。</summary>
    [Fact]
    public async Task Handler_Should_Trigger_ExifTool_Version_Resolution_When_Status_Reports_Unknown()
    {
        // #86 回归：桥惰性启动 → 首个文件前版本恒为 unknown。状态消费者的出现必须驱动补探，
        // 否则两种模式都会一直上报 unknown（P2/E2 实机形态）。
        var runtime = new FakeWorkerIpcRuntime { ExifToolVersion = ExifToolVersionText.Unknown };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        var (response, _) = await handler
            .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.GetExifToolVersion), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(response.Ok);
        Assert.Equal(ExifToolVersionText.Unknown, response.Data!.ExifToolVersion);

        await WaitUntilAsync(() => runtime.EnsureExifToolVersionCalls == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, runtime.EnsureExifToolVersionCalls);
    }

    /// <summary>版本已知时三种状态方法都不得再触发补探。</summary>
    [Fact]
    public async Task Handler_Should_Not_Trigger_ExifTool_Version_Resolution_When_Version_Is_Known()
    {
        // 版本已解析后不得每次轮询都重启/补探——补探只服务「还没有版本」这一状态。
        var runtime = new FakeWorkerIpcRuntime { ExifToolVersion = "13.20" };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        foreach (var method in new[] { WorkerIpcMethods.GetStatus, WorkerIpcMethods.Ping, WorkerIpcMethods.GetExifToolVersion })
        {
            var (response, _) = await handler
                .HandleAsync(new WorkerIpcRequest(method), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(response.Ok);
        }

        Assert.Equal(0, runtime.EnsureExifToolVersionCalls);
    }

    /// <summary>补探在途时状态响应仍立即返回，且轮询不得叠加补探。</summary>
    [Fact]
    public async Task Handler_Status_Response_Should_Not_Block_On_InFlight_Version_Resolution()
    {
        // 补探可能持续数秒（拉进程 + -ver 往返）。状态响应必须立即返回当前快照，
        // 且同一时刻只允许一路补探在途（1s/3s 轮询不得叠加启动尝试）。
        var probeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeWorkerIpcRuntime
        {
            ExifToolVersion = ExifToolVersionText.Unknown,
            EnsureExifToolVersionTask = probeGate.Task
        };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        var (first, _) = await handler
            .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.GetStatus), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.Ok);
        Assert.Equal(ExifToolVersionText.Unknown, first.Data!.ExifToolVersion);

        await WaitUntilAsync(() => runtime.EnsureExifToolVersionCalls == 1, TimeSpan.FromSeconds(5));

        // 补探仍在途时再走两拍轮询：响应照常返回，补探不叠加。
        await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.GetExifToolVersion), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await handler.HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.Ping), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, runtime.EnsureExifToolVersionCalls);
        Assert.False(probeGate.Task.IsCompleted);

        probeGate.TrySetResult(true);
    }

    /// <summary>第一拍 unknown 触发补探，第二拍必须返回真实版本（AC1 红测）。</summary>
    [Fact]
    public async Task Handler_Should_Report_Real_Version_On_Next_Poll_After_Probe_Resolves()
    {
        // AC1 的红测：第一拍返回 unknown 并驱动补探，第二拍必须拿到真实版本——
        // 修复前完全没有触发路径，第二拍仍是 unknown。
        var runtime = new FakeWorkerIpcRuntime
        {
            ExifToolVersion = ExifToolVersionText.Unknown,
            ExifToolVersionAfterEnsure = "13.20"
        };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);

        var (first, _) = await handler
            .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.GetExifToolVersion), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExifToolVersionText.Unknown, first.Data!.ExifToolVersion);

        await WaitUntilAsync(() => runtime.ExifToolVersion == "13.20", TimeSpan.FromSeconds(5));

        var (second, _) = await handler
            .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.GetExifToolVersion), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("13.20", second.Data!.ExifToolVersion);
    }

    /// <summary>轮询等待条件成立（用于 fire-and-forget 补探的完成判定）；超时后由调用方的断言定红绿。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private static string SerializeRequest(string method, string id)
        => JsonSerializer.Serialize(new WorkerIpcRequest(method, id), WorkerIpcJsonContext.Default.WorkerIpcRequest);

    private static async Task<bool> WaitForResponseAsync(ScriptedIpcConnection connection, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (connection.ReadResponse().Length > 0)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return connection.ReadResponse().Length > 0;
    }
}

/// <summary>单连接剧本——预置请求字节，收集响应字节（一连接一消息，无需双工）。</summary>
internal sealed class ScriptedIpcConnection : Stream
{
    private readonly MemoryStream _inbound;

    public ScriptedIpcConnection(string requestLine)
    {
        _inbound = new MemoryStream(Encoding.UTF8.GetBytes(requestLine + "\n"));
    }

    public MemoryStream Outbound { get; } = new();

    public string ReadResponse() => Encoding.UTF8.GetString(Outbound.ToArray());

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => _inbound.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inbound.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Outbound.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => Outbound.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        // 故意不释放 Outbound：测试需要在循环释放连接后仍能读到响应字节。
    }
}

/// <summary>内存传输——接受预推入的连接，不绑定任何真实端点（跨平台、CI 可直接跑）。</summary>
internal sealed class ScriptedIpcTransport : IIpcTransport
{
    private readonly Channel<Stream> _connections = Channel.CreateUnbounded<Stream>();

    public string EndpointName => "scripted-test-endpoint";

    public Task ListenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
        => await _connections.Reader.ReadAsync(cancellationToken);

    public Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => throw new NotSupportedException("scripted transport is server-side only");

    public void PushConnection(Stream connection) => _connections.Writer.TryWrite(connection);

    public ValueTask DisposeAsync()
    {
        _connections.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>可控假运行时——ReloadTask 用于造出「重载挂住数秒」的场景。</summary>
internal sealed class FakeWorkerIpcRuntime : IWorkerIpcRuntime
{
    public bool IsPaused { get; private set; }

    public string ExifToolVersion { get; set; } = "13.20";

    public string WatchDirectory { get; set; } = "/hot";

    public string? AuditLogDirectory { get; set; }

    public RuntimeMode Mode { get; set; } = RuntimeMode.Background;

    public Task ReloadTask { get; set; } = Task.CompletedTask;

    /// <summary>版本补探的挂起点——默认立即完成；置为未完成任务可造出「补探在途」窗口。</summary>
    public Task EnsureExifToolVersionTask { get; set; } = Task.CompletedTask;

    /// <summary>补探完成后写回的版本——模拟桥解析成功；null 表示保持原值。</summary>
    public string? ExifToolVersionAfterEnsure { get; set; }

    public (bool valid, string? error) ValidationResult { get; set; } = (true, null);

    public int PauseCalls { get; private set; }

    public int ResumeCalls { get; private set; }

    public int ReloadCalls { get; private set; }

    public int EnsureExifToolVersionCalls { get; private set; }

    public void Pause()
    {
        PauseCalls++;
        IsPaused = true;
    }

    public void Resume()
    {
        ResumeCalls++;
        IsPaused = false;
    }

    public (bool valid, string? error) TryValidateConfig() => ValidationResult;

    /// <summary>记录补探调用次数，并在补探完成后按需写回版本（模拟桥解析成功）。</summary>
    public async Task EnsureExifToolVersionAsync(CancellationToken cancellationToken)
    {
        EnsureExifToolVersionCalls++;
        await EnsureExifToolVersionTask;
        if (ExifToolVersionAfterEnsure is not null)
        {
            ExifToolVersion = ExifToolVersionAfterEnsure;
        }
    }

    public async Task ReloadConfigAsync()
    {
        ReloadCalls++;
        await ReloadTask;
    }
}
