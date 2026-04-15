using System.Text.Json;
using System.Text.Encodings.Web;

namespace PhotoPrivacy.Core.Audit;

public sealed class JsonLineAuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _logDirectory;
    private readonly int _retainDays;
    private readonly bool _diagnosticMode;

    public JsonLineAuditLogger(string logDirectory, int retainDays, bool diagnosticMode)
    {
        _logDirectory = logDirectory;
        _retainDays = retainDays;
        _diagnosticMode = diagnosticMode;
        Directory.CreateDirectory(_logDirectory);
    }

    public async ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
        var target = Path.Combine(_logDirectory, fileName);
        var payload = new
        {
            event_type = auditEvent.EventType,
            timestamp_utc = auditEvent.TimestampUtc.ToUniversalTime().ToString("O"),
            task_id = auditEvent.TaskId,
            source_path_masked = PathMasker.Mask(auditEvent.SourcePath),
            source_path_hash = PathMasker.StableHash(auditEvent.SourcePath),
            message = auditEvent.Message,
            data = auditEvent.Data,
            diagnostic_mode = _diagnosticMode
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.AppendAllTextAsync(target, json + Environment.NewLine, cancellationToken);
    }

    public void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retainDays);
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "audit-*.jsonl"))
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
