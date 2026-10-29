using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务管道显式 DACL 策略（#70，决策 #82 D3）：安装脚本（scripts/install-service.ps1）
/// 在安装期把安装者用户 SID 登记到 <c>HKLM\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid</c>；
/// 服务端建管道时授予该 SID 读写（并设 PIPE_REJECT_REMOTE_CLIENTS），SYSTEM/Administrators
/// 保留完全控制。SID 缺失或无效时 fail-closed：回退 CurrentUserOnly，桌面用户仍被拒，不多授任何账户。
/// 重装/升级会覆盖登记值，ACL 随最新安装者；不承诺多用户/跨账户（归 #1）。
/// 后台管道不经过本类，保持 CurrentUserOnly。
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsServicePipeAcl
{
    public const string RegistrySubKeyName = @"SOFTWARE\PhotoPrivacy";
    public const string InstallerSidValueName = "ServicePipeInstallerSid";

    /// <summary>
    /// 服务管道客户端（安装者账户）被授予的最小权限集。客户端按 GENERIC_READ|GENERIC_WRITE 打开，
    /// AccessCheck 按文件系统语义映射：除读写数据外还需 ReadPermissions（=READ_CONTROL）、
    /// CreateNewInstance（=FILE_APPEND_DATA 位）与 Synchronize，缺任一位都会整体拒绝。
    /// </summary>
    internal const PipeAccessRights ServiceClientRights =
        PipeAccessRights.ReadWrite
        | PipeAccessRights.ReadPermissions
        | PipeAccessRights.CreateNewInstance
        | PipeAccessRights.Synchronize;

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
    /// 构建服务管道的二进制安全描述符：安装者 SID 读写 + LocalSystem/Administrators 完全控制。
    /// </summary>
    public static byte[] BuildServicePipeSecurityDescriptor(SecurityIdentifier installerSid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(installerSid, ServiceClientRights, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security.GetSecurityDescriptorBinaryForm();
    }
}
