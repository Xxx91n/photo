namespace PhotoPrivacy.Ui;

public sealed class BackgroundUiOptions
{
    public required string RuntimeKind { get; init; }
    public required bool IsBackgroundMode { get; init; }
    public required bool HideMainWindowOnStartup { get; init; }
    public required bool IsServiceInstalled { get; init; }
    public required bool UseTrayIcon { get; init; }

    public required Func<bool> IsPaused { get; init; }
    public required Action Pause { get; init; }
    public required Action Resume { get; init; }
    public required Func<Task> ExitAsync { get; init; }
    public required Func<string> GetExifToolVersion { get; init; }
    public required Func<ServiceRuntimeState> GetServiceRuntimeState { get; init; }
    public required Func<CancellationToken, Task> RestartToDefaultModeAsync { get; init; }
    public Action ShowMainWindow { get; set; } = static () => { };

    public required string ConfigPath { get; init; }
    public required string AuditDirectory { get; init; }
    public string? SelfExecutablePath { get; init; }

    public static BackgroundUiOptions CreateFallback()
    {
        return new BackgroundUiOptions
        {
            RuntimeKind = "tray",
            IsBackgroundMode = true,
            HideMainWindowOnStartup = true,
            IsServiceInstalled = false,
            UseTrayIcon = true,
            IsPaused = static () => false,
            Pause = static () => { },
            Resume = static () => { },
            ExitAsync = static () => Task.CompletedTask,
            GetExifToolVersion = static () => "unknown",
            GetServiceRuntimeState = static () => ServiceRuntimeState.NotInstalled,
            RestartToDefaultModeAsync = static _ => Task.CompletedTask,
            ConfigPath = AppContext.BaseDirectory,
            AuditDirectory = AppContext.BaseDirectory,
            SelfExecutablePath = Environment.ProcessPath
        };
    }
}
