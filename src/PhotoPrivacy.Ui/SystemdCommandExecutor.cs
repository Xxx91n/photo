using System.ComponentModel;
using System.Diagnostics;

namespace PhotoPrivacy.Ui;

/// <summary>
/// ADR 0021: Linux systemd service command executor.
/// Uses pkexec to elevate and call install-systemd-service.sh.
/// Falls back to error message if pkexec not available.
/// </summary>
public sealed class SystemdCommandExecutor : IScCommandExecutor
{
    private readonly string _scriptPath;

    public SystemdCommandExecutor(string? scriptPath = null)
    {
        _scriptPath = scriptPath ?? Path.Combine(AppContext.BaseDirectory, "scripts", "install-systemd-service.sh");
    }

    public ServiceCommandResult Execute(ProcessStartInfo startInfo)
    {
        // This executor uses platform-native commands, not ProcessStartInfo from caller.
        // The startInfo is ignored — we construct our own.
        return ExecuteInstall();
    }

    public ServiceCommandResult ExecuteInstall()
    {
        if (!OperatingSystem.IsLinux())
        {
            return ServiceCommandResult.Failed("systemd only available on Linux");
        }

        if (!File.Exists(_scriptPath))
        {
            return ServiceCommandResult.Failed($"install script not found: {_scriptPath}");
        }

        // Make script executable
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var chmod = Process.Start("chmod", $"+x {_scriptPath}");
                chmod?.WaitForExit(2000);
            }
        }
        catch { /* ignore */ }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add(_scriptPath);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return ServiceCommandResult.Failed("failed to start pkexec");
            }
            process.WaitForExit(60000);
            return process.ExitCode == 0
                ? ServiceCommandResult.Success(process.ExitCode, "systemd service installed")
                : ServiceCommandResult.Failed($"pkexec exit code {process.ExitCode}", process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ServiceCommandResult.ElevationCancelled("pkexec cancelled by user");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed($"pkexec failed: {ex.Message}. Please run 'sudo sh {_scriptPath}' manually.");
        }
    }

    public ServiceCommandResult ExecuteUninstall()
    {
        if (!OperatingSystem.IsLinux())
        {
            return ServiceCommandResult.Failed("systemd only available on Linux");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"systemctl stop photoprivacy 2>/dev/null; systemctl disable photoprivacy 2>/dev/null; rm -f /etc/systemd/system/photoprivacy.service; systemctl daemon-reload");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return ServiceCommandResult.Failed("failed to start pkexec");
            }
            process.WaitForExit(30000);
            return process.ExitCode == 0
                ? ServiceCommandResult.Success(process.ExitCode, "systemd service uninstalled")
                : ServiceCommandResult.Failed($"pkexec exit code {process.ExitCode}", process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ServiceCommandResult.ElevationCancelled("pkexec cancelled by user");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed($"uninstall failed: {ex.Message}");
        }
    }
}
