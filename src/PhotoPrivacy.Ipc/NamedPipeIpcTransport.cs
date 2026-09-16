using System.IO.Pipes;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Windows named-pipe IPC transport. Falls back to FIFO on Unix (not recommended — use UnixDomainSocketIpcTransport instead).
/// 票 04 / ADR 0067：首实例防抢占（FirstPipeInstance）+ 同用户边界（CurrentUserOnly）。
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
        // ADR 0067：防抢占语义只施加于这个首实例（accept 后预建的后续实例不得加标志）。
        _server = CreatePipeServer(_pipeName, isFirstInstance: true);
        return Task.CompletedTask;
    }

    public async Task<Stream> AcceptClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_server is null, this);
        await _server.WaitForConnectionAsync(cancellationToken);
        var connected = _server;
        // Create next server instance for the next accept call.
        // ADR 0067：后续实例不得携带 FirstPipeInstance —— 内核语义上仅首实例可声明，
        // 且管道名已由首实例占据，再加该标志会直接建例失败。
        _server = CreatePipeServer(_pipeName, isFirstInstance: false);
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
            options: ClientOptions);
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
    // ADR 0067：同用户边界改用 PipeOptions.CurrentUserOnly（非 ACL API）；
    // FirstPipeInstance 仅施加于 ListenAsync 预建的首实例。
    private static NamedPipeServerStream CreatePipeServer(string pipeName, bool isFirstInstance)
    {
        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            transmissionMode: PipeTransmissionMode.Byte,
            options: ServerOptions(isFirstInstance));
    }

    /// <summary>
    /// 服务端管道选项。CurrentUserOnly 与 FirstPipeInstance 均为 Windows 内核语义，
    /// 仅在 Windows 上施加；Unix 上 NamedPipeServerStream 为 FIFO 实现，
    /// 保持既有无标志语义（Unix 侧认证由 UnixDomainSocketIpcTransport 负责）。
    /// </summary>
    private static PipeOptions ServerOptions(bool isFirstInstance)
    {
        var options = PipeOptions.Asynchronous;
        if (!OperatingSystem.IsWindows())
        {
            return options;
        }

        options |= PipeOptions.CurrentUserOnly;
        if (isFirstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return options;
    }

    /// <summary>客户端管道选项：Windows 上要求同用户对端（CurrentUserOnly）。</summary>
    private static PipeOptions ClientOptions => OperatingSystem.IsWindows()
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;
}
