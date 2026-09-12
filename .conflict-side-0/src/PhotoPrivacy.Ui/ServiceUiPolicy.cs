using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.Ui;

public readonly record struct ServiceButtonState(
    bool InstallEnabled,
    bool UninstallEnabled,
    bool StartEnabled,
    bool StopEnabled);

public static class ServiceUiPolicy
{
    public static ServiceButtonState BuildButtonState(ServiceRuntimeState state)
    {
        var installed = state != ServiceRuntimeState.NotInstalled;
        var running = IsServiceRunningLike(state);

        if (!installed)
        {
            return new ServiceButtonState(
                InstallEnabled: true,
                UninstallEnabled: false,
                StartEnabled: false,
                StopEnabled: false);
        }

        if (running)
        {
            return new ServiceButtonState(
                InstallEnabled: false,
                UninstallEnabled: false,
                StartEnabled: false,
                StopEnabled: true);
        }

        return new ServiceButtonState(
            InstallEnabled: false,
            UninstallEnabled: true,
            StartEnabled: true,
            StopEnabled: false);
    }

    public static bool ShouldSwitchFromServiceShellToTray(ServiceRuntimeState state)
    {
        return state == ServiceRuntimeState.NotInstalled;
    }

    public static bool ShouldSwitchFromTrayToServiceShell(ServiceRuntimeState state)
    {
        return state != ServiceRuntimeState.NotInstalled;
    }

    private static bool IsServiceRunningLike(ServiceRuntimeState state)
    {
        return state is ServiceRuntimeState.Running
            or ServiceRuntimeState.StartPending
            or ServiceRuntimeState.ContinuePending
            or ServiceRuntimeState.PausePending
            or ServiceRuntimeState.Paused;
    }
}
