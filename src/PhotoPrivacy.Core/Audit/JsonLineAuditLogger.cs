using System.Text.Json;
using System.Text.Encodings.Web;

namespace PhotoPrivacy.Core.Audit;

public sealed class JsonLineAuditLogger : IAuditLogger
{
    /// <summary>
    /// 使用安全编码器：转义 HTML 特殊字符（&lt;, &gt;, &amp;, ', "）防止 XSS，
    /// 同时允许非 ASCII 字符（中文等）正常显示。
    /// 注意：之前使用 UnsafeRelaxedJsonEscaping 不会转义 HTML 特殊字符，
    /// 如果日志在浏览器中渲染可能触发 XSS。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    private readonly string _logDirectory;
    private readonly int _retainDays;
    private readonly bool _diagnosticMode;
    private AuditLevel _minimumWriteLevel;

    public JsonLineAuditLogger(
        string logDirectory,
        int retainDays,
        bool diagnosticMode,
        AuditLevel minimumWriteLevel = AuditLevel.Debug)
    {
        _logDirectory = logDirectory;
        _retainDays = retainDays;
        _diagnosticMode = diagnosticMode;
        _minimumWriteLevel = minimumWriteLevel;
        Directory.CreateDirectory(_logDirectory);
    }

    public void SetMinimumWriteLevel(AuditLevel level) => _minimumWriteLevel = level;

    public async ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent.Level < _minimumWriteLevel)
            return;

        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
        var target = Path.Combine(_logDirectory, fileName);
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

