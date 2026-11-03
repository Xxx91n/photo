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

    // macOS 后台模式: ~/Library/Application Support/photoprivacy/ (per-user，与调用者身份绑定)
    public const string MacSocketDirectory = "Library/Application Support/photoprivacy";
    public static string MacBackgroundSocket => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        MacSocketDirectory, "worker-background.sock");

    // macOS 服务模式: 系统级固定落点，与调用者身份无关（与 Linux /run/photoprivacy 同构）。
    // LaunchDaemon 以服务用户运行（UserName=photoprivacy，NFSHomeDirectory=/opt/photoprivacy），
    // GUI 用户按自身 HOME 拼同一相对路径会指向各自 home 下的幽灵 socket——落点随身份分裂（#167）。
    // 不用 /var/run：macOS 上它是重启即清的 volatile 目录，非 root 的 Worker 无法在启动时重建
    // 目录（Linux 有 systemd RuntimeDirectory 承担这一步，macOS 无等价机制）；持久目录由
    // install-launchd-service.sh 以 root 创建并 0750 限制在服务组内。
    public const string MacSystemSocketDirectory = "/Library/Application Support/photoprivacy";
    public const string MacServiceSocketPath = MacSystemSocketDirectory + "/worker.sock";
    public static string MacServiceSocket => MacServiceSocketPath;
}
