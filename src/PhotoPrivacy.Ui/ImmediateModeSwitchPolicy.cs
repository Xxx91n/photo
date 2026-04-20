namespace PhotoPrivacy.Ui;

public static class ImmediateModeSwitchPolicy
{
    public static bool ShouldSwitchAfterInstall(ServiceCommandResult result)
    {
        return result.Status == ServiceCommandStatus.Success;
    }

    public static bool ShouldSwitchAfterUninstall(ServiceCommandResult result)
    {
        return result.Status == ServiceCommandStatus.Success;
    }
}
