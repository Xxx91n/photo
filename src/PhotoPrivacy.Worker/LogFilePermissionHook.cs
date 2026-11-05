using System.IO;
using System.Text;
using Serilog.Sinks.File;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 运行期日志文件权限收敛（#188，macOS #167 R1 同口径）：Serilog File sink 经
/// <c>File.Open(OpenOrCreate)</c> 建文件，CI Ubuntu runner 实证即便进程 umask 已是 0027
/// （ExecStart 壳兜底 + unit UMask=0027），日志仍落 0666——创建路径不遵循 umask。
/// 这里在每次文件打开（含日滚 / 大小滚新文件）时显式 <see cref="File.SetUnixFileMode"/>
/// 收到 0640（属主读写、组只读、其他无），保证 GUI 用户（socket 组，非数据组）读不到
/// 请求 URL / 文件名 / 错误栈。Windows 无 Unix 文件模式，跳过（命名管道 / NTFS ACL 另管）。
/// 关联 issue #188；落点口径见 docs/agents/ipc-contract.md Linux 段。
/// </summary>
public sealed class LogFilePermissionHook : FileLifecycleHooks
{
    /// <summary>文件打开即收紧 0640；返回原流不改写行为，仅施加权限。</summary>
    public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        return underlyingStream;
    }
}
