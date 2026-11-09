#if PP_WINDOWS_HOST
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 服务模式管道原生路径（#70 起，#1 扩展授权集合，#7 按位去掉 0x4）：服务端以显式 DACL 走
/// managed 正典 <see cref="NamedPipeServerStreamAcl.Create"/>（内部即带 SECURITY_ATTRIBUTES 的
/// CreateNamedPipe）；客户端经 <see cref="CreateClientStream"/> 以细粒度掩码打开（按位去掉 0x4 后的
/// DACL 不再授 FILE_CREATE_PIPE_INSTANCE，GENERIC 打开会被整体拒绝）。
/// public 供行为测试直接驱动 DACL 组装、建例与客户端打开；仅 Windows 宿主编译
/// （csproj PP_WINDOWS_HOST）：PipeAccessRights 在 Linux net10 编译面缺失（CI 实证 CS0246）。
/// 本文件在 Windows 本体编译——win-x64 发布构建（release.yml windows runner /
/// 发版机）是它的编译门禁，CI test job（ubuntu）不覆盖，见 PR Merge Danger。
/// </summary>
public static class WindowsPipeNative
{
    private const int MaxServerInstances = 254;

    /// <summary>
    /// 组装服务管道 DACL：每个授权 SID（#70 安装者保底，#1 opt-in 可含额外授权用户）各一条
    /// 读写 ACE + LocalSystem/Administrators 完全控制。掩码用原始位强转
    /// （PipeAccessRights.ReadWrite 刻意不含 FILE_CREATE_PIPE_INSTANCE，
    /// 不能用它表达客户端的 GENERIC_READ|WRITE 映射）。
    /// DACL 必须给创建者（服务账户）保留 FILE_CREATE_PIPE_INSTANCE（0x4）：
    /// 建第 2..N 个实例时内核按 DACL 对创建者令牌做该项检查（38142f9b 的
    /// UnauthorizedAccessException 即旧 WorldSid-ReadWrite DACL 缺此位所致——见
    /// dotnet/runtime PipeAccessRights.cs 的 Write 定义注释）。
    /// </summary>
    public static PipeSecurity BuildServicePipeSecurity(IReadOnlyList<SecurityIdentifier> allowedSids)
    {
        var security = new PipeSecurity();
        foreach (var allowedSid in allowedSids)
        {
            security.AddAccessRule(new PipeAccessRule(
                allowedSid, (PipeAccessRights)WindowsServicePipeAcl.ServiceClientAccessMask, AccessControlType.Allow));
        }

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
    /// 只放行创建者，与 #70/#1 的跨账户 DACL 互斥）。
    /// </summary>
    public static NamedPipeServerStream CreateAclServerInstance(
        string pipeName, bool isFirstInstance, IReadOnlyList<SecurityIdentifier> allowedSids)
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
            pipeSecurity: BuildServicePipeSecurity(allowedSids));
    }

    /// <summary>
    /// 以 <see cref="WindowsServicePipeAcl.ServiceClientAccessMask"/> 的细粒度掩码构造服务管道
    /// 客户端（#7 / CWE-732）：<see cref="NamedPipeClientStream"/> 的 PipeAccessRights 重载直接
    /// 向 CreateFile 传具体权限位，不走 GENERIC 映射（GENERIC_WRITE 会带入 FILE_APPEND_DATA = 0x4
    /// = FILE_CREATE_PIPE_INSTANCE，按位去掉 0x4 后的 DACL 会整体拒绝）。掩码含 ReadData|WriteData → 方向
    /// InOut；重试语义（ERROR_PIPE_BUSY/FILE_NOT_FOUND）与 GENERIC 路径同实现（ConnectAsync 内部循环）。
    /// 冒充级别固定 Identification：防伪服务端（预蹲者）对客户端发起 ImpersonateNamedPipeClient
    /// 冒充（tokio 客户端默认同姿态）；产品服务端从不冒充客户端，收紧不改变合法链路。
    /// </summary>
    public static NamedPipeClientStream CreateClientStream(string pipeName)
    {
        return new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            desiredAccessRights: (PipeAccessRights)WindowsServicePipeAcl.ServiceClientAccessMask,
            options: PipeOptions.Asynchronous,
            impersonationLevel: TokenImpersonationLevel.Identification,
            inheritability: HandleInheritability.None);
    }
}
#endif
