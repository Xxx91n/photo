using System.Diagnostics;
using System.Runtime.Versioning;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 票20 检查点 B：ServiceManager 顶部契约类型抽出（issue 20）。
/// enum/record/接口与实现（ServiceStateProbe / ScCommandExecutor）分离，
/// ServiceManager.cs 只保留 ServiceManager 本体；单测 fake 依据本文件实现/注入。
/// </summary>
public enum ServiceCommandStatus
{
    Success,
    Skipped,
    ElevationCancelled,
    Failed
}

public sealed record ServiceCommandResult(ServiceCommandStatus Status, string Message, int? ExitCode = null)
{
    public static ServiceCommandResult Success(int? exitCode = 0, string? message = null)
    {
        return new ServiceCommandResult(ServiceCommandStatus.Success, message, exitCode);
    }

    public static ServiceCommandResult Skipped(string message)
    {
        return new ServiceCommandResult(ServiceCommandStatus.Skipped, message);
    }

    public static ServiceCommandResult ElevationCancelled(string message)
    {
        return new ServiceCommandResult(ServiceCommandStatus.ElevationCancelled, message);
    }

    public static ServiceCommandResult Failed(string message, int? exitCode = null)
    {
        return new ServiceCommandResult(ServiceCommandStatus.Failed, message, exitCode);
    }
}

public interface IScCommandExecutor
{
    ServiceCommandResult Execute(ProcessStartInfo startInfo);
}

public enum ServiceRuntimeState
{
    NotInstalled,
    Running,
    StartPending,
    PausePending,
    Paused,
    ContinuePending,
    Stopped,
    StopPending,
    Unknown
}

public interface IServiceStateProbe
{
    ServiceRuntimeState GetState(string serviceName);

    bool ServiceExists(string serviceName);
}

public sealed class ServiceStateProbe : IServiceStateProbe
{
    public ServiceRuntimeState GetState(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceRuntimeState.NotInstalled;
        }

        return GetStateWindows(serviceName);
    }

    public bool ServiceExists(string serviceName)
    {
        return GetState(serviceName) != ServiceRuntimeState.NotInstalled;
    }

    [SupportedOSPlatform("windows")]
    private static ServiceRuntimeState GetStateWindows(string serviceName)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(serviceName);
            _ = sc.Status;
            return sc.Status switch
            {
                System.ServiceProcess.ServiceControllerStatus.Running => ServiceRuntimeState.Running,
                System.ServiceProcess.ServiceControllerStatus.StartPending => ServiceRuntimeState.StartPending,
                System.ServiceProcess.ServiceControllerStatus.PausePending => ServiceRuntimeState.PausePending,
                System.ServiceProcess.ServiceControllerStatus.Paused => ServiceRuntimeState.Paused,
                System.ServiceProcess.ServiceControllerStatus.ContinuePending => ServiceRuntimeState.ContinuePending,
                System.ServiceProcess.ServiceControllerStatus.Stopped => ServiceRuntimeState.Stopped,
                System.ServiceProcess.ServiceControllerStatus.StopPending => ServiceRuntimeState.StopPending,
                _ => ServiceRuntimeState.Unknown
            };
        }
        catch
        {
            return ServiceRuntimeState.NotInstalled;
        }
    }
}

public sealed class ScCommandExecutor : IScCommandExecutor
{
    private const int CommandTimeoutMilliseconds = 60_000;

    public ServiceCommandResult Execute(System.Diagnostics.ProcessStartInfo startInfo)
    {
        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return ServiceCommandResult.Failed("failed to start sc.exe");
        }

        Task<string>? stdoutTask = null;
        Task<string>? stderrTask = null;
        if (!startInfo.UseShellExecute)
        {
            stdoutTask = process.StandardOutput.ReadToEndAsync();
            stderrTask = process.StandardError.ReadToEndAsync();
        }

        // 票10：同步 WaitForExit 必须有界，超时即杀整树，避免 sc.exe 卡死永久阻塞服务管理操作。
        if (!process.WaitForExit(CommandTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore kill race
            }

            return ServiceCommandResult.Failed($"sc.exe did not exit within {CommandTimeoutMilliseconds} ms and was killed");
        }

        var stdout = stdoutTask?.GetAwaiter().GetResult() ?? string.Empty;
        var stderr = stderrTask?.GetAwaiter().GetResult() ?? string.Empty;

        if (process.ExitCode == 0)
        {
            return ServiceCommandResult.Success(process.ExitCode);
        }

        var message = !string.IsNullOrWhiteSpace(stderr)
            ? stderr.Trim()
            : !string.IsNullOrWhiteSpace(stdout)
                ? stdout.Trim()
                : $"sc.exe exited with code {process.ExitCode}";

        return ServiceCommandResult.Failed(message, process.ExitCode);
    }
}
