using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

public sealed class WorkerIpcServerHostedService : BackgroundService
{
    private const int MaxMessageBytes = 64 * 1024; // 64 KB
    private const int ProtocolVersion = 1;

    private readonly WorkerRuntimeContext _runtime;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WorkerIpcServerHostedService> _logger;
    private IIpcTransport? _transport;

    public WorkerIpcServerHostedService(
        WorkerRuntimeContext runtime,
        IHostApplicationLifetime lifetime,
        ILogger<WorkerIpcServerHostedService> logger)
    {
        _runtime = runtime;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = IpcTransportFactory.ResolveEndpoint(_runtime.Mode == RuntimeMode.Service);

        try
        {
            _transport = IpcTransportFactory.CreateServer(endpoint);
            await _transport.ListenAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bind IPC transport on {Endpoint}", endpoint);
            return;
        }

        await using (_transport)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var client = await _transport.AcceptClientAsync(stoppingToken);

                    var line = await ReadBoundedLineAsync(client, MaxMessageBytes, stoppingToken);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var request = JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcRequest);
                    var (response, shouldShutdown) = HandleRequest(request);
                    var responseText = JsonSerializer.Serialize(response, WorkerIpcJsonContext.Default.WorkerIpcResponse);
                    var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseText);
                    await client.WriteAsync(responseBytes, stoppingToken);
                    await client.FlushAsync(stoppingToken);

                    if (shouldShutdown)
                    {
                        _lifetime.StopApplication();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "worker ipc loop error");
                    await Task.Delay(100, stoppingToken);
                }
            }
        }
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

    /// <summary>
    /// Handle IPC request. Returns (response, shouldShutdown).
    /// ADR 0031: Shutdown is only allowed in CLI mode. Service/Background rejects it.
    /// ADR 0033: ReloadConfig validates new config before applying; failure preserves old config.
    /// ADR 0030: Response includes protocol version V field.
    /// </summary>
    private (WorkerIpcResponse response, bool shouldShutdown) HandleRequest(WorkerIpcRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            return (new WorkerIpcResponse(false, Message: "invalid request", V: ProtocolVersion), false);
        }

        switch (request.Method)
        {
            case WorkerIpcMethods.Ping:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.GetStatus:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.GetExifToolVersion:
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Pause:
                _runtime.Pause();
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Resume:
                _runtime.Resume();
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);

            case WorkerIpcMethods.Shutdown:
                // ADR 0031: Mode-Scoped Shutdown — only CLI mode can trigger shutdown via IPC
                if (_runtime.Mode != RuntimeMode.Cli)
                {
                    return (new WorkerIpcResponse(false,
                        Message: "\u8bf7\u901a\u8fc7 systemctl/launchd \u505c\u6b62\u670d\u52a1",
                        Id: request.Id, V: ProtocolVersion), false);
                }
                return (new WorkerIpcResponse(true, Data: BuildStatus(), Message: "shutdown", Id: request.Id, V: ProtocolVersion), true);

            case WorkerIpcMethods.ReloadConfig:
                try
                {
                    // ADR 0033: Validate config before applying
                    var (valid, error) = _runtime.TryValidateConfig();
                    if (!valid)
                    {
                        _logger.LogWarning("ReloadConfig validation failed: {Error}", error);
                        return (new WorkerIpcResponse(false,
                            Message: "\u914d\u7f6e\u9a8c\u8bc1\u5931\u8d25: " + error,
                            Id: request.Id, V: ProtocolVersion), false);
                    }
                    _runtime.ReloadConfigAsync().GetAwaiter().GetResult();
                    return (new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id, V: ProtocolVersion), false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ReloadConfig failed");
                    return (new WorkerIpcResponse(false,
                        Message: $"reload_failed: {ex.Message}",
                        Id: request.Id, V: ProtocolVersion), false);
                }

            default:
                return (new WorkerIpcResponse(false, Message: "unknown method", Id: request.Id, V: ProtocolVersion), false);
        }
    }

    private WorkerStatusDto BuildStatus()
    {
        return new WorkerStatusDto(
            IsPaused: _runtime.IsPaused,
            ExifToolVersion: _runtime.ExifToolVersion,
            WatchDirectory: _runtime.WatchDirectory,
            Mode: _runtime.Mode == RuntimeMode.Service ? "service" : "background");
    }
}
