using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Views;

public partial class MainWindow : Window
{
    private AuditTailService? _auditTail;
    private TrayHost? _trayHost;
    private ExifToolVersionSnapshot? _versionSnapshot;
    private CancellationTokenSource? _versionPollCts;
    private Task? _versionPollTask;
    private BackgroundUiOptions? _options;
    private readonly ServiceManager _serviceManager = new();
    private readonly WorkerProcessManager _workerManager = new(new WorkerIpcClient());
    private CancellationTokenSource? _serviceModePollCts;
    private Task? _serviceModePollTask;
    private bool _isSwitchingMode;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void InitializeRuntime(BackgroundUiOptions options)
    {
        _options = options;
        var effectiveConfig = LoadConfigOrDefault(options.ConfigPath);
        var hotFolder = effectiveConfig?.Watch.HotFolder;
        var exifToolPathFromConfig = effectiveConfig?.ExifTool.Path;
        var viewModel = DataContext as MainWindowViewModel;

        if (viewModel is not null)
        {
            viewModel.CurrentMode = MapModeLabel(options.RuntimeKind);
            _versionSnapshot = new ExifToolVersionSnapshot(() =>
            {
                try
                {
                    return options.GetExifToolVersionAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    return "unknown";
                }
            });
            viewModel.ExifToolVersion = NormalizeExifToolStatus(_versionSnapshot.ReadInitial());
            viewModel.ShowDetailedEvents = false;
            viewModel.RuntimeStatus = BuildRuntimeStatusText(options.RuntimeKind, options.GetServiceRuntimeState(), false);
            viewModel.ShowServiceManagerTab = OperatingSystem.IsWindows();
            viewModel.ServiceStatus = _serviceManager.GetStatusText();

            if (effectiveConfig is not null)
            {
                viewModel.ExifToolPath = effectiveConfig.ExifTool.Path;
                viewModel.BackupEnabled = effectiveConfig.Backup.Enabled;
                viewModel.LogEnabled = effectiveConfig.Audit.DiagnosticMode;
                viewModel.HotFolderPath = effectiveConfig.Watch.HotFolder;
                viewModel.HideGuiOnStartup = effectiveConfig.Ui.HideMainWindowOnStartup;
            }

            UpdateServiceButtons(viewModel);
        }

        ServiceManagerTab.IsVisible = OperatingSystem.IsWindows();

        PauseResumeButton.Click += OnPauseResumeClick;
        ClearLogsButton.Click += OnClearLogsClick;
        OpenConfigDirButton.Click += OnOpenConfigDirClick;
        RefreshServiceStatusButton.Click += OnRefreshServiceStatusClick;
        if (this.FindControl<Button>("OpenServiceManagerTabButton") is { } openServiceManagerTabButton)
        {
            openServiceManagerTabButton.Click += OnOpenServiceManagerTabClick;
        }

        InstallServiceButton.Click += OnInstallServiceClick;
        UninstallServiceButton.Click += OnUninstallServiceClick;
        StartServiceButton.Click += OnStartServiceClick;
        StopServiceButton.Click += OnStopServiceClick;
        if (this.FindControl<Button>("SaveConfigButton") is { } saveConfigButton)
        {
            saveConfigButton.Click += OnSaveConfigClick;
        }

        _auditTail = new AuditTailService(
            hotFolder: hotFolder ?? Path.GetDirectoryName(options.AuditDirectory) ?? AppContext.BaseDirectory,
            onEntry: entry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (viewModel is not null)
                    {
                        viewModel.AppendLog(entry);
                    }
                });
            },
            includeDetailedEvents: () => viewModel?.ShowDetailedEvents ?? false,
            onExifToolExePathDetected: exePath => _ = ResolveExifToolVersionAsync(exePath ?? exifToolPathFromConfig));

        _auditTail.Start();
        _ = ResolveExifToolVersionAsync(exifToolPathFromConfig);

        _versionPollCts = new CancellationTokenSource();
        _versionPollTask = Task.Run(() => PollVersionAsync(_versionPollCts.Token), _versionPollCts.Token);

        if (options.UseTrayIcon && !options.HideTrayIcon)
        {
            _trayHost = new TrayHost(this, options, ExitApplicationAsync);
            _trayHost.IsVisible = true;
        }

        if (MainWindowRuntimePolicy.ShouldHideOnStartup(options.HideMainWindowOnStartup, options.UseTrayIcon, options.HideTrayIcon))
        {
            Hide();
        }

        _serviceModePollCts = new CancellationTokenSource();
        _serviceModePollTask = Task.Run(
            () => PollServiceModeTransitionAsync(_serviceModePollCts.Token),
            _serviceModePollCts.Token);
    }

    private static AppConfig? LoadConfigOrDefault(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return AppConfig.Default;
            }

            return AppConfigLoader.Load(configPath);
        }
        catch
        {
            return AppConfig.Default;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_versionPollCts is not null)
        {
            await _versionPollCts.CancelAsync();
            _versionPollCts.Dispose();
            _versionPollCts = null;
        }

        if (_versionPollTask is not null)
        {
            try
            {
                await _versionPollTask;
            }
            catch (OperationCanceledException)
            {
                // expected when window closes
            }

            _versionPollTask = null;
        }

        if (_auditTail is not null)
        {
            await _auditTail.StopAsync();
        }

        if (_serviceModePollCts is not null)
        {
            await _serviceModePollCts.CancelAsync();
            _serviceModePollCts.Dispose();
            _serviceModePollCts = null;
        }

        if (_serviceModePollTask is not null)
        {
            try
            {
                await _serviceModePollTask;
            }
            catch (OperationCanceledException)
            {
                // expected on close
            }

            _serviceModePollTask = null;
        }

        _trayHost?.Dispose();
        base.OnClosed(e);
    }

    private void OnPauseResumeClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null)
        {
            return;
        }

        var isPaused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (isPaused)
        {
            _options.ResumeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        else
        {
            _options.PauseAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        if (DataContext is MainWindowViewModel vm)
        {
            var latestPaused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
            vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), latestPaused);
        }

        var pausedAfter = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
        PauseResumeButton.Content = pausedAfter ? "恢复" : "暂停";
        _trayHost?.Refresh();
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearLogs();
        }
    }

    private void OnOpenConfigDirClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null)
        {
            return;
        }

        var path = Path.GetDirectoryName(_options.ConfigPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OpenDirectory(path);
    }

    private void OnRefreshServiceStatusClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ServiceStatus = _serviceManager.GetStatusText();
            UpdateServiceButtons(vm);
        }
    }

    private void OnOpenServiceManagerTabClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<TabControl>("MainTabControl") is { } mainTab)
        {
            mainTab.SelectedItem = ServiceManagerTab;
        }
    }

    private void OnInstallServiceClick(object? sender, RoutedEventArgs e)
    {
        var workerExecutablePath = ResolveServiceWorkerExecutablePath(_options);
        if (_options is null || string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            if (DataContext is MainWindowViewModel vmMissingWorker)
            {
                vmMissingWorker.ServiceStatus = "未找到 Worker 可执行文件（PhotoPrivacyWorker）";
                UpdateServiceButtons(vmMissingWorker);
            }

            return;
        }

        var result = _serviceManager.Install(workerExecutablePath, _options.ConfigPath);
        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterInstall(result))
        {
            _ = SwitchToDefaultModeAsync(CancellationToken.None);
        }
    }

    private void OnUninstallServiceClick(object? sender, RoutedEventArgs e)
    {
        var result = _serviceManager.Uninstall();
        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterUninstall(result))
        {
            _ = SwitchToDefaultModeAsync(CancellationToken.None);
        }
    }

    private void OnStartServiceClick(object? sender, RoutedEventArgs e)
    {
        var configPath = _options?.ConfigPath;
        var workerExecutablePath = ResolveServiceWorkerExecutablePath(_options);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            ApplyServiceResult(ServiceCommandResult.Failed("未找到 Worker 可执行文件（PhotoPrivacyWorker）"));
            return;
        }

        var result = _serviceManager.Start(workerExecutablePath, configPath);
        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }
    }

    private void OnStopServiceClick(object? sender, RoutedEventArgs e)
    {
        var result = _serviceManager.Stop();
        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }
    }

    private void ApplyServiceResult(ServiceCommandResult result)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var status = _serviceManager.GetStatusText();
        vm.ServiceStatus = result.Status switch
        {
            ServiceCommandStatus.Success => $"{status} | {result.Message}",
            ServiceCommandStatus.Skipped => $"{status} | 跳过：{result.Message}",
            ServiceCommandStatus.ElevationCancelled => $"{status} | 已取消：{result.Message}",
            ServiceCommandStatus.Failed => $"{status} | 失败：{result.Message}",
            _ => status
        };

        UpdateServiceButtons(vm);
    }

    private async Task ExitApplicationAsync()
    {
        if (_options is not null)
        {
            await _options.ExitApplicationAsync(CancellationToken.None);
        }

        _trayHost?.AllowWindowClose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(0);
        }
        else
        {
            Close();
        }
    }

    private void UpdateServiceButtons(MainWindowViewModel vm)
    {
        if (!OperatingSystem.IsWindows())
        {
            InstallServiceButton.IsEnabled = false;
            UninstallServiceButton.IsEnabled = false;
            StartServiceButton.IsEnabled = false;
            StopServiceButton.IsEnabled = false;
            return;
        }

        var state = _serviceManager.GetRuntimeState();
        var buttonState = ServiceUiPolicy.BuildButtonState(state);
        InstallServiceButton.IsEnabled = buttonState.InstallEnabled;
        UninstallServiceButton.IsEnabled = buttonState.UninstallEnabled;
        StartServiceButton.IsEnabled = buttonState.StartEnabled;
        StopServiceButton.IsEnabled = buttonState.StopEnabled;

        vm.ServiceStatus = _serviceManager.GetStatusText();
        if (_options is not null)
        {
            var paused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
            vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, state, paused);
        }
    }

    private async Task PollServiceModeTransitionAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);

            if (_options is null || _isSwitchingMode)
            {
                continue;
            }

            var state = _options.GetServiceRuntimeState();

            if (string.Equals(_options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && !await _options.IsWorkerAliveAsync(token))
            {
                await SwitchToDefaultModeAsync(token);
                return;
            }

            if (string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase)
                && ServiceUiPolicy.ShouldSwitchFromServiceShellToTray(state))
            {
                await SwitchToDefaultModeAsync(token);
                return;
            }

            if (string.Equals(_options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && ServiceUiPolicy.ShouldSwitchFromTrayToServiceShell(state))
            {
                await SwitchToDefaultModeAsync(token);
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    UpdateServiceButtons(vm);
                }
            });
        }
    }

    private async Task SwitchToDefaultModeAsync(CancellationToken token)
    {
        if (_options is null || _isSwitchingMode)
        {
            return;
        }

        var previousRuntimeKind = _options.RuntimeKind;
        var previousEndpoint = _options.WorkerEndpointName;

        _isSwitchingMode = true;
        _trayHost?.AllowWindowClose();

        try
        {
            if (string.Equals(previousRuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(previousEndpoint))
            {
                await _workerManager.ShutdownAsync(previousEndpoint, token);
            }

            var next = await _options.ConnectOrLaunchWorkerAsync(token);
            _options.RuntimeKind = next.RuntimeKind;
            _options.WorkerEndpointName = next.EndpointName;
            _options.UseTrayIcon = next.ShouldShowTrayIcon && !_options.HideTrayIcon;

            if (DataContext is MainWindowViewModel vm)
            {
                vm.CurrentMode = MapModeLabel(_options.RuntimeKind);
                vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), next.Status?.IsPaused ?? false);
                vm.ExifToolVersion = NormalizeExifToolStatus(next.Status?.ExifToolVersion);
            }

            if (_options.UseTrayIcon)
            {
                _trayHost ??= new TrayHost(this, _options, ExitApplicationAsync);
                _trayHost.IsVisible = true;
            }
            else
            {
                _trayHost?.Dispose();
                _trayHost = null;
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

    private void OnSaveConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        try
        {
            var command = new ConfigEditCommand(
                ExifToolPath: vm.ExifToolPath,
                BackupEnabled: vm.BackupEnabled,
                LogEnabled: vm.LogEnabled,
                HotFolderPath: vm.HotFolderPath,
                HideMainWindowOnStartup: vm.HideGuiOnStartup,
                HideTrayIcon: _options.HideTrayIcon);

            ConfigEditor.UpdateConfig(_options.ConfigPath, command);
            vm.RuntimeStatus = "配置已保存";
        }
        catch (Exception ex)
        {
            vm.RuntimeStatus = "配置保存失败";
            vm.AppendLog(new AuditLogEntry(
                TimeText: DateTime.Now.ToString("HH:mm:ss"),
                EventType: "config_save_failed",
                DisplayEvent: "❌ 配置保存失败",
                SourcePathMasked: _options.ConfigPath,
                Message: ex.Message,
                ColorHex: "#C62828"));
        }
    }

    private async Task PollVersionAsync(CancellationToken token)
    {
        if (_versionSnapshot is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            var changed = _versionSnapshot.TryReadChanged();
            if (changed is null)
            {
                continue;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = NormalizeExifToolStatus(changed);
                }
            });
        }
    }

    private static string NormalizeExifToolStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "未找到 ExifTool";
        }

        var text = raw.Trim();
        return text.StartsWith("ExifTool", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"ExifTool v{text} ✓";
    }

    private async Task ResolveExifToolVersionAsync(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = "未找到 ExifTool";
                }
            });
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-ver",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("failed to start exiftool");
            }

            var version = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var text = string.IsNullOrWhiteSpace(version)
                ? "未找到 ExifTool"
                : $"ExifTool v{version.Trim()} ✓";

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = text;
                }
            });
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = "未找到 ExifTool";
                }
            });
        }
    }

    private static void OpenDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = path,
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = path,
                UseShellExecute = true
            });
        }
    }

    private static string MapModeLabel(string runtimeKind)
    {
        if (string.Equals(runtimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            return "🔵 服务模式";
        }

        if (string.Equals(runtimeKind, "tray", StringComparison.OrdinalIgnoreCase))
        {
            return "🟢 托盘模式";
        }

        return runtimeKind;
    }

    private static string BuildRuntimeStatusText(string runtimeKind, ServiceRuntimeState state, bool isPaused)
    {
        if (string.Equals(runtimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            return state is ServiceRuntimeState.Running or ServiceRuntimeState.StartPending or ServiceRuntimeState.ContinuePending
                ? "服务运行中"
                : "服务已停止";
        }

        return isPaused ? "托盘已暂停" : "托盘运行中";
    }

    private static string? ResolveServiceWorkerExecutablePath(BackgroundUiOptions? options)
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
}
