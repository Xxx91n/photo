using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：Windows 服务管道显式 DACL（#70，决策 #82 D3）。
/// 覆盖三点：
/// ① 显式 DACL 建例后，被授权 SID（安装者）客户端能完成连接与一次读写——这验证 DACL 权限位
///   覆盖 GENERIC_READ|GENERIC_WRITE 的文件系统语义映射（缺 ReadPermissions/Synchronize 等位
///   会让 AccessCheck 整体拒绝，连接直接 UnauthorizedAccessException）；
/// ② 安装者 SID 缺失/无效时 fail-closed 回退 CurrentUserOnly：绑定不失败、同用户客户端仍可往返
///   （后台模式回归边界），且不多授任何账户；
/// ③ 安全描述符内容：安装者读写 + LocalSystem/Administrators 完全控制。
///
/// CI 平台限制（如实登记，先例 IpcAuthenticationLayeringTests）：.github/workflows/ci.yml 的
/// test job 为 ubuntu-latest，Windows 专属断言在 CI 不执行；类型与调用的编译覆盖由 ubuntu
/// 全量编译承担；Windows 实机验收（非提权令牌对服务管道 GetStatus 往返、后台回归）按 #82 D3
/// 归人工 S4 复测，不在 CI 内替证。
/// </summary>
public sealed class WindowsServicePipeAclTests
{
    private static string NewPipeName() => "pp-t70-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ServiceShared_Authorized_InstallerSid_Client_Can_Connect_And_RoundTrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = NewPipeName();
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            pipeName, WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => installerSid);
        await transport.ListenAsync(CancellationToken.None);

        var acceptTask = transport.AcceptClientAsync(CancellationToken.None);
        // 走传输层客户端（CurrentUserOnly 客户端要求服务端同用户——测试进程即建例者，满足）。
        await using var client = await transport.ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await using var accepted = await acceptTask;

        var request = System.Text.Encoding.UTF8.GetBytes("ping\n");
        await client.WriteAsync(request, CancellationToken.None);
        var buffer = new byte[request.Length];
        var totalRead = 0;
        while (totalRead < request.Length)
        {
            var read = await accepted.ReadAsync(buffer.AsMemory(totalRead), CancellationToken.None);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        Assert.Equal(request.Length, totalRead);
        Assert.Equal(request, buffer);
    }

    [Fact]
    public async Task ServiceShared_Missing_InstallerSid_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // fail-closed 回潮形态：SID 解析不到时不允许以「放空 DACL/WorldSid」求可用——
        // 必须回退同用户边界（绑住 #82 D3 的「不多授任何账户」）。
        var pipeName = NewPipeName();
        await using var transport = new NamedPipeIpcTransport(
            pipeName, WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => null);
        await transport.ListenAsync(CancellationToken.None);

        var acceptTask = transport.AcceptClientAsync(CancellationToken.None);
        await using var client = await transport.ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await using var accepted = await acceptTask;

        var request = System.Text.Encoding.UTF8.GetBytes("ping\n");
        await client.WriteAsync(request, CancellationToken.None);
        var buffer = new byte[request.Length];
        var totalRead = 0;
        while (totalRead < request.Length)
        {
            var read = await accepted.ReadAsync(buffer.AsMemory(totalRead), CancellationToken.None);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        Assert.Equal(request.Length, totalRead);
    }

    [Fact]
    public async Task ServiceShared_Invalid_InstallerSid_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 回潮形态：注册表里出现非法 SID 字符串时抛异常炸绑定，服务直接起不来。
        var pipeName = NewPipeName();
        await using var transport = new NamedPipeIpcTransport(
            pipeName, WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => "not-a-sid");
        await transport.ListenAsync(CancellationToken.None);

        var acceptTask = transport.AcceptClientAsync(CancellationToken.None);
        await using var client = await transport.ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await using var accepted = await acceptTask;

        // 回退路径必须保持后台模式同界：同用户客户端完整往返可用，而不是仅"能连"。
        var request = System.Text.Encoding.UTF8.GetBytes("ping\n");
        await client.WriteAsync(request, CancellationToken.None);
        var buffer = new byte[request.Length];
        var totalRead = 0;
        while (totalRead < request.Length)
        {
            var read = await accepted.ReadAsync(buffer.AsMemory(totalRead), CancellationToken.None);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        Assert.Equal(request.Length, totalRead);
    }

    [Fact]
    public void SecurityDescriptor_Grants_Installer_ReadWrite_And_System_Admin_FullControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        var bytes = WindowsServicePipeAcl.BuildServicePipeSecurityDescriptor(new SecurityIdentifier(installerSid));

        var security = new PipeSecurity();
        security.SetSecurityDescriptorBinaryForm(bytes);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));

        PipeAccessRule? installerRule = null;
        var systemFullControl = false;
        var administratorsFullControl = false;
        foreach (var rule in rules)
        {
            var pipeRule = (PipeAccessRule)rule;
            if (pipeRule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }
            if (string.Equals(pipeRule.IdentityReference.Value, installerSid, StringComparison.OrdinalIgnoreCase))
            {
                installerRule = pipeRule;
            }
            else if (pipeRule.IdentityReference.Value == new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value)
            {
                systemFullControl = pipeRule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl);
            }
            else if (pipeRule.IdentityReference.Value == new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value)
            {
                administratorsFullControl = pipeRule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl);
            }
        }

        Assert.Multiple(
            () => Assert.NotNull(installerRule),
            () => Assert.True(installerRule!.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite), "installer must get ReadWrite"),
            () => Assert.True(installerRule!.PipeAccessRights.HasFlag(PipeAccessRights.ReadPermissions), "client opens with GENERIC_* which maps READ_CONTROL"),
            () => Assert.True(installerRule!.PipeAccessRights.HasFlag(PipeAccessRights.Synchronize), "client opens with GENERIC_* which maps SYNCHRONIZE"),
            () => Assert.True(systemFullControl, "LocalSystem must keep FullControl"),
            () => Assert.True(administratorsFullControl, "Administrators must keep FullControl"));
    }
}
