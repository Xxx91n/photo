using System.Security.AccessControl;
using System.Security.Principal;
using PhotoPrivacy.Ipc;
#if PP_WINDOWS_HOST
using System.IO.Pipes;
#endif

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：Windows 服务管道显式 DACL（#70，决策 #82 D3；#1 opt-in 授权集合扩展；#7 掩码拆位）。
/// 覆盖四点：
/// ① 显式 DACL 建例后，被授权 SID（安装者）客户端能完成连接与一次读写——这验证 DACL 权限位
///   覆盖细粒度请求掩码（#7 起客户端不再 GENERIC 打开，改请求 FILE_GENERIC_READ|WRITE 去位后的
///   0x12019B；缺任一语义位会让 AccessCheck 整体拒绝，连接直接 UnauthorizedAccessException）；
/// ② 安装者 SID 缺失/无效时 fail-closed 回退 CurrentUserOnly：绑定不失败、同用户客户端仍可往返。
///   边界说明（如实登记）：本套测试的客户端与建例者同用户，证明不了「其他账户确实被拒」——
///   CurrentUserOnly 内核拒绝与 DACL 跨令牌 AccessCheck 都只在 Windows 实机上由人工 S4 复测覆盖；
///   这里证明的是回退路径与 DACL 建例路径不炸绑定、同用户往返不回归；
/// ③ 安全描述符内容：每个授权 SID（#1 集合可含额外用户）各自读写掩码（#7 拆位：剥除
///   FILE_CREATE_PIPE_INSTANCE，仅 SYSTEM/Administrators 保留完全控制含该位）；
/// ④ 授权集合解析（#1 opt-in）：集合注入时走集合 DACL；集合为空/全非法时回退安装者单值，
///   两者皆无时回退 CurrentUserOnly——未 opt-in 不扩权。
/// 注入 `servicePipeAdvertiser`（#7）：本套 ServiceShared 用例全部注入通告器，监听走随机名路径
/// （往返同时覆盖细粒度客户端对拆位 DACL 的可达性），且永不触碰真实注册表（含 elevated 运行）。
/// 掩码常量断言全平台可跑（CI ubuntu 为主证据，见 ServiceClientAccessMask_Drops_CreatePipe_Instance_Bit）。
///
/// CI 平台（如实登记，#9 起多平台矩阵）：test job 在 ubuntu/windows/macos 三平台跑。
/// Windows 专属断言（本类的 ServiceShared DACL 建例/回退往返）由 windows-pipe-isolation
/// 专属 job 单类隔离真跑——本类与任何其他 collection 并发会冻结整个 test host
/// （xunit.runner.visualstudio 3.1.4 + windows runner 实证，#9 组合二分 run 41920404363：
/// 含本类的并发组合 0 完成直至超时、单类隔离 11/11 全绿），故不入主矩阵 windows 腿；
/// 掩码常量断言（ServiceClientAccessMask_Drops_CreatePipe_Instance_Bit）跨平台编译，
/// 随主矩阵 ubuntu 腿继续作为 CI 主证据（issue 04 AC#2 同口径）。
/// </summary>
public sealed class WindowsServicePipeAclTests
{
    private static string NewPipeName() => "pp-t70-" + Guid.NewGuid().ToString("N");

    /// <summary>#7：注入通告器，ServiceShared 用例走随机名路径且不触碰真实注册表（含 elevated 运行）。</summary>
    private static bool AdvertiseAlways(string _) => true;

    /// <summary>
    /// 掩码常量契约（#7 / CWE-732，全平台可跑，CI ubuntu 为主证据）：
    /// FILE_GENERIC_READ|WRITE 语义位保全（缺任一，客户端连接被 AccessCheck 整体拒绝），
    /// 同时剥除 FILE_CREATE_PIPE_INSTANCE（0x4，与客户端 GENERIC_WRITE 必需位 FILE_APPEND_DATA
    /// 同值的 API 位碰撞缺陷）——带回该位即被授权用户重新获得建实例权（冒充服务端/抢占连接/DoS）。
    /// 位级行为（DACL 实际生效）归 Windows 实机复测，见类注释。
    /// </summary>
    [Fact]
    public void ServiceClientAccessMask_Drops_CreatePipe_Instance_Bit()
    {
        const uint fileGenericRead = 0x00120089u;
        const uint fileGenericWrite = 0x00120116u;
        const uint fileCreatePipeInstance = 0x00000004u;

        Assert.Equal(0u, WindowsServicePipeAcl.ServiceClientAccessMask & fileCreatePipeInstance);
        Assert.Equal(
            fileGenericRead | (fileGenericWrite & ~fileCreatePipeInstance),
            WindowsServicePipeAcl.ServiceClientAccessMask);
    }

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
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => installerSid,
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        // 走传输层客户端：服务模式客户端不带 CurrentUserOnly，AccessCheck 完全由 DACL 裁决
        // （#7 起走细粒度掩码路径，对拆位 DACL 同构）。
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
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => null,
            servicePipeAdvertiser: AdvertiseAlways);
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
            NewPipeName(), WindowsPipeAccessMode.ServiceShared, installerSidResolver: () => "not-a-sid",
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_AllowedSids_Collection_RoundTrip_Does_Not_Regress_Same_User()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // #1 opt-in 集合路径：注入「本用户 + 一个伪造但格式合法的额外 SID」的集合，
        // 验证集合 DACL 建例不炸绑定、集合内成员（本用户）往返不回归。
        // 「集合外账户确实被拒」由内核 AccessCheck 保证，归人工复测（见类注释边界说明）。
        var ownSid = WindowsIdentity.GetCurrent().User!.Value;
        var extraSid = "S-1-5-21-1-2-3-4";
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(),
            WindowsPipeAccessMode.ServiceShared,
            allowedSidsResolver: () => new[] { ownSid, extraSid },
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_Empty_AllowedSids_Falls_Back_To_InstallerSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // #1 opt-in 语义：集合未 opt-in（空集/未登记）→ 回退安装者单值（#70 默认行为）。
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(),
            WindowsPipeAccessMode.ServiceShared,
            installerSidResolver: () => installerSid,
            allowedSidsResolver: () => Array.Empty<string>(),
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_All_Invalid_AllowedSids_Falls_Back_To_InstallerSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // CodeRabbit 返修锁定：注入集合非空但无任何合法条目（模拟注册表 MULTI_SZ 全损坏）时，
        // 与生产路径同构——回退安装者单值，而不是直接 CurrentUserOnly；安装者（本用户）往返可用。
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(),
            WindowsPipeAccessMode.ServiceShared,
            installerSidResolver: () => installerSid,
            allowedSidsResolver: () => new[] { "not-a-sid", "also-not-a-sid" },
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_All_Invalid_AllowedSids_Without_Installer_Resolver_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // CodeRabbit 返修锁定（注入边界）：只注入集合解析器（无单值注入）时，注入即完全接管——
        // 集合无合法条目不回退读注册表单值，直接 CurrentUserOnly，行为不受本机登记影响
        // （否则本机恰有安装者登记时 DACL 会用机器特定 SID，测试随机器漂移）。
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(),
            WindowsPipeAccessMode.ServiceShared,
            allowedSidsResolver: () => new[] { "not-a-sid" },
            servicePipeAdvertiser: AdvertiseAlways);
        await transport.ListenAsync(CancellationToken.None);

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_All_Invalid_AllowedSids_And_No_InstallerSid_Falls_Back_To_CurrentUserOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 集合全非法（注册表被手改坏）且安装者单值也缺失 → 整体 fail-closed 回退
        // CurrentUserOnly：不因集合损坏而放宽任何边界。
        await using var transport = new NamedPipeIpcTransport(
            NewPipeName(),
            WindowsPipeAccessMode.ServiceShared,
            installerSidResolver: () => null,
            allowedSidsResolver: () => new[] { "not-a-sid", "also-not-a-sid" },
            servicePipeAdvertiser: AdvertiseAlways);
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
        var security = WindowsPipeNative.BuildServicePipeSecurity(new[] { new SecurityIdentifier(installerSid) });
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));

        // 掩码与 WindowsServicePipeAcl 的常量同源：FILE_GENERIC_READ|WRITE 去位（#7 拆位）、FILE_ALL_ACCESS。
        const uint fileCreatePipeInstance = 0x00000004u;
        const uint fileGenericReadWrite = (0x00120089u | 0x00120116u) & ~fileCreatePipeInstance;
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
            // #7 拆位：客户端细粒度掩码（0x12019B）所需全部语义位都在，但 0x4 不在；
            // 特权账户（SYSTEM/Administrators）保留 FILE_ALL_ACCESS 含 0x4（服务自建 2..N 实例依赖，38142f9b 教训）。
            () => Assert.Equal(fileGenericReadWrite, installerMask.GetValueOrDefault()),
            () => Assert.Equal(0u, installerMask.GetValueOrDefault() & fileCreatePipeInstance),
            () => Assert.True(systemMask.HasValue, "LocalSystem ACE must exist"),
            () => Assert.Equal(fileAllAccess, systemMask.GetValueOrDefault()),
            () => Assert.Equal(fileCreatePipeInstance, systemMask.GetValueOrDefault() & fileCreatePipeInstance),
            () => Assert.True(administratorsMask.HasValue, "Administrators ACE must exist"),
            () => Assert.Equal(fileAllAccess, administratorsMask.GetValueOrDefault()),
            () => Assert.Equal(fileCreatePipeInstance, administratorsMask.GetValueOrDefault() & fileCreatePipeInstance));
    }

    [Fact]
    public void ServicePipeSecurity_Grants_Each_AllowedSid_ReadWrite_And_System_Admin_FullControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // #1 opt-in：授权集合内每个 SID 各自一条读写 ACE（掩码与单 SID 路径同源，#7 拆位后同为去位掩码），
        // SYSTEM/Administrators 仍各一条完全控制——集合扩权不改变掩码与特权账户规则。
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        var extraSid = new SecurityIdentifier("S-1-5-21-1-2-3-4");
        var security = WindowsPipeNative.BuildServicePipeSecurity(
            new SecurityIdentifier[] { new(installerSid), extraSid });
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));

        const uint fileCreatePipeInstance = 0x00000004u;
        const uint fileGenericReadWrite = (0x00120089u | 0x00120116u) & ~fileCreatePipeInstance;
        const uint fileAllAccess = 0x001F01FFu;

        uint? installerMask = null;
        uint? extraMask = null;
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
            else if (string.Equals(sidValue, extraSid.Value, StringComparison.OrdinalIgnoreCase))
            {
                extraMask = mask;
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
            () => Assert.Equal(fileGenericReadWrite, installerMask.GetValueOrDefault()),
            // #7 拆位：集合内每个授权 SID 都不得拿回建实例权，特权账户保留（服务自建 2..N 实例依赖）。
            () => Assert.Equal(0u, installerMask.GetValueOrDefault() & fileCreatePipeInstance),
            () => Assert.Equal(0u, extraMask.GetValueOrDefault() & fileCreatePipeInstance),
            () => Assert.True(extraMask.HasValue, "extra allowed SID ACE must exist"),
            () => Assert.Equal(fileGenericReadWrite, extraMask.GetValueOrDefault()),
            () => Assert.True(systemMask.HasValue, "LocalSystem ACE must exist"),
            () => Assert.Equal(fileAllAccess, systemMask.GetValueOrDefault()),
            () => Assert.Equal(fileCreatePipeInstance, systemMask.GetValueOrDefault() & fileCreatePipeInstance),
            () => Assert.True(administratorsMask.HasValue, "Administrators ACE must exist"),
            () => Assert.Equal(fileAllAccess, administratorsMask.GetValueOrDefault()),
            () => Assert.Equal(fileCreatePipeInstance, administratorsMask.GetValueOrDefault() & fileCreatePipeInstance));
    }
#endif
}
