using System.Net.Sockets;
using System.Net;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Unix Domain Socket IPC transport for Linux/macOS.
/// Uses System.Net.Sockets with AddressFamily.Unix + UnixDomainSocketEndPoint.
/// </summary>
public sealed class UnixDomainSocketIpcTransport : IIpcTransport
{
    private readonly string _socketPath;
    private Socket? _listener;

    public UnixDomainSocketIpcTransport(string socketPath)
    {
        _socketPath = socketPath;
    }

    public string EndpointName => _socketPath;

    public Task ListenAsync(CancellationToken cancellationToken)
    {
        // ADR 0026: Stale socket cleanup — delete stale .sock file before bind
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { /* ignore */ }
        }

        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(_socketPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(254);

        // Set permissions so service user and GUI user (same group) can access
        try
        {
            File.SetUnixFileMode(_socketPath, System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupWrite | System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
        }
        catch { /* not on Unix */ }

        return Task.CompletedTask;
    }

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_listener is null, this);
        var client = await _listener.AcceptAsync(cancellationToken);
        return new NetworkStream(client, ownsSocket: true);
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cts.Token);
        return new NetworkStream(client, ownsSocket: true);
    }

    public ValueTask DisposeAsync()
    {
        bool ownsEndpoint = _listener is not null;
        _listener?.Dispose();
        _listener = null;
        // Best-effort cleanup of socket file — only by the endpoint owner (server side).
        // Client instances (per-call WorkerIpcClient.SendAsync transports) share the same socket
        // path; deleting it there unlinks the live server endpoint after the first call, making
        // every later connect fail (B11 finding: 2nd connect fails with AddressNotAvailable).
        if (ownsEndpoint)
        {
            try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { /* ignore */ }
        }
        return ValueTask.CompletedTask;
    }
}
