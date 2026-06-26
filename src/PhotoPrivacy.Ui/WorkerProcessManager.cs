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
            var serviceStatus = await _ipcClient.SendAsync(
                WorkerIpcEndpointNames.ServicePipe,
                new WorkerIpcRequest(WorkerIpcMethods.GetStatus),
                cancellationToken);

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
            var backgroundStatus = await _ipcClient.SendAsync(
                WorkerIpcEndpointNames.BackgroundPipe,
                new WorkerIpcRequest(WorkerIpcMethods.GetStatus),
                cancellationToken);

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
                var status = await _ipcClient.SendAsync(
                    WorkerIpcEndpointNames.BackgroundPipe,
                    new WorkerIpcRequest(WorkerIpcMethods.GetStatus),
                    cancellationToken);

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
