namespace PhotoPrivacy.Ipc;

public static class WorkerIpcEndpointNames
{
    // Windows named pipes (kernel objects, no stale cleanup needed)
    public const string BackgroundPipe = "PhotoPrivacyCleaner.Background";
    public const string ServicePipe = "PhotoPrivacyCleaner.Service";

    // Linux 服务模式: /run/photoprivacy/（systemd RuntimeDirectory 创建，属主服务用户；
    // #167/#180 已定落点、#188 权限规则，#201 明确不动）。
    public const string LinuxSocketDirectory = "/run/photoprivacy";
    public const string LinuxServiceSocket = "/run/photoprivacy/worker.sock";

    // Linux 后台模式（#201）: $XDG_RUNTIME_DIR/photoprivacy/worker-background.sock。
    // 旧落点 /run/photoprivacy/worker-background.sock 对普通用户是死路：该目录只由 systemd
    // RuntimeDirectory 在服务模式创建（0750 服务用户），普通用户拉起的后台 Worker 在 /run 下
    // mkdir 吃 EACCES，UI 连接编排只见 ENOENT——未安装服务时回落不到后台模式
    // （#31 轮 2026-11-04 实机观察，原记录已随 #254 移入 git 历史）。迁往 per-user 会话运行时目录
    // （pam_systemd/logind 按会话建 /run/user/<uid>，0700 属主用户），与 macOS 背景端点
    // per-user（HOME）、Windows 背景管道 CurrentUserOnly 的平台语义同构。
    // XDG_RUNTIME_DIR 缺失/空/非绝对根路径 → 解析为 null（背景端点不可用），与 macOS 缺 HOME
    // 形态对称；不回落 /run/photoprivacy（缺陷本体），也不回落 /tmp（世界可写目录的可预测
    // 路径 = CWE-377 预蹲面，且跨用户端点相撞）。与 Rust 侧 endpoints.rs 的
    // linux_background_endpoint 逐字同构（对照检查：IpcAuthClaimReconciliationTests 的 #201
    // 对；双端行为测试钉同一路径形态）。
    public const string LinuxRuntimeDirectoryVariable = "XDG_RUNTIME_DIR";
    public const string LinuxBackgroundSocketDirectoryName = "photoprivacy";
    public const string LinuxBackgroundSocketFileName = "worker-background.sock";

    /// <summary>Linux 背景端点：XDG_RUNTIME_DIR 缺失/空/非绝对根路径时为 null（#201）。</summary>
    public static string? LinuxBackgroundSocket => ResolveLinuxBackgroundSocket(
        Environment.GetEnvironmentVariable(LinuxRuntimeDirectoryVariable));

    /// <summary>
    /// Linux 背景端点的纯函数形态（#201）：只认「非空且绝对根路径」的运行时目录，其余一律
    /// null；不裁剪空白（与 Rust 侧 <c>has_root</c> 判定逐字节同构），不做平台分支
    /// （生产落点仅 Linux；测试矩阵三平台跑同一拼接，断言侧归一分隔符）。
    /// </summary>
    public static string? ResolveLinuxBackgroundSocket(string? runtimeDirectory)
    {
        if (string.IsNullOrEmpty(runtimeDirectory) || !Path.IsPathRooted(runtimeDirectory))
        {
            return null;
        }

        return Path.Combine(
            runtimeDirectory, LinuxBackgroundSocketDirectoryName, LinuxBackgroundSocketFileName);
    }

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
