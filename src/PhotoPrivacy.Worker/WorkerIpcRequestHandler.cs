using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// Worker IPC 请求处理（从 <c>WorkerIpcServerHostedService</c> 抽出，语义逐字保持）。
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
    private const int ProtocolVersion = WorkerIpcProtocol.Version;

    /// <summary>
    /// 版本补探的最小间隔（#86）。ExifTool 不可用时 UI 的 1 s 轮询会退化成每秒一次启动尝试
    /// 与一行告警；限流后同一降级态最多每 5 s 重试一次。
    /// </summary>
    private const int VersionProbeMinIntervalSeconds = 5;

    private readonly IWorkerIpcRuntime _runtime;
    private readonly ILogger<WorkerIpcRequestHandler> _logger;

    private int _versionProbeInFlight;
    private long _nextVersionProbeAtTicks;

    public WorkerIpcRequestHandler(IWorkerIpcRuntime runtime, ILogger<WorkerIpcRequestHandler> logger)
    {
        _runtime = runtime;
        _logger = logger;
    }

    /// <summary>
    /// Handle IPC request. Returns (response, shouldShutdown).
    /// Shutdown is only allowed in CLI mode. Service/Background rejects it.
    /// ReloadConfig 先校验再应用：校验失败不触碰运行期组件（旧配置保留）；应用阶段失败如实回
    /// <c>ok:false</c> 并带原因，worker 侧已回滚到上一份生效配置（#112）；只有回滚本身也失败时
    /// 才需下一次成功重载或重启恢复。
    /// Response includes protocol version V field.
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
                // Mode-Scoped Shutdown — only CLI mode can trigger shutdown via IPC
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
                    // Validate config before applying
                    var (valid, error) = _runtime.TryValidateConfig();
                    if (!valid)
                    {
                        SafeLog.Try(() => _logger.LogWarning("ReloadConfig validation failed: {Error}", error));
                        return (new WorkerIpcResponse(false,
                            Message: "\u914d\u7f6e\u9a8c\u8bc1\u5931\u8d25: " + error,
                            Id: request.Id, V: ProtocolVersion), false);
                    }

                    // 真 await。禁止回退为 GetAwaiter().GetResult()/Result/Wait()
                    // —— 那会把 accept 循环重新堵死（守卫 WorkerIpcSyncOverAsyncGuardTests 锁死此约束）。
                    await _runtime.ReloadConfigAsync().ConfigureAwait(false);
                    return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);
                }
                catch (Exception ex)
                {
                    // 日志失败不得吃掉回包：EventLog provider 抛异常时仍要带回 reload_failed 原因（#85）。
                    SafeLog.Try(() => _logger.LogWarning(ex, "ReloadConfig failed"));
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

    /// <summary>
    /// 构造状态快照。版本仍为 unknown 时顺手驱动一次后台补探（#86），但当前响应只返回快照。
    /// </summary>
    private WorkerStatusDto BuildStatus()
    {
        var exifToolVersion = _runtime.ExifToolVersion;

        // #86：桥惰性启动，首个文件到达前版本恒为 unknown。状态消费者出现时补探一次，
        // 但绝不把响应挂在这条探针上——本次响应仍返回当前快照，真实版本由下一拍轮询带出。
        if (ExifToolVersionText.IsUnknown(exifToolVersion))
        {
            TriggerExifToolVersionProbe();
        }

        return new WorkerStatusDto(
            IsPaused: _runtime.IsPaused,
            ExifToolVersion: exifToolVersion,
            WatchDirectory: _runtime.WatchDirectory,
            Mode: _runtime.Mode == RuntimeMode.Service ? "service" : "background");
    }

    /// <summary>
    /// 版本补探（#86）：状态快照仍为 <see cref="ExifToolVersionText.Unknown"/> 时，
    /// 在后台驱动一次版本解析（未启动则冷启动、已启动则重探冷启动时超时的那次）。
    ///
    /// 两道闸：in-flight 门闩保证轮询不叠加启动尝试；最小间隔保证降级态不演变成每秒重试。
    /// 失败只记降级告警，版本继续上报 unknown，下一次到点重试。
    /// </summary>
    // Design(#82): 版本解析由状态消费者驱动（按需冷启动 + 补探），状态响应内容与字段语义不变。
    private void TriggerExifToolVersionProbe()
    {
        if (Interlocked.CompareExchange(ref _versionProbeInFlight, 1, 0) != 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var dueAt = Volatile.Read(ref _nextVersionProbeAtTicks);
        if (now < dueAt)
        {
            Interlocked.Exchange(ref _versionProbeInFlight, 0);
            return;
        }

        Volatile.Write(ref _nextVersionProbeAtTicks, now + (VersionProbeMinIntervalSeconds * 1000L));
        _ = RunExifToolVersionProbeAsync();
    }

    /// <summary>
    /// 在后台执行一次版本解析；异常降级为 warning（停机取消不告警），结束后释放 in-flight 门闩。
    /// </summary>
    private async Task RunExifToolVersionProbeAsync()
    {
        try
        {
            // 不随单次 IPC 请求取消：补探是 worker 级动作，连接断开不应中断它。
            // worker 自己的停机信号由 runtime 侧链接，停机后不会再把桥拉起来。
            await _runtime.EnsureExifToolVersionAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 停机取消——不是降级，不告警。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ExifTool version probe failed; exifToolVersion stays '{VersionText}': {Message}",
                ExifToolVersionText.Unknown,
                ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _versionProbeInFlight, 0);
        }
    }

    /// <summary>
    /// Read the tail N lines of today audit JSONL file.
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
