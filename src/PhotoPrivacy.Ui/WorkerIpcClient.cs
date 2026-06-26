using System.IO.Pipes;
using System.Text.Json;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Ui;

public sealed class WorkerIpcClient
{
    /// <summary>
    /// 最大响应消息字节数，防止恶意服务器发送超大消息导致 OOM。
    /// </summary>
    private const int MaxResponseBytes = 64 * 1024; // 64 KB

    public async Task<WorkerIpcResponse?> SendAsync(string pipeName, WorkerIpcRequest request, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(700));
        await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest);
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload + "\n");
        await client.WriteAsync(payloadBytes, timeoutCts.Token).ConfigureAwait(false);
        await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

        // 使用有限缓冲区读取响应，防止 OOM 攻击。
        var line = await ReadBoundedLineAsync(client, MaxResponseBytes, timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcResponse);
    }

    public async Task<bool> IsAliveAsync(string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(pipeName, new WorkerIpcRequest(WorkerIpcMethods.Ping), cancellationToken);
            return response is { Ok: true };
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task<WorkerIpcResponse?> ReloadConfigAsync(string pipeName, CancellationToken cancellationToken)
    {
        return SendAsync(pipeName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), cancellationToken);
    }

    /// <summary>
    /// 从管道流中读取一行，限制最大字节数防止 OOM DoS。
    /// </summary>
    private static async Task<string?> ReadBoundedLineAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var byteBuf = new byte[1];
        while (buffer.Length < maxBytes)
        {
            var read = await stream.ReadAsync(byteBuf, cancellationToken);
            if (read == 0)
            {
                break; // 连接关闭
            }

            if (byteBuf[0] == (byte)'\n')
            {
                break; // 行结束
            }

            if (byteBuf[0] != (byte)'\r')
            {
                buffer.WriteByte(byteBuf[0]);
            }
        }

        if (buffer.Length >= maxBytes)
        {
            return null; // 消息过大，丢弃
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
