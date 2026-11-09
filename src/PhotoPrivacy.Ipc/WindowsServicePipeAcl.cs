using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using Microsoft.Win32;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务管道显式 DACL 策略（#70，决策 #82 D3）：安装脚本（scripts/install-service.ps1）
/// 在安装期把安装者用户 SID 登记到 <c>HKLM\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid</c>；
/// 服务端建管道时经 <see cref="WindowsPipeNative.CreateAclServerInstance"/> 携带显式 DACL：
/// 授予安装者 SID 读写，SYSTEM/Administrators 保留完全控制。
/// 多用户授权（#1 opt-in）：安装脚本可把「安装者 + -AuthorizedUsers 显式授权用户」的完整
/// SID 集合另登记到 <c>ServicePipeAllowedSids</c>（REG_MULTI_SZ）；服务端优先读集合值构建 DACL，
/// 集合值缺失时回退安装者单值（默认行为与 #70 完全一致，未 opt-in 即不扩权）。
/// SID 缺失或无效时 fail-closed：回退 CurrentUserOnly，桌面用户仍被拒，不多授任何账户。
/// 重装/升级会覆盖登记值，ACL 随最新安装者；未列入授权集合的账户仍被拒（#1）。
/// 后台管道不经过本类，保持 CurrentUserOnly。
/// 授权用户掩码自 #7（CWE-732）起剥除 FILE_CREATE_PIPE_INSTANCE（见 <see cref="ServiceClientAccessMask"/>）；
/// 服务管道随机名（#7 命名熵）由服务进程建例后经 <see cref="TryAdvertiseServicePipeName"/> 通告。
/// 本类全平台可编译（CI test job 只跑 ubuntu）：注册表读取 + SID 校验是平台无关 API；
/// DACL 组装在 WindowsPipeNative（PP_WINDOWS_HOST 分区）——managed PipeAccessRights 在
/// Linux net10 编译面缺失（CI 实证），不得在全平台编译的代码里引用。
/// </summary>
public static class WindowsServicePipeAcl
{
    public const string RegistrySubKeyName = @"SOFTWARE\PhotoPrivacy";
    public const string InstallerSidValueName = "ServicePipeInstallerSid";
    /// <summary>授权用户 SID 集合（安装者 + -AuthorizedUsers 显式授权，#1 opt-in）；REG_MULTI_SZ。</summary>
    public const string AllowedSidsValueName = "ServicePipeAllowedSids";
    /// <summary>
    /// 服务管道通告名（#7 命名熵）：服务建首例成功后由服务进程（LocalSystem）写入（REG_SZ），
    /// UI 动态解析服务端点优先读它，缺失/非法回退静态名。每次服务启动随新随机名覆盖。
    /// 键默认 ACL 仅管理员/SYSTEM 可写——发现通道的完整性高于管道客户端集合（授权用户只可读）。
    /// </summary>
    public const string AdvertisedServicePipeValueName = "ServicePipeName";

    /// <summary>
    /// 服务管道客户端（安装者/授权用户）被授予的访问掩码（原始位，经强转进 PipeAccessRights）。
    /// 客户端按 FILE_GENERIC_READ|FILE_GENERIC_WRITE 语义打开（含 READ_CONTROL/SYNCHRONIZE 等位），
    /// 缺任一位都会整体拒绝；#7（CWE-732）起刻意剥除 FILE_CREATE_PIPE_INSTANCE（0x4）——该位与
    /// 客户端 GENERIC_WRITE 必需位 FILE_APPEND_DATA 同值（微软 Named Pipe 文档登记的 API 位碰撞缺陷），
    /// 不拆则被授权用户可创建后续管道实例（FirstPipeInstance 只保护首实例）→ 冒充服务端/抢占连接/DoS。
    /// 掩码同时是客户端侧细粒度打开的请求值（双端同值：服务端授多少，客户端要多少）；
    /// 服务身份（SYSTEM/Administrators）保留 FILE_ALL_ACCESS 含该位，服务自建 2..N 实例
    /// 不受影响（38142f9b 教训保位）。
    /// </summary>
    public const uint ServiceClientAccessMask = (FileGenericRead | FileGenericWrite) & ~FileCreatePipeInstance;
    internal const uint FileAllAccess = 0x001F01FF;
    private const uint FileGenericRead = 0x00120089;
    private const uint FileGenericWrite = 0x00120116;
    /// <summary>FILE_CREATE_PIPE_INSTANCE：与 FILE_APPEND_DATA 同值 0x4（GENERIC_WRITE 映射必含，位碰撞缺陷源）。</summary>
    private const uint FileCreatePipeInstance = 0x00000004;

    /// <summary>
    /// 读取安装期登记的安装者 SID（install-service.ps1 写入；SID 字符串非敏感，键默认继承 Users 可读）。
    /// 非 Windows、键缺失、值缺失或非法 SID 均返回 false（调用方 fail-closed）。
    /// </summary>
    public static bool TryResolveInstallerSid([NotNullWhen(true)] out string? installerSid)
    {
        installerSid = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.LocalMachine.OpenSubKey(RegistrySubKeyName);
        var raw = key?.GetValue(InstallerSidValueName) as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            _ = new SecurityIdentifier(raw);
        }
        catch (ArgumentException)
        {
            return false;
        }

        installerSid = raw;
        return true;
    }

    /// <summary>
    /// 读取安装期登记的授权用户 SID 集合（install-service.ps1 的 -AuthorizedUsers 写入；
    /// 集合含安装者保底，未 opt-in 时该值不存在）。MULTI_SZ 读出 string[]；值为单字符串时
    /// （异常形态）按单元素处理。逐条校验 SID：非法条目剔除，全非法/空集视为缺失——
    /// 调用方回退安装者单值路径，不因集合损坏而放宽或收紧任何边界。
    /// 非 Windows、键缺失、值缺失或无合法条目均返回 false。
    /// </summary>
    public static bool TryResolveAllowedSids([NotNullWhen(true)] out IReadOnlyList<string>? allowedSids)
    {
        allowedSids = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.LocalMachine.OpenSubKey(RegistrySubKeyName);
        var raw = key?.GetValue(AllowedSidsValueName);
        var candidates = raw switch
        {
            string[] multi => multi,
            string single when !string.IsNullOrWhiteSpace(single) => new[] { single },
            _ => null,
        };
        if (candidates is null || candidates.Length == 0)
        {
            return false;
        }

        var valid = new List<string>();
        foreach (var candidate in candidates)
        {
            var trimmed = candidate?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                _ = new SecurityIdentifier(trimmed);
            }
            catch (ArgumentException)
            {
                // 非法条目剔除（部分损坏不放大为整体失效）；全部非法时下方 Count==0 判缺失。
                continue;
            }

            valid.Add(trimmed);
        }

        if (valid.Count == 0)
        {
            return false;
        }

        allowedSids = valid;
        return true;
    }

    /// <summary>
    /// 通告服务管道随机名（#7 命名熵）：服务（LocalSystem）建首例成功后调用——先建例后通告，
    /// 预蹲者无从得知未诞生的名字。键不存在则创建（LocalSystem 有权，默认 ACL 继承自 HKLM\SOFTWARE：
    /// 管理员可写、Users 可读）；非 Windows、名字空白或写入失败（如非 LocalSystem 服务账户）返回
    /// false，调用方回退静态名重绑（fail-safe 可用性优先，宿主记 warning 观测）。
    /// </summary>
    public static bool TryAdvertiseServicePipeName(string pipeName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(pipeName))
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistrySubKeyName, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(AdvertisedServicePipeValueName, pipeName, RegistryValueKind.String);
            return true;
        }
        catch (Exception)
        {
            // 登记动作不得炸绑定路径：失败由调用方回退静态名承担。
            return false;
        }
    }

    /// <summary>
    /// 清除服务管道通告名（#7）：服务优雅停机时调用（best-effort，不抛）；崩溃/被杀的残留由
    /// 下一次启动覆盖或卸载清除，客户端对陈旧通告名按「探不到」处理（回退静态名 → 后台）。
    /// </summary>
    public static void TryClearAdvertisedServicePipeName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistrySubKeyName, writable: true);
            key?.DeleteValue(AdvertisedServicePipeValueName, throwOnMissingValue: false);
        }
        catch (Exception)
        {
            // best-effort：停机路径不得被登记清理炸掉。
        }
    }
}
