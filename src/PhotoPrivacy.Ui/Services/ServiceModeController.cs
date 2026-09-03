using PhotoPrivacy.Ui.Localization;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 服务模式编排状态机（issue 06 / C1）：从 MainWindow.axaml.cs 抽取的服务安装/卸载/启停
/// 与运行时模式切换编排。MainWindow 只转发点击事件并读取其产生的视图状态。
/// 依赖缝：IServiceManagerOps（ServiceManager 包装）、WorkerProcessManager、WorkerIpcClient
/// 以及 BackgroundUiOptions 上的既有 Func 委托，均可 fake 注入单测。
/// </summary>
public sealed class ServiceModeController
{
    private readonly IServiceManagerOps _serviceManager;
    private readonly WorkerProcessManager _workerManager;
    private readonly WorkerIpcClient _workerIpc;
    private readonly IUiHost _host;
    private readonly IViewModelView _view;

    private BackgroundUiOptions? _options;
    private bool _isSwitchingMode;

    public ServiceModeController(
        IServiceManagerOps serviceManager,
        WorkerProcessManager workerManager,
        WorkerIpcClient workerIpc,
        IUiHost host,
        IViewModelView view)
    {
        _serviceManager = serviceManager;
        _workerManager = workerManager;
        _workerIpc = workerIpc;
        _host = host;
        _view = view;
    }

    /// <summary>由 MainWindow.InitializeRuntime 绑定真实运行时选项；null 表示 UI 未就绪，全部编排为 no-op。</summary>
    public void Attach(BackgroundUiOptions options)
    {
        _options = options;
    }

    /// <summary>服务当前状态文本（ViewModel 初始化 / RefreshServiceStatus 用，不触碰视图）。</summary>
    public string StatusText => _serviceManager.GetStatusText();

    /// <summary>是否正处于模式切换事务中（暂停读取等旁路观察用）。</summary>
    public bool IsSwitchingMode => _isSwitchingMode;

    /// <summary>模式切换轮询循环（启动时由 MainWindow 用 Task.Run 拉起）。</summary>
    public async Task PollServiceModeTransitionAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);

            var options = _options;
            if (options is null || _isSwitchingMode)
            {
                continue;
            }

            var state = options.GetServiceRuntimeState();

            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && !await options.IsWorkerAliveAsync(token).ConfigureAwait(false))
            {
                await SwitchToDefaultModeAsync(token).ConfigureAwait(false);
                return;
            }

            if (string.Equals(options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase)
                && ServiceUiPolicy.ShouldSwitchFromServiceShellToTray(state))
            {
                await SwitchToDefaultModeAsync(token).ConfigureAwait(false);
                return;
            }

            if (string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && ServiceUiPolicy.ShouldSwitchFromTrayToServiceShell(state))
            {
                await SwitchToDefaultModeAsync(token).ConfigureAwait(false);
                return;
            }

            _host.Post(() => _view.UpdateServiceButtons());
        }
    }

    public async Task InstallAsync()
    {
        var options = _options;
        var workerExecutablePath = ResolveServiceWorkerExecutablePath(options);
        if (options is null || string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            _view.SetServiceStatusText(LocalizationService.Instance.Get("msg.worker_not_found_detail"));
            UpdateServiceButtons();
            return;
        }

        SetServiceButtonsBusy(isBusy: true);

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Install(workerExecutablePath, options.ConfigPath), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);
        _view.SetCurrentMode(MapModeLabel(options.RuntimeKind));

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterInstall(result))
        {
            _ = Task.Run(async () =>
            {
                try { await SwitchToServiceModeAfterInstallAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { UiDiagnosticLog.Write("SwitchToServiceMode failed: " + ex.Message); }
            });
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    public async Task UninstallAsync()
    {
        SetServiceButtonsBusy(isBusy: true);
        _view.SetUninstallingStatus(_serviceManager.GetStatusText(), LocalizationService.Instance.Get("service.uninstalling"));

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Uninstall(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);
        _view.SetCurrentMode(MapModeLabel(_options?.RuntimeKind ?? "tray"));

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterUninstall(result))
        {
            _ = Task.Run(async () =>
            {
                try { await EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { UiDiagnosticLog.Write("EnsureTrayWorker failed: " + ex.Message); }
            });
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    public async Task StartAsync()
    {
        var options = _options;
        var configPath = options?.ConfigPath;
        var workerExecutablePath = ResolveServiceWorkerExecutablePath(options);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            ApplyServiceResult(ServiceCommandResult.Failed(LocalizationService.Instance.Get("msg.worker_not_found_detail")));
            return;
        }

        SetServiceButtonsBusy(isBusy: true);

        var requiresTrayShutdown = options is not null && string.Equals(
            options.WorkerEndpointName,
            PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
            StringComparison.Ordinal);

        if (requiresTrayShutdown)
        {
            var trayShutdownDone = await ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken.None).ConfigureAwait(false);
            if (!trayShutdownDone)
            {
                ApplyServiceResult(ServiceCommandResult.Failed(LocalizationService.Instance.Get("msg.tray_worker_running")));
                SetServiceButtonsBusy(isBusy: false);
                return;
            }
        }

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Start(workerExecutablePath, configPath), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);
        _view.SetCurrentMode(MapModeLabel(options?.RuntimeKind ?? "tray"));

        if (result.Status == ServiceCommandStatus.Success)
        {
            _ = Task.Run(async () =>
            {
                try { await SwitchToServiceModeAfterInstallAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { UiDiagnosticLog.Write("SwitchToServiceMode failed: " + ex.Message); }
            });
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    public async Task StopAsync()
    {
        SetServiceButtonsBusy(isBusy: true);

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.StopService(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);

        var options = _options;
        if (options is not null)
        {
            var state = _serviceManager.GetRuntimeState();
            if (state != ServiceRuntimeState.NotInstalled)
            {
                options.UpdateRuntimeState("service", options.WorkerEndpointName, useTrayIcon: false);
            }
        }

        _view.SetCurrentMode(MapModeLabel(options?.RuntimeKind ?? "service"));

        SetServiceButtonsBusy(isBusy: false);
    }

    /// <summary>刷新按钮可用性 + 状态文本（InitializeRuntime、RefreshServiceStatus 与轮询共用）。</summary>
    public void UpdateServiceButtons()
    {
        var options = _options;
        if (!OperatingSystem.IsWindows())
        {
            _view.SetServiceButtons(ServiceButtonStates.AllDisabled);
            return;
        }

        var state = _serviceManager.GetRuntimeState();
        var buttonState = ServiceUiPolicy.BuildButtonState(state);
        _view.SetServiceButtons(buttonState);

        _view.SetServiceStatusText(_serviceManager.GetStatusText());
        if (options is not null)
        {
            var paused = false;
            var shouldReadTrayPauseStatus =
                !_isSwitchingMode
                && string.Equals(options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    options.WorkerEndpointName,
                    PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
                    StringComparison.Ordinal);

            // ponytail: skip sync-over-async pause read on UI thread — causes deadlock when Worker IPC stalls.
            // RuntimeStatus is updated by background poll tasks; initial state defaults to false.
            if (shouldReadTrayPauseStatus)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var p = await options.IsPausedAsync(CancellationToken.None).ConfigureAwait(false);
                        _host.Post(() =>
                        {
                            if (!_isSwitchingMode)
                            {
                                _view.SetRuntimeStatus(BuildRuntimeStatusText(options.RuntimeKind, state, p));
                            }
                        });
                    }
                    catch { }
                });
            }

            _view.SetRuntimeStatus(BuildRuntimeStatusText(options.RuntimeKind, state, paused));
        }
    }

    private void ApplyServiceResult(ServiceCommandResult result)
    {
        var status = _serviceManager.GetStatusText();
        var text = result.Status switch
        {
            ServiceCommandStatus.Success => status + " | " + result.Message,
            ServiceCommandStatus.Skipped => LocalizationService.Instance.Get("service.status_format_skip", status, result.Message),
            ServiceCommandStatus.ElevationCancelled => LocalizationService.Instance.Get("service.status_format_cancel", status, result.Message),
            ServiceCommandStatus.Failed => LocalizationService.Instance.Get("service.status_format_fail", status, result.Message),
            _ => status
        };

        _view.SetServiceStatusText(text);
        UpdateServiceButtons();
    }

    private async Task SwitchToDefaultModeAsync(CancellationToken token, Func<ServiceRuntimeState>? getServiceRuntimeStateOverride = null)
    {
        var options = _options;
        if (options is null || _isSwitchingMode)
        {
            return;
        }

        _isSwitchingMode = true;

        try
        {
            var next = getServiceRuntimeStateOverride is null
                ? await options.ConnectOrLaunchWorkerAsync(token).ConfigureAwait(false)
                : await _workerManager.ConnectOrLaunchAsync(
                    ResolveServiceWorkerExecutablePath(options),
                    token,
                    getServiceRuntimeState: getServiceRuntimeStateOverride).ConfigureAwait(false);

            options.UpdateRuntimeState(next.RuntimeKind, next.EndpointName, next.ShouldShowTrayIcon && !options.HideTrayIcon);

            _view.SetModeAndRuntimeStatus(
                MapModeLabel(options.RuntimeKind),
                BuildRuntimeStatusText(options.RuntimeKind, options.GetServiceRuntimeState(), next.Status?.IsPaused ?? false),
                NormalizeExifToolStatus(next.Status?.ExifToolVersion));

            var isServiceMode = string.Equals(options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase);
            _host.SetPauseResumeAvailability(!isServiceMode);

            if (options.UseTrayIcon)
            {
                _host.EnsureTrayVisible();
            }
            else
            {
                _host.DisposeTray();
            }
        }
        catch
        {
            // best effort mode switch
        }
        finally
        {
            _isSwitchingMode = false;
        }
    }

    public async Task SwitchToServiceModeAfterInstallAsync(CancellationToken token)
    {
        var options = _options;
        if (options is null)
        {
            return;
        }

        await ShutdownTrayWorkerForServiceSwitchAsync(token).ConfigureAwait(false);
        await SwitchToDefaultModeAsync(token).ConfigureAwait(false);

        options.UpdateRuntimeState("service", PhotoPrivacy.Ipc.WorkerIpcEndpointNames.ServicePipe, useTrayIcon: false);

        _host.Post(() =>
        {
            _view.SetModeAndRuntimeStatus(
                MapModeLabel(options.RuntimeKind),
                BuildRuntimeStatusText(options.RuntimeKind, options.GetServiceRuntimeState(), false),
                null);

            _host.DisposeTray();
            _host.ShowAndActivateWindow();
        });
    }

    public async Task<bool> ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken token)
    {
        var options = _options;
        if (options is null)
        {
            return false;
        }

        var endpoint = options.WorkerEndpointName;
        if (!string.Equals(endpoint, PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var status = await _workerIpc.GetStatusAsync(endpoint, token).ConfigureAwait(false);
            if (status is null)
            {
                return true;
            }

            await _workerIpc.ShutdownAsync(endpoint, token).ConfigureAwait(false);

            for (var i = 0; i < 15; i++)
            {
                if (!await _workerIpc.IsAliveAsync(PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe, token).ConfigureAwait(false))
                {
                    return true;
                }

                await Task.Delay(200, token).ConfigureAwait(false);
            }

            return false;
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write("ShutdownTrayWorkerForServiceSwitchAsync failed: " + ex.Message);
            return false;
        }
    }

    public async Task EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken token)
    {
        var options = _options;
        await SwitchToDefaultModeAsync(token, getServiceRuntimeStateOverride: () => ServiceRuntimeState.NotInstalled).ConfigureAwait(false);

        if (options is null)
        {
            return;
        }

        options.UpdateRuntimeState("tray", PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe, !options.HideTrayIcon);

        _host.Post(() =>
        {
            try
            {
                _view.SetModeAndRuntimeStatus(
                    MapModeLabel(options.RuntimeKind),
                    BuildRuntimeStatusText(options.RuntimeKind, options.GetServiceRuntimeState(), false),
                    null);

                _host.SetPauseResumeAvailability(true);

                if (options.UseTrayIcon)
                {
                    _host.EnsureTrayVisible();
                }

                _host.ShowAndActivateWindow();
            }
            catch (Exception ex)
            {
                UiDiagnosticLog.Write("EnsureTrayWorkerAfterServiceUninstallAsync UI post failed: " + ex.Message);
            }
        });
    }

    private void SetServiceButtonsBusy(bool isBusy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (isBusy)
        {
            _view.SetServiceButtons(ServiceButtonStates.AllDisabled);
            return;
        }

        UpdateServiceButtons();
    }

    public static string? ResolveServiceWorkerExecutablePath(BackgroundUiOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options.WorkerExecutablePath) && File.Exists(options.WorkerExecutablePath))
        {
            return options.WorkerExecutablePath;
        }

        var workerName = OperatingSystem.IsWindows() ? "PhotoPrivacyWorker.exe" : "PhotoPrivacyWorker";
        var candidate = Path.Combine(AppContext.BaseDirectory, workerName);
        return File.Exists(candidate) ? candidate : null;
    }

    public static string MapModeLabel(string runtimeKind)
    {
        if (string.Equals(runtimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Instance.Get("mode.service");
        }

        if (string.Equals(runtimeKind, "tray", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Instance.Get("mode.tray");
        }

        return runtimeKind;
    }

    public static string BuildRuntimeStatusText(string runtimeKind, ServiceRuntimeState state, bool isPaused)
    {
        if (string.Equals(runtimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            return state is ServiceRuntimeState.Running or ServiceRuntimeState.StartPending or ServiceRuntimeState.ContinuePending
                ? LocalizationService.Instance.Get("status.service_running")
                : LocalizationService.Instance.Get("status.service_stopped");
        }

        return isPaused ? LocalizationService.Instance.Get("status.tray_paused") : LocalizationService.Instance.Get("status.tray_running");
    }

    public static string NormalizeExifToolStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Instance.Get("status.exiftool_not_found");
        }

        var text = raw.Trim();
        return text.StartsWith("ExifTool", StringComparison.OrdinalIgnoreCase)
            ? text
            : "ExifTool v" + text + " ✓";
    }
}