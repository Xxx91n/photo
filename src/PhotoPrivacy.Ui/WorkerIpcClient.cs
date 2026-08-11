using System.Net.Sockets;
using System.Text.Json;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Ui;

public sealed class WorkerIpcClient
{
    private const int MaxResponseBytes = 64 * 1024; // 64 KB

    /// <summary>
    /// Send an IPC request to the worker. Uses cross-platform transport (ADR 0019).
    /// <paramref name="endpointName"/> is the pipe name (Windows) or socket path (Linux/macOS).
    /// </summary>
    public async Task<WorkerIpcResponse?> SendAsync(string endpointName, WorkerIpcRequest request, CancellationToken cancellationToken)
    {
        var transport = IpcTransportFactory.CreateServer(endpointName);
        try
        {
            using var stream = await transport.ConnectAsync(TimeSpan.FromMilliseconds(700), cancellationToken);

            var payload = JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest);
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload + "\n");
            await stream.WriteAsync(payloadBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var line = await ReadBoundedLineAsync(stream, MaxResponseBytes, cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            return JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcResponse);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    public async Task<bool> IsAliveAsync(string endpointName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Ping), cancellationToken);
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
        catch (SocketException)
        {
            return false;
        }
    }

    public Task<WorkerIpcResponse?> ReloadConfigAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), cancellationToken);
    }

    private static async Task<string?> ReadBoundedLineAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var byteBuf = new byte[1];
        while (buffer.Length < maxBytes)
        {
            var read = await stream.ReadAsync(byteBuf, cancellationToken);
            if (read == 0) break;
            if (byteBuf[0] == (byte)'\n') break;
            if (byteBuf[0] != (byte)'\r') buffer.WriteByte(byteBuf[0]);
        }
        if (buffer.Length >= maxBytes) return null;
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
