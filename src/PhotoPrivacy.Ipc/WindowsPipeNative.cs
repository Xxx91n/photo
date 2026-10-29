using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 原生安全描述符封装：把显式 DACL 应用到已存在的命名管道对象（#70 服务模式专用）。
/// 刻意不复活 managed <c>NamedPipeServerStreamAcl.Create</c>/CreateNamedPipeW 自建路径——
/// 前者在单文件发布上下文曾抛 UnauthorizedAccessException 并按 accept 逐次泄漏管道实例
/// 直到 254 上限（38142f9b）；后者与 <c>NamedPipeServerStream.CreateFromPipe</c> 在 Linux
/// net10 编译面缺失（CI 实证），本仓 CI test job 只跑 ubuntu，全部代码必须可跨平台编译。
/// 管道实例本身仍由 managed 构造器创建（remote clients 默认被拒），DACL 经
/// <c>SetNamedSecurityInfoW</c> 事后应用；创建与应用间隙管道带 SYSTEM 令牌默认 DACL，
/// 只放行 SYSTEM/Administrators——窗口 fail-closed。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsPipeNative
{
    private const uint SeKernelObject = 6;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ErrorSuccess = 0;

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint SetNamedSecurityInfoW(
        string pObjectName,
        uint objectType,
        uint securityInfo,
        IntPtr pSidOwner,
        IntPtr pSidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    /// <summary>
    /// 把 <paramref name="daclBinary"/>（ACL 二进制，RawAcl.GetBinaryForm 产物）作为 DACL
    /// 应用到命名管道对象。返回是否成功（SetNamedSecurityInfoW == ERROR_SUCCESS）。
    /// </summary>
    public static bool ApplyPipeDacl(string pipeName, byte[] daclBinary)
    {
        IntPtr pDacl = IntPtr.Zero;
        try
        {
            pDacl = Marshal.AllocHGlobal(daclBinary.Length);
            Marshal.Copy(daclBinary, 0, pDacl, daclBinary.Length);
            return SetNamedSecurityInfoW(
                pObjectName: $@"\\.\pipe\{pipeName}",
                objectType: SeKernelObject,
                securityInfo: DaclSecurityInformation,
                pSidOwner: IntPtr.Zero,
                pSidGroup: IntPtr.Zero,
                pDacl: pDacl,
                pSacl: IntPtr.Zero) == ErrorSuccess;
        }
        finally
        {
            if (pDacl != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pDacl);
            }
        }
    }
}
