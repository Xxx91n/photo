using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 行为测试：#85 服务管道 <c>ReloadConfig</c> 挂起的回归面。
///
/// 事故机制（2026-10-29 实机验证 + 服务侧日志 <c>C:\Windows\System32\logs\worker-20261029.log</c>）：
/// 服务模式 <c>AddWindowsService</c> 隐式挂载的 EventLog provider 在服务会话下写入失败
/// （<c>Cannot open log for source 'PhotoPrivacyWorker'</c>），MEL <c>Logger.Log</c> 直接抛
/// <see cref="AggregateException"/>；而 ReloadConfig 的异常处理链每一层都用 ILogger 记录，
/// catch 里再抛 → 异常一路逃出 <c>HandleClientAsync</c> → 响应写入被跳过 →
/// 客户端等 EOF 直到读超时（10 s / 15 s 均"无响应"）。同管道其余方法无日志调用，故正常。
///
/// 本套用例锁死两条外部行为：
/// ① 请求处理抛异常时，连接仍必须收到一条 <c>ok:false</c> 响应（不得只靠客户端超时收场）；
/// ② 与事故同形态时同样成立：真实 Handler + 失败的 ReloadConfig + 抛异常的 logger
///    （处理器 catch 的 LogWarning 会再抛，循环自身 catch 的日志同源同病）。
/// </summary>
public sealed class WorkerIpcResponseResilienceTests
{
    [Fact]
    public async Task Loop_Should_Respond_With_Error_When_Request_Handling_Throws()
    {
        var transport = new ScriptedIpcTransport();
        var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
        using var cts = new CancellationTokenSource();

        var loopTask = loop.RunAsync(
            transport,
            static (_, _) => throw new InvalidOperationException("handler blew up"),
            () => { },
            cts.Token);

        var connection = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.ReloadConfig, "fault"));
        transport.PushConnection(connection);

        Assert.True(
            await WaitForResponseAsync(connection, TimeSpan.FromSeconds(5)),
            "请求处理抛异常时连接必须收到响应；旧形态静默丢包，客户端只能等到读超时（#85）");

        var response = connection.ReadResponse();
        Assert.Contains("\"ok\":false", response, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"fault\"", response, StringComparison.Ordinal);
        Assert.Contains($"\"v\":{WorkerIpcProtocol.Version}", response, StringComparison.Ordinal);

        cts.Cancel();
        await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Loop_Should_Respond_When_Failure_Logging_Also_Throws()
    {
        // 事故同形态：ReloadConfig 因 processed.ndjson 句柄冲突失败 → Handler catch 的
        // LogWarning 被 EventLog provider 炸掉 → 处理器拿不到响应对象；循环 catch 的
        // LogWarning 同源再抛。两层日志都失败时，连接仍必须拿到 ok:false。
        var runtime = new FakeWorkerIpcRuntime
        {
            ReloadTask = Task.FromException(new IOException("processed.ndjson is being used by another process")),
        };
        var handler = new WorkerIpcRequestHandler(runtime, new ThrowingLogger<WorkerIpcRequestHandler>());
        var loop = new WorkerIpcServerLoop(new ThrowingLogger<WorkerIpcServerLoop>());
        var transport = new ScriptedIpcTransport();
        using var cts = new CancellationTokenSource();

        var loopTask = loop.RunAsync(transport, handler.HandleAsync, () => { }, cts.Token);

        var connection = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.ReloadConfig, "s5"));
        transport.PushConnection(connection);

        Assert.True(
            await WaitForResponseAsync(connection, TimeSpan.FromSeconds(5)),
            "处理链异常 + 日志 provider 抛异常时，服务管道 ReloadConfig 仍必须在读预算内回包（#85）");

        var response = connection.ReadResponse();
        Assert.Contains("\"ok\":false", response, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"s5\"", response, StringComparison.Ordinal);
        // 日志 provider 抛异常不得吃掉失败原因：处理器自己的 catch 也走 SafeLog，仍带回 reload_failed（#85）。
        Assert.Contains("reload_failed", response, StringComparison.Ordinal);

        // 处理链已失败，但仍必须能被后续连接正常服务（连接循环不得被日志异常带崩）。
        var ping = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.Ping, "after"));
        transport.PushConnection(ping);
        Assert.True(await WaitForResponseAsync(ping, TimeSpan.FromSeconds(5)));
        Assert.Contains("\"ok\":true", ping.ReadResponse(), StringComparison.Ordinal);

        cts.Cancel();
        await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Loop_Should_Respond_With_Invalid_Request_For_Malformed_Json()
    {
        var transport = new ScriptedIpcTransport();
        var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
        using var cts = new CancellationTokenSource();

        var loopTask = loop.RunAsync(
            transport,
            static (request, _) => Task.FromResult((new WorkerIpcResponse(true, Id: request?.Id, V: WorkerIpcProtocol.Version), false)),
            () => { },
            cts.Token);

        var connection = new ScriptedIpcConnection("{not-json");
        transport.PushConnection(connection);

        Assert.True(
            await WaitForResponseAsync(connection, TimeSpan.FromSeconds(5)),
            "畸形请求行也必须在读预算内回包；旧形态静默丢包，客户端只等到超时（#85）");
        Assert.Contains("invalid_request", connection.ReadResponse(), StringComparison.Ordinal);

        cts.Cancel();
        await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
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

/// <summary>
/// 复刻 MEL 在 provider 写失败时的行为：<c>Logger.Log</c> 聚合 provider 异常后直接抛。
/// 服务模式 EventLog provider 在验证机上正是这一形态（#85）。
/// </summary>
internal sealed class ThrowingLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => throw new AggregateException("An error occurred while writing to logger(s).");
}
