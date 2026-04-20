namespace PhotoPrivacy.Cli;

public readonly record struct RuntimeBootstrapDecision(
    RuntimeMode EffectiveMode,
    bool RunUiControlShell,
    bool UseTrayIcon,
    string RuntimeKind);

public static class RuntimeBootstrapPolicy
{
    public static RuntimeBootstrapDecision Decide(RuntimeMode requestedMode, bool hasModeOption, bool isUserInteractive, bool isServiceInstalled)
    {
        if (!hasModeOption && isUserInteractive)
        {
            return isServiceInstalled
                ? new RuntimeBootstrapDecision(RuntimeMode.Background, RunUiControlShell: true, UseTrayIcon: false, RuntimeKind: "service")
                : new RuntimeBootstrapDecision(RuntimeMode.Background, RunUiControlShell: false, UseTrayIcon: true, RuntimeKind: "tray");
        }

        if (requestedMode == RuntimeMode.Service)
        {
            return new RuntimeBootstrapDecision(RuntimeMode.Service, RunUiControlShell: false, UseTrayIcon: false, RuntimeKind: "service");
        }

        if (requestedMode == RuntimeMode.Cli)
        {
            return new RuntimeBootstrapDecision(RuntimeMode.Cli, RunUiControlShell: false, UseTrayIcon: false, RuntimeKind: "cli");
        }

        return new RuntimeBootstrapDecision(RuntimeMode.Background, RunUiControlShell: false, UseTrayIcon: !isServiceInstalled, RuntimeKind: isServiceInstalled ? "service" : "tray");
    }
}
