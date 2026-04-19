using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
#if WINDOWS
using System.ServiceProcess;
#endif

namespace PhotoPrivacy.Ui;

public enum ServiceCommandStatus
{
    Success,
    Skipped,
    ElevationCancelled,
    Failed
}

public sealed record ServiceCommandResult(ServiceCommandStatus Status, string Message, int? ExitCode = null)
{
    public static ServiceCommandResult Success(int? exitCode = 0)
    {
        return new ServiceCommandResult(ServiceCommandStatus.Success, "ok", exitCode);
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

public sealed class ScCommandExecutor : IScCommandExecutor
{
    public ServiceCommandResult Execute(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return ServiceCommandResult.Failed("failed to start sc.exe");
        }

        process.WaitForExit();
        return process.ExitCode == 0
            ? ServiceCommandResult.Success(process.ExitCode)
            : ServiceCommandResult.Failed($"sc.exe exited with code {process.ExitCode}.", process.ExitCode);
    }
}

public sealed class ServiceManager
{
    private const string ServiceName = "PhotoPrivacyCleaner";
    private readonly IScCommandExecutor _executor;

    public ServiceManager()
        : this(new ScCommandExecutor())
    {
    }

    public ServiceManager(IScCommandExecutor executor)
    {
        _executor = executor;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string GetStatusText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Linux/macOS: use systemd service management";
        }

#if WINDOWS
        using var sc = new ServiceController(ServiceName);
        try
        {
            _ = sc.Status;
            return sc.Status.ToString();
        }
        catch
        {
            return "NotInstalled";
        }
#else
        return "Unsupported";
#endif
    }

    public ServiceCommandResult Install(string exePath, string configPath)
    {
        var args = BuildInstallArguments(exePath, configPath);
        return RunSc(args, requireAdmin: true, forceElevation: null);
    }

    public ServiceCommandResult Install(string exePath, string configPath, bool forceElevation)
    {
        var args = BuildInstallArguments(exePath, configPath);
        return RunSc(args, requireAdmin: true, forceElevation: forceElevation);
    }

    public static string BuildInstallArguments(string exePath, string configPath)
    {
        return $"create {ServiceName} binPath= \"\"{exePath}\" --mode service --config \"{configPath}\"\" start= auto";
    }

    public ServiceCommandResult Uninstall()
    {
        return RunSc($"delete {ServiceName}", requireAdmin: true, forceElevation: null);
    }

    public ServiceCommandResult Uninstall(bool forceElevation)
    {
        return RunSc($"delete {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
    }

    public ServiceCommandResult Start()
    {
        return RunSc($"start {ServiceName}", requireAdmin: true, forceElevation: null);
    }

    public ServiceCommandResult Start(bool forceElevation)
    {
        return RunSc($"start {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
    }

    public ServiceCommandResult Stop()
    {
        return RunSc($"stop {ServiceName}", requireAdmin: true, forceElevation: null);
    }

    public ServiceCommandResult Stop(bool forceElevation)
    {
        return RunSc($"stop {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
    }

    public static string BuildRunScVerb(bool requireAdmin, bool isAdministrator)
    {
        if (!requireAdmin)
        {
            return string.Empty;
        }

        return isAdministrator ? string.Empty : "runas";
    }

    public static ProcessStartInfo BuildRunScStartInfo(string args, bool requireAdmin, bool isAdministrator, bool? forceElevation)
    {
        var needElevation = forceElevation ?? (requireAdmin && OperatingSystem.IsWindows() && !isAdministrator);
        return new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = true,
            Verb = BuildRunScVerb(requireAdmin, isAdministrator: !needElevation)
        };
    }

    public static bool IsElevationCancelled(Exception exception)
    {
        return exception is Win32Exception { NativeErrorCode: 1223 };
    }

    private ServiceCommandResult RunSc(string args, bool requireAdmin, bool? forceElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceCommandResult.Skipped("service manager is windows-only");
        }

        try
        {
            var startInfo = BuildRunScStartInfo(args, requireAdmin, IsAdministrator(), forceElevation);
            return _executor.Execute(startInfo);
        }
        catch (Exception ex) when (IsElevationCancelled(ex))
        {
            return ServiceCommandResult.ElevationCancelled("operation cancelled by user");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed(ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
#if WINDOWS
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#else
        return false;
#endif
    }
}
