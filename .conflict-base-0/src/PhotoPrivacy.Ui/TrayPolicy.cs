namespace PhotoPrivacy.Ui;

public static class TrayPolicy
{
    public static bool ShouldShowTrayIcon(bool isBackgroundMode, bool isServiceInstalled)
    {
        return isBackgroundMode && !isServiceInstalled;
    }
}
