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

    /// <summary>
    /// 解析形态端点并绑定传输，随后把连接循环跑到停机。绑定失败（含 #187 服务模式缺
    /// <c>PHOTOPRIVACY_SERVICE_GROUP</c> 的 fail-closed）时释放传输、置非零退出码并停宿主——
    /// 进程不得在「活着但没有端点」的状态下静默挂住。
    /// </summary>
    /// <param name="stoppingToken">宿主停机令牌，透传给连接循环与绑定路径。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = IpcTransportFactory.ResolveEndpoint(_runtime.Mode == RuntimeMode.Service);

        // 背景端点不可解析 fail-closed（#201）：Linux 背景/CLI 形态在 XDG_RUNTIME_DIR 缺失
        // （容器、root shell、无 logind 会话）时解析为 null——不得静默挂在「活着但没有端点」
        // （#187 同款口径），也不得回落与 UI 解析不同的落点（端点分裂 = UI 永远发现不了）。
        // 非零退出码停宿主，诊断点明变量名与会话外语境的注入动作。
        if (endpoint is null)
        {
            SafeLog.Try(() => _logger.LogError(
                "Background IPC endpoint is unresolvable: {Variable} must be set to an absolute, " +
                "user-writable runtime directory (pam_systemd/logind sets it for user sessions). " +
                "Inject it explicitly when starting the worker outside a user session " +
                "(containers, root shells). The worker exits (#201).",
                WorkerIpcEndpointNames.LinuxRuntimeDirectoryVariable));
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
            return;
        }

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

        // 服务模式 socket 组 fail-closed 可观测性（#187）：服务模式必须由 plist/systemd 注入
        // PHOTOPRIVACY_SERVICE_GROUP，缺注入时传输层直接拒启（不再回落数据组）。这里先给出指向安装
        // 动作的诊断，否则宿主只剩一句 InvalidOperationException，S4 复测又要靠猜。
        if (_runtime.Mode == RuntimeMode.Service && !OperatingSystem.IsWindows()
            && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable)))
        {
            SafeLog.Try(() => _logger.LogError(
                "Service mode requires {Key} to be injected by the installer unit (plist EnvironmentVariables / " +
                "systemd Environment=); the Unix socket group boundary is fail-closed without it. " +
                "Reinstall the service so the unit carries the socket access group (#187).",
                UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable));
        }

        IIpcTransport? transport = null;
        try
        {
            transport = IpcTransportFactory.CreateServer(endpoint, _runtime.Mode == RuntimeMode.Service);
            await transport.ListenAsync(stoppingToken);

            // 服务模式命名熵（#7）：通告失败回退静态名时必须可观测——否则防抢占面静默回到 #7 前。
            if (transport is NamedPipeIpcTransport pipeTransport
                && _runtime.Mode == RuntimeMode.Service
                && pipeTransport.ServicePipeBind == ServicePipeBindOutcome.StaticFallback)
            {
                SafeLog.Try(() => _logger.LogWarning(
                    "Service pipe name advertisement failed ({Key}\\{Value}); bound static name as fallback — " +
                    "anti-preemption entropy is OFF and the pre-#7 static-name squatting surface is back. " +
                    "Check the service account can write HKLM (LocalSystem default). " +
                    "The CWE-732 instance-creation split in the DACL remains active. (#7)",
                    WindowsServicePipeAcl.RegistrySubKeyName,
                    WindowsServicePipeAcl.AdvertisedServicePipeValueName));
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

            // #187：服务模式组名 fail-closed（缺注入即拒启）不得让进程「活着但没有端点」——
            // 宿主静默跑着、UI 只见连不上，安装现场无从判断是配置缺失还是服务没起。停宿主让服务管理器
            // 看到「起了又停」；非零退出码与 MetadataCleanerWorker 的致命配置路径同口径（Environment.ExitCode=1），
            // 否则服务管理器与实机取证只能看到一次干净退出、判成正常停机。
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
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
