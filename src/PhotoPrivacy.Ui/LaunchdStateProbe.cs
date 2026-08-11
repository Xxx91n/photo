using System.ComponentModel;
using System.Diagnostics;

namespace PhotoPrivacy.Ui;

/// <summary>
/// ADR 0020: macOS launchd service state probe.
/// Uses launchctl list to parse PID and exit code.
/// </summary>
public sealed class LaunchdStateProbe : IServiceStateProbe
{
    private const string Label = "com.photoprivacy.cleaner";

    public ServiceRuntimeState GetState(string serviceName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return ServiceRuntimeState.NotInstalled;
        }

        try
        {
            var (exitCode, output) = RunProcess("launchctl", $"list {Label}");
            if (exitCode != 0)
            {
                return ServiceRuntimeState.NotInstalled;
            }

            // Parse "PID" field — if present and non-zero, service is running
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var pid) && pid > 0)
                    {
                        return ServiceRuntimeState.Running;
                    }
                }
            }
            return ServiceRuntimeState.Stopped;
        }
        catch
        {
            return ServiceRuntimeState.Unknown;
        }
    }

    public bool ServiceExists(string serviceName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var (exitCode, _) = RunProcess("launchctl", $"list {Label}");
            return exitCode == 0;
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
