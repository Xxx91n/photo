using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

public sealed class WorkerIpcServerHostedService : BackgroundService
{
    private readonly WorkerRuntimeContext _runtime;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WorkerIpcServerHostedService> _logger;

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
        var pipeName = _runtime.Mode == RuntimeMode.Service
            ? WorkerIpcEndpointNames.ServicePipe
            : WorkerIpcEndpointNames.BackgroundPipe;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName: pipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(stoppingToken);

                using var reader = new StreamReader(server);
                using var writer = new StreamWriter(server) { AutoFlush = true };
                var line = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcRequest);
                var response = HandleRequest(request);
                var responseText = JsonSerializer.Serialize(response, WorkerIpcJsonContext.Default.WorkerIpcResponse);
                await writer.WriteLineAsync(responseText.AsMemory(), stoppingToken);

                if (string.Equals(request?.Method, WorkerIpcMethods.Shutdown, StringComparison.Ordinal))
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

    private WorkerIpcResponse HandleRequest(WorkerIpcRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            return new WorkerIpcResponse(false, Message: "invalid request");
        }

        switch (request.Method)
        {
            case WorkerIpcMethods.Ping:
                return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);

            case WorkerIpcMethods.GetStatus:
                return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);

            case WorkerIpcMethods.GetExifToolVersion:
                return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);

            case WorkerIpcMethods.Pause:
                _runtime.Pause();
                return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);

            case WorkerIpcMethods.Resume:
                _runtime.Resume();
                return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);

            case WorkerIpcMethods.Shutdown:
                return new WorkerIpcResponse(true, Data: BuildStatus(), Message: "shutdown", Id: request.Id);

            case WorkerIpcMethods.ReloadConfig:
                try
                {
                    _runtime.ReloadConfigAsync().GetAwaiter().GetResult();
                    return new WorkerIpcResponse(true, Data: BuildStatus(), Id: request.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ReloadConfig failed");
                    return new WorkerIpcResponse(true, Data: BuildStatus(), Message: $"reload_failed: {ex.Message}", Id: request.Id);
                }

            default:
                return new WorkerIpcResponse(false, Message: "unknown method", Id: request.Id);
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
