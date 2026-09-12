namespace PhotoPrivacy.Ui;

public sealed record AuditLogEntry(
    string TimeText,
    string EventType,
    string DisplayEvent,
    string SourcePathMasked,
    string Message,
    string ColorHex);
