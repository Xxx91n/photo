using System.Net.Sockets;
using System.Net;
using System.IO.Pipes;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Cross-platform IPC transport abstraction.
/// Windows: NamedPipeServerStream. Linux/macOS: Unix Domain Socket.
/// </summary>
public interface IIpcTransport : IAsyncDisposable
{
    /// <summary>Bind the server endpoint and begin listening for connections.</summary>
    Task ListenAsync(CancellationToken cancellationToken);

    /// <summary>Wait for a single client connection. Returns a readable/writable Stream.</summary>
    Task<Stream> AcceptClientAsync(CancellationToken cancellationToken);

    /// <summary>Connect as a client to the given endpoint. Returns a readable/writable Stream.</summary>
    Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>The platform-specific endpoint name (pipe name or socket path).</summary>
    string EndpointName { get; }
}
