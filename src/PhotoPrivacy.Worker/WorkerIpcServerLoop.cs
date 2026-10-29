using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// Worker IPC 连接循环（从 <c>WorkerIpcServerHostedService</c> 抽出，使行为测试
/// 能用内存假传输直接驱动「长重载不阻塞后续 accept」这条验收项）。
///
/// 相对原实现的三处改动：
/// 1) 每个连接交给独立任务处理（原实现是 <c>await using var client</c> 严格串行 + 循环体内
///    同步调用请求处理）——于是循环体内一旦出现 sync-over-async，整条循环被堵死，
///    后续 accept 与 UI 心跳探针全部拿不到服务；
/// 2) 读走 <see cref="IpcLineReader"/> 缓冲读（原为 <c>new byte[1]</c> 逐字节读）；
/// 3) 请求处理链抛异常时仍写出 <c>ok:false</c> 兜底响应，且本类日志调用走 best-effort
///    （<see cref="SafeLog"/>）——响应是否送达不得取决于日志 provider 是否健康（#85）。
///
/// 退出前 best-effort 排空在途连接，避免传输释放与在途读写在停机窗口竞争（不变量②）。
/// </summary>
public sealed class WorkerIpcServerLoop
{
    private const int MaxMessageBytes = 64 * 1024; // 64 KB

    private readonly ILogger<WorkerIpcServerLoop> _logger;
    private readonly ConcurrentDictionary<long, Task> _inFlight = new();
    private long _connectionSequence;

    public WorkerIpcServerLoop(ILogger<WorkerIpcServerLoop> logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(
        IIpcTransport transport,
        Func<WorkerIpcRequest?, CancellationToken, Task<(WorkerIpcResponse response, bool shouldShutdown)>> handleAsync,
        Action requestShutdown,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await transport.AcceptClientAsync(stoppingToken);

                // accept 后立即回到 accept——请求处理在独立任务中跑，
                // 因此一次数秒级的 ReloadConfig 不再阻塞后续连接（心跳探针仍被服务）。
                var connectionId = Interlocked.Increment(ref _connectionSequence);
                var task = HandleClientAsync(client, handleAsync, requestShutdown, stoppingToken);
                _inFlight[connectionId] = task;
                _ = ForgetWhenCompletedAsync(connectionId, task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "worker ipc accept loop error");
                try
                {
                    await Task.Delay(100, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await DrainInFlightAsync();
    }

    private async Task ForgetWhenCompletedAsync(long connectionId, Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // HandleClientAsync 已自吞全部异常；此处仅为保证登记表不泄漏。
        }

        _inFlight.TryRemove(connectionId, out _);
    }

    private async Task DrainInFlightAsync()
    {
        var pending = _inFlight.Values.ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        catch
        {
            // best-effort：停机路径上的排空失败不得反噬宿主停机。
        }
    }

    private async Task HandleClientAsync(
        Stream client,
        Func<WorkerIpcRequest?, CancellationToken, Task<(WorkerIpcResponse response, bool shouldShutdown)>> handleAsync,
        Action requestShutdown,
        CancellationToken stoppingToken)
    {
        WorkerIpcRequest? request = null;
        try
        {
            await using (client)
            {
                var line = await IpcLineReader.ReadBoundedLineAsync(client, MaxMessageBytes, stoppingToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                request = JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcRequest);

                var shouldShutdown = false;
                WorkerIpcResponse response;
                try
                {
                    (response, shouldShutdown) = await handleAsync(request, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机取消路径：不再回包（与旧行为一致）。
                    return;
                }
                catch (Exception ex)
                {
                    // 服务模式 EventLog provider 写失败会让 catch 里的 ILogger 再抛、异常逃出本方法，
                    // 响应写入被跳过，客户端只能等到读超时——这正是服务管道 ReloadConfig 挂起的直接机制。
                    // 因此请求处理链的任何异常都必须仍回一条 ok:false。
                    // Design(#85): 处理链异常必须仍写出 ok:false 响应；本类日志调用失败只降级不反噬。
                    SafeLog(() => _logger.LogWarning(ex, "worker ipc request handling fault"));
                    response = new WorkerIpcResponse(
                        false,
                        Message: "internal_error",
                        Id: request?.Id,
                        V: WorkerIpcProtocol.Version);
                }

                var responseText = JsonSerializer.Serialize(response, WorkerIpcJsonContext.Default.WorkerIpcResponse);
                var responseBytes = Encoding.UTF8.GetBytes(responseText);
                await client.WriteAsync(responseBytes, stoppingToken);
                await client.FlushAsync(stoppingToken);

                if (shouldShutdown)
                {
                    requestShutdown();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停机取消路径。
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogWarning(ex, "worker ipc client handling error"));
        }
    }

    /// <summary>
    /// 日志失败只降级、不反噬：服务模式下 EventLog provider 写失败会让 MEL <c>Logger.Log</c>
    /// 直接抛（#85），连接循环的响应与 accept 语义不得被日志带崩。
    /// </summary>
    private static void SafeLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // best-effort：日志写失败静默丢弃，不打断连接处理。
        }
    }
}
