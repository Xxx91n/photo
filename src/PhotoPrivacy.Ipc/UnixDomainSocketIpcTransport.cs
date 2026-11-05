using System.Net.Sockets;
using System.Net;

namespace PhotoPrivacy.Ipc;

/// <summary>Unix socket 文件模式策略。</summary>
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
/// 认证分层 —— 第 1 层文件模式收紧（内核强制）+ 第 2 层对端凭据校验（纵深）。
/// </summary>
public sealed class UnixDomainSocketIpcTransport : IIpcTransport
{
    /// <summary>
    /// Service 模式缺省 socket 组名 —— 「socket 访问组」，<b>不是</b>数据组（#187）。
    /// 与 install-launchd-service.sh / verify-launchd-service.sh 的 SOCKET_GROUP 默认值对齐；
    /// 安装脚本把 socket 访问组与数据目录组（默认同名前缀 <c>photoprivacy</c>，`hot`/`quarantine`
    /// 2770 组写）分开后，缺省值若落在数据组上等于把组成员升级成「可绕过 IPC 删审计与隔离」，
    /// 即 CWE-732 原始形态静默复活。
    /// </summary>
    public const string DefaultServiceGroupName = "photoprivacy-ipc";

    /// <summary>
    /// 覆盖 socket 组名的环境变量：只有该组成员能连 Service 模式 socket（#167）。
    /// 安装脚本把「socket 访问组」与「数据目录组」分开后，由 plist/systemd 注入实际组名，
    /// 服务端对端白名单必须跟着走，否则会拒掉合法 GUI 调用者。
    /// </summary>
    public const string ServiceGroupEnvironmentVariable = "PHOTOPRIVACY_SERVICE_GROUP";

    /// <summary>
    /// socket 目录不存在时本进程建目录所用的模式：与安装器承诺同口径（Linux RuntimeDirectoryMode
    /// / macOS SERVICE_SOCKET_DIR 均为 0750）。.NET 的 <see cref="Directory.CreateDirectory(string)"/>
    /// 按 umask 建出 0755，等于把「目录 0750 收口」这层防线换成缺省值（#187 缺口 1）。
    /// </summary>
    public const UnixFileMode SocketDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

    private const string ServiceGroupDiagnostic =
        "Unix socket 服务模式的 socket 访问组必须显式确定：传入 serviceGroupName 或经 " +
        ServiceGroupEnvironmentVariable + " 注入。";

    /// <summary>
    /// 解析实际生效的 socket 组名：环境变量优先，缺失/空白一律 fail-closed 抛
    /// <see cref="InvalidOperationException"/>，<b>不</b>回落到任何组名（#187 缺口 2）。
    /// 回落不可用：默认值只能是数据组或自造名——回落到数据组即组隔离静默退化成 CWE-732 原始形态；
    /// 静默换成本进程自造的组名则让 plist/systemd 的注入失效、合法 GUI 调用者全被拒查无实据。
    /// 缺注入的场景（手起 Worker、unit 未更新、容器）要在绑定端点上直接失败并点明环境变量名。
    /// </summary>
    public static string ResolveServiceGroupName()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ServiceGroupEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(fromEnvironment))
        {
            throw new InvalidOperationException(ServiceGroupDiagnostic);
        }

        return fromEnvironment.Trim();
    }

    private readonly string _socketPath;
    private readonly UnixSocketAccessMode _accessMode;
    private readonly string _serviceGroupName;
    private readonly UnixPeerCredentialPolicy _peerPolicy;
    private Socket? _listener;
    private IReadOnlySet<uint> _allowedPeerUids = new HashSet<uint>();

    public UnixDomainSocketIpcTransport(
        string socketPath,
        UnixSocketAccessMode accessMode = UnixSocketAccessMode.BackgroundOnly,
        string? serviceGroupName = null,
        UnixPeerCredentialPolicy? peerCredentialPolicy = null)
    {
        _socketPath = socketPath;
        _accessMode = accessMode;

        // 组名只在服务模式解：后台模式 socket 0600 仅属主可连，不看组，缺注入不是错误
        // （手起后台 Worker、容器内运行照旧可用）；服务模式缺失即在此 fail-fast，不等到 Listen，
        // 也不留下「已按 0660 绑定、白名单却按数据组」的半失效窗口（#187 缺口 2）。
        var hasExplicitGroup = !string.IsNullOrWhiteSpace(serviceGroupName);
        if (hasExplicitGroup)
        {
            _serviceGroupName = serviceGroupName!.Trim();
        }
        else if (accessMode == UnixSocketAccessMode.ServiceShared)
        {
            _serviceGroupName = ResolveServiceGroupName();
        }
        else
        {
            _serviceGroupName = DefaultServiceGroupName;
        }

        _peerPolicy = peerCredentialPolicy ?? new UnixPeerCredentialPolicy();
    }

    public string EndpointName => _socketPath;

    /// <summary>本实例的 socket 访问策略。</summary>
    public UnixSocketAccessMode AccessMode => _accessMode;

    /// <summary>本实例 ListenAsync 实际施加的 socket 文件模式（未 Listen 或非 Unix 为 null）。</summary>
    public UnixFileMode? AppliedSocketMode { get; private set; }

    /// <summary>ListenAsync 解析出的对端 uid 白名单（诊断/守卫用，运行期不再变更）。</summary>
    public IReadOnlySet<uint> AllowedPeerUids => _allowedPeerUids;

    /// <summary>
    /// 兜底创建 socket 目录：末级目录建为 0750 并显式 <see cref="File.SetUnixFileMode(string, UnixFileMode)"/>。
    /// 只在本进程确实要建目录时调用——已存在的目录一律不碰（属主/组/模式是安装器与服务管理器的事实源，
    /// 运维手工收紧到 0700 的目录不得被兜底路径放宽）。逐级建中间目录时同样按 0750，
    /// 避免 <see cref="Directory.CreateDirectory(string, UnixFileMode)"/> 对中间目录只按 umask 施加。
    /// </summary>
    private static void EnsureSocketDirectory(string dir)
    {
        var missing = new List<string>();
        var current = Path.GetFullPath(dir);
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            missing.Add(current);
            current = Path.GetDirectoryName(current);
        }

        // 自根向下建（列表是 leaf → root 顺序，倒序走）。
        for (var i = missing.Count - 1; i >= 0; i--)
        {
            var level = missing[i];
            if (OperatingSystem.IsWindows())
            {
                // Unix 模式在 Windows 上无从施加（生产路径走命名管道，到不了这里，只为防御性分支）。
                Directory.CreateDirectory(level);
                continue;
            }

            // 带模式创建：目录从出现起就是 0750，不留「先 0755 再 chmod」的窗口。
            Directory.CreateDirectory(level, SocketDirectoryMode);
            // createMode 仍受 umask 与平台实现影响，显式 SetUnixFileMode 才是与安装器同值（0750）的保证。
            // 这里不吞异常：建得出 0755 却收紧不了，等于把 #187 的漏洞留在原地，宁可让绑定失败。
            File.SetUnixFileMode(level, SocketDirectoryMode);
        }
    }

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // Stale socket cleanup — delete stale .sock file before bind
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { /* ignore */ }
        }

        // Ensure parent directory exists — 与安装器同口径建 0750（#187 缺口 1）。
        // 裸 Directory.CreateDirectory 按 umask 建成 0755：目录可被任意本地用户遍历，
        // socket 端点存在性与目录内容外泄，安装器承诺的「目录 0750 收口」被缺省值替换。
        // 已存在的目录不动（安装器/服务管理器是权限的事实源，这里不覆写属主与组）。
        var dir = Path.GetDirectoryName(_socketPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            EnsureSocketDirectory(dir);
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(254);

        // .NET 10 无「bind 后自动 0600」加固（.NET 11 才引入），
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

        // 内核只识别不拦截，拒绝必须由本层执行并立即 close。
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
        // every later connect fail (2nd connect fails with AddressNotAvailable).
        if (ownsEndpoint)
        {
            try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { /* ignore */ }
        }
        return ValueTask.CompletedTask;
    }
}
