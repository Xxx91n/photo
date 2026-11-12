using System.Text.Encodings.Web;
using System.Text.Json;

namespace PhotoPrivacy.Core.Audit;

/// <summary>
/// 审计行的唯一 JSON 拼装入口：常驻 Worker 的批量写（<see cref="JsonLineAuditLogger"/>）
/// 和冲突/让位退出的同步写（<c>InstanceConflictAudit</c>）共用，字段名、UTC 时间格式、
/// 路径脱敏与 diagnostic_mode 只在这里定义，防止两条路径各拼一份后漂移（#211）。
/// </summary>
internal static class AuditLineFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    public static string Format(AuditEvent auditEvent, bool diagnosticMode)
    {
        var payload = new
        {
            event_type = auditEvent.EventType,
            level = auditEvent.Level.ToString().ToUpperInvariant(),
            timestamp_utc = auditEvent.TimestampUtc.ToUniversalTime().ToString("O"),
            task_id = auditEvent.TaskId,
            source_path_masked = PathMasker.Mask(auditEvent.SourcePath),
            source_path_hash = PathMasker.StableHash(auditEvent.SourcePath),
            message = auditEvent.Message,
            data = auditEvent.Data,
            diagnostic_mode = diagnosticMode
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
