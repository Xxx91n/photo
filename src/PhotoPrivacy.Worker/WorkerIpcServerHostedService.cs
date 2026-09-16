using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 票 05（A-005）：Worker IPC 宿主的薄接线层——绑定传输后交给 <see cref="WorkerIpcServerLoop"/> 跑连接循环，
/// 请求语义由 <see cref="WorkerIpcRequestHandler"/> 承担。
///
/// 抽出这两者是为了让本票两条验收项可以被行为测试直接驱动：
/// ① 「accept 循环无 sync-over-async、重启重载不阻塞后续 accept」（WorkerIpcServerLoopTests）；
/// ② 「ReloadConfig 真 await 而不是 sync-over-async」（WorkerIpcRequestHandlerTests）。
/// 协议、响应体、ADR 0030/0031/0033/0046 语义均未变。
/// </summary>
public sealed class WorkerIpcServerHostedService : BackgroundService
{
    private readonly WorkerRuntimeContext _runtime;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly WorkerIpcRequestHandler _handler;
    private readonly WorkerIpcServerLoop _loop;
    private readonly ILogger<WorkerIpcServerHostedService> _logger;

    public WorkerIpcServerHostedService(
        WorkerRuntimeContext runtime,
        IHostApplicationLifetime lifetime,
        ILogger<WorkerIpcServerHostedService> logger,
        ILoggerFactory loggerFactory)
    {
        _runtime = runtime;
        _lifetime = lifetime;
        _logger = logger;
        _handler = new WorkerIpcRequestHandler(runtime, loggerFactory.CreateLogger<WorkerIpcRequestHandler>());
        _loop = new WorkerIpcServerLoop(loggerFactory.CreateLogger<WorkerIpcServerLoop>());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = IpcTransportFactory.ResolveEndpoint(_runtime.Mode == RuntimeMode.Service);

        IIpcTransport? transport = null;
        try
        {
            transport = IpcTransportFactory.CreateServer(endpoint, _runtime.Mode == RuntimeMode.Service);
            await transport.ListenAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bind IPC transport on {Endpoint}", endpoint);
            if (transport is not null)
            {
                await transport.DisposeAsync();
            }
            return;
        }

        await using (transport)
        {
            await _loop.RunAsync(
                transport,
                _handler.HandleAsync,
                _lifetime.StopApplication,
                stoppingToken);
        }
    }
}
