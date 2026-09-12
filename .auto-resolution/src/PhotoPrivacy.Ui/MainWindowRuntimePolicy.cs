namespace PhotoPrivacy.Ui;

public static class MainWindowRuntimePolicy
{
    public static bool ShouldHideOnStartup(
        bool hideMainWindowOnStartup,
        bool useTrayIcon,
        bool hideTrayIcon,
        bool trayIconReady)
    {
        return hideMainWindowOnStartup && useTrayIcon && !hideTrayIcon && trayIconReady;
    }
}
