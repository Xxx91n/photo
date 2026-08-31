using System.Net.Sockets;
using System.Text.Json;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Ui;

/// <summary>
/// Single typed IPC entry point for every Worker call (issue 04 / C3a).
/// All protocol methods (WorkerIpcMethods list) are declared here and nowhere else;
/// WorkerProcessManager keeps process-launch duty only.
/// </summary>
public sealed class WorkerIpcClient
{
    private const int MaxResponseBytes = 64 * 1024; // 64 KB
    private const int ReadTimeoutMs = 3000;

    /// <summary>
    /// Send an IPC request to the worker. Uses cross-platform transport (ADR 0019).
    /// <paramref name="endpointName"/> is the pipe name (Windows) or socket path (Linux/macOS).
    /// </summary>
    public async Task<WorkerIpcResponse?> SendAsync(string endpointName, WorkerIpcRequest request, CancellationToken cancellationToken)
    {
        var transport = IpcTransportFactory.CreateServer(endpointName);
        try
        {
            using var stream = await transport.ConnectAsync(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest);
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload + "\n");
            await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            // ponytail: linked CTS with read timeout — if caller passes CancellationToken.None
            // (e.g. from UpdateServiceButtons GetAwaiter().GetResult() on UI thread), the read
            // must still time out instead of blocking the UI thread forever when Worker stalls.
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(ReadTimeoutMs);
            try
            {
                var line = await ReadBoundedLineAsync(stream, MaxResponseBytes, readCts.Token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return null;
                }

                return JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcResponse);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Read timed out — Worker accepted connection but never responded
                return null;
            }
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
        catch (System.Text.Json.JsonException)
        {
            // ponytail: probe must never kill the UI. Worker returned malformed/truncated JSON
            // (e.g. empty/1-byte response during shutdown race, stale pipe). Treat as "not alive".
            return false;
        }
    }

    public Task<WorkerIpcResponse?> GetStatusAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.GetStatus), cancellationToken);
    }

    /// <summary>
    /// ponytail: status probe with resilient IO downgrade (moved from WorkerProcessManager, issue 04).
    /// During first-launch the freshly spawned Worker can hit a short "zombie window": .NET Host
    /// bound the pipe, IsAliveAsync(Ping) succeeded, but then config validation fails and the
    /// Worker exits — the next SendAsync(GetStatus) hits IOException("Pipe is broken") /
    /// SocketException / TimeoutException. ConnectOrLaunchAsync must treat these as "Worker not
    /// actually alive" (return null -> downgrade/continue) — NOT let the exception escape and
    /// kill the whole UI process. See ADR 0035 (IPC probe degrade-not-crash) and the release UI
    /// log 02:04:43 Fatal.
    /// </summary>
    public async Task<WorkerIpcResponse?> ProbeStatusSafeAsync(string endpointName, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(
                endpointName,
                new WorkerIpcRequest(WorkerIpcMethods.GetStatus),
                cancellationToken);
        }
        catch (System.IO.IOException)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public Task<WorkerIpcResponse?> PauseAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Pause), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ResumeAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Resume), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ShutdownAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Shutdown), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ReloadConfigAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), cancellationToken);
    }

    /// <summary>
    /// ADR 0046: Pull recent audit log lines from the worker via IPC.
    /// Used by UI to backfill missed log entries on reconnect.
    /// </summary>
    public Task<WorkerIpcResponse?> GetRecentLogsAsync(string endpointName, CancellationToken cancellationToken)
    {
        return SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.GetRecentLogs), cancellationToken);
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
