using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.Versioning;

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
    public static ServiceCommandResult Success(int? exitCode = 0, string message = "操作成功")
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
            using var sc = new ServiceController(serviceName);
            _ = sc.Status;
            return sc.Status switch
            {
                ServiceControllerStatus.Running => ServiceRuntimeState.Running,
                ServiceControllerStatus.StartPending => ServiceRuntimeState.StartPending,
                ServiceControllerStatus.PausePending => ServiceRuntimeState.PausePending,
                ServiceControllerStatus.Paused => ServiceRuntimeState.Paused,
                ServiceControllerStatus.ContinuePending => ServiceRuntimeState.ContinuePending,
                ServiceControllerStatus.Stopped => ServiceRuntimeState.Stopped,
                ServiceControllerStatus.StopPending => ServiceRuntimeState.StopPending,
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
    public ServiceCommandResult Execute(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return ServiceCommandResult.Failed("failed to start sc.exe");
        }

        string stdout = string.Empty;
        string stderr = string.Empty;
        if (!startInfo.UseShellExecute)
        {
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
        }

        process.WaitForExit();

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

public sealed class ServiceManager
{
    private const string ServiceName = "PhotoPrivacyCleaner";
    private readonly IScCommandExecutor _executor;
    private readonly IServiceStateProbe _stateProbe;

    public ServiceManager()
        : this(new ScCommandExecutor(), new ServiceStateProbe())
    {
    }

    public ServiceManager(IScCommandExecutor executor)
        : this(executor, new ServiceStateProbe())
    {
    }

    public ServiceManager(IScCommandExecutor executor, IServiceStateProbe stateProbe)
    {
        _executor = executor;
        _stateProbe = stateProbe;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool IsInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return _stateProbe.ServiceExists(ServiceName);
    }

    public ServiceRuntimeState GetRuntimeState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceRuntimeState.NotInstalled;
        }

        return _stateProbe.GetState(ServiceName);
    }

    public string GetStatusText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Linux/macOS: use systemd service management";
        }

        var state = _stateProbe.GetState(ServiceName);
        return state == ServiceRuntimeState.NotInstalled ? "NotInstalled" : state.ToString();
    }

    public ServiceCommandResult Install(string exePath, string configPath)
    {
        return InstallCore(exePath, configPath, forceElevation: null);
    }

    public ServiceCommandResult Install(string exePath, string configPath, bool forceElevation)
    {
        return InstallCore(exePath, configPath, forceElevation: forceElevation);
    }

    private ServiceCommandResult InstallCore(string exePath, string configPath, bool? forceElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceCommandResult.Skipped("Linux/macOS 请使用 systemd 管理服务");
        }

        var precheck = EnsureRemovedBeforeInstall(forceElevation);
        if (precheck is { Status: ServiceCommandStatus.Failed })
        {
            return precheck;
        }

        var createArgs = BuildInstallArguments(exePath, configPath);
        var create = RunSc(createArgs, requireAdmin: true, forceElevation: forceElevation);
        if (create.ExitCode == 1073)
        {
            var retryRemove = EnsureRemovedBeforeInstall(forceElevation);
            if (retryRemove is { Status: ServiceCommandStatus.Failed })
            {
                return retryRemove;
            }

            create = RunSc(createArgs, requireAdmin: true, forceElevation: forceElevation);
        }

        return WithFriendlyMessage(create);
    }

    public static string BuildInstallArguments(string exePath, string configPath)
    {
        return $"create {ServiceName} binPath= \"\"{exePath}\" --mode service --config \"{configPath}\"\" start= auto";
    }

    public static string BuildReconfigArguments(string exePath, string configPath)
    {
        return $"config {ServiceName} binPath= \"\"{exePath}\" --mode service --config \"{configPath}\"\" start= auto";
    }

    public ServiceCommandResult Uninstall()
    {
        return UninstallCore(forceElevation: null);
    }

    public ServiceCommandResult Uninstall(bool forceElevation)
    {
        return UninstallCore(forceElevation: forceElevation);
    }

    private ServiceCommandResult UninstallCore(bool? forceElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceCommandResult.Skipped("Linux/macOS 请使用 systemd 管理服务");
        }

        var state = _stateProbe.GetState(ServiceName);
        if (state is ServiceRuntimeState.Running
            or ServiceRuntimeState.StartPending
            or ServiceRuntimeState.PausePending
            or ServiceRuntimeState.Paused
            or ServiceRuntimeState.ContinuePending)
        {
            var stopResult = RunSc($"stop {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
            if (stopResult is { Status: ServiceCommandStatus.Failed } && stopResult.ExitCode != 1062)
            {
                return WithFriendlyMessage(stopResult);
            }
        }

        var result = RunSc($"delete {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
        return WithFriendlyMessage(result);
    }

    public ServiceCommandResult Start()
    {
        var exe = Environment.ProcessPath;
        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        return Start(exe, configPath, forceElevation: null);
    }

    public ServiceCommandResult Start(bool forceElevation)
    {
        var exe = Environment.ProcessPath;
        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        return Start(exe, configPath, forceElevation: forceElevation);
    }

    public ServiceCommandResult Start(string? exePath, string? configPath)
    {
        return Start(exePath, configPath, forceElevation: null);
    }

    public ServiceCommandResult Start(string? exePath, string? configPath, bool? forceElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceCommandResult.Skipped("Linux/macOS 请使用 systemd 管理服务");
        }

        if (!_stateProbe.ServiceExists(ServiceName))
        {
            return ServiceCommandResult.Failed(TranslateExitCode(1060), 1060);
        }

        var ensureConfig = EnsureInstalledConfigSynced(exePath, configPath, forceElevation);
        if (ensureConfig is { Status: ServiceCommandStatus.Failed })
        {
            return ensureConfig;
        }

        var result = RunSc($"start {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
        return WithFriendlyMessage(result);
    }

    public ServiceCommandResult Stop()
    {
        return StopCore(forceElevation: null);
    }

    public ServiceCommandResult Stop(bool forceElevation)
    {
        return StopCore(forceElevation: forceElevation);
    }

    private ServiceCommandResult StopCore(bool? forceElevation)
    {
        var result = RunSc($"stop {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
        return WithFriendlyMessage(result);
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
        var needElevation = forceElevation ?? (requireAdmin && !isAdministrator);
        return new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = needElevation,
            Verb = needElevation ? "runas" : string.Empty,
            CreateNoWindow = !needElevation,
            RedirectStandardOutput = !needElevation,
            RedirectStandardError = !needElevation,
            WindowStyle = needElevation
                ? ProcessWindowStyle.Hidden
                : ProcessWindowStyle.Normal
        };
    }

    public static string TranslateExitCode(int? code) => code switch
    {
        0 => "操作成功",
        5 => "权限不足，请以管理员身份运行",
        1053 => "服务启动超时，请检查 ExifTool 路径和配置文件是否正确",
        1055 => "服务数据库被锁定，请稍后重试",
        1056 => "服务已在运行",
        1058 => "服务已被禁用",
        1060 => "服务不存在，请先安装",
        1062 => "服务未运行，无需停止",
        1072 => "服务已标记为删除，请重启系统后重试",
        1073 => "服务已存在，已自动执行先卸载再安装",
        _ => $"未知错误（代码 {code}）"
    };

    public static bool IsElevationCancelled(Exception exception)
    {
        return exception is Win32Exception { NativeErrorCode: 1223 };
    }

    [SupportedOSPlatform("windows")]
    private ServiceCommandResult EnsureRemovedBeforeInstall(bool? forceElevation)
    {
        if (!_stateProbe.ServiceExists(ServiceName))
        {
            return ServiceCommandResult.Success(message: "service not installed");
        }

        var state = _stateProbe.GetState(ServiceName);
        if (state is ServiceRuntimeState.Running
            or ServiceRuntimeState.StartPending
            or ServiceRuntimeState.PausePending
            or ServiceRuntimeState.Paused
            or ServiceRuntimeState.ContinuePending)
        {
            var stopResult = RunSc($"stop {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
            if (stopResult is { Status: ServiceCommandStatus.Failed } && stopResult.ExitCode != 1062)
            {
                return WithFriendlyMessage(stopResult);
            }
        }

        var deleteResult = RunSc($"delete {ServiceName}", requireAdmin: true, forceElevation: forceElevation);
        if (deleteResult is { Status: ServiceCommandStatus.Failed } && deleteResult.ExitCode != 1060 && deleteResult.ExitCode != 1072)
        {
            return WithFriendlyMessage(deleteResult);
        }

        return ServiceCommandResult.Success(message: "service removed before install");
    }

    private ServiceCommandResult EnsureInstalledConfigSynced(string? exePath, string? configPath, bool? forceElevation)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(configPath))
        {
            return ServiceCommandResult.Success(message: "skip reconfig");
        }

        var args = BuildReconfigArguments(exePath, configPath);
        var result = RunSc(args, requireAdmin: true, forceElevation: forceElevation);
        return WithFriendlyMessage(result);
    }

    private ServiceCommandResult RunSc(string args, bool requireAdmin, bool? forceElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceCommandResult.Skipped("Linux/macOS 请使用 systemd 管理服务");
        }

        try
        {
            var startInfo = BuildRunScStartInfo(args, requireAdmin, IsAdministrator(), forceElevation);
            return _executor.Execute(startInfo);
        }
        catch (Exception ex) when (IsElevationCancelled(ex))
        {
            return ServiceCommandResult.ElevationCancelled("用户取消了管理员授权");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed(ex.Message);
        }
    }

    private static ServiceCommandResult WithFriendlyMessage(ServiceCommandResult result)
    {
        if (result.Status is ServiceCommandStatus.Skipped or ServiceCommandStatus.ElevationCancelled)
        {
            return result;
        }

        var friendly = TranslateExitCode(result.ExitCode);
        return result with { Message = friendly };
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsAdministratorWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministratorWindows()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
