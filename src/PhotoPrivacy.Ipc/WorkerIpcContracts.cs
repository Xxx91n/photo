using System.Text.Json.Serialization;

namespace PhotoPrivacy.Ipc;

public static class WorkerIpcMethods
{
    public const string Ping = "Ping";
    public const string GetStatus = "GetStatus";
    public const string Pause = "Pause";
    public const string Resume = "Resume";
    public const string GetExifToolVersion = "GetExifToolVersion";
    public const string Shutdown = "Shutdown";
}

public sealed record WorkerIpcRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("id")] string? Id = null);

public sealed record WorkerIpcResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("data")] WorkerStatusDto? Data = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("id")] string? Id = null);

public sealed record WorkerStatusDto(
    [property: JsonPropertyName("isPaused")] bool IsPaused,
    [property: JsonPropertyName("exifToolVersion")] string ExifToolVersion,
    [property: JsonPropertyName("watchDirectory")] string WatchDirectory,
    [property: JsonPropertyName("mode")] string Mode);
