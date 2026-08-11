using System.ComponentModel;
using System.Diagnostics;

namespace PhotoPrivacy.Ui;

/// <summary>
/// ADR 0020: Linux systemd service state probe.
/// Uses systemctl is-active/is-enabled to map to ServiceRuntimeState.
/// </summary>
public sealed class SystemdStateProbe : IServiceStateProbe
{
    private const string ServiceName = "photoprivacy";

    public ServiceRuntimeState GetState(string serviceName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return ServiceRuntimeState.NotInstalled;
        }

        try
        {
            var (exitCode, output) = RunProcess("systemctl", $"is-active {ServiceName}");
            return exitCode switch
            {
                0 => ServiceRuntimeState.Running,
                3 => ServiceRuntimeState.Stopped,
                4 => ServiceRuntimeState.NotInstalled,
                _ => ServiceRuntimeState.Unknown
            };
        }
        catch
        {
            return ServiceRuntimeState.Unknown;
        }
    }

    public bool ServiceExists(string serviceName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var (exitCode, _) = RunProcess("systemctl", $"is-enabled {ServiceName}");
            return exitCode == 0 || exitCode == 1; // enabled or disabled (exists)
        }
        catch
        {
            return false;
        }
    }

    private static (int exitCode, string output) RunProcess(string fileName, string arguments)
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
