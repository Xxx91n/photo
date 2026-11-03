using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 管道访问策略（对齐 <see cref="UnixSocketAccessMode"/> 的 Unix 侧语义）。
/// </summary>
public enum WindowsPipeAccessMode
{
    /// <summary>后台模式：同用户边界（CurrentUserOnly）。</summary>
    BackgroundOnly = 0,

    /// <summary>服务模式：授权 SID 集合显式 DACL（#70 安装者保底，#1 opt-in 扩展）；解析不到时 fail-closed 回退同用户边界。</summary>
    ServiceShared = 1,
}

/// <summary>
/// 服务管道绑定结果（#7 命名熵）：宿主据 <see cref="StaticFallback"/> 观测通告回退（记 warning），
/// 其余调用方（测试）据此断言随机化生效与否。
/// </summary>
public enum ServicePipeBindOutcome
{
    /// <summary>非服务模式，或尚未 Listen。</summary>
    None = 0,

    /// <summary>随机名绑定成功且已通告（HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName）。</summary>
    RandomizedAdvertised = 1,

    /// <summary>通告失败，回退静态名重绑：可用性优先（fail-safe，与 #70 的 SID 缺失回退同构），防抢占面回到 #7 前，CWE-732 拆位后的 DACL 仍生效。</summary>
    StaticFallback = 2,
}

/// <summary>
/// Windows named-pipe IPC transport. Falls back to FIFO on Unix (not recommended — use UnixDomainSocketIpcTransport instead).
/// 首实例防抢占（FirstPipeInstance）+ 后台模式同用户边界（CurrentUserOnly）；
/// 服务模式以授权 SID 集合的显式 DACL 认证（#70 安装者保底，#1 opt-in 扩展；
/// managed NamedPipeServerStreamAcl.Create 建例，仅 Windows 宿主编译，
/// 见 PhotoPrivacy.Ipc.csproj 的 PP_WINDOWS_HOST）；#7 起服务模式叠加两项 Hijack 加固：
/// ① 授权用户掩码拆位（CWE-732，建例权仅授服务身份）+ 客户端细粒度打开；
/// ② 命名熵（每次监听随机名 + 建例后 HKLM 通告，防静态名预蹲）。
/// </summary>
// Design(#82): 服务模式管道以安装者 SID 显式 DACL 取代 CurrentUserOnly（fail-closed 回退 CurrentUserOnly），后台管道保持 CurrentUserOnly；授权集合 opt-in 扩展由 #1 落地（决策记录见该票），残余状态：集合跨令牌验收归人工复测、授权用户可建实例的加固已由 #7 拆位落地（熵与拆位的锚点见 TryBindRandomizedServicePipe / ServerOptions / WindowsServicePipeAcl.ServiceClientAccessMask）。
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    /// <summary>有效管道名：服务模式监听后为「基名 + 随机后缀」（#7），其余为构造名。</summary>
    private string _pipeName;
    private readonly WindowsPipeAccessMode _accessMode;
    /// <summary>服务模式安装者 SID 解析器（null → 读安装期登记的注册表值）；测试经此注入免注册表依赖。</summary>
    private readonly Func<string?>? _installerSidResolver;
    /// <summary>服务模式授权 SID 集合解析器（null → 读安装期登记的 MULTI_SZ 集合值）；测试经此注入免注册表依赖。</summary>
    private readonly Func<IReadOnlyList<string>?>? _allowedSidsResolver;
    /// <summary>服务管道通告器（#7 命名熵）：null → 生产注册表路径（WindowsServicePipeAcl.TryAdvertiseServicePipeName）；注入即接管（含清除语义：注入方自管生命周期）。</summary>
    private readonly Func<string, bool>? _servicePipeAdvertiser;
    /// <summary>服务模式解析出的授权 SID 集合（null → 走 CurrentUserOnly 回退路径），ListenAsync 时解析一次。</summary>
    private IReadOnlyList<SecurityIdentifier>? _serviceClientSids;
    private NamedPipeServerStream? _server;

    /// <summary>服务管道绑定结果（#7 命名熵），宿主据此观测通告回退；RandomizedAdvertised 且生产路径时 DisposeAsync 清除通告。</summary>
    public ServicePipeBindOutcome ServicePipeBind { get; private set; }

    /// <summary>通告走的是生产注册表路径（非注入）——Dispose 清除仅对生产路径生效。</summary>
    private bool _advertisedViaProductionPath;

    public NamedPipeIpcTransport(string pipeName)
        : this(pipeName, WindowsPipeAccessMode.BackgroundOnly)
    {
    }

    public NamedPipeIpcTransport(
        string pipeName,
        WindowsPipeAccessMode accessMode,
        Func<string?>? installerSidResolver = null,
        Func<IReadOnlyList<string>?>? allowedSidsResolver = null,
        Func<string, bool>? servicePipeAdvertiser = null)
    {
        _pipeName = pipeName;
        _accessMode = accessMode;
        _installerSidResolver = installerSidResolver;
        _allowedSidsResolver = allowedSidsResolver;
        _servicePipeAdvertiser = servicePipeAdvertiser;
    }

    public string EndpointName => _pipeName;

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // Named pipes don't need explicit bind/listen — the server is created per-connection.
        // We pre-create the first server instance here to match the IIpcTransport lifecycle.
        // 防抢占语义只施加于这个首实例（accept 后预建的后续实例不得加标志）。
        ResolveServiceClientSids();
        if (_accessMode == WindowsPipeAccessMode.ServiceShared)
        {
            TryBindRandomizedServicePipe();
        }
        else
        {
            _server = CreatePipeServer(_pipeName, isFirstInstance: true);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务模式命名熵绑定（#7）：名字即地址、地址即攻击面——每次监听生成 crypto-random 后缀，
    /// 预蹲者无从得知未诞生的名字。先建首实例（FirstPipeInstance：名被占即 fail-fast 上抛，
    /// 传感器保留），成功后通告（HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName，键默认仅管理员/SYSTEM
    /// 可写——发现通道的完整性 ≥ 管道本体客户端集合）。通告失败（非 LocalSystem 服务账户/
    /// 注册表损坏）：随机名无人可发现，回退静态名重绑——可用性优先（fail-safe，与 #70 的
    /// SID 缺失回退 CurrentUserOnly 同构：保持服务、降安全、由宿主记 warning 观测），
    /// 此时防抢占面回到 #7 前，但 CWE-732 拆位后的 DACL 仍生效。
    /// </summary>
    // Design(#7): 服务管道名每次监听随机化并建例后通告，通告失败回退静态名；后台管道保持静态名 + CurrentUserOnly（同用户残余登记在 #7）。
    private void TryBindRandomizedServicePipe()
    {
        var baseName = _pipeName;
        _pipeName = baseName + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _server = CreatePipeServer(_pipeName, isFirstInstance: true);

        if (AdvertiseServicePipeName(_pipeName))
        {
            ServicePipeBind = ServicePipeBindOutcome.RandomizedAdvertised;
            return;
        }

        // 通告失败 → 丢弃随机名实例，回退静态名重绑；静态名被占则照旧上抛（fail-fast 传感器保留）。
        _server.Dispose();
        _server = null;
        _pipeName = baseName;
        _server = CreatePipeServer(baseName, isFirstInstance: true);
        ServicePipeBind = ServicePipeBindOutcome.StaticFallback;

        // 审查返修（m1）：生产路径通告失败时，注册表可能残留合法形态的旧通告值——客户端只对
        // 「缺失/非法」回退静态名，会钉死在陈旧名上（探测恒失败，服务页恒不可达）。此处
        // best-effort 清除残留，让 UI 的下一拍探测落回静态名（本传输此刻就绑在静态名上）。
        // 只对生产路径生效：注入通告器的测试（含「返回 false」形态）不得触碰真实注册表；
        // 清除本身失败（权限性）时残留照旧，残余形态登记在 ipc-contract 已知残余。
        if (_servicePipeAdvertiser is null)
        {
            WindowsServicePipeAcl.TryClearAdvertisedServicePipeName();
        }
    }

    /// <summary>通告服务管道名（#7）：注入优先，null 走生产注册表路径；记录是否生产路径供 Dispose 清理。</summary>
    private bool AdvertiseServicePipeName(string pipeName)
    {
        var advertise = _servicePipeAdvertiser ?? WindowsServicePipeAcl.TryAdvertiseServicePipeName;
        var succeeded = advertise(pipeName);
        _advertisedViaProductionPath = succeeded && _servicePipeAdvertiser is null;
        return succeeded;
    }

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_server is null, this);
        await _server.WaitForConnectionAsync(cancellationToken);
        var connected = _server;
        // Create next server instance for the next accept call.
        // 后续实例不得携带 FirstPipeInstance —— 内核语义上仅首实例可声明，
        // 且管道名已由首实例占据，再加该标志会直接建例失败。
        _server = CreatePipeServer(_pipeName, isFirstInstance: false);
        return connected;
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
#if PP_WINDOWS_HOST
        // 服务模式客户端（#7 / CWE-732）：细粒度掩码打开（不含 FILE_CREATE_PIPE_INSTANCE）——
        // 与拆位后的服务端 DACL 同构，GENERIC 打开会因 0x4 位被整体拒绝；后台模式维持 GENERIC
        // 路径（CurrentUserOnly 属主校验不变）。重试语义（ERROR_PIPE_BUSY / ERROR_FILE_NOT_FOUND）
        // 由 ConnectAsync 内部循环承担，与 GENERIC 路径同实现。交叉发布（非 Windows 宿主产出
        // win-x64）时此分支被编译排除，GENERIC 路径与 CurrentUserOnly 回退同域（同 #70 的
        // fail-closed 编译分区）。
        NamedPipeClientStream client = _accessMode == WindowsPipeAccessMode.ServiceShared
            ? WindowsPipeNative.CreateClientStream(_pipeName)
            : new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                options: ClientOptions);
#else
        var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: ClientOptions);
#endif
        await client.ConnectAsync(cts.Token);
        return client;
    }

    public ValueTask DisposeAsync()
    {
        _server?.Dispose();
        _server = null;
        // #7：优雅停机清除生产路径通告（best-effort，不抛）；注入广告器的测试自管生命周期，
        // 崩溃/被杀残留由下一次启动覆盖或卸载清除，客户端对陈旧通告名按「探不到」处理。
        if (ServicePipeBind == ServicePipeBindOutcome.RandomizedAdvertised && _advertisedViaProductionPath)
        {
            WindowsServicePipeAcl.TryClearAdvertisedServicePipeName();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 服务模式解析授权 SID 集合（#1 opt-in）：优先 MULTI_SZ 集合值（安装者 + 显式授权用户），
    /// 集合未 opt-in、空集或无任何合法条目时回退安装者单值（#70 默认行为，生产两路语义
    /// 同构），两者皆无合法 SID 时不赋值（fail-closed 回退 CurrentUserOnly，不多授任何账户）。
    /// 注入任一解析器即完全接管解析（两类回退均不读注册表）：集合注入存在时，回退只看
    /// 单值注入；注册表单值仅在两个解析器都未注入的生产路径读取。避免本机登记值旁路测试注入。
    /// </summary>
    private void ResolveServiceClientSids()
    {
        if (_accessMode != WindowsPipeAccessMode.ServiceShared || !OperatingSystem.IsWindows())
        {
            return;
        }

        IReadOnlyList<string>? rawCollection = null;
        if (_allowedSidsResolver is not null)
        {
            rawCollection = _allowedSidsResolver();
        }
        else if (_installerSidResolver is null)
        {
            // 生产路径：读安装期登记的 MULTI_SZ 集合值（#1 opt-in）。
            rawCollection = WindowsServicePipeAcl.TryResolveAllowedSids(out var registeredSids)
                ? registeredSids
                : null;
        }

        // 解析同样纳入 PP_WINDOWS_HOST：交叉发布（非 Windows 宿主产出 win-x64）时原生建例
        // 分支已被编译排除，若仍解析会得到「无 CurrentUserOnly 也无 DACL」的管道；
        // 守卫使该场景 fail-closed 回退 CurrentUserOnly。注入解析器可能返回非法 SID
        // 字符串；注册表路径已在 WindowsServicePipeAcl 内校验过。
#if PP_WINDOWS_HOST
        var sids = ParseSids(rawCollection);
        if (sids.Count == 0)
        {
            // 集合未 opt-in / 空集 / 无合法条目 → 安装者单值回退（#70 默认语义；
            // 注册表集合全损坏或注入全非法时同样回退，与生产路径同构）。
            // 注入边界：集合注入存在时回退只看单值注入；注册表单值仅生产路径读取。
            string? rawSingle;
            if (_installerSidResolver is not null)
            {
                rawSingle = _installerSidResolver();
            }
            else if (_allowedSidsResolver is null)
            {
                rawSingle = WindowsServicePipeAcl.TryResolveInstallerSid(out var registered)
                    ? registered
                    : null;
            }
            else
            {
                rawSingle = null;
            }

            if (!string.IsNullOrWhiteSpace(rawSingle))
            {
                sids = ParseSids(new[] { rawSingle });
            }
        }

        if (sids.Count > 0)
        {
            _serviceClientSids = sids;
        }
#endif
    }

#if PP_WINDOWS_HOST
    /// <summary>逐条构造 SID：空白/非法条目剔除（部分损坏不放大为整体失效），返回合法集合。</summary>
    private static List<SecurityIdentifier> ParseSids(IReadOnlyList<string>? candidates)
    {
        var sids = new List<SecurityIdentifier>();
        foreach (var raw in candidates ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                sids.Add(new SecurityIdentifier(raw));
            }
            catch (ArgumentException)
            {
                // 非法 SID 条目视同缺失，剔除后继续。
            }
        }

        return sids;
    }
#endif

    // ponytail: 后台模式仍走 plain NamedPipeServerStream（无 ACL）。服务模式 ACL 走 managed 正典
    // NamedPipeServerStreamAcl.Create（带 PipeSecurity 的建例，仅 Windows 宿主编译，csproj
    // PP_WINDOWS_HOST 分区）。38142f9b 把当年 NamedPipeServerStreamAcl.Create 的
    // UnauthorizedAccessException 归因「单文件解压上下文」系误诊：真因是旧 WorldSid-ReadWrite
    // DACL 缺 FILE_CREATE_PIPE_INSTANCE（PipeAccessRights.Write 刻意不含该位）——服务建第
    // 2..N 个实例时内核按 DACL 对创建者令牌做该项检查，失败且按 accept 逐次泄漏实例直到 254
    // 上限。本票 DACL 给创建者保留 FILE_ALL_ACCESS（含该位），同一 API 不再触发该失败。
    private NamedPipeServerStream CreatePipeServer(string pipeName, bool isFirstInstance)
    {
#if PP_WINDOWS_HOST
        if (_serviceClientSids is { Count: > 0 })
        {
            return WindowsPipeNative.CreateAclServerInstance(pipeName, isFirstInstance, _serviceClientSids);
        }
#endif

        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            transmissionMode: PipeTransmissionMode.Byte,
            options: ServerOptions(isFirstInstance));
    }

    /// <summary>
    /// 服务端管道选项。CurrentUserOnly 与 FirstPipeInstance 均为 Windows 内核语义，
    /// 仅在 Windows 上施加；Unix 上 NamedPipeServerStream 为 FIFO 实现，
    /// 保持既有无标志语义（Unix 侧认证由 UnixDomainSocketIpcTransport 负责）。
    /// 服务模式显式 DACL 路径不带 CurrentUserOnly——CurrentUserOnly 的 DACL 只放行创建者，
    /// 会在内核层面挡住 DACL 已放行的桌面用户，二者互斥（#70）。
    /// </summary>
    // Design(#7): IPC 认证以 OS 身份为边界——Windows 侧 CurrentUserOnly/授权 SID 集合 DACL、Unix 侧 SO_PEERCRED 比对；服务管道自 #7 起叠加命名熵与实例创建权拆位（防预蹲与授权用户建实例）；后台管道的同用户抢先建面仍未闭环——发现通道同用户可伪造，需协议级服务端认证，残余登记在 #7。
    private PipeOptions ServerOptions(bool isFirstInstance)
    {
        var options = PipeOptions.Asynchronous;
        if (!OperatingSystem.IsWindows())
        {
            return options;
        }

        if (_serviceClientSids is not { Count: > 0 })
        {
            options |= PipeOptions.CurrentUserOnly;
        }
        if (isFirstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return options;
    }

    /// <summary>
    /// 客户端管道选项：后台模式在 Windows 上要求同用户对端（CurrentUserOnly）；
    /// 服务模式不带该标志——服务端 DACL 已按安装者 SID 放行，客户端再设 CurrentUserOnly
    /// 会在内核之外把跨账户（含对 SYSTEM 服务的合法连接）整体拒绝，与服务端自相矛盾（#70）。
    /// #7 起服务模式客户端不走本属性：改经 <see cref="WindowsPipeNative.CreateClientStream"/>
    /// 以细粒度掩码（不含 FILE_CREATE_PIPE_INSTANCE）+ Identification 冒充级别打开。
    /// </summary>
    private PipeOptions ClientOptions => _accessMode == WindowsPipeAccessMode.BackgroundOnly
        ? (OperatingSystem.IsWindows()
            ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            : PipeOptions.Asynchronous)
        : PipeOptions.Asynchronous;
}
