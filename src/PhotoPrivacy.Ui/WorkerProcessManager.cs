using System.Diagnostics;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.Ui;

public sealed class WorkerProcessManager
{
    private readonly WorkerIpcClient _ipcClient;

    public WorkerProcessManager(WorkerIpcClient ipcClient)
    {
        _ipcClient = ipcClient;
    }

    public async Task<WorkerConnectionResult> ConnectOrLaunchAsync(
        string? workerExecutablePath,
        CancellationToken cancellationToken,
        Func<ServiceRuntimeState>? getServiceRuntimeState = null,
        string? configPath = null)
    {
        if (await _ipcClient.IsAliveAsync(WorkerIpcEndpointNames.ServicePipe, cancellationToken))
        {
            var serviceStatus = await GetStatusSafeAsync(WorkerIpcEndpointNames.ServicePipe, cancellationToken);

            return new WorkerConnectionResult(
                RuntimeKind: "service",
                EndpointName: WorkerIpcEndpointNames.ServicePipe,
                ShouldShowTrayIcon: false,
                Status: serviceStatus?.Data);
        }

        var serviceState = getServiceRuntimeState?.Invoke() ?? ServiceRuntimeState.NotInstalled;
        if (serviceState != ServiceRuntimeState.NotInstalled)
        {
            return new WorkerConnectionResult(
                RuntimeKind: "service",
                EndpointName: WorkerIpcEndpointNames.ServicePipe,
                ShouldShowTrayIcon: false,
                Status: null);
        }

        if (await _ipcClient.IsAliveAsync(WorkerIpcEndpointNames.BackgroundPipe, cancellationToken))
        {
            var backgroundStatus = await GetStatusSafeAsync(WorkerIpcEndpointNames.BackgroundPipe, cancellationToken);

            return new WorkerConnectionResult(
                RuntimeKind: "tray",
                EndpointName: WorkerIpcEndpointNames.BackgroundPipe,
                ShouldShowTrayIcon: true,
                Status: backgroundStatus?.Data);
        }

        if (!string.IsNullOrWhiteSpace(workerExecutablePath) && File.Exists(workerExecutablePath))
        {
            Process.Start(BuildBackgroundLaunchStartInfo(workerExecutablePath, configPath));
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await _ipcClient.IsAliveAsync(WorkerIpcEndpointNames.BackgroundPipe, cancellationToken))
            {
                var status = await GetStatusSafeAsync(WorkerIpcEndpointNames.BackgroundPipe, cancellationToken);

                return new WorkerConnectionResult(
                    RuntimeKind: "tray",
                    EndpointName: WorkerIpcEndpointNames.BackgroundPipe,
                    ShouldShowTrayIcon: true,
                    Status: status?.Data);
            }

            await Task.Delay(200, cancellationToken);
        }

        return new WorkerConnectionResult(
            RuntimeKind: "tray",
            EndpointName: WorkerIpcEndpointNames.BackgroundPipe,
            ShouldShowTrayIcon: true,
            Status: null);
    }

    public Task<WorkerIpcResponse?> GetStatusAsync(string endpointName, CancellationToken cancellationToken)
    {
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.GetStatus), cancellationToken);
    }

    /// <summary>
    /// ponytail: GetStatus with resilient IO downgrade. During first-launch the freshly spawned
    /// Worker can hit a short "zombie window": .NET Host bound the pipe, IsAlive(Ping) succeeded,
    /// but then config validation fails and the Worker exits — the next SendAsync(GetStatus) hits
    /// IOException("Pipe is broken") / SocketException / TimeoutException. ConnectingOrLaunch must
    /// treat these as "Worker not actually alive" (return null -> downgrade/continue) — NOT let the
    /// exception escape to UiProgram.Start's GetAwaiter().GetResult and kill the whole UI process.
    /// See ADR 0035 (IPC probe degrade-not-crash) and the release UI log 02:04:43 Fatal.
    /// </summary>
    private async Task<WorkerIpcResponse?> GetStatusSafeAsync(string endpointName, CancellationToken cancellationToken)
    {
        try
        {
            return await _ipcClient.SendAsync(
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
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Pause), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ResumeAsync(string endpointName, CancellationToken cancellationToken)
    {
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Resume), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ShutdownAsync(string endpointName, CancellationToken cancellationToken)
    {
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.Shutdown), cancellationToken);
    }

    public Task<WorkerIpcResponse?> ReloadConfigAsync(string endpointName, CancellationToken cancellationToken)
    {
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig), cancellationToken);
    }

    /// <summary>
    /// ADR 0046: Pull recent audit log lines from the worker via IPC.
    /// Used by UI to backfill missed log entries on reconnect.
    /// </summary>
    public Task<WorkerIpcResponse?> GetRecentLogsAsync(string endpointName, CancellationToken cancellationToken)
    {
        return _ipcClient.SendAsync(endpointName, new WorkerIpcRequest(WorkerIpcMethods.GetRecentLogs), cancellationToken);
    }

    /// <summary>
    /// 构建后台 Worker 启动参数。
    /// 使用 ArgumentList.Add 安全传递参数，避免字符串拼接导致的参数注入漏洞。
    /// </summary>
    public static ProcessStartInfo BuildBackgroundLaunchStartInfo(string workerExecutablePath, string? configPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // 使用 ArgumentList 安全传递参数，系统自动处理转义，防止参数注入。
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("background");

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configPath);
        }

        return startInfo;
    }
}

public sealed record WorkerConnectionResult(
    string RuntimeKind,
    string EndpointName,
    bool ShouldShowTrayIcon,
    WorkerStatusDto? Status);
