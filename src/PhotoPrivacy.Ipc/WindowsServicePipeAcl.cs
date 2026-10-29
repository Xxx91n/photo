using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务管道显式 DACL 策略（#70，决策 #82 D3）：安装脚本（scripts/install-service.ps1）
/// 在安装期把安装者用户 SID 登记到 <c>HKLM\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid</c>；
/// 服务端建管道（不加 CurrentUserOnly）后经 <c>SetNamedSecurityInfoW</c> 应用本类构建的 DACL：
/// 授予安装者 SID 读写，LocalSystem/Administrators 保留完全控制。
/// SID 缺失或无效时 fail-closed：回退 CurrentUserOnly，桌面用户仍被拒，不多授任何账户；
/// DACL 应用失败的间隙与后果同样 fail-closed（管道带 SYSTEM 令牌默认 DACL，仅 SYSTEM/管理员可连）。
/// 重装/升级会覆盖登记值，ACL 随最新安装者；不承诺多用户/跨账户（归 #1）。
/// 后台管道不经过本类，保持 CurrentUserOnly。
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsServicePipeAcl
{
    public const string RegistrySubKeyName = @"SOFTWARE\PhotoPrivacy";
    public const string InstallerSidValueName = "ServicePipeInstallerSid";

    /// <summary>
    /// 服务管道客户端（安装者账户）被授予的访问掩码。客户端按 GENERIC_READ|GENERIC_WRITE 打开，
    /// AccessCheck 映射为 FILE_GENERIC_READ|FILE_GENERIC_WRITE（含 READ_CONTROL/SYNCHRONIZE 等位），
    /// 缺任一位都会整体拒绝，故 ACE 直接写文件语义的具体掩码。
    /// 注意：不用 managed PipeAccessRights——该枚举在 Linux net10 编译面缺失（CI 实证），
    /// ACL 一律走 System.Security.AccessControl 基础机制 + 原始掩码，Linux 可编译、Windows 运行。
    /// </summary>
    internal const uint ServiceClientAccessMask = FileGenericRead | FileGenericWrite;
    private const uint FileGenericRead = 0x00120089;
    private const uint FileGenericWrite = 0x00120116;
    private const uint FileAllAccess = 0x001F01FF;

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
    /// 构建服务管道的 DACL 二进制（RawAcl.GetBinaryForm 产物）：安装者 SID 读写
    /// （FILE_GENERIC_READ|WRITE）+ LocalSystem/Administrators 完全控制（FILE_ALL_ACCESS）。
    /// </summary>
    public static byte[] BuildServicePipeDacl(SecurityIdentifier installerSid)
    {
        var dacl = new DiscretionaryAcl(false, false, 3);
        dacl.AddAccess(AccessControlType.Allow, installerSid, (int)ServiceClientAccessMask, InheritanceFlags.None, PropagationFlags.None);
        dacl.AddAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), (int)FileAllAccess, InheritanceFlags.None, PropagationFlags.None);
        dacl.AddAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), (int)FileAllAccess, InheritanceFlags.None, PropagationFlags.None);
        var binary = new byte[dacl.BinaryLength];
        dacl.GetBinaryForm(binary, 0);
        return binary;
    }
}
