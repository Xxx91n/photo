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
    /// 服务管道客户端（安装者账户）被授予的访问掩码（原始位，经强转进 PipeAccessRights）。
    /// 客户端按 GENERIC_READ|GENERIC_WRITE 打开，AccessCheck 映射为
    /// FILE_GENERIC_READ|FILE_GENERIC_WRITE（含 READ_CONTROL/SYNCHRONIZE 等位），缺任一位都会整体拒绝。
    /// </summary>
    internal const uint ServiceClientAccessMask = FileGenericRead | FileGenericWrite;
    internal const uint FileAllAccess = 0x001F01FF;
    private const uint FileGenericRead = 0x00120089;
    private const uint FileGenericWrite = 0x00120116;

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
    // Design(#1): 服务管道授权集合 opt-in——优先 MULTI_SZ 全集（安装者+显式授权用户），无该值回退安装者单值（默认不扩权），非法条目剔除、空集视为缺失；未列入集合的账户仍被拒。
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
}
