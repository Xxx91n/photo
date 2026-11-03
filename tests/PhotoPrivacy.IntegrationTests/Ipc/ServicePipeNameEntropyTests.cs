using System.Security.Principal;
using System.Text.RegularExpressions;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：服务管道命名熵与通告（#7）。
/// 覆盖四点：
/// ① 服务模式监听即随机化：有效名 = 基名 + "." + 32 位大写 hex（crypto-random 后缀，名字即地址、
///    地址即攻击面——预蹲者无从得知未诞生的名字）；通告器收到的是实际绑定名（先建例后通告的
///    端到端证据），绑定结果 <see cref="ServicePipeBindOutcome.RandomizedAdvertised"/>；
///    随机名上的往返同时覆盖细粒度客户端掩码（0x12019B）对拆位 DACL 的可达性；
/// ② 通告失败回退静态名：有效名 == 传入基名，绑定结果 <see cref="ServicePipeBindOutcome.StaticFallback"/>，
///    往返不回归（fail-safe 可用性优先，与 #70 的 SID 缺失回退 CurrentUserOnly 同构）；
/// ③ 后台模式不随机化：有效名 == 构造名，绑定结果 None——熵只施加于服务管道（后台同用户
///    抢先建面无法以熵闭环，见 #7 票内登记）；
/// ④ 注入边界：通告器注入即接管，测试永不触碰真实注册表（含 elevated 运行）；生产注册表
///    通告（HKLM 写入）与 UI 侧动态解析的行为验收归人工 S4 复测。
///
/// CI 平台限制（如实登记，先例 WindowsServicePipeAclTests）：.github/workflows/ci.yml 的
/// test job 为 ubuntu-latest，Windows 专属断言在 CI 不执行；类型与调用的编译覆盖由 ubuntu
/// 全量编译承担。随机后缀分布（不可预测性）不做统计断言——随机源为
/// RandomNumberGenerator（CSPRNG），格式与绑定行为是这里的主证据。
/// </summary>
public sealed class ServicePipeNameEntropyTests
{
    private static readonly Regex SuffixPattern = new("^[0-9A-F]{32}$", RegexOptions.Compiled);

    private static string NewPipeName() => "pp-t7-" + Guid.NewGuid().ToString("N");

    /// <summary>连接 → 写一行 → 服务端循环读满，验证往返可用（服务端 accept 先行防竞态）。</summary>
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
    public async Task ServiceShared_Listen_Binds_Randomized_Name_And_Advertises_Bound_Name()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // #7：注入安装者 SID + 通告器（免注册表依赖，先例 WindowsServicePipeAclTests）。
        var baseName = NewPipeName();
        string? advertised = null;
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            baseName,
            WindowsPipeAccessMode.ServiceShared,
            installerSidResolver: () => installerSid,
            servicePipeAdvertiser: name =>
            {
                advertised = name;
                return true;
            });
        await transport.ListenAsync(CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ServicePipeBindOutcome.RandomizedAdvertised, transport.ServicePipeBind),
            // 通告的必须是实际绑定名：先建例后通告（预蹲者无从得知未诞生的名字）的端到端证据。
            () => Assert.Equal(transport.EndpointName, advertised),
            // 有效名 = 基名 + "." + 32 位 hex；不等于基名（随机化生效），前缀保持（可读性/可归属）。
            () => Assert.NotEqual(baseName, transport.EndpointName),
            () => Assert.StartsWith(baseName + ".", transport.EndpointName, StringComparison.Ordinal),
            () => Assert.True(
                SuffixPattern.IsMatch(transport.EndpointName[(baseName.Length + 1)..]),
                $"random suffix must be 32 uppercase hex chars, got: {transport.EndpointName[(baseName.Length + 1)..]}"));

        // 随机名 + 拆位 DACL + 细粒度客户端掩码三者同链路往返（① 的可达性证据）。
        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task ServiceShared_Advertisement_Failure_Falls_Back_To_Static_Name_And_Serves()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 通告失败（模拟非 LocalSystem 服务账户/注册表损坏）→ 回退静态名重绑：可用性优先，
        // 防抢占面回到 #7 前（宿主记 warning 观测，见 WorkerIpcServerHostedService），
        // 拆位后的 DACL 与细粒度客户端路径仍生效。
        var baseName = NewPipeName();
        var installerSid = WindowsIdentity.GetCurrent().User!.Value;
        await using var transport = new NamedPipeIpcTransport(
            baseName,
            WindowsPipeAccessMode.ServiceShared,
            installerSidResolver: () => installerSid,
            servicePipeAdvertiser: _ => false);
        await transport.ListenAsync(CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ServicePipeBindOutcome.StaticFallback, transport.ServicePipeBind),
            () => Assert.Equal(baseName, transport.EndpointName));

        await AssertRoundTripAsync(transport);
    }

    [Fact]
    public async Task Background_Listen_Does_Not_Randomize_Endpoint_Name()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 后台管道保持静态名 + CurrentUserOnly：同用户发现通道（HKCU/管道枚举）同用户可伪造，
        // 熵无法闭环——刻意不施加（#7 残余登记，完整修复需协议级服务端认证）。
        var baseName = NewPipeName();
        await using var transport = new NamedPipeIpcTransport(baseName);
        await transport.ListenAsync(CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ServicePipeBindOutcome.None, transport.ServicePipeBind),
            () => Assert.Equal(baseName, transport.EndpointName));
    }

    [Fact]
    public async Task ServiceShared_Without_Listen_Has_None_Outcome_And_Keeps_Ctor_Name()
    {
        // 绑定结果与有效名只在 ListenAsync 后变化：未监听的传输（含服务模式）不预生成名字。
        // 本用例无平台依赖（不建管道不碰注册表），Linux CI 也真跑——outcome API 的跨平台编译面证据。
        var baseName = NewPipeName();
        await using var transport = new NamedPipeIpcTransport(baseName, WindowsPipeAccessMode.ServiceShared);

        Assert.Multiple(
            () => Assert.Equal(ServicePipeBindOutcome.None, transport.ServicePipeBind),
            () => Assert.Equal(baseName, transport.EndpointName));
    }
}
