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
/// 不是带着未知争用风险放行。服务/后台探测编排的体验面归 #58。
/// </summary>
// Design(#162): 常驻 Worker（后台/服务）触碰审计文件前必须过本准入门；拿不到锁或门异常一律
// 让位干净退出（禁静默降级、禁带未知争用风险放行），编排预防归 #58。
public static class SingleWriterGate
{
    /// <summary>
    /// 尝试以给定配置的审计目录取得单写者准入锁。
    /// Guard 非空 = 获锁（进程生命周期内持有）；Guard 空、Error 空 = 已有 Worker 持锁，调用方让位；
    /// Error 非空 = 门自身不可用（权限/环境，含跨账户打不开既有锁），调用方同样让位（无法确证独占就不写）。
    /// </summary>
    public static SingleWriterAdmission TryAcquire(string? configuredAuditDirectory)
    {
        var auditDirectory = ResolveEffectiveAuditDirectory(configuredAuditDirectory);
        try
        {
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
            return new SingleWriterAdmission(Guard: null, AuditDirectory: auditDirectory, Error: ex);
        }
    }

    /// <summary>与 FileProcessedRecordStore 构造的目录回退逐字对齐：空配置回落 BaseDirectory/_audit。</summary>
    public static string ResolveEffectiveAuditDirectory(string? configuredAuditDirectory)
    {
        return string.IsNullOrWhiteSpace(configuredAuditDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "_audit")
            : configuredAuditDirectory;
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
