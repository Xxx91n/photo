using System.ComponentModel;
using System.Diagnostics;

namespace PhotoPrivacy.Ui;

/// <summary>
/// ADR 0021: macOS launchd service command executor.
/// Uses osascript 'do shell script with administrator privileges' to elevate.
/// </summary>
public sealed class LaunchdCommandExecutor : IScCommandExecutor
{
    private readonly string _scriptPath;

    public LaunchdCommandExecutor(string? scriptPath = null)
    {
        _scriptPath = scriptPath ?? Path.Combine(AppContext.BaseDirectory, "scripts", "install-launchd-service.sh");
    }

    public ServiceCommandResult Execute(ProcessStartInfo startInfo)
    {
        return ExecuteInstall();
    }

    public ServiceCommandResult ExecuteInstall()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return ServiceCommandResult.Failed("launchd only available on macOS");
        }

        if (!File.Exists(_scriptPath))
        {
            return ServiceCommandResult.Failed($"install script not found: {_scriptPath}");
        }

        // Make script executable
        try
        {
            var chmod = Process.Start("chmod", $"+x {_scriptPath}");
            chmod?.WaitForExit(2000);
        }
        catch { /* ignore */ }

        try
        {
            var script = $"do shell script \"sh {_scriptPath}\" with administrator privileges";
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return ServiceCommandResult.Failed("failed to start osascript");
            }
            process.WaitForExit(60000);
            return process.ExitCode == 0
                ? ServiceCommandResult.Success(process.ExitCode, "launchd service installed")
                : ServiceCommandResult.Failed($"osascript exit code {process.ExitCode}", process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ServiceCommandResult.ElevationCancelled("osascript cancelled by user");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed($"osascript failed: {ex.Message}");
        }
    }

    public ServiceCommandResult ExecuteUninstall()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return ServiceCommandResult.Failed("launchd only available on macOS");
        }

        try
        {
            var script = $"do shell script \"launchctl unload -w /Library/LaunchDaemons/com.photoprivacy.cleaner.plist 2>/dev/null; rm -f /Library/LaunchDaemons/com.photoprivacy.cleaner.plist\" with administrator privileges";
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return ServiceCommandResult.Failed("failed to start osascript");
            }
            process.WaitForExit(30000);
            return process.ExitCode == 0
                ? ServiceCommandResult.Success(process.ExitCode, "launchd service uninstalled")
                : ServiceCommandResult.Failed($"osascript exit code {process.ExitCode}", process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ServiceCommandResult.ElevationCancelled("osascript cancelled by user");
        }
        catch (Exception ex)
        {
            return ServiceCommandResult.Failed($"uninstall failed: {ex.Message}");
        }
    }
}
