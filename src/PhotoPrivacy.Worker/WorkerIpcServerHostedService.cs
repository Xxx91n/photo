using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// Worker IPC 宿主的薄接线层——绑定传输后交给 <see cref="WorkerIpcServerLoop"/> 跑连接循环，
/// 请求语义由 <see cref="WorkerIpcRequestHandler"/> 承担。
///
/// 抽出这两者是为了让本票两条验收项可以被行为测试直接驱动：
/// ① 「accept 循环无 sync-over-async、重启重载不阻塞后续 accept」（WorkerIpcServerLoopTests）；
/// ② 「ReloadConfig 真 await 而不是 sync-over-async」（WorkerIpcRequestHandlerTests）。
/// 协议、响应体、版本字段、停机范围、重载校验与日志拉取语义均未变。
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

        // 服务模式 fail-closed 可观测性（#70）：安装者 SID 解析不到时传输层回退 CurrentUserOnly，
        // 桌面用户仍会被拒——这里记 warning 指向安装动作，否则 S4 复测只剩一句 ACCESS_DENIED 无从下手。
        if (_runtime.Mode == RuntimeMode.Service && OperatingSystem.IsWindows()
            && !WindowsServicePipeAcl.TryResolveInstallerSid(out _))
        {
            SafeLog.Try(() => _logger.LogWarning(
                "Service pipe installer SID not resolved (install-service.ps1 writes {Key}); " +
                "service pipe falls back to current-user-only and non-elevated desktop UI will be denied. " +
                "Reinstall the service to register it (#70).",
                WindowsServicePipeAcl.RegistrySubKeyName + "\\" + WindowsServicePipeAcl.InstallerSidValueName));
        }

        IIpcTransport? transport = null;
        try
        {
            transport = IpcTransportFactory.CreateServer(endpoint, _runtime.Mode == RuntimeMode.Service);
            await transport.ListenAsync(stoppingToken);
            // 服务模式 DACL 应用失败的可观测性（#70）：管道仍可用但回退 SYSTEM 默认 DACL，
            // 桌面用户会被拒——记 warning 指向复测口径，避免 S4 只剩一句 ACCESS_DENIED 无从下手。
            if (transport is NamedPipeIpcTransport pipeTransport
                && pipeTransport.ServicePipeAclApplied == false)
            {
                SafeLog.Try(() => _logger.LogWarning(
                    "Service pipe DACL could not be applied (SetNamedSecurityInfoW failed); pipe keeps the " +
                    "SYSTEM default DACL and non-elevated desktop UI will be denied (#70)."));
            }
        }
        catch (Exception ex)
        {
            // 日志失败不得让绑定失败路径再炸一次（#85 同类：日志 provider 抛 → 宿主停摆）。
            SafeLog.Try(() => _logger.LogError(ex, "Failed to bind IPC transport on {Endpoint}", endpoint));
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
