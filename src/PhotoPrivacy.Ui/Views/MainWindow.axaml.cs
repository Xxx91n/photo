using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Watcher;
using PhotoPrivacy.Ui.Localization;
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
    private string _exifToolHint = string.Empty;
    private CancellationTokenSource? _saveStatusResetCts;
    private CancellationTokenSource? _configApplyDebounceCts;
    private static readonly HashSet<string> _configProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(MainWindowViewModel.ExifToolPath),
        nameof(MainWindowViewModel.BackupEnabled),
        nameof(MainWindowViewModel.LogEnabled),
        nameof(MainWindowViewModel.HotFolderPath),
        nameof(MainWindowViewModel.HideGuiOnStartup),
        nameof(MainWindowViewModel.HideTrayIcon),
        nameof(MainWindowViewModel.ThemeVariant),
        nameof(MainWindowViewModel.BackupDirectory),
        nameof(MainWindowViewModel.AuditLogDirectory),
        nameof(MainWindowViewModel.LogLevel),
        nameof(MainWindowViewModel.QuarantineEnabled),
        nameof(MainWindowViewModel.QuarantineDirectory)
    };
    internal IStorageProvider? TestStorageProvider { get; set; }
    internal Task? LastPickerTask { get; private set; }
    private static readonly string[] WellKnownExifToolPaths =
    [
        Path.Combine(AppContext.BaseDirectory, "ExifTool", "exiftool.exe"),
        Path.Combine(AppContext.BaseDirectory, "exiftool.exe"),
        @"C:\Program Files\ExifTool\exiftool.exe",
        @"C:\Program Files (x86)\ExifTool\exiftool.exe",
        @"C:\Windows\exiftool.exe",
        "exiftool.exe"
    ];

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

        // i18n initialization: load persisted locale preference from config + subscribe to CultureChanged
        LocalizationService.Instance.Initialize(); // auto-detect from system culture
        LocalizationService.Instance.CultureChanged += (_, locale) =>
        {
            Dispatcher.UIThread.Post(() => viewModel?.RefreshLocaleDependent(), DispatcherPriority.Background);
        };
        viewModel?.ApplyLocaleFlowDirection();

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
            // ponytail: fire-and-forget initial version read — avoids UI-thread deadlock from sync-over-async
            // PollVersionAsync (background thread) will detect the real version within 1s and post via Dispatcher.UIThread
            viewModel.ExifToolVersion = "检测中…";
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
                viewModel.CurrentLocale = string.IsNullOrWhiteSpace(effectiveConfig.Ui.Locale) ? "zh-CN" : effectiveConfig.Ui.Locale;
                viewModel.BackupDirectory = effectiveConfig.Backup.Directory;
                viewModel.AuditLogDirectory = effectiveConfig.Audit.LogDirectory;
                viewModel.LogLevel = effectiveConfig.Audit.LogLevel;
                viewModel.QuarantineEnabled = effectiveConfig.Quarantine.Enabled;
                viewModel.QuarantineDirectory = effectiveConfig.Quarantine.Directory;

                viewModel.SystemAutoExcludedDirectories.Clear();
                var systemExcluded = WatchPathFilter.ResolveAutoExcludedSubdirectories(effectiveConfig);
                foreach (var dir in systemExcluded)
                        viewModel.SystemAutoExcludedDirectories.Add(dir);

                viewModel.UserExcludedDirectories.Clear();
                if (effectiveConfig.Watch.AutoExcludedDirectories is { Length: > 0 })
                {
                    foreach (var dir in effectiveConfig.Watch.AutoExcludedDirectories)
                        viewModel.UserExcludedDirectories.Add(dir);
                }
            }
            else
            {
                viewModel.ThemeVariant = "system";
                viewModel.CurrentLocale = "zh-CN";
            }

            ApplyThemeVariantToApplication(viewModel.ThemeVariant);
            SyncThemeVariantComboSelection(viewModel.ThemeVariant);
            SyncLogLevelComboSelection(viewModel.LogLevel);
            SyncLocaleComboSelection(viewModel.CurrentLocale);
            viewModel.SaveStatus = string.Empty;
            SetCurrentPage(viewModel.CurrentPage);

            var autoDetected = ResolveExifToolPath(viewModel.ExifToolPath);
            if (!string.IsNullOrWhiteSpace(autoDetected)
                    && !string.Equals(autoDetected, viewModel.ExifToolPath, StringComparison.OrdinalIgnoreCase))
            {
                viewModel.ExifToolPath = autoDetected;
                _exifToolHint = "已自动检测到";
                viewModel.ExifToolPathHint = _exifToolHint;
            }
            else
            {
                _exifToolHint = string.Empty;
                viewModel.ExifToolPathHint = string.Empty;
            }

            var isServiceMode = string.Equals(options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase);
            PauseResumeButton.IsEnabled = !isServiceMode;
            PauseResumeButton.Content = isServiceMode ? "暂停（服务模式不可用）" : viewModel.PauseResumeLabel;

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
        LogLevelComboBox.SelectionChanged += OnLogLevelSelectionChanged;
        // LocaleVariantComboBox uses inline SelectionChanged in AXAML, no manual wire needed
        BrowseExifToolButton.Click += OnBrowseExifToolClick;
        BrowseHotFolderButton.Click += OnBrowseHotFolderClick;
        BrowseBackupDirectoryButton.Click += OnBrowseBackupDirectoryClick;
        BrowseAuditLogDirectoryButton.Click += OnBrowseAuditLogDirectoryClick;
        BrowseQuarantineDirectoryButton.Click += OnBrowseQuarantineDirectoryClick;
        AddExcludedDirectoryButton.Click += OnAddExcludedDirectoryClick;
        RemoveExcludedDirectoryButton.Click += OnRemoveExcludedDirectoryClick;

        // ADR 0037: instant-apply via debounced PropertyChanged — no manual "应用配置" button.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

           var auditDir = effectiveConfig?.Audit.LogDirectory
                          ?? options.AuditDirectory
                          ?? Path.Combine(AppContext.BaseDirectory, "_audit");
            // ponytail: config.sample.json paths can be empty string (not null), ?? won't fall through
            if (string.IsNullOrWhiteSpace(auditDir))
            {
                auditDir = Path.Combine(AppContext.BaseDirectory, "_audit");
            }

        _auditTail = new AuditTailService(
            logDirectory: auditDir,
            onBatch: batch => viewModel?.AppendLogBatch(batch),
            getLogLevel: () =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                    return (DataContext as MainWindowViewModel)?.LogLevel ?? "info";
                return Dispatcher.UIThread.Invoke(() => (DataContext as MainWindowViewModel)?.LogLevel ?? "info");
            },
            onExifToolExePathDetected: exePath => _ = ResolveExifToolVersionAsync(exePath ?? exifToolPathFromConfig));

        _auditTail.Start();
        _ = ResolveExifToolVersionAsync(exifToolPathFromConfig);
        _ = BackfillRecentLogsAsync();

        _versionPollCts = new CancellationTokenSource();
        _versionPollTask = Task.Run(() => PollVersionAsync(_versionPollCts.Token), _versionPollCts.Token);

        var trayReady = false;
        if (options.UseTrayIcon && !options.HideTrayIcon)
        {
            _trayHost = new TrayHost(this, options, ExitApplicationAsync);
            // ponytail: defer tray visibility via Post to avoid Avalonia 11.1.3 TrayIcon.IsVisible setter
            // dead-locking the UI thread during InitializeRuntime (Win32 Shell_NotifyIcon reentrancy).
            trayReady = _trayHost.IconLoaded;
            Dispatcher.UIThread.Post(() => _trayHost.IsVisible = true, DispatcherPriority.Background);
            UiDiagnosticLog.Write($"Tray icon created. IconLoaded={trayReady}, visibility deferred");
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
        _saveStatusResetCts?.Cancel();
        _saveStatusResetCts?.Dispose();
        _saveStatusResetCts = null;
        _configApplyDebounceCts?.Cancel();
        _configApplyDebounceCts?.Dispose();
        _configApplyDebounceCts = null;

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

        if (string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase))
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
            _auditTail?.SkipToCurrentEnd();
        }
    }

    /// <summary>
    /// ADR 0046: Backfill recent audit log lines from Worker via IPC on UI startup/reconnect.
    /// Fills the gap that FileSystemWatcher-based AuditTailService misses between Worker writes and UI connect.
    /// </summary>
    private async Task BackfillRecentLogsAsync()
    {
        if (_options is null)
            return;

        try
        {
            var endpoint = _options.WorkerEndpointName;
            var response = await _workerManager.GetRecentLogsAsync(endpoint, CancellationToken.None);
            if (response is not null && response.Ok && response.Logs is { Lines: { Length: > 0 } lines })
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var logLevel = vm.LogLevel;
                        var batch = new List<AuditLogEntry>();
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var entry = AuditTailService.ParseAuditLine(line, logLevel);
                            if (entry is not null)
                            {
                                batch.Add(entry);
                            }
                        }
                        if (batch.Count > 0)
                        {
                            vm.AppendLogBatch(batch);
                        }
                    });
                }
            }
        }
        catch (TaskCanceledException)
        {
            // shutdown — ignore
        }
        catch
        {
            // best-effort — don't block startup
        }
    }

    private async void OnBrowseExifToolClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        var task = storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 ExifTool 可执行文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ExifTool")
                {
                    Patterns = ["exiftool.exe", "exiftool"]
                }
            ]
        });
        LastPickerTask = task;
        var files = await task;
        if (files.Count == 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.ExifToolPath = files[0].Path.LocalPath;
            vm.ExifToolPathHint = string.Empty;
        }
    }

    private async void OnBrowseHotFolderClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择监控目录",
            AllowMultiple = false
        });
        LastPickerTask = task;
        var folders = await task;
        if (folders.Count == 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.HotFolderPath = folders[0].Path.LocalPath;
        }
    }

    private async void OnBrowseBackupDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择备份目录",
            AllowMultiple = false
        });
        LastPickerTask = task;
        var folders = await task;
        if (folders.Count == 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.BackupDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnBrowseAuditLogDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择日志目录",
            AllowMultiple = false
        });
        LastPickerTask = task;
        var folders = await task;
        if (folders.Count == 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.AuditLogDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnBrowseQuarantineDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null) return;
        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择隔离目录",
            AllowMultiple = false
        });
        LastPickerTask = task;
        var folders = await task;
        if (folders.Count == 0) return;
        if (DataContext is MainWindowViewModel vm)
            vm.QuarantineDirectory = folders[0].Path.LocalPath;
    }

    private async void OnAddExcludedDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null) return;
        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "添加排除监听的目录",
            AllowMultiple = false
        });
        LastPickerTask = task;
        var folders = await task;
        if (folders.Count == 0) return;
        if (DataContext is MainWindowViewModel vm)
            vm.UserExcludedDirectories.Add(folders[0].Path.LocalPath);
    }

    private void OnRemoveExcludedDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm
            && UserExcludedDirectoriesListBox.SelectedItem is string selected)
        {
            vm.UserExcludedDirectories.Remove(selected);
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
        try
        {
            if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var variant = NormalizeThemeVariant(item.Tag?.ToString());
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ThemeVariant = variant;
            }

            ApplyThemeVariantToApplication(variant);
        }
        catch
        {
            // prevent crash on unexpected combo state
        }
    }

    private void OnLogLevelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var level = item.Tag?.ToString() ?? "info";
            if (DataContext is MainWindowViewModel vm)
            {
                vm.LogLevel = level;
                vm.LogEnabled = level is "all" or "debug";
                vm.ShowDetailedEvents = level is "all" or "debug";
            }
        }
        catch
        {
            // prevent crash on unexpected combo state
        }
    }

    private void OnLocaleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var locale = item.Tag?.ToString() ?? "zh-CN";
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CurrentLocale = locale;
            }

            LocalizationService.Instance.SwitchLocale(locale);
            if (DataContext is MainWindowViewModel vm2)
            {
                vm2.RefreshLocaleDependent();
            }
        }
        catch
        {
            // prevent crash on unexpected combo state
        }
    }

    private void SyncLocaleComboSelection(string locale)
    {
        try
        {
            if (LocaleVariantComboBox is null || LocaleVariantComboBox.Items is null)
            {
                return;
            }

            var target = string.IsNullOrWhiteSpace(locale) ? "zh-CN" : locale;
            foreach (var item in LocaleVariantComboBox.Items)
            {
                if (item is ComboBoxItem comboItem
                    && string.Equals(comboItem.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
                {
                    LocaleVariantComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            LocaleVariantComboBox.SelectedIndex = 0;
        }
        catch
        {
            // control not yet ready
        }
    }

    private static string ReadComboItemString(object? selectedItem)
    {
        if (selectedItem is ComboBoxItem comboItem)
        {
            return comboItem.Content?.ToString() ?? string.Empty;
        }

        return selectedItem?.ToString() ?? string.Empty;
    }

    private void SyncThemeVariantComboSelection(string variant)
    {
        try
        {
            if (ThemeVariantComboBox is null || ThemeVariantComboBox.Items is null)
            {
                return;
            }

            var normalized = NormalizeThemeVariant(variant);
            foreach (var item in ThemeVariantComboBox.Items)
            {
                if (item is ComboBoxItem comboItem
                    && string.Equals(comboItem.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    ThemeVariantComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            ThemeVariantComboBox.SelectedIndex = 0;
        }
        catch
        {
            // control not yet ready
        }
    }

    private void SyncLogLevelComboSelection(string level)
    {
        try
        {
            if (LogLevelComboBox is null || LogLevelComboBox.Items is null)
            {
                return;
            }

            foreach (var item in LogLevelComboBox.Items)
            {
                if (item is ComboBoxItem comboItem
                    && string.Equals(comboItem.Tag?.ToString(), level, StringComparison.OrdinalIgnoreCase))
                {
                    LogLevelComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            LogLevelComboBox.SelectedIndex = 1;
        }
        catch
        {
            // control not yet ready
        }
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
            _ = Task.Run(async () =>
            {
                try { await SwitchToServiceModeAfterInstallAsync(CancellationToken.None); }
                catch (Exception ex) { UiDiagnosticLog.Write($"SwitchToServiceMode failed: {ex.Message}"); }
            });
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
            _ = Task.Run(async () =>
            {
                try { await EnsureTrayWorkerAfterServiceUninstallAsync(CancellationToken.None); }
                catch (Exception ex) { UiDiagnosticLog.Write($"EnsureTrayWorker failed: {ex.Message}"); }
            });
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
            _ = Task.Run(async () =>
            {
                try { await SwitchToServiceModeAfterInstallAsync(CancellationToken.None); }
                catch (Exception ex) { UiDiagnosticLog.Write($"SwitchToServiceMode failed: {ex.Message}"); }
            });
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

            // ponytail: skip sync-over-async pause read on UI thread — causes deadlock when Worker IPC stalls.
            // RuntimeStatus is updated by background poll tasks; initial state defaults to false.
            _ = Task.Run(async () =>
            {
                try
                {
                    var p = await _options.IsPausedAsync(CancellationToken.None).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (DataContext is MainWindowViewModel vm)
                            vm.RuntimeStatus = BuildRuntimeStatusText(_options.RuntimeKind, state, p);
                    });
                }
                catch { }
            });

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

        var isServiceMode = string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            PauseResumeButton.IsEnabled = !isServiceMode;
            PauseResumeButton.Content = isServiceMode
                ? "暂停（服务模式不可用）"
                : (DataContext as MainWindowViewModel)?.PauseResumeLabel ?? "⏸ 暂停";
        });

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

                PauseResumeButton.IsEnabled = true;
                PauseResumeButton.Content = (DataContext as MainWindowViewModel)?.PauseResumeLabel ?? "⏸ 暂停";

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


    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_configProperties.Contains(e.PropertyName ?? string.Empty))
            return;
        ScheduleDebouncedConfigApply();
    }

    private void ScheduleDebouncedConfigApply()
    {
        _configApplyDebounceCts?.Cancel();
        _configApplyDebounceCts?.Dispose();
        _configApplyDebounceCts = new CancellationTokenSource();
        var token = _configApplyDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                if (token.IsCancellationRequested)
                    return;
                await Dispatcher.UIThread.InvokeAsync(async () => await ApplyConfigImmediatelyAsync());
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private async Task ApplyConfigImmediatelyAsync()
    {
        if (_options is null || DataContext is not MainWindowViewModel vm)
            return;

        try
        {
            var command = new ConfigEditCommand(
                ExifToolPath: vm.ExifToolPath,
                BackupEnabled: vm.BackupEnabled,
                LogEnabled: vm.LogEnabled,
                HotFolderPath: vm.HotFolderPath,
                HideMainWindowOnStartup: vm.HideGuiOnStartup,
                HideTrayIcon: vm.HideTrayIcon,
                ThemeVariant: vm.ThemeVariant,
                Locale: vm.CurrentLocale,
                BackupDirectory: vm.BackupDirectory,
                AuditLogDirectory: vm.AuditLogDirectory,
                LogLevel: vm.LogLevel,
                QuarantineEnabled: vm.QuarantineEnabled,
                QuarantineDirectory: vm.QuarantineDirectory);
            ConfigEditor.UpdateConfig(_options.ConfigPath, command);
            try
            {
                await _workerManager.ReloadConfigAsync(_options.WorkerEndpointName, CancellationToken.None);
            }
            catch
            {
                vm.SaveStatus = "✓ 已自动保存（Worker 未运行）";
                ScheduleSaveStatusClear();
                return;
            }
            ApplyRuntimeConfigToUiState();
            vm.SaveStatus = "✓ 已自动保存";
            ScheduleSaveStatusClear();
        }
        catch (Exception ex)
        {
            vm.SaveStatus = $"✗ 保存失败：{ex.Message}";
            ScheduleSaveStatusClear();
        }
    }


    private async void OnApplyConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.ExifToolPath)
            || !Path.IsPathFullyQualified(vm.ExifToolPath)
            || !File.Exists(vm.ExifToolPath))
        {
            vm.SaveStatus = "✗ ExifTool 路径无效，请先选择正确的 exiftool.exe";
            ScheduleSaveStatusClear();
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
                ThemeVariant: vm.ThemeVariant,
                Locale: vm.CurrentLocale,
                BackupDirectory: vm.BackupDirectory,
                AuditLogDirectory: vm.AuditLogDirectory,
                LogLevel: vm.LogLevel,
                QuarantineEnabled: vm.QuarantineEnabled,
                QuarantineDirectory: vm.QuarantineDirectory);
            ConfigEditor.UpdateConfig(_options.ConfigPath, command);

            try
            {
                await _workerManager.ReloadConfigAsync(_options.WorkerEndpointName, CancellationToken.None);
            }
            catch
            {
                vm.SaveStatus = "✓ 已保存（Worker 未运行，下次启动生效）";
                ScheduleSaveStatusClear();
                return;
            }

            ApplyRuntimeConfigToUiState();
            vm.SaveStatus = "✓ 已应用";
            ScheduleSaveStatusClear();
        }
        catch (Exception ex)
        {
            vm.SaveStatus = $"✗ 应用失败：{ex.Message}";
            ScheduleSaveStatusClear();
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
        }
    }

    private void ScheduleSaveStatusClear()
    {
        _saveStatusResetCts?.Cancel();
        _saveStatusResetCts?.Dispose();
        _saveStatusResetCts = new CancellationTokenSource();
        var token = _saveStatusResetCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.SaveStatus = string.Empty;
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }, token);
    }

    private async Task ApplyConfigForCurrentModeAsync(CancellationToken token)
    {
        if (_options is null)
        {
            return;
        }

        await _workerManager.ReloadConfigAsync(_options.WorkerEndpointName, token);
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
                vm.BackupDirectory = cfg.Backup.Directory;
                vm.AuditLogDirectory = cfg.Audit.LogDirectory;
                vm.LogLevel = cfg.Audit.LogLevel;
                SyncLogLevelComboSelection(vm.LogLevel);
                vm.ExifToolPathHint = _exifToolHint;
                vm.QuarantineEnabled = cfg.Quarantine.Enabled;
                vm.QuarantineDirectory = cfg.Quarantine.Directory;

                vm.SystemAutoExcludedDirectories.Clear();
                var systemExcluded = WatchPathFilter.ResolveAutoExcludedSubdirectories(cfg);
                foreach (var dir in systemExcluded)
                    vm.SystemAutoExcludedDirectories.Add(dir);

                vm.UserExcludedDirectories.Clear();
                if (cfg.Watch.AutoExcludedDirectories is { Length: > 0 })
                {
                    foreach (var dir in cfg.Watch.AutoExcludedDirectories)
                        vm.UserExcludedDirectories.Add(dir);
                }
            }

            ApplyThemeVariantToApplication(normalizedThemeVariant);
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"ApplyRuntimeConfigToUiState failed: {ex.Message}");
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

public partial class MainWindow
{
    internal Task TestPickHotFolderAsync()
    {
        OnBrowseHotFolderClick(this, new RoutedEventArgs());
        return LastPickerTask ?? Task.CompletedTask;
    }

    private IStorageProvider? ResolveStorageProvider()
    {
        if (TestStorageProvider is not null)
        {
            return TestStorageProvider;
        }

        return StorageProvider;
    }

    private static string ResolveExifToolPath(string? current)
    {
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
        {
            return current;
        }

        foreach (var path in WellKnownExifToolPaths)
        {
            if (string.Equals(path, "exiftool.exe", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolveFromPath("exiftool.exe");
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }

                continue;
            }

            if (File.Exists(path))
            {
                return path;
            }
        }

        return current ?? string.Empty;
    }

    private static string? ResolveFromPath(string fileName)
    {
        var env = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(env))
        {
            return null;
        }

        foreach (var segment in env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(segment.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
