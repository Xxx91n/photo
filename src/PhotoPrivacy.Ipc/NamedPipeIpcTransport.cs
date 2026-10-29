using System.IO.Pipes;
using System.Security.Principal;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 管道访问策略（对齐 <see cref="UnixSocketAccessMode"/> 的 Unix 侧语义）。
/// </summary>
public enum WindowsPipeAccessMode
{
    /// <summary>后台模式：同用户边界（CurrentUserOnly）。</summary>
    BackgroundOnly = 0,

    /// <summary>服务模式：安装者 SID 显式 DACL（#70）；SID 解析不到时 fail-closed 回退同用户边界。</summary>
    ServiceShared = 1,
}

/// <summary>
/// Windows named-pipe IPC transport. Falls back to FIFO on Unix (not recommended — use UnixDomainSocketIpcTransport instead).
/// 首实例防抢占（FirstPipeInstance）+ 后台模式同用户边界（CurrentUserOnly）；
/// 服务模式以安装者 SID 显式 DACL 认证（managed NamedPipeServerStreamAcl.Create 建例，
/// 仅 Windows 宿主编译，见 PhotoPrivacy.Ipc.csproj 的 PP_WINDOWS_HOST）。
/// </summary>
// Design(#70): 服务模式管道以安装者 SID 显式 DACL 取代 CurrentUserOnly（SID 缺失/无效时 fail-closed 回退 CurrentUserOnly），后台管道保持 CurrentUserOnly；不承诺多用户/跨账户（归 #1）。
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    private readonly string _pipeName;
    private readonly WindowsPipeAccessMode _accessMode;
    /// <summary>服务模式安装者 SID 解析器（null → 读安装期登记的注册表值）；测试经此注入免注册表依赖。</summary>
    private readonly Func<string?>? _installerSidResolver;
    /// <summary>服务模式解析出的安装者 SID（null → 走 CurrentUserOnly 路径），ListenAsync 时解析一次。</summary>
    private SecurityIdentifier? _serviceClientSid;
    private NamedPipeServerStream? _server;

    public NamedPipeIpcTransport(string pipeName)
        : this(pipeName, WindowsPipeAccessMode.BackgroundOnly)
    {
    }

    public NamedPipeIpcTransport(
        string pipeName,
        WindowsPipeAccessMode accessMode,
        Func<string?>? installerSidResolver = null)
    {
        _pipeName = pipeName;
        _accessMode = accessMode;
        _installerSidResolver = installerSidResolver;
    }

    public string EndpointName => _pipeName;

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // Named pipes don't need explicit bind/listen — the server is created per-connection.
        // We pre-create the first server instance here to match the IIpcTransport lifecycle.
        // 防抢占语义只施加于这个首实例（accept 后预建的后续实例不得加标志）。
        ResolveServiceClientSid();
        _server = CreatePipeServer(_pipeName, isFirstInstance: true);
        return Task.CompletedTask;
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
        var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: ClientOptions);
        await client.ConnectAsync(cts.Token);
        return client;
    }

    public ValueTask DisposeAsync()
    {
        _server?.Dispose();
        _server = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 服务模式解析安装者 SID；缺失或无效时置 null（fail-closed 回退 CurrentUserOnly，不多授任何账户）。
    /// </summary>
    private void ResolveServiceClientSid()
    {
        if (_accessMode != WindowsPipeAccessMode.ServiceShared || !OperatingSystem.IsWindows())
        {
            return;
        }

        var rawSid = _installerSidResolver is not null
            ? _installerSidResolver()
            : WindowsServicePipeAcl.TryResolveInstallerSid(out var registered)
                ? registered
                : null;
        // 注入解析器可能返回非法 SID 字符串；注册表路径已在 TryResolveInstallerSid 内校验过。
        if (string.IsNullOrWhiteSpace(rawSid))
        {
            return;
        }

        // 解析同样纳入 PP_WINDOWS_HOST：交叉发布（非 Windows 宿主产出 win-x64）时原生建例
        // 分支已被编译排除，若仍解析会得到「无 CurrentUserOnly 也无 DACL」的管道；
        // 守卫使该场景 fail-closed 回退 CurrentUserOnly。
#if PP_WINDOWS_HOST
        try
        {
            _serviceClientSid = new SecurityIdentifier(rawSid);
        }
        catch (ArgumentException)
        {
            // 非法 SID 视同缺失，走 fail-closed 回退。
        }
#endif
    }

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
        if (_serviceClientSid is not null)
        {
            return WindowsPipeNative.CreateAclServerInstance(pipeName, isFirstInstance, _serviceClientSid);
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
    // Design(#7): IPC 认证以 OS 身份为边界——Windows 侧 CurrentUserOnly、Unix 侧 SO_PEERCRED 比对；只防同机低权限，同用户抢先建管道暂未加固，候选记在 #7。
    private PipeOptions ServerOptions(bool isFirstInstance)
    {
        var options = PipeOptions.Asynchronous;
        if (!OperatingSystem.IsWindows())
        {
            return options;
        }

        if (_serviceClientSid is null)
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
    /// </summary>
    private PipeOptions ClientOptions => _accessMode == WindowsPipeAccessMode.BackgroundOnly
        ? (OperatingSystem.IsWindows()
            ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            : PipeOptions.Asynchronous)
        : PipeOptions.Asynchronous;
}
