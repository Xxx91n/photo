using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// libc P/Invoke 封装（Linux glibc 与 macOS libSystem 的结构布局一致）。
/// 仅在非 Windows 上被调用；每个入口自带异常降级，读取失败一律返回 false / 空集（fail-closed）。
/// </summary>
internal static class UnixNativeInterop
{
    // Linux: SOL_SOCKET = 1, SO_PEERCRED = 17
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    // 组名解析上限（防病态数据无限循环）
    private const int MaxGroupMembers = 4096;

    /// <summary>Linux struct ucred { pid_t pid; uid_t uid; gid_t gid; }</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Ucred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

#pragma warning disable CA5392 // libc / libSystem 为系统库，Unix 上不存在 DLL 劫持面
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSockOpt(int sockfd, int level, int optname, ref Ucred optval, ref uint optlen);

    [DllImport("libc", EntryPoint = "getpeereid", SetLastError = true)]
    private static extern int GetPeerEid(int sockfd, out uint euid, out uint egid);

    [DllImport("libc", EntryPoint = "getgrnam", SetLastError = true)]
    private static extern IntPtr GetGrNam([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libc", EntryPoint = "getpwnam", SetLastError = true)]
    private static extern IntPtr GetPwNam([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
#pragma warning restore CA5392

    /// <summary>本进程有效 uid（geteuid）。</summary>
    internal static bool TryGetOwnUid(out uint uid)
    {
        uid = 0;
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            uid = GetEuid();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 已连接 socket 的对端 uid。
    /// Linux 走 SO_PEERCRED（connect 时刻由内核固化，不可伪造）；
    /// macOS 走 getpeereid（LOCAL_PEERCRED 的 libc 封装，仅 euid/egid，无 pid）。
    /// </summary>
    internal static bool TryGetPeerUid(Socket socket, out uint uid)
    {
        uid = 0;
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var fd = socket.Handle.ToInt32();
            if (OperatingSystem.IsMacOS())
            {
                return GetPeerEid(fd, out uid, out _) == 0;
            }

            var cred = default(Ucred);
            var len = (uint)Marshal.SizeOf<Ucred>();
            if (GetSockOpt(fd, SolSocket, SoPeerCred, ref cred, ref len) != 0)
            {
                return false;
            }

            uid = cred.Uid;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 组名 → 成员用户名列表（getgrnam）。
    /// struct group { char* gr_name; char* gr_passwd; gid_t gr_gid; char** gr_mem; }
    /// 64 位布局下 gr_mem 位于偏移 24（8 + 8 + 4 + 4 对齐）。
    /// </summary>
    internal static IReadOnlyList<string> ReadGroupMemberNames(string groupName)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(groupName))
        {
            return [];
        }

        try
        {
            var groupPtr = GetGrNam(groupName);
            if (groupPtr == IntPtr.Zero)
            {
                return [];
            }

            var membersPtr = Marshal.ReadIntPtr(groupPtr, 3 * IntPtr.Size);
            if (membersPtr == IntPtr.Zero)
            {
                return [];
            }

            var names = new List<string>();
            for (var i = 0; i < MaxGroupMembers; i++)
            {
                var entry = Marshal.ReadIntPtr(membersPtr, i * IntPtr.Size);
                if (entry == IntPtr.Zero)
                {
                    break;
                }

                var name = Marshal.PtrToStringUTF8(entry);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch (EntryPointNotFoundException)
        {
            return [];
        }
    }

    /// <summary>
    /// 用户名 → uid（getpwnam）。
    /// struct passwd { char* pw_name; char* pw_passwd; uid_t pw_uid; gid_t pw_gid; ... }
    /// 64 位布局下 pw_uid 位于偏移 16（8 + 8），glibc 与 XNU 一致。
    /// </summary>
    internal static bool TryGetUidByName(string userName, out uint uid)
    {
        uid = 0;
        if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        try
        {
            var pwPtr = GetPwNam(userName);
            if (pwPtr == IntPtr.Zero)
            {
                return false;
            }

            uid = unchecked((uint)Marshal.ReadInt32(pwPtr, 2 * IntPtr.Size));
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
