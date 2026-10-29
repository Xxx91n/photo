using System.Text.Json.Serialization;

namespace PhotoPrivacy.Ipc;

// Design(#49): IPC JSON 行协议是新旧 UI 的唯一主缝——换栈只换客户端实现，方法名与既有字段语义不动，新能力一律以 method 名扩展。
public static class WorkerIpcMethods
{
    public const string Ping = "Ping";
    public const string GetStatus = "GetStatus";
    public const string Pause = "Pause";
    public const string Resume = "Resume";
    public const string GetExifToolVersion = "GetExifToolVersion";
    public const string Shutdown = "Shutdown";
    public const string ReloadConfig = "ReloadConfig";
    public const string GetRecentLogs = "GetRecentLogs";
}

// 协议版本唯一出处：只在 `v` 字段语义变更时动它；新增 method 名不动版本（docs/agents/ipc-contract.md）。
public static class WorkerIpcProtocol
{
    public const int Version = 1;
}

public sealed record WorkerIpcRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("v")] int? V = null);

public sealed record WorkerIpcResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("data")] WorkerStatusDto? Data = null,
    [property: JsonPropertyName("logs")] RecentLogsDto? Logs = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("v")] int? V = null);

public sealed record WorkerStatusDto(
    [property: JsonPropertyName("isPaused")] bool IsPaused,
    [property: JsonPropertyName("exifToolVersion")] string ExifToolVersion,
    [property: JsonPropertyName("watchDirectory")] string WatchDirectory,
    [property: JsonPropertyName("mode")] string Mode);

public sealed record RecentLogsDto(
    [property: JsonPropertyName("lines")] string[] Lines);
