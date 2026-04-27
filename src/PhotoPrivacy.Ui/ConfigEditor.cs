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
                Enabled = command.BackupEnabled
            },
            Audit = config.Audit with
            {
                DiagnosticMode = command.LogEnabled
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
}
