using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

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

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeSecurity = new PipeSecurity();
            var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            try
            {
                return NamedPipeServerStreamAcl.Create(
                    pipeName: pipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: 254,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);
            }
            catch
            {
                // Fallback: no ACL (e.g. container environment)
            }
        }
        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);
    }
}
