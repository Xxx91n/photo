using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 行为测试：#42 会话/启动顺序边界的协议版本协商——UI 可能先于 Worker 启动，双方通过
/// 请求/响应的 v 字段交换协议版本。
///
/// 外部行为：
/// ① 客户端申报的协议版本高于服务端所能说话的版本时，服务端 fail-closed：读不懂就明确拒绝，
///   不执行方法，回 ok:false 并带 message=protocol_version_mismatch，响应仍回带服务端 V，
///   供客户端下调重试；
/// ② 客户端申报小于等于服务端版本（或干脆不申报 v）时，正常处理，响应照常回带服务端 V；
/// ③ 当前协议版本为 1，双端均为 v1 时与既有行为完全一致（无告警、无额外往返）。
/// </summary>
public sealed class WorkerIpcVersionNegotiationTests
{
    [Fact]
    public async Task Handler_Should_Reject_Client_Version_Above_Server()
    {
        var handler = new WorkerIpcRequestHandler(new FakeWorkerIpcRuntime(), NullLogger<WorkerIpcRequestHandler>.Instance);

        // 客户端申报 v 比服务端高一个主版本：服务端不会说这个版本，按 fail-closed 拒收。
        var request = new WorkerIpcRequest(WorkerIpcMethods.Ping, "hi-v", WorkerIpcProtocol.Version + 1);
        var (response, shouldShutdown) = await handler.HandleAsync(request, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("protocol_version_mismatch", response.Message);
        Assert.Equal(WorkerIpcProtocol.Version, response.V);
        Assert.Equal("hi-v", response.Id);
        Assert.False(shouldShutdown);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task Handler_Should_Accept_Client_Version_At_Or_Below_Server()
    {
        var handler = new WorkerIpcRequestHandler(new FakeWorkerIpcRuntime(), NullLogger<WorkerIpcRequestHandler>.Instance);

        foreach (var (id, version) in new (string Id, int? Version)[]
        {
            ("eq-v", WorkerIpcProtocol.Version),
            ("below-v", WorkerIpcProtocol.Version - 1),
            ("omitted-v", null),
        })
        {
            var request = new WorkerIpcRequest(WorkerIpcMethods.Ping, id, version);
            var (response, _) = await handler.HandleAsync(request, CancellationToken.None);

            Assert.True(response.Ok, $"申报版本 {version} 不应被拒收：id={id}");
            Assert.Equal(WorkerIpcProtocol.Version, response.V);
        }
    }

    [Fact]
    public async Task Loop_Should_Write_Mismatch_Response_Back_Over_Connection()
    {
        var transport = new ScriptedIpcTransport();
        var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
        using var cts = new CancellationTokenSource();

        var handler = new WorkerIpcRequestHandler(new FakeWorkerIpcRuntime(), NullLogger<WorkerIpcRequestHandler>.Instance);
        var loopTask = loop.RunAsync(transport, handler.HandleAsync, () => { }, cts.Token);

        try
        {
            // 客户端申报 v 高于服务端：连接仍必须在读预算内收到一条 ok:false 响应，
            // 而不是静默丢包让客户端一直等到读超时（#42）。
            var connection = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.Ping, "vm", WorkerIpcProtocol.Version + 1));
            transport.PushConnection(connection);

            Assert.True(
                await WaitForResponseAsync(connection, TimeSpan.FromSeconds(5)),
                "客户端申报过高版本时，服务端必须在读预算内回 protocol_version_mismatch，而非静默丢包（#42）");

            var response = connection.ReadResponse();
            Assert.Contains("\"ok\":false", response, StringComparison.Ordinal);
            Assert.Contains("protocol_version_mismatch", response, StringComparison.Ordinal);
            Assert.Contains($"\"v\":{WorkerIpcProtocol.Version}", response, StringComparison.Ordinal);

            // 拒收过高版本的请求不得带崩连接循环：后续连接仍能正常服务。
            var ping = new ScriptedIpcConnection(SerializeRequest(WorkerIpcMethods.Ping, "after", null));
            transport.PushConnection(ping);
            Assert.True(await WaitForResponseAsync(ping, TimeSpan.FromSeconds(5)));
            Assert.Contains("\"ok\":true", ping.ReadResponse(), StringComparison.Ordinal);
        }
        finally
        {
            cts.Cancel();
            await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static string SerializeRequest(string method, string id, int? v = null)
        => JsonSerializer.Serialize(new WorkerIpcRequest(method, id, v), WorkerIpcJsonContext.Default.WorkerIpcRequest);

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
