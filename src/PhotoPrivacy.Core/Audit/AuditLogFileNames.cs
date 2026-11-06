using System.Globalization;

namespace PhotoPrivacy.Core.Audit;

/// <summary>
/// 审计文件名日期基准的唯一入口（#129 决策）。按本地「今天」分卷命名，写入端
/// （JsonLineAuditLogger）、IPC 回填（WorkerIpcRequestHandler）、UI tail（feed.rs audit_path）
/// 三处统一走本口径；行内 <c>timestamp_utc</c> 仍是 UTC，文件名日期只做按日分卷键，
/// 与 #102 的绝对时刻水位比较正交。
/// </summary>
// Design(#208): 审计文件名日期基准统一取本地日期，写入端与 IPC 回填、UI tail 三处同口径；UTC 口径只保留在行内 timestamp_utc，不进文件名。
public static class AuditLogFileNames
{
    /// <summary>
    /// 按本地墙钟日期生成审计基础名 <c>audit-yyyy-MM-dd</c>（不含扩展名）。
    /// 传入值即「本地墙钟」，实现不得再做 UTC 换算——UTC+8 00:00–07:59 本地≠UTC 的
    /// 时窗内，任何换算都会让文件名比本地「今天」小一天（#129 根因）。
    /// 格式化固定使用 InvariantCulture，保证文件名始终是格里高利年（如 th-TH 佛历
    /// 会输出 2569 而非 2026，会与 Rust UI tail 期望的文件名失配，见 CodeRabbit PR#149）。
    /// </summary>
    public static string BaseName(DateTime localDay) =>
        $"audit-{localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
