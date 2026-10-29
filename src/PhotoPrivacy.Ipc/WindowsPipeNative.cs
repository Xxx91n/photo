#if PP_WINDOWS_HOST
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows 原生 CreateNamedPipeW 封装：以显式 DACL 创建管道服务端实例（#70 服务模式专用）。
/// 走建例时带 SECURITY_ATTRIBUTES 的正典路径（MSDN：命名管道安全描述符在 CreateNamedPipe 时指定）；
/// 事后改 DACL 的两条路都不通——SetNamedSecurityInfoW 不支持按名操作命名管道（Securable Objects
/// 表只给句柄版 SetSecurityInfo），而 managed 服务端句柄无 WRITE_DAC，SetKernelObjectSecurity 会被拒。
/// 刻意不复活 managed <c>NamedPipeServerStreamAcl.Create</c>：该 API 在本仓单文件发布上下文曾抛
/// UnauthorizedAccessException 并按 accept 逐次泄漏管道实例直到 254 实例上限（38142f9b），
/// 而服务 exe 恰是单文件发布。本文件仅在 Windows 宿主编译（csproj PP_WINDOWS_HOST）。
/// </summary>
internal static class WindowsPipeNative
{
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const int MaxServerInstances = 254;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafePipeHandle CreateNamedPipeW(
        string lpName,
        uint dwOpenMode,
        uint dwPipeMode,
        uint nMaxInstances,
        uint nOutBufferSize,
        uint nInBufferSize,
        uint nDefaultTimeOut,
        ref SECURITY_ATTRIBUTES lpSecurityAttributes);

    /// <summary>
    /// 以 <paramref name="securityDescriptorBinary"/>（self-relative 二进制 SD）为 DACL 创建一个
    /// 管道服务端实例。等价于 <c>NamedPipeServerStream(FirstPipeInstance)</c> + 显式 DACL：
    /// 首实例带 FILE_FLAG_FIRST_PIPE_INSTANCE，字节模式，PIPE_REJECT_REMOTE_CLIENTS 拒绝远程连接
    /// （DAC 放宽到桌面账户后必须显式关掉网络面）。建例失败即抛——DACL 建例路径没有
    /// 「建好但没应用」的静默降级形态。
    /// </summary>
    public static NamedPipeServerStream CreateAclServerInstance(
        string pipeName, bool isFirstInstance, byte[] securityDescriptorBinary)
    {
        IntPtr sdBuffer = IntPtr.Zero;
        try
        {
            sdBuffer = Marshal.AllocHGlobal(securityDescriptorBinary.Length);
            Marshal.Copy(securityDescriptorBinary, 0, sdBuffer, securityDescriptorBinary.Length);
            var securityAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = sdBuffer,
                bInheritHandle = false,
            };

            var openMode = PipeAccessDuplex | FileFlagOverlapped
                | (isFirstInstance ? FileFlagFirstPipeInstance : 0);
            var handle = CreateNamedPipeW(
                lpName: $@"\\.\pipe\{pipeName}",
                dwOpenMode: openMode,
                // 字节模式 + 等待 + 拒绝远程客户端（与 managed 默认管道模式对齐）。
                dwPipeMode: PipeRejectRemoteClients,
                nMaxInstances: (uint)MaxServerInstances,
                nOutBufferSize: 0,
                nInBufferSize: 0,
                nDefaultTimeOut: 0,
                lpSecurityAttributes: ref securityAttributes);
            if (handle.IsInvalid)
            {
                // 先取错误码再 Dispose：CloseHandle 会覆盖 last error。
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, $"CreateNamedPipeW failed for pipe {pipeName}");
            }

            try
            {
                return NamedPipeServerStream.CreateFromPipe(
                    handle,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: MaxServerInstances,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0);
            }
            catch
            {
                // CreateFromPipe 接管句柄前抛出时不得泄漏内核句柄。
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            // CreateNamedPipeW 在调用期间复制 SD，返回后即可释放。
            if (sdBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sdBuffer);
            }
        }
    }
}
#endif
