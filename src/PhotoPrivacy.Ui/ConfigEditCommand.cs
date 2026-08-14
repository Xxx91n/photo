namespace PhotoPrivacy.Ui;

public sealed record ConfigEditCommand(
    string ExifToolPath,
    bool BackupEnabled,
    bool LogEnabled,
    string HotFolderPath,
    bool HideMainWindowOnStartup,
    bool HideTrayIcon,
    string ThemeVariant,
    string Locale,
    string BackupDirectory,
    string AuditLogDirectory,
    string LogLevel,
    bool QuarantineEnabled,
    string QuarantineDirectory);
