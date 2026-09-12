using System.IO.Pipes;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows named-pipe IPC transport. Falls back to FIFO on Unix (not recommended — use UnixDomainSocketIpcTransport instead).
/// </summary>
public sealed class NamedPipeIpcTransport : IIpcTransport
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _server;

    public NamedPipeIpcTransport(string pipeName)
    {
        _pipeName = pipeName;
    }

    public string EndpointName => _pipeName;

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // Named pipes don't need explicit bind/listen — the server is created per-connection.
        // We pre-create the first server instance here to match the IIpcTransport lifecycle.
        _server = CreatePipeServer(_pipeName);
        return Task.CompletedTask;
    }

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_server is null, this);
        await _server.WaitForConnectionAsync(cancellationToken);
        var connected = _server;
        // Create next server instance for the next accept call
        _server = CreatePipeServer(_pipeName);
        return connected;
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);
        return client;
    }

    public ValueTask DisposeAsync()
    {
        _server?.Dispose();
        _server = null;
        return ValueTask.CompletedTask;
    }

    // ponytail: plain NamedPipeServerStream — no ACL. WorldSid ReadWrite ACL was only needed for
    // cross-user service mode (service as LocalSystem, UI as user). Background mode runs same-user,
    // so ACL is unnecessary and NamedPipeServerStreamAcl.Create throws UnauthorizedAccessException
    // in single-file extract context, leaking pipe instances until the 254-instance limit is hit.
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);
    }
}
