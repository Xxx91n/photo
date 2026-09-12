using System.Diagnostics;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 票20 检查点 B：SystemdStateProbe 与 LaunchdStateProbe 重复的 RunProcess 进程探针合并（issue 20）。
/// 语义与原实现逐字一致：3s 超时、捕获 stdout/stderr、UseShellExecute=false、返回 (exitCode, trimmed stdout)。
/// 进程未启动返回 (-1, empty)；超时后读取可能抛异常，由调用方的 catch 兜底（与原实现相同）。
/// </summary>
public static class ServiceProbeProcess
{
    public static (int exitCode, string output) Run(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return (-1, string.Empty);
        process.WaitForExit(3000);
        return (process.ExitCode, process.StandardOutput.ReadToEnd().Trim());
    }
}
