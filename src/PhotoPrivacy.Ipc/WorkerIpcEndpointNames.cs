namespace PhotoPrivacy.Ipc;

public static class WorkerIpcEndpointNames
{
    // Windows named pipes (kernel objects, no stale cleanup needed)
    public const string BackgroundPipe = "PhotoPrivacyCleaner.Background";
    public const string ServicePipe = "PhotoPrivacyCleaner.Service";

    // Linux: /run/photoprivacy/ (systemd RuntimeDirectory creates this, owned by photoprivacy user)
    public const string LinuxSocketDirectory = "/run/photoprivacy";
    public const string LinuxBackgroundSocket = "/run/photoprivacy/worker-background.sock";
    public const string LinuxServiceSocket = "/run/photoprivacy/worker.sock";

    // macOS: ~/Library/Application Support/photoprivacy/ (no root needed, per-user)
    public const string MacSocketDirectory = "Library/Application Support/photoprivacy";
    public static string MacBackgroundSocket => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        MacSocketDirectory, "worker-background.sock");
    public static string MacServiceSocket => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        MacSocketDirectory, "worker.sock");
}
