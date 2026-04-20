namespace PhotoPrivacy.Ipc;

public static class WorkerIpcEndpointNames
{
    public const string BackgroundPipe = "PhotoPrivacyCleaner.Background";
    public const string ServicePipe = "PhotoPrivacyCleaner.Service";

    public const string LinuxSocketDirectory = "/tmp";
    public const string LinuxBackgroundSocket = "/tmp/photoprivacy-background.sock";
    public const string LinuxServiceSocket = "/tmp/photoprivacy-service.sock";
}
