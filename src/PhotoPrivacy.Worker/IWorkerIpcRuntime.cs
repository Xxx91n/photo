using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 票 05（A-005）：IPC 请求处理所需的最小 Worker 运行时面。
///
/// 抽出接口的唯一目的是让请求处理逻辑（<see cref="WorkerIpcRequestHandler"/>）可在行为测试中
/// 用可控假件驱动——尤其要能造出「ReloadConfigAsync 挂住数秒」的场景，验证 accept 循环
/// 不再被 sync-over-async 堵死、后续心跳探针仍被服务（issue 05 Acceptance criteria 第 2 条）。
///
/// <see cref="WorkerRuntimeContext"/> 是唯一生产实现，成员签名与其既有公开成员逐字一致。
/// </summary>
public interface IWorkerIpcRuntime
{
    bool IsPaused { get; }

    string ExifToolVersion { get; }

    string WatchDirectory { get; }

    /// <summary>ADR 0046: audit log directory for GetRecentLogs IPC channel.</summary>
    string? AuditLogDirectory { get; }

    RuntimeMode Mode { get; }

    void Pause();

    void Resume();

    Task ReloadConfigAsync();

    /// <summary>ADR 0033: Validate config without applying. Returns (valid, errorMessage).</summary>
    (bool valid, string? error) TryValidateConfig();
}
