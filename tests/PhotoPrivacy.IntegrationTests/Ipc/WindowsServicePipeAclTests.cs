using System.Security.AccessControl;
using System.Security.Principal;
using PhotoPrivacy.Ipc;
#if PP_WINDOWS_HOST
using System.IO.Pipes;
#endif

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：Windows 服务管道显式 DACL（#70，决策 #82 D3）。
/// 覆盖三点：
/// ① 显式 DACL 建例后，被授权 SID（安装者）客户端能完成连接与一次读写——这验证 DACL 权限位
///   覆盖 GENERIC_READ|GENERIC_WRITE 的文件系统语义映射（缺 ReadPermissions/Synchronize 等位
///   会让 AccessCheck 整体拒绝，连接直接 UnauthorizedAccessException）；
/// ② 安装者 SID 缺失/无效时 fail-closed 回退 CurrentUserOnly：绑定不失败、同用户客户端仍可往返。
///   边界说明（如实登记）：本套测试的客户端与建例者同用户，证明不了「其他账户确实被拒」——
///   CurrentUserOnly 内核拒绝与 DACL 跨令牌 AccessCheck 都只在 Windows 实机上由人工 S4 复测覆盖；
///   这里证明的是回退路径与 DACL 建例路径不炸绑定、同用户往返不回归；
/// ③ 安全描述符内容：安装者读写 + LocalSystem/Administrators 完全控制。
///
/// CI 平台限制（如实登记，先例 IpcAuthenticationLayeringTests）：.github/workflows/ci.yml 的
/// test job 为 ubuntu-latest，Windows 专属断言在 CI 不执行；类型与调用的编译覆盖由 ubuntu
/// 全量编译承担；Windows 实机验收（非提权令牌对服务管道 GetStatus 往返、后台管道只许当前用户）
/// 按 #82 D3 归人工 S4 复测，不在 CI 内替证。
/// </summary>
public sealed class WindowsServicePipeAclTests
{
    private static string NewPipeName() => "pp-t70-" + Guid.NewGuid().ToString("N");

    /// <summary>连接 → 写一行 → 服务端循环读满，验证同用户往返可用（服务端 accept 先行防竞态）。</summary>
    private static async Task AssertRoundTripAsync(NamedPipeIpcTransport transport)
    {
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

        Assert.Equal(request, buffer);
    }

    [Fact]
    public async Task ServiceShared_Authorized_InstallerSid_Client_Can_Connect_And_RoundTrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => installerSid);
        await transport.ListenAsync(CancellationToken.None);

        // 走传输层客户端：服务模式客户端不带 CurrentUserOnly，AccessCheck 完全由 DACL 裁决。
        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_Missing_InstallerSid_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // fail-closed 回退路径：SID 解析不到时绑定不失败、回退 CurrentUserOnly 后同用户往返可用；
        // 「其他账户确实被拒」由内核 CurrentUserOnly 保证，归人工 S4 复测（见类注释边界说明）。
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => null);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_Invalid_InstallerSid_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 防回潮：注册表出现非法 SID 字符串时抛异常炸绑定、服务起不来——必须与缺失同样走回退。
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => "not-a-sid");
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

#if PP_WINDOWS_HOST
    [Fact]
    public void ServicePipeSecurity_Grants_Installer_ReadWrite_And_System_Admin_FullControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        var security = WindowsPipeNative.BuildServicePipeSecurity(new SecurityIdentifier(installerSid));
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));

        // 掩码与 WindowsServicePipeAcl 的常量同源：FILE_GENERIC_READ|WRITE、FILE_ALL_ACCESS。
        const uint fileGenericReadWrite = 0x00120089u | 0x00120116u;
        const uint fileAllAccess = 0x001F01FFu;

        uint? installerMask = null;
        uint? systemMask = null;
        uint? administratorsMask = null;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        foreach (var rule in rules)
        {
            var pipeRule = (PipeAccessRule)rule;
            if (pipeRule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }
            var mask = (uint)pipeRule.PipeAccessRights;
            var sidValue = pipeRule.IdentityReference.Value;
            if (string.Equals(sidValue, installerSid, StringComparison.OrdinalIgnoreCase))
            {
                installerMask = mask;
            }
            else if (sidValue == systemSid)
            {
                systemMask = mask;
            }
            else if (sidValue == administratorsSid)
            {
                administratorsMask = mask;
            }
        }

        Assert.Multiple(
            () => Assert.True(installerMask.HasValue, "installer ACE must exist"),
            // 客户端按 GENERIC_READ|GENERIC_WRITE 打开，AccessCheck 映射需要全部文件语义位。
            () => Assert.Equal(fileGenericReadWrite, installerMask),
            () => Assert.True(systemMask.HasValue, "LocalSystem ACE must exist"),
            () => Assert.Equal(fileAllAccess, systemMask),
            () => Assert.True(administratorsMask.HasValue, "Administrators ACE must exist"),
            () => Assert.Equal(fileAllAccess, administratorsMask));
    }
#endif
}
