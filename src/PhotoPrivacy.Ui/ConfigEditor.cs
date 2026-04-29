using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Ui;

public static class ConfigEditor
{
    public static void UpdateConfig(string configPath, ConfigEditCommand command)
    {
        var config = File.Exists(configPath)
            ? AppConfigLoader.Load(configPath)
            : AppConfig.Default;

        var updated = config with
        {
            ExifTool = config.ExifTool with
            {
                Path = command.ExifToolPath
            },
            Backup = config.Backup with
            {
                Enabled = command.BackupEnabled,
                Directory = ResolveBackupDirectory(command.BackupDirectory, command.HotFolderPath)
            },
            Audit = config.Audit with
            {
                DiagnosticMode = ResolveDiagnosticMode(command.LogLevel, command.LogEnabled),
                LogDirectory = command.AuditLogDirectory
            },
            Watch = config.Watch with
            {
                HotFolder = command.HotFolderPath
            },
            Ui = config.Ui with
            {
                HideMainWindowOnStartup = command.HideMainWindowOnStartup,
                HideTrayIcon = command.HideTrayIcon,
                ThemeVariant = command.ThemeVariant
            }
        };

        var json = AppConfigJson.ToIndentedJson(updated);
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(configPath, json);
    }

    private static string ResolveBackupDirectory(string backupDirectory, string hotFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            return backupDirectory;
        }

        if (string.IsNullOrWhiteSpace(hotFolderPath))
        {
            return string.Empty;
        }

        return Path.Combine(hotFolderPath, "bak");
    }

    private static bool ResolveDiagnosticMode(string logLevel, bool fallback)
    {
        if (string.Equals(logLevel, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(logLevel, "debug", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(logLevel, "info", StringComparison.OrdinalIgnoreCase)
            || string.Equals(logLevel, "warn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(logLevel, "error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }
}
