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
/// 服务模式以安装者 SID 显式 DACL 认证。
/// </summary>
// Design(#70): 服务模式管道以安装者 SID 显式 DACL 取代 CurrentUserOnly（SID 缺失/无效时 fail-closed 回退 CurrentUserOnly），后台管道保持 CurrentUserOnly；不承诺多用户/跨账户（归 #1）。
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    private readonly string _pipeName;
    private readonly WindowsPipeAccessMode _accessMode;
    /// <summary>服务模式安装者 SID 解析器（null → 读安装期登记的注册表值）；测试经此注入免注册表依赖。</summary>
    private readonly Func<string?>? _installerSidResolver;
    /// <summary>服务模式管道的安全描述符二进制（null → 走 CurrentUserOnly 路径），ListenAsync 时解析一次。</summary>
    private byte[]? _servicePipeSecurityDescriptor;
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
        _servicePipeSecurityDescriptor = ResolveServicePipeSecurityDescriptor();
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
    /// 服务模式解析安装者 SID 并构建显式 DACL 的二进制安全描述符；
    /// SID 缺失或无效时返回 null（fail-closed 回退 CurrentUserOnly，不多授任何账户）。
    /// </summary>
    private byte[]? ResolveServicePipeSecurityDescriptor()
    {
        if (_accessMode != WindowsPipeAccessMode.ServiceShared || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var rawSid = _installerSidResolver is not null
            ? _installerSidResolver()
            : WindowsServicePipeAcl.TryResolveInstallerSid(out var registered)
                ? registered
                : null;
        if (string.IsNullOrWhiteSpace(rawSid))
        {
            return null;
        }

        try
        {
            return WindowsServicePipeAcl.BuildServicePipeSecurityDescriptor(new SecurityIdentifier(rawSid));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ponytail: 后台模式仍走 plain NamedPipeServerStream（无 ACL）。managed NamedPipeServerStreamAcl.Create
    // 在本仓单文件发布上下文曾抛 UnauthorizedAccessException 并按 accept 逐次泄漏管道实例直到
    // 254 实例上限（38142f9b）；服务 exe 恰是单文件发布，故 #70 服务模式 ACL 直落原生
    // CreateNamedPipeW（WindowsPipeNative），不复活 managed ACL API。
    // 同用户边界改用 PipeOptions.CurrentUserOnly（非 ACL API）；
    // FirstPipeInstance 仅施加于 ListenAsync 预建的首实例。
    private NamedPipeServerStream CreatePipeServer(string pipeName, bool isFirstInstance)
    {
        if (_servicePipeSecurityDescriptor is not null && OperatingSystem.IsWindows())
        {
            return WindowsPipeNative.CreateAclServerInstance(pipeName, isFirstInstance, _servicePipeSecurityDescriptor);
        }

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
    /// 服务模式显式 DACL 路径不经此构造（CurrentUserOnly 会在 DACL 之外再次拒绝桌面用户）。
    /// </summary>
    // Design(#7): IPC 认证以 OS 身份为边界——Windows 侧 CurrentUserOnly、Unix 侧 SO_PEERCRED 比对；只防同机低权限，同用户抢先建管道暂未加固，候选记在 #7。
    private static PipeOptions ServerOptions(bool isFirstInstance)
    {
        var options = PipeOptions.Asynchronous;
        if (!OperatingSystem.IsWindows())
        {
            return options;
        }

        options |= PipeOptions.CurrentUserOnly;
        if (isFirstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return options;
    }

    /// <summary>客户端管道选项：Windows 上要求同用户对端（CurrentUserOnly）。</summary>
    private static PipeOptions ClientOptions => OperatingSystem.IsWindows()
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;
}
