using System.Net.Sockets;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 票 04 行为测试：IPC 认证分层（ADR 0067）。
/// 第 1 层 = 内核强制的 OS 身份边界（Windows CurrentUserOnly / Unix socket 文件模式）；
/// 第 2 层 = accepted socket 上的对端凭据校验（Unix SO_PEERCRED / getpeereid）；
/// 第 3 层 = 首实例防抢占 fail-fast（Windows FirstPipeInstance）。
/// 本套全部断言真实行为（真实 bind/connect、真实文件模式、真实内核 fail-fast），
/// 不是读源码文本断言。
///
/// CI 平台限制（如实登记）：.github/workflows/ci.yml 的 test job 为 ubuntu-latest，
/// 故 Windows 专属断言（FirstPipeInstance fail-fast）在 CI 不执行；其证据为单文件
/// 冒烟实验留证 .scratch/architecture-recovery/reports/04-smoke-evidence.log。
/// Unix 侧断言（文件模式 + 对端凭据）在 ubuntu-latest 上真跑，是 CI 可测范围内
/// 的主证据（issue 04 AC#2）。
/// </summary>
public sealed class IpcAuthenticationLayeringTests
{
    private const UnixFileMode BackgroundSocketMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode ServiceSocketMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

    private static string NewSocketPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-ipc-t04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "worker.sock");
    }

    private static void CleanupSocketDir(string socketPath)
    {
        var dir = Path.GetDirectoryName(socketPath);
        if (dir is not null && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------
    // 第 1 层：socket 文件模式收紧（内核强制，Linux/macOS CI 真跑）
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnixTransport_Background_Mode_Should_Set_Socket_Mode_To_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            await using var transport = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly);
            await transport.ListenAsync(CancellationToken.None);

            Assert.Equal(BackgroundSocketMode, File.GetUnixFileMode(path));
            Assert.Equal(BackgroundSocketMode, transport.AppliedSocketMode.GetValueOrDefault());
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    [Fact]
    public async Task UnixTransport_Service_Mode_Should_Set_Socket_Mode_To_0660()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            await using var transport = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.ServiceShared);
            await transport.ListenAsync(CancellationToken.None);

            Assert.Equal(ServiceSocketMode, File.GetUnixFileMode(path));
            Assert.Equal(ServiceSocketMode, transport.AppliedSocketMode.GetValueOrDefault());
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    // ------------------------------------------------------------------
    // 第 2 层：对端凭据校验
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnixTransport_Accept_Should_Admit_Same_User_Peer_Via_Real_Peer_Credential_Read()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            // 无注入：走真实 geteuid + SO_PEERCRED / getpeereid
            await using var server = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly);
            await server.ListenAsync(CancellationToken.None);

            var acceptTask = server.AcceptClientAsync(CancellationToken.None);
            await using var client = new UnixDomainSocketIpcTransport(path);
            await using var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await using var accepted = await acceptTask;

            Assert.NotNull(accepted);
            var ownUid = new UnixPeerCredentialPolicy().ReadOwnUid();
            Assert.NotNull(ownUid);
            Assert.Contains(ownUid!.Value, server.AllowedPeerUids);
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    [Fact]
    public async Task UnixTransport_Accept_Should_Reject_Foreign_Peer_Uid()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            // 跨用户场景可模拟：注入伪造对端 uid（own=1000 / peer=9999）
            var policy = new FakePeerCredentialPolicy(ownUid: 1000, peerUid: 9999);
            await using var server = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly, peerCredentialPolicy: policy);
            await server.ListenAsync(CancellationToken.None);

            var acceptTask = server.AcceptClientAsync(CancellationToken.None);
            await using var client = new UnixDomainSocketIpcTransport(path);
            await using var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => acceptTask);
            Assert.Contains("not in allowed peer set", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    [Fact]
    public async Task UnixTransport_Accept_Should_Fail_Closed_When_Peer_Credential_Unreadable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            var policy = new FakePeerCredentialPolicy(ownUid: 1000, peerUid: null);
            await using var server = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly, peerCredentialPolicy: policy);
            await server.ListenAsync(CancellationToken.None);

            var acceptTask = server.AcceptClientAsync(CancellationToken.None);
            await using var client = new UnixDomainSocketIpcTransport(path);
            await using var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => acceptTask);
            Assert.Contains("unreadable", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    [Fact]
    public async Task UnixTransport_Service_Mode_Should_Admit_Group_Member_And_Reject_Outsider()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewSocketPath();
        try
        {
            // 组内成员（2000）应被接纳；组外（9999）应被拒
            var memberPolicy = new FakePeerCredentialPolicy(
                ownUid: 1000, peerUid: 2000, groupMemberUids: new HashSet<uint> { 2000 });
            await using (var server = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.ServiceShared, peerCredentialPolicy: memberPolicy))
            {
                await server.ListenAsync(CancellationToken.None);
                Assert.Contains(2000u, server.AllowedPeerUids);
                Assert.Contains(1000u, server.AllowedPeerUids);

                var acceptTask = server.AcceptClientAsync(CancellationToken.None);
                await using var client = new UnixDomainSocketIpcTransport(path);
                await using var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await using var accepted = await acceptTask;
                Assert.NotNull(accepted);
            }

            var path2 = NewSocketPath();
            try
            {
                var outsiderPolicy = new FakePeerCredentialPolicy(
                    ownUid: 1000, peerUid: 9999, groupMemberUids: new HashSet<uint> { 2000 });
                await using var server2 = new UnixDomainSocketIpcTransport(
                    path2, UnixSocketAccessMode.ServiceShared, peerCredentialPolicy: outsiderPolicy);
                await server2.ListenAsync(CancellationToken.None);

                var acceptTask2 = server2.AcceptClientAsync(CancellationToken.None);
                await using var client2 = new UnixDomainSocketIpcTransport(path2);
                await using var connected2 = await client2.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => acceptTask2);
            }
            finally
            {
                CleanupSocketDir(path2);
            }
        }
        finally
        {
            CleanupSocketDir(path);
        }
    }

    // ------------------------------------------------------------------
    // 第 3 层：首实例防抢占 fail-fast（Windows 内核语义）
    // ------------------------------------------------------------------

    [Fact]
    public async Task NamedPipeTransport_Listen_Should_Fail_Fast_When_Name_Already_Owned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = "PhotoPrivacyT04_" + Guid.NewGuid().ToString("N");
        await using var first = new NamedPipeIpcTransport(pipeName);
        await first.ListenAsync(CancellationToken.None);

        await using var second = new NamedPipeIpcTransport(pipeName);
        Exception? captured = null;
        try
        {
            await second.ListenAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        Assert.True(captured is not null,
            "second binder on an owned pipe name must fail fast (PipeOptions.FirstPipeInstance)");
        Assert.True(captured is UnauthorizedAccessException or IOException,
            "expected kernel fail-fast on duplicate first instance, got " + captured!.GetType().Name);
    }

    [Fact]
    public async Task NamedPipeTransport_Should_Accept_Same_User_Client_With_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = "PhotoPrivacyT04_" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeIpcTransport(pipeName);
        await server.ListenAsync(CancellationToken.None);

        var acceptTask = server.AcceptClientAsync(CancellationToken.None);
        await using var connected = await server.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await using var accepted = await acceptTask;

        Assert.NotNull(accepted);
    }

    // ------------------------------------------------------------------
    // 工厂选择（跨平台真行为）
    // ------------------------------------------------------------------

    [Fact]
    public void IpcTransportFactory_Should_Select_Transport_Matching_Current_Os()
    {
        var transport = IpcTransportFactory.CreateServer("PhotoPrivacyT04.Factory");
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<NamedPipeIpcTransport>(transport);
        }
        else
        {
            var unix = Assert.IsType<UnixDomainSocketIpcTransport>(transport);
            Assert.Equal(UnixSocketAccessMode.BackgroundOnly, unix.AccessMode);
        }
    }

    [Fact]
    public void IpcTransportFactory_Service_Mode_Should_Request_Shared_Unix_Access()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var transport = Assert.IsType<UnixDomainSocketIpcTransport>(
            IpcTransportFactory.CreateServer("/run/photoprivacy/worker.sock", isServiceMode: true));
        Assert.Equal(UnixSocketAccessMode.ServiceShared, transport.AccessMode);
    }

    /// <summary>可配置的伪造凭据策略：模拟跨用户连接与凭据不可读场景。</summary>
    private sealed class FakePeerCredentialPolicy : UnixPeerCredentialPolicy
    {
        private readonly uint? _ownUid;
        private readonly uint? _peerUid;
        private readonly IReadOnlySet<uint> _groupMembers;

        public FakePeerCredentialPolicy(uint? ownUid, uint? peerUid, IReadOnlySet<uint>? groupMemberUids = null)
        {
            _ownUid = ownUid;
            _peerUid = peerUid;
            _groupMembers = groupMemberUids ?? new HashSet<uint>();
        }

        public override uint? ReadOwnUid() => _ownUid;

        public override uint? ReadPeerUid(Socket socket) => _peerUid;

        public override IReadOnlySet<uint> ReadGroupMemberUids(string groupName) => _groupMembers;
    }
}
