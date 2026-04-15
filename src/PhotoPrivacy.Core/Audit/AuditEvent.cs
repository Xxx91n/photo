namespace PhotoPrivacy.Core.Audit;

public sealed record AuditEvent(
    string EventType,
    DateTimeOffset TimestampUtc,
    string TaskId,
    string SourcePath,
    string Message,
    IReadOnlyDictionary<string, string>? Data);
