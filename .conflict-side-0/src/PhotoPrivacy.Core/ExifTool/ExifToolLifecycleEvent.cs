namespace PhotoPrivacy.Core.ExifTool;

public sealed record ExifToolLifecycleEvent(
    string EventType,
    string SourcePath,
    string Message,
    IReadOnlyDictionary<string, string>? Data = null);
