namespace PhotoPrivacy.Ui;

public static class ImmediateModeSwitchPolicy
{
    public static bool ShouldRestartAfterInstall(ServiceCommandResult result)
    {
        return result.Status == ServiceCommandStatus.Success;
    }

    public static bool ShouldRestartAfterUninstall(ServiceCommandResult result)
    {
        return result.Status == ServiceCommandStatus.Success;
    }
}
