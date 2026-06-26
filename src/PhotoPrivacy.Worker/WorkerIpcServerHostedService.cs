using System.IO.Pipes;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Worker;

public sealed class WorkerIpcServerHostedService : BackgroundService
{
    /// <summary>
    /// 最大 IPC 消息字节数，防止恶意客户端发送超大消息导致 OOM。
    /// </summary>
    private const int MaxMessageBytes = 64 * 1024; // 64 KB

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
                // 使用 PipeSecurity 限制只有当前用户可以连接管道，防止跨用户 IPC 注入。
                using var server = CreatePipeServer(pipeName);

                await server.WaitForConnectionAsync(stoppingToken);

                // 使用有限缓冲区读取消息，防止 OOM 攻击。
                var line = await ReadBoundedLineAsync(server, MaxMessageBytes, stoppingToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize(line, WorkerIpcJsonContext.Default.WorkerIpcRequest);
                var response = HandleRequest(request);
                var responseText = JsonSerializer.Serialize(response, WorkerIpcJsonContext.Default.WorkerIpcResponse);

                // 写入响应
                var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseText);
                await server.WriteAsync(responseBytes, stoppingToken);
                await server.FlushAsync(stoppingToken);

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

    /// <summary>
    /// 创建带访问控制的命名管道服务器。限制只有当前用户可以连接。
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser is not null)
                {
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        currentUser,
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));
                }

                return NamedPipeServerStreamAcl.Create(
                    pipeName: pipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);
            }
            catch
            {
                // 回退：如果 ACL 操作失败（如容器环境），使用无 ACL 版本
            }
        }

        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);
    }

    /// <summary>
    /// 从管道流中读取一行，限制最大字节数防止 OOM DoS。
    /// </summary>
    private static async Task<string?> ReadBoundedLineAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var byteBuf = new byte[1];
        while (buffer.Length < maxBytes)
        {
            var read = await stream.ReadAsync(byteBuf, cancellationToken);
            if (read == 0)
            {
                break; // 连接关闭
            }

            if (byteBuf[0] == (byte)'\n')
            {
                break; // 行结束
            }

            if (byteBuf[0] != (byte)'\r')
            {
                buffer.WriteByte(byteBuf[0]);
            }
        }

        if (buffer.Length >= maxBytes)
        {
            return null; // 消息过大，丢弃
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
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
