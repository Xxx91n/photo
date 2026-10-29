using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using Microsoft.Win32;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务管道显式 DACL 策略（#70，决策 #82 D3）：安装脚本（scripts/install-service.ps1）
/// 在安装期把安装者用户 SID 登记到 <c>HKLM\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid</c>；
/// 服务端建管道时经 <see cref="WindowsPipeNative.CreateAclServerInstance"/> 携带显式 DACL：
/// 授予安装者 SID 读写，SYSTEM/Administrators 保留完全控制。
/// SID 缺失或无效时 fail-closed：回退 CurrentUserOnly，桌面用户仍被拒，不多授任何账户。
/// 重装/升级会覆盖登记值，ACL 随最新安装者；不承诺多用户/跨账户（归 #1）。
/// 后台管道不经过本类，保持 CurrentUserOnly。
/// 本类全平台可编译（CI test job 只跑 ubuntu）：注册表读取 + SID 校验是平台无关 API；
/// DACL 组装在 WindowsPipeNative（PP_WINDOWS_HOST 分区）——managed PipeAccessRights 在
/// Linux net10 编译面缺失（CI 实证），不得在全平台编译的代码里引用。
/// </summary>
public static class WindowsServicePipeAcl
{
    public const string RegistrySubKeyName = @"SOFTWARE\PhotoPrivacy";
    public const string InstallerSidValueName = "ServicePipeInstallerSid";

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
}
