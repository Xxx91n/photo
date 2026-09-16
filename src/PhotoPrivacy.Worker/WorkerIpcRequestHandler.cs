using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 票 05（A-005）：Worker IPC 请求处理（从 <c>WorkerIpcServerHostedService</c> 抽出，语义逐字保持）。
///
/// 本票唯一的行为改动：请求处理整体异步化。原 <c>HandleRequest</c> 是同步方法，
/// ReloadConfig 分支用 <c>_runtime.ReloadConfigAsync().GetAwaiter().GetResult()</c> 阻塞调用线程；
/// 它又被 accept 循环同步调用，于是整条循环被堵死数秒（ApplyConfigAsync：停 watcher →
/// 停 bridge 4s CTS → 重建 → 重启 watcher），而 UI 心跳读超时只有 3s
/// （WorkerIpcClient.ReadTimeoutMs）→ 一次配置热重载就足以让 UI 误判 Worker 掉线，
/// 破坏不变量④「驻守可观测」（不允许「看起来在跑实际没在跑」）。
/// 现在 <see cref="HandleAsync"/> 真 await，并由 <see cref="WorkerIpcServerLoop"/>
/// 在独立任务中驱动，后续 accept 与心跳探针不再被重载阻塞。
/// </summary>
public sealed class WorkerIpcRequestHandler
{
    private const int ProtocolVersion = 1;

    private readonly IWorkerIpcRuntime _runtime;
    private readonly ILogger<WorkerIpcRequestHandler> _logger;

    public WorkerIpcRequestHandler(IWorkerIpcRuntime runtime, ILogger<WorkerIpcRequestHandler> logger)
    {
        _runtime = runtime;
        _logger = logger;
    }

    /// <summary>
    /// Handle IPC request. Returns (response, shouldShutdown).
    /// ADR 0031: Shutdown is only allowed in CLI mode. Service/Background rejects it.
    /// ADR 0033: ReloadConfig validates new config before applying; failure preserves old config.
    /// ADR 0030: Response includes protocol version V field.
    /// </summary>
    public async Task<(WorkerIpcResponse response, bool shouldShutdown)> HandleAsync(
        WorkerIpcRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            return (new WorkerIpcResponse(false, Message: "invalid request", V: ProtocolVersion), false);
        }

        switch (request.Method)
        {
            case WorkerIpcMethods.Ping:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.GetStatus:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.GetExifToolVersion:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Pause:
                _runtime.Pause();
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Resume:
                _runtime.Resume();
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Shutdown:
                // ADR 0031: Mode-Scoped Shutdown — only CLI mode can trigger shutdown via IPC
                if (_runtime.Mode != RuntimeMode.Cli)
                {
                    return (new WorkerIpcResponse(false,
                        Message: "\u8bf7\u901a\u8fc7 systemctl/launchd \u505c\u6b62\u670d\u52a1",
                        Id: request.Id, V: ProtocolVersion), false);
                }
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Message: "shutdown", Id: request.Id, V: ProtocolVersion), true);

            case WorkerIpcMethods.ReloadConfig:
                try
                {
                    // ADR 0033: Validate config before applying
                    var (valid, error) = _runtime.TryValidateConfig();
                    if (!valid)
                    {
                        _logger.LogWarning("ReloadConfig validation failed: {Error}", error);
                        return (new WorkerIpcResponse(false,
                            Message: "\u914d\u7f6e\u9a8c\u8bc1\u5931\u8d25: " + error,
                            Id: request.Id, V: ProtocolVersion), false);
                    }

                    // 票 05（A-005）：真 await。禁止回退为 GetAwaiter().GetResult()/Result/Wait()
                    // —— 那会把 accept 循环重新堵死（守卫 WorkerIpcSyncOverAsyncGuardTests 锁死此约束）。
                    await _runtime.ReloadConfigAsync().ConfigureAwait(false);
                    return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ReloadConfig failed");
                    return (new WorkerIpcResponse(false,
                        Message: $"reload_failed: {ex.Message}",
                        Id: request.Id, V: ProtocolVersion), false);
                }

            case WorkerIpcMethods.GetRecentLogs:
                {
                    var logs = ReadRecentAuditLogs(50);
                    return (new WorkerIpcResponse(true, Logs: new RecentLogsDto(logs), Id: request.Id, V: ProtocolVersion), false);
                }

            default:
                return (new WorkerIpcResponse(false, Message: "unknown method", Id: request.Id, V: ProtocolVersion), false);
        }
    }

    private WorkerStatusDto BuildStatus()
    {
        return new WorkerStatusDto(
            IsPaused: _runtime.IsPaused,
            ExifToolVersion: _runtime.ExifToolVersion,
            WatchDirectory: _runtime.WatchDirectory,
            Mode: _runtime.Mode == RuntimeMode.Service ? "service" : "background");
    }

    /// <summary>
    /// ADR 0046: Read the tail N lines of today audit JSONL file.
    /// Returns empty array if the log directory or file is missing.
    /// </summary>
    private string[] ReadRecentAuditLogs(int maxLines)
    {
        try
        {
            var logDir = _runtime.AuditLogDirectory;
            if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
            {
                return [];
            }

            var auditPath = Path.Combine(logDir, $"audit-{DateTime.Today:yyyy-MM-dd}.jsonl");
            if (!File.Exists(auditPath))
            {
                return [];
            }

            var lines = File.ReadAllLines(auditPath);
            return lines.Length <= maxLines ? lines : lines[^maxLines..];
        }
        catch
        {
            return [];
        }
    }
}
