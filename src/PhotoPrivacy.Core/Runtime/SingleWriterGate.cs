using System.Security.Cryptography;
using System.Text;

namespace PhotoPrivacy.Core.Runtime;

/// <summary>
/// 单写者准入门（#162）：同一有效审计目录同时至多一个常驻 Worker 持锁写 processed.ndjson。
/// 锁身份 = 有效审计目录（与 FileProcessedRecordStore 的目录回退逐字对齐），
/// 锁生命周期 = 进程生命周期（Mutex/flock 随持有者消亡自动释放，无 stale lock、无 PID 探活）。
/// 拿不到锁的 Worker 必须让位干净退出（日志 + 审计留痕由 Program 层负责），禁止静默降级凑合跑，
/// 也不得让 store 常驻句柄冲突炸成 IOException → StopHost 服务自停。
/// 门自身异常（权限/环境）同样让位：无法确证独占就不写审计——fail-safe 方向是宁可不起，
/// 不是带着未知争用风险放行。
/// 残余：锁身份的别名归一只解析最终链接目标，路径中间分量的别名（subst/逐段符号链接）未逐段展开，
/// 该形态下两侧仍可能各持一锁；真正的防线还有审计文件自身的 FileShare 语义兜底。
/// 服务/后台探测编排的体验面归 #58。
/// </summary>
// Design(#238): 常驻 Worker 触碰审计文件前必须过本准入门；拿不到锁或门异常一律让位（禁静默降级/禁带险放行），编排预防归 #58。
public static class SingleWriterGate
{
    /// <summary>
    /// 尝试以给定配置的审计目录取得单写者准入锁。
    /// Guard 非空 = 获锁（进程生命周期内持有）；Guard 空、Error 空 = 已有 Worker 持锁，调用方让位；
    /// Error 非空 = 门自身不可用（权限/环境，含跨账户打不开既有锁），调用方同样让位（无法确证独占就不写）。
    /// </summary>
    public static SingleWriterAdmission TryAcquire(string? configuredAuditDirectory)
    {
        try
        {
            var auditDirectory = ResolveEffectiveAuditDirectory(configuredAuditDirectory);
            var guard = SingleInstanceGuardFactory.Create(BuildLockName(auditDirectory));
            if (guard.IsOwner)
            {
                return new SingleWriterAdmission(guard, auditDirectory, Error: null);
            }

            guard.Dispose();
            return new SingleWriterAdmission(Guard: null, AuditDirectory: auditDirectory, Error: null);
        }
        catch (Exception ex)
        {
            // 非法/不可解析的路径也走让位（#153）：异常不得逃出本方法——Program 的
            // 调用点不在 try 内，逃逸会在 UnhandledException 注册前崩掉 Worker。
            return new SingleWriterAdmission(
                Guard: null,
                AuditDirectory: string.IsNullOrWhiteSpace(configuredAuditDirectory)
                    ? Path.Combine(AppContext.BaseDirectory, "_audit")
                    : configuredAuditDirectory,
                Error: ex);
        }
    }

    /// <summary>
    /// 与 FileProcessedRecordStore 构造的目录回退逐字对齐：空配置回落 BaseDirectory/_audit。
    /// 随后 best-effort 归一别名（symlink/junction 最终目标）：锁身份必须跟「同一个物理目录」走，
    /// 否则别名路径下的两个 Worker 会各自获锁、绕过准入门。归一失败（不存在/无权限）按原路径继续，
    /// 中间路径分量上的别名未逐段解析，残余记录在类注释。
    /// </summary>
    public static string ResolveEffectiveAuditDirectory(string? configuredAuditDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(configuredAuditDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "_audit")
            : configuredAuditDirectory;
        try
        {
            var fullPath = Path.GetFullPath(directory);
            return Directory.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
        }
        catch
        {
            // 不再二次 Path.GetFullPath：非法字符会在 catch 里再抛，异常逃出 TryAcquire（#153）。
            return directory;
        }
    }

    /// <summary>
    /// 锁名由目录全路径的 SHA-256 前 8 字节派生：规避路径字符/长度对 Mutex 名与 POSIX 文件名的限制，
    /// 也避免把可预测的目录名直接暴露成锁名。Windows 路径大小写不敏感，折叠后取哈希；
    /// POSIX 大小写敏感，保持原样（否则两个真实不同的目录会被误判争用）。
    /// </summary>
    public static string BuildLockName(string auditDirectory)
    {
        var fullPath = Path.GetFullPath(auditDirectory);
        if (OperatingSystem.IsWindows())
        {
            fullPath = fullPath.ToLowerInvariant();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return $"Global\\PhotoPrivacyWorker_Audit_{hash[..16]}";
    }
}

/// <summary>准入门结果：Guard 非 null 即获锁；Guard 空 = 让位或门不可用（以 Error 区分）。</summary>
public sealed record SingleWriterAdmission(
    ISingleInstanceGuard? Guard,
    string AuditDirectory,
    Exception? Error);
