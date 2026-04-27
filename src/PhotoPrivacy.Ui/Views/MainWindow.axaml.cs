using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
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
        UiDiagnosticLog.Write($"MainWindow.InitializeRuntime begin. RuntimeKind={options.RuntimeKind}, UseTrayIcon={options.UseTrayIcon}, HideTrayIcon={options.HideTrayIcon}, HideMainWindowOnStartup={options.HideMainWindowOnStartup}");
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
                viewModel.HideTrayIcon = effectiveConfig.Ui.HideTrayIcon;
                viewModel.ThemeVariant = NormalizeThemeVariant(effectiveConfig.Ui.ThemeVariant);
            }
            else
            {
                viewModel.ThemeVariant = "system";
            }

            ApplyThemeVariantToApplication(viewModel.ThemeVariant);
            SyncThemeVariantComboSelection(viewModel.ThemeVariant);
            viewModel.SaveStatus = string.Empty;
            SetCurrentPage(viewModel.CurrentPage);

            UpdateServiceButtons(viewModel);
        }

        if (!OperatingSystem.IsWindows())
        {
            ServiceManagerTab.IsVisible = false;
        }

        PauseResumeButton.Click += OnPauseResumeClick;
        ClearLogsButton.Click += OnClearLogsClick;
        OpenConfigDirButton.Click += OnOpenConfigDirClick;
        RefreshServiceStatusButton.Click += OnRefreshServiceStatusClick;
        ConfigNavButton.Click += OnNavigateClick;
        LogNavButton.Click += OnNavigateClick;
        if (this.FindControl<Button>("OpenServiceManagerTabButton") is { } openServiceManagerTabButton)
        {
            openServiceManagerTabButton.Click += OnNavigateClick;
        }

        InstallServiceButton.Click += OnInstallServiceClick;
        UninstallServiceButton.Click += OnUninstallServiceClick;
        StartServiceButton.Click += OnStartServiceClick;
        StopServiceButton.Click += OnStopServiceClick;
        ThemeVariantComboBox.SelectionChanged += OnThemeVariantSelectionChanged;
        if (this.FindControl<Button>("SaveConfigButton") is { } saveConfigButton)
        {
            saveConfigButton.Click += OnSaveConfigClick;
        }

        if (this.FindControl<Button>("ApplyConfigButton") is { } applyConfigButton)
        {
            applyConfigButton.Click += OnApplyConfigClick;
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

        var trayReady = false;
        if (options.UseTrayIcon && !options.HideTrayIcon)
        {
            _trayHost = new TrayHost(this, options, ExitApplicationAsync);
            _trayHost.IsVisible = true;
            UiDiagnosticLog.Write("Tray icon host created and set visible");
            trayReady = _trayHost.IconLoaded;
            UiDiagnosticLog.Write($"Tray icon ready state: {trayReady}");
        }

        if (MainWindowRuntimePolicy.ShouldHideOnStartup(
                options.HideMainWindowOnStartup,
                options.UseTrayIcon,
                options.HideTrayIcon,
                trayReady))
        {
            UiDiagnosticLog.Write("MainWindow.Hide() due to startup hide policy");
            Hide();
        }
        else
        {
            UiDiagnosticLog.Write("MainWindow startup policy keeps window visible");
        }

        _serviceModePollCts = new CancellationTokenSource();
        _serviceModePollTask = Task.Run(
            () => PollServiceModeTransitionAsync(_serviceModePollCts.Token),
            _serviceModePollCts.Token);
        UiDiagnosticLog.Write("MainWindow.InitializeRuntime end");

        EnsureWindowVisibleFallback(options);
    }

    private void EnsureWindowVisibleFallback(BackgroundUiOptions options)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (MainWindowRuntimePolicy.ShouldHideOnStartup(
                        options.HideMainWindowOnStartup,
                        options.UseTrayIcon,
                        options.HideTrayIcon,
                        _trayHost?.IconLoaded ?? false))
                {
                    UiDiagnosticLog.Write("EnsureWindowVisibleFallback skipped due to startup hide policy");
                    return;
                }

                if (!IsVisible)
                {
                    UiDiagnosticLog.Write("EnsureWindowVisibleFallback forcing Show/Activate");
                    Show();
                }

                WindowState = WindowState.Normal;
                Activate();
            });
        });
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

    private void OnNavigateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string pageTag)
        {
            return;
        }

        SetCurrentPage(pageTag);
    }

    private void SetCurrentPage(string page)
    {
        var normalized = string.Equals(page, "log", StringComparison.OrdinalIgnoreCase)
            ? "log"
            : string.Equals(page, "service", StringComparison.OrdinalIgnoreCase)
                ? "service"
                : "config";

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentPage = normalized;
        }

        var showServicePage = string.Equals(normalized, "service", StringComparison.Ordinal)
            && OperatingSystem.IsWindows()
            && (DataContext as MainWindowViewModel)?.ShowServiceManagerTab == true;

        ConfigPage.IsVisible = string.Equals(normalized, "config", StringComparison.Ordinal);
        LogPage.IsVisible = string.Equals(normalized, "log", StringComparison.Ordinal);
        ServiceManagerTab.IsVisible = showServicePage;

        SetNavButtonActive(ConfigNavButton, string.Equals(normalized, "config", StringComparison.Ordinal));
        SetNavButtonActive(LogNavButton, string.Equals(normalized, "log", StringComparison.Ordinal));
        SetNavButtonActive(OpenServiceManagerTabButton, string.Equals(normalized, "service", StringComparison.Ordinal));
    }

    private static void SetNavButtonActive(Button button, bool isActive)
    {
        if (isActive)
        {
            if (!button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }

            return;
        }

        button.Classes.Remove("active");
    }

    private void OnThemeVariantSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        var variant = ReadThemeVariantSelection(combo.SelectedItem);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ThemeVariant = variant;
        }

        ApplyThemeVariantToApplication(variant);
    }

    private void SyncThemeVariantComboSelection(string variant)
    {
        var normalized = NormalizeThemeVariant(variant);
        foreach (var item in ThemeVariantComboBox.Items)
        {
            if (item is ComboBoxItem comboItem
                && string.Equals(comboItem.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                ThemeVariantComboBox.SelectedItem = comboItem;
                return;
            }
        }

        ThemeVariantComboBox.SelectedIndex = 0;
    }

    private static string ReadThemeVariantSelection(object? selectedItem)
    {
        if (selectedItem is ComboBoxItem comboItem)
        {
            return NormalizeThemeVariant(comboItem.Content?.ToString());
        }

        return NormalizeThemeVariant(selectedItem?.ToString());
    }

    private static string NormalizeThemeVariant(string? value)
    {
        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
        {
            return "light";
        }

        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return "dark";
        }

        return "system";
    }

    private static void ApplyThemeVariantToApplication(string variant)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = NormalizeThemeVariant(variant) switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private async void OnInstallServiceClick(object? sender, RoutedEventArgs e)
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

        SetServiceButtonsBusy(isBusy: true);

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Install(workerExecutablePath, _options.ConfigPath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterInstall(result))
        {
            _ = SwitchToServiceModeAfterInstallAsync(CancellationToken.None);
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    private async void OnUninstallServiceClick(object? sender, RoutedEventArgs e)
    {
        SetServiceButtonsBusy(isBusy: true);
        if (DataContext is MainWindowViewModel vmBusy)
        {
            vmBusy.ServiceStatus = $"{_serviceManager.GetStatusText()} | 正在卸载服务...";
        }

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Uninstall(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }

        if (ImmediateModeSwitchPolicy.ShouldSwitchAfterUninstall(result))
        {
            _ = EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken.None);
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    private async void OnStartServiceClick(object? sender, RoutedEventArgs e)
    {
        var configPath = _options?.ConfigPath;
        var workerExecutablePath = ResolveServiceWorkerExecutablePath(_options);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            ApplyServiceResult(ServiceCommandResult.Failed("未找到 Worker 可执行文件（PhotoPrivacyWorker）"));
            return;
        }

        SetServiceButtonsBusy(isBusy: true);

        var requiresTrayShutdown = _options is not null && string.Equals(
            _options.WorkerEndpointName,
            PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
            StringComparison.Ordinal);

        if (requiresTrayShutdown)
        {
            var trayShutdownDone = await ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken.None);
            if (!trayShutdownDone)
            {
                ApplyServiceResult(ServiceCommandResult.Failed("托盘 Worker 仍在运行，已取消服务启动，请稍后重试"));
                SetServiceButtonsBusy(isBusy: false);
                return;
            }
        }

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Start(workerExecutablePath, configPath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "tray");
        }

        if (result.Status == ServiceCommandStatus.Success)
        {
            _ = SwitchToServiceModeAfterInstallAsync(CancellationToken.None);
        }

        SetServiceButtonsBusy(isBusy: false);
    }

    private async void OnStopServiceClick(object? sender, RoutedEventArgs e)
    {
        SetServiceButtonsBusy(isBusy: true);

        ServiceCommandResult result;
        try
        {
            result = await Task.Run(() => _serviceManager.Stop(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = ServiceCommandResult.Failed(ex.Message);
        }

        ApplyServiceResult(result);

        if (_options is not null)
        {
            var state = _serviceManager.GetRuntimeState();
            if (state != ServiceRuntimeState.NotInstalled)
            {
                _options.RuntimeKind = "service";
                _options.UseTrayIcon = false;
            }
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options?.RuntimeKind ?? "service");
        }

        SetServiceButtonsBusy(isBusy: false);
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
            var paused = false;
            var shouldReadTrayPauseStatus =
                !_isSwitchingMode
                && string.Equals(_options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    _options.WorkerEndpointName,
                    PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
                    StringComparison.Ordinal);

            if (shouldReadTrayPauseStatus)
            {
                try
                {
                    paused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    paused = false;
                }
            }

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

    private async Task SwitchToDefaultModeAsync(CancellationToken token, Func<ServiceRuntimeState>? getServiceRuntimeStateOverride = null)
    {
        if (_options is null || _isSwitchingMode)
        {
            return;
        }

        _isSwitchingMode = true;

        try
        {
            var next = getServiceRuntimeStateOverride is null
                ? await _options.ConnectOrLaunchWorkerAsync(token)
                : await _workerManager.ConnectOrLaunchAsync(
                    ResolveServiceWorkerExecutablePath(_options),
                    token,
                    getServiceRuntimeState: getServiceRuntimeStateOverride);
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

    private async Task SwitchToServiceModeAfterInstallAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return;
        }

        await ShutdownTrayWorkerForServiceSwitchAsync(token);
        await SwitchToDefaultModeAsync(token);

        _options.RuntimeKind = "service";
        _options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.ServicePipe;
        _options.UseTrayIcon = false;

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CurrentMode = MapModeLabel(_options.RuntimeKind);
                vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), false);
            }

            _trayHost?.Dispose();
            _trayHost = null;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private async Task<bool> ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return false;
        }

        var endpoint = _options.WorkerEndpointName;
        if (!string.Equals(endpoint, PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var status = await _workerManager.GetStatusAsync(endpoint, token);
            if (status is null)
            {
                return true;
            }

            await _workerManager.ShutdownAsync(endpoint, token);

            var aliveProbe = new WorkerIpcClient();
            for (var i = 0; i < 15; i++)
            {
                if (!await aliveProbe.IsAliveAsync(PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe, token))
                {
                    return true;
                }

                await Task.Delay(200, token);
            }

            return false;
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"ShutdownTrayWorkerForServiceSwitchAsync failed: {ex.Message}");
            return false;
        }
    }

    private async Task EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken token)
    {
        await SwitchToDefaultModeAsync(token, getServiceRuntimeStateOverride: () => ServiceRuntimeState.NotInstalled);

        if (_options is null)
        {
            return;
        }

        _options.RuntimeKind = "tray";
        _options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;
        _options.UseTrayIcon = !_options.HideTrayIcon;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.CurrentMode = MapModeLabel(_options.RuntimeKind);
                    vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), false);
                }

                if (_options.UseTrayIcon)
                {
                    _trayHost ??= new TrayHost(this, _options, ExitApplicationAsync);
                    _trayHost.IsVisible = true;
                }

                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
            catch (Exception ex)
            {
                UiDiagnosticLog.Write($"EnsureTrayWorkerAfterServiceUninstallAsync UI post failed: {ex.Message}");
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
            InstallServiceButton.IsEnabled = false;
            UninstallServiceButton.IsEnabled = false;
            StartServiceButton.IsEnabled = false;
            StopServiceButton.IsEnabled = false;
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            UpdateServiceButtons(vm);
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
                HideTrayIcon: vm.HideTrayIcon,
                ThemeVariant: vm.ThemeVariant);

            ConfigEditor.UpdateConfig(_options.ConfigPath, command);
            vm.SaveStatus = "已保存，待应用";
        }
        catch (Exception ex)
        {
            vm.SaveStatus = $"保存失败：{ex.Message}";
            vm.AppendLog(new AuditLogEntry(
                TimeText: DateTime.Now.ToString("HH:mm:ss"),
                EventType: "config_save_failed",
                DisplayEvent: "❌ 配置保存失败",
                SourcePathMasked: _options.ConfigPath,
                Message: ex.Message,
                ColorHex: "#C62828"));
        }
    }

    private async void OnApplyConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        SetConfigButtonsBusy(isBusy: true);
        try
        {
            ApplyRuntimeConfigToUiState();
            await ApplyConfigForCurrentModeAsync(CancellationToken.None);
            vm.SaveStatus = "配置已应用";
        }
        catch (Exception ex)
        {
            vm.SaveStatus = $"应用失败：{ex.Message}";
            vm.AppendLog(new AuditLogEntry(
                TimeText: DateTime.Now.ToString("HH:mm:ss"),
                EventType: "config_apply_failed",
                DisplayEvent: "⚠ 配置应用失败",
                SourcePathMasked: _options.ConfigPath,
                Message: ex.Message,
                ColorHex: "#C62828"));
        }
        finally
        {
            SetConfigButtonsBusy(isBusy: false);
        }
    }

    private async Task ApplyConfigForCurrentModeAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return;
        }

        if (string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyConfigInServiceModeAsync(token);
            return;
        }

        await ApplyConfigInTrayModeAsync(token);
    }

    private async Task ApplyConfigInTrayModeAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return;
        }

        var endpoint = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;
        var aliveProbe = new WorkerIpcClient();
        if (await aliveProbe.IsAliveAsync(endpoint, token))
        {
            await _workerManager.ShutdownAsync(endpoint, token);
            for (var i = 0; i < 15; i++)
            {
                if (!await aliveProbe.IsAliveAsync(endpoint, token))
                {
                    break;
                }

                await Task.Delay(200, token);
            }
        }

        var workerExecutablePath = ResolveServiceWorkerExecutablePath(_options);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            throw new InvalidOperationException("未找到 Worker 可执行文件（PhotoPrivacyWorker）");
        }

        System.Diagnostics.Process.Start(WorkerProcessManager.BuildBackgroundLaunchStartInfo(workerExecutablePath, _options.ConfigPath));

        var connected = false;
        for (var i = 0; i < 25; i++)
        {
            if (await aliveProbe.IsAliveAsync(endpoint, token))
            {
                connected = true;
                break;
            }

            await Task.Delay(200, token);
        }

        if (!connected)
        {
            throw new InvalidOperationException("后台模式未能在预期时间内完成重连");
        }

        _options.RuntimeKind = "tray";
        _options.WorkerEndpointName = endpoint;
        _options.UseTrayIcon = !_options.HideTrayIcon;

        var status = await _workerManager.GetStatusAsync(endpoint, token);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options.RuntimeKind);
            vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), status?.Data?.IsPaused ?? false);
            vm.ExifToolVersion = NormalizeExifToolStatus(status?.Data?.ExifToolVersion);
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

    private async Task ApplyConfigInServiceModeAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return;
        }

        var workerExecutablePath = ResolveServiceWorkerExecutablePath(_options);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            throw new InvalidOperationException("未找到 Worker 可执行文件（PhotoPrivacyWorker）");
        }

        await Task.Run(() => _serviceManager.Stop(), token);
        var startResult = await Task.Run(() => _serviceManager.Start(workerExecutablePath, _options.ConfigPath), token);
        ApplyServiceResult(startResult);

        if (startResult.Status != ServiceCommandStatus.Success)
        {
            throw new InvalidOperationException(startResult.Message);
        }

        _options.RuntimeKind = "service";
        _options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.ServicePipe;
        _options.UseTrayIcon = false;

        _trayHost?.Dispose();
        _trayHost = null;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = MapModeLabel(_options.RuntimeKind);
            vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, _serviceManager.GetRuntimeState(), false);
        }
    }

    private void ApplyRuntimeConfigToUiState()
    {
        if (_options is null)
        {
            return;
        }

        try
        {
            var cfg = LoadConfigOrDefault(_options.ConfigPath) ?? AppConfig.Default;
            _options.HideMainWindowOnStartup = cfg.Ui.HideMainWindowOnStartup;
            _options.HideTrayIcon = cfg.Ui.HideTrayIcon;
            var normalizedThemeVariant = NormalizeThemeVariant(cfg.Ui.ThemeVariant);

            if (string.Equals(_options.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
            {
                _options.UseTrayIcon = !_options.HideTrayIcon;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                vm.ExifToolPath = cfg.ExifTool.Path;
                vm.BackupEnabled = cfg.Backup.Enabled;
                vm.LogEnabled = cfg.Audit.DiagnosticMode;
                vm.HotFolderPath = cfg.Watch.HotFolder;
                vm.HideGuiOnStartup = cfg.Ui.HideMainWindowOnStartup;
                vm.HideTrayIcon = cfg.Ui.HideTrayIcon;
                vm.ThemeVariant = normalizedThemeVariant;
                SyncThemeVariantComboSelection(vm.ThemeVariant);
            }

            ApplyThemeVariantToApplication(normalizedThemeVariant);
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"ApplyRuntimeConfigToUiState failed: {ex.Message}");
        }
    }

    private void SetConfigButtonsBusy(bool isBusy)
    {
        SaveConfigButton.IsEnabled = !isBusy;
        ApplyConfigButton.IsEnabled = !isBusy;
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
            return "服务模式";
        }

        if (string.Equals(runtimeKind, "tray", StringComparison.OrdinalIgnoreCase))
        {
            return "托盘模式";
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
