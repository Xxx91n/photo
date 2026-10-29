#if PP_WINDOWS_HOST
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务模式管道建例（#70 专用）：以显式 DACL 走 managed 正典路径
/// <see cref="NamedPipeServerStreamAcl.Create"/>（内部即带 SECURITY_ATTRIBUTES 的
/// CreateNamedPipe）。public 供行为测试直接驱动 DACL 组装与建例；仅 Windows 宿主编译
/// （csproj PP_WINDOWS_HOST）：PipeAccessRights 在 Linux net10 编译面缺失（CI 实证 CS0246）。
/// 本文件在 Windows 本体编译——win-x64 发布构建（release.yml windows runner /
/// 发版机）是它的编译门禁，CI test job（ubuntu）不覆盖，见 PR Merge Danger。
/// </summary>
public static class WindowsPipeNative
{
    private const int MaxServerInstances = 254;

    /// <summary>
    /// 组装服务管道 DACL：安装者 SID 读写 + LocalSystem/Administrators 完全控制。
    /// 掩码用原始位强转（PipeAccessRights.ReadWrite 刻意不含 FILE_CREATE_PIPE_INSTANCE，
    /// 不能用它表达客户端的 GENERIC_READ|WRITE 映射）。
    /// DACL 必须给创建者（服务账户）保留 FILE_CREATE_PIPE_INSTANCE（0x4）：
    /// 建第 2..N 个实例时内核按 DACL 对创建者令牌做该项检查（38142f9b 的
    /// UnauthorizedAccessException 即旧 WorldSid-ReadWrite DACL 缺此位所致——见
    /// dotnet/runtime PipeAccessRights.cs 的 Write 定义注释）。
    /// </summary>
    public static PipeSecurity BuildServicePipeSecurity(SecurityIdentifier installerSid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            installerSid, (PipeAccessRights)WindowsServicePipeAcl.ServiceClientAccessMask, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            (PipeAccessRights)WindowsServicePipeAcl.FileAllAccess, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            (PipeAccessRights)WindowsServicePipeAcl.FileAllAccess, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// 以 <see cref="BuildServicePipeSecurity"/> 的 DACL 建一个管道服务端实例。
    /// 首实例带 FirstPipeInstance；不传 CurrentUserOnly（它会忽略传入的 pipeSecurity 并
    /// 只放行创建者，与 #70 的跨账户 DACL 互斥）。
    /// </summary>
    public static NamedPipeServerStream CreateAclServerInstance(
        string pipeName, bool isFirstInstance, SecurityIdentifier installerSid)
    {
        var options = PipeOptions.Asynchronous
            | (isFirstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: MaxServerInstances,
            transmissionMode: PipeTransmissionMode.Byte,
            options: options,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: BuildServicePipeSecurity(installerSid));
    }
}
#endif
