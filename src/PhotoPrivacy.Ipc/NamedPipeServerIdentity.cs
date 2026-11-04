using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// 服务端进程身份的期望基准（#7 残余①：客户端认证服务端）。期望 PID 优先（UI 拉起的子进程），
/// 镜像路径作回退（UI 重启但后台 Worker 仍在的场景）。
/// </summary>
public sealed class ServerProcessIdentityExpectation
{
    public ServerProcessIdentityExpectation(int? expectedProcessId = null, string? expectedImagePath = null)
    {
        if (expectedProcessId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedProcessId), "PID 必须为正整数");
        }

        ExpectedProcessId = expectedProcessId;
        ExpectedImagePath = string.IsNullOrWhiteSpace(expectedImagePath)
            ? null
            : Path.GetFullPath(expectedImagePath);
    }

    /// <summary>UI 本次拉起的 Worker PID；null 表示未知（只走镜像回退）。</summary>
    public int? ExpectedProcessId { get; }

    /// <summary>期望的 Worker 可执行文件全路径；null 表示无回退基准。</summary>
    public string? ExpectedImagePath { get; }
}

/// <summary>
/// 命名管道服务端进程身份核验（#7 残余①，与 Rust <c>ui/crates/photoprivacy-ipc/src/verify.rs</c>
/// 同构）。后台管道同用户进程可抢建同名管道冒充服务端；客户端连上后先核验对端进程身份，
/// 再写第一笔业务请求。
///
/// 事实源：<c>GetNamedPipeServerProcessId</c> 支持 Vista+，客户端句柄可用且无需特权
/// （Chromium mojo 的 <c>VerifyServerPrivilege</c> 走同一路径）。.NET 10 没有取服务端 PID 的
/// 托管 API，故 P/Invoke。
///
/// 上限：PID 快照核验存在固有的 PID 复用窗口；镜像是 speed bump 而非不可伪造凭据；
/// 同用户恶意软件可注入客户端本身（同用户本就是无隔离级差的信任域）。签名核验未入首版。
/// 与 Rust 产品客户端的差异：本类没有连接编排循环，核验期间进程消失不重试，
/// 由调用方按连接失败上抛（Rust 侧在连接预算内重试，见 ui/crates/photoprivacy-ipc/src/client.rs）。
/// 本类不依赖 Windows 专有托管 API，跨平台可编译；非 Windows 上调用一律返回 false。
/// 与 <see cref="WindowsPipeNative"/> 不同，本文件在全部宿主编译，ubuntu CI 覆盖其编译面。
/// </summary>
public static class NamedPipeServerIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessNameWin32 = 0;
    private const int MaxImagePathChars = 1024;

#pragma warning disable CA5392 // kernel32 为系统库，Windows 上不存在 DLL 劫持面
    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeServerProcessId", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] exeName,
        ref uint size);
#pragma warning restore CA5392

    /// <summary>取已连接管道客户端的服务端 PID（仅 Windows；失败时 <paramref name="processId"/> 为 0）。</summary>
    public static bool TryGetServerProcessId(SafePipeHandle pipe, out int processId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        processId = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!GetNamedPipeServerProcessId(pipe, out var serverProcessId))
        {
            return false;
        }

        processId = unchecked((int)serverProcessId);
        return true;
    }

    /// <summary>取进程镜像全路径（仅 Windows；<c>PROCESS_QUERY_LIMITED_INFORMATION</c>，不需读内存）。</summary>
    public static bool TryGetProcessImagePath(int processId, out string? imagePath)
    {
        imagePath = null;
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return false;
        }

        using var process = OpenProcess(ProcessQueryLimitedInformation, false, unchecked((uint)processId));
        if (process.IsInvalid)
        {
            return false;
        }

        var buffer = new char[MaxImagePathChars];
        var size = (uint)buffer.Length;
        if (!QueryFullProcessImageName(process, ProcessNameWin32, buffer, ref size))
        {
            return false;
        }

        imagePath = new string(buffer, 0, (int)size);
        return true;
    }

    /// <summary>
    /// 判定对端是否为期望的 Worker：PID 直配 → 镜像路径回退 → 失败（fail-closed）。
    /// <paramref name="failure"/> 在失败时给出去因（PID 不符 / 进程已退出 / 查询失败）。
    /// </summary>
    public static bool Verify(
        SafePipeHandle pipe,
        ServerProcessIdentityExpectation expectation,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(expectation);

        if (!TryGetServerProcessId(pipe, out var pid))
        {
            failure = $"服务端 PID 查询失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        if (expectation.ExpectedProcessId == pid)
        {
            failure = string.Empty;
            return true;
        }

        if (expectation.ExpectedImagePath is null)
        {
            failure = expectation.ExpectedProcessId is { } expected
                ? $"服务端 PID {pid} 与 UI 拉起的 Worker PID {expected} 不符"
                : $"服务端 PID {pid} 无核验基准";
            return false;
        }

        if (!TryGetProcessImagePath(pid, out var actual))
        {
            // OpenProcess 失败即 fail-closed（Chromium 同姿态）：进程已退出或身份不可查都拒。
            failure = $"服务端进程 {pid} 镜像查询失败";
            return false;
        }

        if (string.Equals(actual, expectation.ExpectedImagePath, StringComparison.OrdinalIgnoreCase))
        {
            failure = string.Empty;
            return true;
        }

        failure = $"服务端 PID {pid} 的镜像 {actual} 不是期望的 {expectation.ExpectedImagePath}";
        return false;
    }
}
