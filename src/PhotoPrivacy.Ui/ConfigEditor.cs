using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Pipeline;

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
                LogDirectory = command.AuditLogDirectory,
                LogLevel = command.LogLevel
            },
            Watch = config.Watch with
            {
                HotFolder = command.HotFolderPath
            },
            Quarantine = config.Quarantine with
            {
                Enabled = command.QuarantineEnabled,
                Directory = command.QuarantineDirectory
            },
            Ui = config.Ui with
            {
                HideMainWindowOnStartup = command.HideMainWindowOnStartup,
                HideTrayIcon = command.HideTrayIcon,
                ThemeVariant = command.ThemeVariant,
                Locale = command.Locale
            }
        };

        var json = AppConfigJson.ToIndentedJson(updated);
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Atomic backup before write: copy existing config to .bak via temp+rename
        if (File.Exists(configPath))
        {
            var bakPath = configPath + ".bak";
            var tmpPath = bakPath + ".tmp";
            File.Copy(configPath, tmpPath, overwrite: true);
            File.Move(tmpPath, bakPath, overwrite: true);
        }

        // Atomic write: write to temp then rename
        var writeTmp = configPath + ".write.tmp";
        File.WriteAllText(writeTmp, json);
        File.Move(writeTmp, configPath, overwrite: true);
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

        return BackupPathResolver.ResolveDefaultBackupDir(hotFolderPath);
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
