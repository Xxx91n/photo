using System.Text.Json.Serialization;

namespace PhotoPrivacy.Ipc;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkerIpcRequest))]
[JsonSerializable(typeof(WorkerIpcResponse))]
[JsonSerializable(typeof(WorkerStatusDto))]
[JsonSerializable(typeof(RecentLogsDto))]
public partial class WorkerIpcJsonContext : JsonSerializerContext
{
}
