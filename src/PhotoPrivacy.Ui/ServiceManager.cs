using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using PhotoPrivacy.Ui.Localization;
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
        : this(CreateDefaultExecutor(), CreateDefaultStateProbe())
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

    public bool IsAvailable => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public bool IsInstalled()
    {
        return _stateProbe.ServiceExists(ServiceName);
    }

    public ServiceRuntimeState GetRuntimeState()
    {
        return _stateProbe.GetState(ServiceName);
    }

    public string GetStatusText()
    {
        var state = _stateProbe.GetState(ServiceName);
        return state switch
        {
            ServiceRuntimeState.NotInstalled => LocalizationService.Instance.Get("service.state.not_installed"),
            ServiceRuntimeState.Running => LocalizationService.Instance.Get("status.running"),
            ServiceRuntimeState.StartPending => LocalizationService.Instance.Get("service.state.start_pending"),
            ServiceRuntimeState.PausePending => LocalizationService.Instance.Get("service.state.pause_pending"),
            ServiceRuntimeState.Paused => LocalizationService.Instance.Get("status.paused"),
            ServiceRuntimeState.ContinuePending => LocalizationService.Instance.Get("service.state.continue_pending"),
            ServiceRuntimeState.Stopped => LocalizationService.Instance.Get("status.stopped"),
            ServiceRuntimeState.StopPending => LocalizationService.Instance.Get("service.state.stop_pending"),
            _ => LocalizationService.Instance.Get("service.state.unknown")
        };
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
            return ServiceCommandResult.Skipped(LocalizationService.Instance.Get("service.msg.use_systemd"));
        }

        if (!IsWorkerExecutablePath(exePath))
        {
            return ServiceCommandResult.Failed(LocalizationService.Instance.Get("service.msg.must_use_worker"));
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
        ValidatePathForScCommand(exePath, nameof(exePath));
        ValidatePathForScCommand(configPath, nameof(configPath));
        return $@"config {ServiceName} binPath= """"{exePath}"" --mode service --config ""{configPath}"""" start= auto";
    }

    private static void ValidatePathForScCommand(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(LocalizationService.Instance.Get("service.msg.path_empty"), paramName);
        var dangerousChars = new[] { '"', '`', '$', '|', '>', '<', '&', '\0' };
        if (path.IndexOfAny(dangerousChars) >= 0)
            throw new ArgumentException(LocalizationService.Instance.Get("service.msg.path_invalid_chars", path), paramName);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException(LocalizationService.Instance.Get("service.msg.path_must_be_absolute"), paramName);
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
            return ServiceCommandResult.Skipped(LocalizationService.Instance.Get("service.msg.use_systemd"));
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
        var exe = ResolveDefaultWorkerExecutablePath();
        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        return Start(exe, configPath, forceElevation: null);
    }

    public ServiceCommandResult Start(bool forceElevation)
    {
        var exe = ResolveDefaultWorkerExecutablePath();
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
            return ServiceCommandResult.Skipped(LocalizationService.Instance.Get("service.msg.use_systemd"));
        }

        if (!_stateProbe.ServiceExists(ServiceName))
        {
            return ServiceCommandResult.Failed(TranslateExitCode(1060), 1060);
        }

        if (!string.IsNullOrWhiteSpace(exePath) && !IsWorkerExecutablePath(exePath))
        {
            return ServiceCommandResult.Failed(LocalizationService.Instance.Get("service.msg.invalid_target"));
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
        0 => LocalizationService.Instance.Get("service.exitcode.0"),
        5 => LocalizationService.Instance.Get("service.exitcode.5"),
        1053 => LocalizationService.Instance.Get("service.exitcode.1053"),
        1055 => LocalizationService.Instance.Get("service.exitcode.1055"),
        1056 => LocalizationService.Instance.Get("service.exitcode.1056"),
        1058 => LocalizationService.Instance.Get("service.exitcode.1058"),
        1060 => LocalizationService.Instance.Get("service.exitcode.1060"),
        1062 => LocalizationService.Instance.Get("service.exitcode.1062"),
        1072 => LocalizationService.Instance.Get("service.exitcode.1072"),
        1073 => LocalizationService.Instance.Get("service.exitcode.1073"),
        _ => LocalizationService.Instance.Get("service.exitcode.unknown", code)
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
            return ServiceCommandResult.Skipped(LocalizationService.Instance.Get("service.msg.use_systemd"));
        }

        try
        {
            var startInfo = BuildRunScStartInfo(args, requireAdmin, IsAdministrator(), forceElevation);
            return _executor.Execute(startInfo);
        }
        catch (Exception ex) when (IsElevationCancelled(ex))
        {
            return ServiceCommandResult.ElevationCancelled(LocalizationService.Instance.Get("service.msg.elevation_cancelled"));
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

    private static string? ResolveDefaultWorkerExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        if (IsWorkerExecutablePath(processPath))
        {
            return processPath;
        }

        var dir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var workerPath = Path.Combine(dir, "PhotoPrivacyWorker.exe");
        return File.Exists(workerPath) ? workerPath : null;
    }

    private static bool IsWorkerExecutablePath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileName(exePath),
            "PhotoPrivacyWorker.exe",
            StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministratorWindows()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// ADR 0020+0021: Create platform-appropriate default state probe.
    /// </summary>
    private static IServiceStateProbe CreateDefaultStateProbe()
    {
        if (OperatingSystem.IsLinux()) return new SystemdStateProbe();
        if (OperatingSystem.IsMacOS()) return new LaunchdStateProbe();
        return new ServiceStateProbe();
    }

    /// <summary>
    /// ADR 0020+0021: Create platform-appropriate default command executor.
    /// Cross-platform: Windows uses sc.exe; Linux uses pkexec+systemd script; macOS uses osascript+launchd.
    /// </summary>
    private static IScCommandExecutor CreateDefaultExecutor()
    {
        if (OperatingSystem.IsLinux()) return new SystemdCommandExecutor();
        if (OperatingSystem.IsMacOS()) return new LaunchdCommandExecutor();
        return new ScCommandExecutor();
    }

    /// <summary>
    /// ADR 0032: Post-action probe retry with exponential backoff.
    /// </summary>
    public async Task<ServiceRuntimeState> WaitForStateAsync(
        ServiceRuntimeState desired, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 8; i++)
        {
            var state = _stateProbe.GetState(ServiceName);
            if (state == desired)
            {
                return state;
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, i));
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return _stateProbe.GetState(ServiceName);
            }
        }

        return _stateProbe.GetState(ServiceName);
    }
}


