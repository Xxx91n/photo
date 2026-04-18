namespace PhotoPrivacy.Cli;

public static class InstanceConflictUiPolicy
{
    public static bool ShouldShowInteractivePrompt(RuntimeMode mode, bool isUserInteractive)
    {
        if (!isUserInteractive)
        {
            return false;
        }

        return mode is RuntimeMode.Background or RuntimeMode.Service;
    }
}
