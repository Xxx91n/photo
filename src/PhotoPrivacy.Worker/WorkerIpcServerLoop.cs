using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

/// <summary>
/// 票 05（A-005）：Worker IPC 连接循环（从 <c>WorkerIpcServerHostedService</c> 抽出，使行为测试
/// 能用内存假传输直接驱动「长重载不阻塞后续 accept」这条验收项）。
///
/// 相对原实现的两处改动：
/// 1) 每个连接交给独立任务处理（原实现是 <c>await using var client</c> 严格串行 + 循环体内
///    同步调用请求处理）——于是循环体内一旦出现 sync-over-async，整条循环被堵死，
///    后续 accept 与 UI 心跳探针全部拿不到服务；
/// 2) 读走 <see cref="IpcLineReader"/> 缓冲读（原为 <c>new byte[1]</c> 逐字节读）。
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

                // 票 05（A-005）：accept 后立即回到 accept——请求处理在独立任务中跑，
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
        try
        {
            await using (client)
            {
                var line = await IpcLineReader.ReadBoundedLineAsync(client, MaxMessageBytes, stoppingToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                var request = JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcRequest);
                var (response, shouldShutdown) = await handleAsync(request, stoppingToken);
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
            _logger.LogWarning(ex, "worker ipc client handling error");
        }
    }
}
