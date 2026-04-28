using System.IO.Pipes;
using System.Text.Json;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Ui;

public sealed class WorkerIpcClient
{
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

        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };

        var payload = JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest);
        await writer.WriteLineAsync(payload.AsMemory(), timeoutCts.Token).ConfigureAwait(false);

        var line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
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
        catch
        {
            return false;
        }
    }

    public Task<WorkerIpcResponse?> ReloadConfigAsync(string pipeName, CancellationToken cancellationToken)
    {
        return SendAsync(pipeName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), cancellationToken);
    }
}
