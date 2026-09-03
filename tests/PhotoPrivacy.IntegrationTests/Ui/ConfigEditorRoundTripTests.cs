using System.Text.Json;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ConfigEditorRoundTripTests
{
    [Fact]
    public void UpdateConfig_Should_Write_Selected_Fields_And_Reload()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-ui-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(AppConfig.Default));

           var command = new ConfigEditCommand(
               ExifToolPath: @"C:\Program Files\ExifTool\exiftool.exe",
                BackupEnabled: true,
                LogEnabled: true,
                HotFolderPath: @"D:\hot",
                HideMainWindowOnStartup: true,
                HideTrayIcon: false,
                ThemeVariant: "dark",
                Locale: "en",
                BackupDirectory: @"D:\hot\bak",
                AuditLogDirectory: @"D:\hot\_audit",
                LogLevel: "debug",
                QuarantineEnabled: true,
                QuarantineDirectory: @"D:\hot\_quarantine");

            ConfigEditor.UpdateConfig(configPath, command);

            var reloaded = AppConfigLoader.Load(configPath);
            Assert.Equal(command.ExifToolPath, reloaded.ExifTool.Path);
            Assert.Equal(command.BackupEnabled, reloaded.Backup.Enabled);
            Assert.Equal(command.HotFolderPath, reloaded.Watch.HotFolder);
            Assert.Equal(command.HideMainWindowOnStartup, reloaded.Ui.HideMainWindowOnStartup);
            Assert.Equal(command.HideTrayIcon, reloaded.Ui.HideTrayIcon);
            Assert.Equal(command.ThemeVariant, reloaded.Ui.ThemeVariant);
            Assert.Equal(command.Locale, reloaded.Ui.Locale);
            Assert.Equal(command.LogEnabled, reloaded.Audit.DiagnosticMode);

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var rootEl = doc.RootElement;
            Assert.True(rootEl.TryGetProperty("ui", out var uiEl));
            Assert.True(uiEl.TryGetProperty("hide_main_window_on_startup", out _));
            Assert.True(uiEl.TryGetProperty("hide_tray_icon", out _));
            Assert.True(uiEl.TryGetProperty("theme_variant", out _));
            Assert.True(uiEl.TryGetProperty("locale", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
