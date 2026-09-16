using System.Net.Sockets;
using System.Net;

namespace PhotoPrivacy.Ipc;

/// <summary>Unix socket 文件模式策略（ADR 0067 to-spec 定值）。</summary>
public enum UnixSocketAccessMode
{
    /// <summary>Background 模式：Worker 与 GUI 同用户 —— socket 0600，仅属主可连。</summary>
    BackgroundOnly = 0,

    /// <summary>Service 模式：Worker 以服务用户、GUI 以人类用户 —— socket 0660 + 组边界（目录 0750）。</summary>
    ServiceShared = 1,
}

/// <summary>
/// Unix Domain Socket IPC transport for Linux/macOS.
/// Uses System.Net.Sockets with AddressFamily.Unix + UnixDomainSocketEndPoint.
/// 票 04 / ADR 0067：认证分层 —— 第 1 层文件模式收紧（内核强制）+ 第 2 层对端凭据校验（纵深）。
/// </summary>
public sealed class UnixDomainSocketIpcTransport : IIpcTransport
{
    /// <summary>Service 模式默认 socket 组名（与 install-systemd-service.sh 的 GROUP_NAME 默认值对齐）。</summary>
    public const string DefaultServiceGroupName = "photoprivacy";

    private readonly string _socketPath;
    private readonly UnixSocketAccessMode _accessMode;
    private readonly string _serviceGroupName;
    private readonly UnixPeerCredentialPolicy _peerPolicy;
    private Socket? _listener;
    private IReadOnlySet<uint> _allowedPeerUids = new HashSet<uint>();

    public UnixDomainSocketIpcTransport(
        string socketPath,
        UnixSocketAccessMode accessMode = UnixSocketAccessMode.BackgroundOnly,
        string serviceGroupName = DefaultServiceGroupName,
        UnixPeerCredentialPolicy? peerCredentialPolicy = null)
    {
        _socketPath = socketPath;
        _accessMode = accessMode;
        _serviceGroupName = serviceGroupName;
        _peerPolicy = peerCredentialPolicy ?? new UnixPeerCredentialPolicy();
    }

    public string EndpointName => _socketPath;

    /// <summary>本实例的 socket 访问策略（ADR 0067 to-spec）。</summary>
    public UnixSocketAccessMode AccessMode => _accessMode;

    /// <summary>本实例 ListenAsync 实际施加的 socket 文件模式（未 Listen 或非 Unix 为 null）。</summary>
    public UnixFileMode? AppliedSocketMode { get; private set; }

    /// <summary>ListenAsync 解析出的对端 uid 白名单（诊断/守卫用，运行期不再变更）。</summary>
    public IReadOnlySet<uint> AllowedPeerUids => _allowedPeerUids;

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // ADR 0026: Stale socket cleanup — delete stale .sock file before bind
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { /* ignore */ }
        }

        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(_socketPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(254);

        // 票 04 / ADR 0067：.NET 10 无「bind 后自动 0600」加固（.NET 11 才引入），
        // 文件模式必须由传输层显式负责 —— background 0600 / service 0660+组边界。
        var mode = _accessMode == UnixSocketAccessMode.ServiceShared
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            File.SetUnixFileMode(_socketPath, mode);
            AppliedSocketMode = mode;
        }
        catch
        {
            // not on Unix
            AppliedSocketMode = null;
        }

        _allowedPeerUids = ResolveAllowedPeerUids();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 纵深第二层白名单：background = 仅本用户；service = 本用户 ∪ socket 组全体成员。
    /// 组解析失败时 fail-closed（退化为仅本用户）—— 宁可拒绝合法连接，也不放行未识别对端。
    /// </summary>
    private IReadOnlySet<uint> ResolveAllowedPeerUids()
    {
        var allowed = new HashSet<uint>();
        var ownUid = _peerPolicy.ReadOwnUid();
        if (ownUid is not null)
        {
            allowed.Add(ownUid.Value);
        }

        if (_accessMode == UnixSocketAccessMode.ServiceShared)
        {
            foreach (var uid in _peerPolicy.ReadGroupMemberUids(_serviceGroupName))
            {
                allowed.Add(uid);
            }
        }

        return allowed;
    }

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_listener is null, this);
        var client = await _listener.AcceptAsync(cancellationToken);

        // 票 04 / ADR 0067：内核只识别不拦截，拒绝必须由本层执行并立即 close。
        // 凭据不可读或不在白名单内 → fail-closed 拒绝（不变量①）。
        var peerUid = _peerPolicy.ReadPeerUid(client);
        if (peerUid is null || !_allowedPeerUids.Contains(peerUid.Value))
        {
            client.Dispose();
            var seen = peerUid is null ? "unreadable" : peerUid.Value.ToString();
            throw new UnauthorizedAccessException(
                "IPC peer rejected on " + _socketPath + ": peer uid " + seen + " not in allowed peer set");
        }

        return new NetworkStream(client, ownsSocket: true);
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cts.Token);
        return new NetworkStream(client, ownsSocket: true);
    }

    public ValueTask DisposeAsync()
    {
        bool ownsEndpoint = _listener is not null;
        _listener?.Dispose();
        _listener = null;
        // Best-effort cleanup of socket file — only by the endpoint owner (server side).
        // Client instances (per-call WorkerIpcClient.SendAsync transports) share the same socket
        // path; deleting it there unlinks the live server endpoint after the first call, making
        // every later connect fail (B11 finding: 2nd connect fails with AddressNotAvailable).
        if (ownsEndpoint)
        {
            try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { /* ignore */ }
        }
        return ValueTask.CompletedTask;
    }
}
