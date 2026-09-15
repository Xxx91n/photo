using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Watcher;
using PhotoPrivacy.Ui.Localization;
using PhotoPrivacy.Ui.Services;
using PhotoPrivacy.Ui.ViewModels;
using PhotoPrivacy.Ui.Views.Controls;

namespace PhotoPrivacy.Ui.Views;

public partial class MainWindow : Window
{
    private AuditTailService? _auditTail;
    internal TrayHost? _trayHost;
    internal BackgroundUiOptions? _options;
    private readonly WorkerIpcClient _workerIpc;
    internal readonly ServiceModeController _serviceModeController;
    // 票 31：版本/服务状态轮询已下沉 WindowPollingHostedService（_versionSnapshot/_versionPollCts/
    // _versionPollTask/_serviceModePollCts/_serviceModePollTask 五字段随之删除）。
    private readonly WindowPollingHostedService _pollingHostedService;
    // 票 08（ui-craft2 / D-007 / 规范 §6）：应用级 toast 服务（DI 单例）——配置保存/规则/服务操作
    // 反馈链统一出口；SaveStatus 行内文本保留为持久态，toast 承载瞬时反馈。
    private readonly ToastService _toastService;
    private string _exifToolHint = string.Empty;
    private CancellationTokenSource? _saveStatusResetCts;
    private CancellationTokenSource? _configApplyDebounceCts;
    // 票 27：ConfigFileWatcher — 监听磁盘 config.json 变更后回调 ApplyRuntimeConfigToUiState
    private ConfigFileWatcher? _configFileWatcher;
    private static readonly HashSet<string> _configProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(MainWindowViewModel.ExifToolPath),
        nameof(MainWindowViewModel.BackupEnabled),
        nameof(MainWindowViewModel.LogEnabled),
        nameof(MainWindowViewModel.HotFolderPath),
        nameof(MainWindowViewModel.HideGuiOnStartup),
        nameof(MainWindowViewModel.HideTrayIcon),
        nameof(MainWindowViewModel.ThemeVariant),
        nameof(MainWindowViewModel.CurrentLocale),
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

    // 票 29（架构恢复第七轮）：构造注入 —— 服务依赖经组合根容器（AppComposition）装配，
    // MainWindow 不再手写 new WorkerIpcClient/ServiceManager/ServiceModeController 依赖链；
    // 窗口仅保留对自身视图缝（UiHost/ViewModelView 适配器）的落位。
    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceManagerOps serviceManagerOps,
        WorkerProcessManager workerManager,
        WorkerIpcClient workerIpc,
        WindowPollingHostedService pollingHostedService,
        ToastService toastService)
    {
        InitializeComponent();
        _workerIpc = workerIpc;
        _pollingHostedService = pollingHostedService;
        _toastService = toastService;
        _serviceModeController = new ServiceModeController(
            serviceManagerOps,
            workerManager,
            _workerIpc,
            new MainWindowUiHost(this),
            new MainWindowViewModelView(this),
            toastService);
        // 票 31：服务模式轮询委托挂载（破 DI 环，见 WindowPollingHostedService 类注释）。
        _pollingHostedService.AttachServiceModePoll(_serviceModeController.PollServiceModeTransitionAsync);
        DataContext = viewModel;
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
           // ponytail: ADR 0035 — NEVER sync-over-async on UI thread.
           // The previous blocking call pattern inside
           // Dispatcher.UIThread.Post caused window freeze when Worker pipe
           // was stale/broken. Status text is refreshed by the background
           // poll loop (PollRuntimeStatusAsync) within ~1s; i18n switch
           // must NOT block on IPC.
           Dispatcher.UIThread.Post(() =>
           {
               viewModel?.RefreshLocaleDependent();
               RefreshI18nComboBoxItems();
               // Refresh RuntimeStatus immediately so the left-top status text
               // switches language without waiting for the ~1s background poll
               // (PollRuntimeStatusAsync). Previously the status stayed in the
               // old language until a poll cycle happened to re-assign it.
               if (viewModel is not null && _options is not null)
               {
                   viewModel.RuntimeStatus = RunModeStatusTextSnapshot();
               }
           }, DispatcherPriority.Normal);
        };
        viewModel?.ApplyLocaleFlowDirection();

        if (viewModel is not null)
        {
            viewModel.CurrentMode = ServiceModeController.MapModeLabel(options.RuntimeKind);
            // 票 31：版本快照与轮询由 WindowPollingHostedService.Activate 构建（窗口就绪后），
            // 初始 detecting 占位语义保持 —— 1s 内轮询经 Dispatcher 检出真实版本。
            viewModel.ExifToolVersion = LocalizationService.Instance.Get("status.detecting");
            viewModel.ShowDetailedEvents = false;
            // ADR 0053 M1: Show "Worker connecting…" if ConnectionState is Connecting (Worker not yet connected).
            viewModel.RuntimeStatus = options.ConnectionState is { State: ConnectionState.Connecting }
                ? LocalizationService.Instance.Get("status.connecting")
                : ServiceModeController.BuildRuntimeStatusText(options.RuntimeKind, options.GetServiceRuntimeState(), false);
            viewModel.ShowServiceManagerTab = OperatingSystem.IsWindows();
            viewModel.ServiceStatus = _serviceModeController.StatusText;

            if (effectiveConfig is not null)
            {
                viewModel.ExifToolPath = effectiveConfig.ExifTool.Path;
                viewModel.BackupEnabled = effectiveConfig.Backup.Enabled;
                viewModel.LogEnabled = effectiveConfig.Audit.DiagnosticMode;
                viewModel.HotFolderPath = effectiveConfig.Watch.HotFolder;
                viewModel.HideGuiOnStartup = effectiveConfig.Ui.HideMainWindowOnStartup;
                viewModel.HideTrayIcon = effectiveConfig.Ui.HideTrayIcon;
                viewModel.ThemeVariant = NormalizeThemeVariant(effectiveConfig.Ui.ThemeVariant);
                viewModel.ThemeId = string.IsNullOrWhiteSpace(effectiveConfig.Ui.ThemeId) ? "catppuccin" : effectiveConfig.Ui.ThemeId;
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
                viewModel.ThemeId = "catppuccin";
                viewModel.CurrentLocale = "zh-CN";
            }

            ApplyThemeVariantToApplication(viewModel.ThemeVariant);
            App.ApplyCommunityThemeResources(viewModel.ThemeId, applyDark: string.Equals(NormalizeThemeVariant(viewModel.ThemeVariant), "dark", StringComparison.OrdinalIgnoreCase));
            RestoreSidebarWidth(effectiveConfig.Ui.SidebarWidth);
            viewModel.SaveStatus = string.Empty;
            SetCurrentPage(viewModel.CurrentPage);

            var autoDetected = ResolveExifToolPath(viewModel.ExifToolPath);
            if (!string.IsNullOrWhiteSpace(autoDetected)
                    && !string.Equals(autoDetected, viewModel.ExifToolPath, StringComparison.OrdinalIgnoreCase))
            {
                viewModel.ExifToolPath = autoDetected;
                _exifToolHint = LocalizationService.Instance.Get("status.auto_detected");
                viewModel.ExifToolPathHint = _exifToolHint;
            }
            else
            {
                _exifToolHint = string.Empty;
                viewModel.ExifToolPathHint = string.Empty;
            }

            var isServiceMode = string.Equals(options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase);
            // 票 24（ADR 0061）：Service→View 直写清零 — 可用性/文案改经 VM 中转，由 XAML 绑定消费。
            viewModel.PauseResumeAvailable = !isServiceMode;

            _serviceModeController.Attach(options);
            _serviceModeController.UpdateServiceButtons();
        }

        if (!OperatingSystem.IsWindows())
        {
            ServiceManagerTab.IsVisible = false;
        }

        PauseResumeButton.Click += OnPauseResumeClick;
        // 票 24（ADR 0061）：页面控件经 Pages code-behind 内部访问器接线，事件订阅仍统一收口在 MainWindow。
        LogPage.ClearLogsButtonControl.Click += OnClearLogsClick;
        OpenConfigDirButton.Click += OnOpenConfigDirClick;
        ServiceManagerTab.RefreshServiceStatusButtonControl.Click += OnRefreshServiceStatusClick;
        ConfigNavButton.NavButtonControl.Click += OnNavigateClick;
        LogNavButton.NavButtonControl.Click += OnNavigateClick;
        RulesNavButton.NavButtonControl.Click += OnNavigateClick;
        OpenServiceManagerTabButton.NavButtonControl.Click += OnNavigateClick;

        ServiceManagerTab.InstallServiceButtonControl.Click += OnInstallServiceClick;
        ServiceManagerTab.UninstallServiceButtonControl.Click += OnUninstallServiceClick;
        ServiceManagerTab.StartServiceButtonControl.Click += OnStartServiceClick;
        ServiceManagerTab.StopServiceButtonControl.Click += OnStopServiceClick;
        // 票 30：主题/语言/日志级别三套 SelectionChanged 手动镜像删除 —— ComboBox 选中态经
        // SelectedIndex TwoWay 绑定 VM（ThemeVariantIndex/CurrentLocaleIndex/LogLevelIndex），
        // 联动副作用在 VM setter；色板 Click 转发保留（ThemeId 回写）。
        ConfigPage.AddExcludedDirectoryButtonControl.Click += OnAddExcludedDirectoryClick;
        ConfigPage.RemoveExcludedDirectoryButtonControl.Click += OnRemoveExcludedDirectoryClick;
        // 票 25：ThemeSwatch Click 路由统一收口（PART_Radio 的 Button.Click 自 ItemTemplate 内冒泡至 ItemsControl；程序化回填 IsChecked 不触发 = 原 Click 语义零回归）
        ConfigPage.ThemeSwatchListControl.AddHandler(Button.ClickEvent, OnThemePresetSwatchClick);
        RulesPage.SaveRulesButtonControl.Click += OnSaveRulesClick;
        RulesPage.ResetRulesButtonControl.Click += OnResetRulesClick;

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
            onExifToolExePathDetected: exePath => _ = _pollingHostedService.ApplyExifToolVersionFromIpcAsync(_options!.GetExifToolVersionAsync),
            backfillFetcher: token =>
            {
                // 票号05: backfill 协调已收口在 AuditTailService 内部（ADR 0046 语义保持：
                // 失败静默重试不杀 UI）。这里只是薄适配器：惰性读 endpoint + 自兜底 IO 异常。
                var endpoint = _options?.WorkerEndpointName;
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return Task.FromResult<string[]?>(null);
                }
                return FetchBackfillLinesAsync(endpoint, token);
            });

        _auditTail.Start();
        _ = _pollingHostedService.ApplyExifToolVersionFromIpcAsync(options.GetExifToolVersionAsync);

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

        // 票 31：版本轮询与服务状态轮询统一由宿主服务拉起（1s/3s 周期、取消/等待语义不变）。
        _pollingHostedService.Activate();

        // 票 27：启动 ConfigFileWatcher 监听磁盘变更（手改/外部工具/备份回滚）.
        // 与 OnSelfWrite 抑制回调配对，避免 UI 自身写盘触发的 FSW 反弹 reload 覆盖未确认改动.
        _configFileWatcher = new ConfigFileWatcher(
            configPath: options.ConfigPath,
            onReload: () => Dispatcher.UIThread.Post(ApplyRuntimeConfigToUiState, DispatcherPriority.Background));
        ConfigEditor.OnSelfWrite = _configFileWatcher.SuppressNextReload;
        _configFileWatcher.Start();

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

        // 票 27：释放 ConfigFileWatcher 并解绑 OnSelfWrite 抑制回调（避免静态事件悬挂）
        if (_configFileWatcher is not null)
        {
            _configFileWatcher.Dispose();
            _configFileWatcher = null;
        }

        ConfigEditor.OnSelfWrite = null;

        if (_auditTail is not null)
        {
            await _auditTail.StopAsync();
        }

        // 票 31：两轮询的取消/等待收口宿主服务 StopAsync（顺序：版本→服务模式，语义不变）。
        await _pollingHostedService.StopAsync(CancellationToken.None);

        _trayHost?.Dispose();
        base.OnClosed(e);
    }

    // ADR 0050 A4 — self-drawn titlebar caption button handlers.
    // WindowDrawnDecorations is bypassed by WindowDecorations="None" + ExtendClientAreaToDecorationsHint;
    // we own the caption buttons and drive Window state directly (verified via Avalonia 12.1 docs + source).
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ADR 0053 M2: async void to eliminate sync-over-async on UI thread.
    private async void OnPauseResumeClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null)
        {
            return;
        }

        if (string.Equals(_options.RuntimeKind, "service", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isPaused = await _options.IsPausedAsync(CancellationToken.None).ConfigureAwait(true);
        if (isPaused)
        {
            await _options.ResumeAsync(CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await _options.PauseAsync(CancellationToken.None).ConfigureAwait(true);
        }

        if (DataContext is MainWindowViewModel vm)
        {
            var latestPaused = await _options.IsPausedAsync(CancellationToken.None).ConfigureAwait(true);
            vm.RuntimeStatus = ServiceModeController.BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), latestPaused);
        }

        _trayHost?.Refresh();
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearLogs();
            // 票号05: ADR 0037 — drain pending + 水位移尾，但保留原始行去重记忆，
            // 防止清空后 backfill/尾读把已清空的历史行重新送回 UI。
            _auditTail?.NotifyLogsCleared();
        }
    }

    /// <summary>
    /// 票号05: thin adapter over the single IPC entry (票号04). Worker-reachable → raw audit
    /// lines for backfill; unreachable/timeout → null (AuditTailService retries silently).
    /// </summary>
    private async Task<string[]?> FetchBackfillLinesAsync(string endpoint, CancellationToken token)
    {
        try
        {
            var response = await _workerIpc.GetRecentLogsAsync(endpoint, token).ConfigureAwait(false);
            return response is { Ok: true, Logs.Lines: { Length: > 0 } lines } ? lines : null;
        }
        catch (OperationCanceledException)
        {
            throw; // shutdown — let the backfill loop exit
        }
        catch
        {
            return null; // ADR 0035: unreachable worker must degrade silently
        }
    }

    private async void OnAddExcludedDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null) return;
        var task = storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Instance.Get("dialog.select_excluded_dir"),
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
            && ConfigPage.UserExcludedDirectoriesListBoxControl.SelectedItem is string selected)
        {
            vm.UserExcludedDirectories.Remove(selected);
        }
    }

    private async void OnOpenConfigDirClick(object? sender, RoutedEventArgs e)
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

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            UiDiagnosticLog.Write("OpenConfigDir skipped: TopLevel.Launcher unavailable");
            return;
        }

        try
        {
            var launched = await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            if (!launched)
            {
                UiDiagnosticLog.Write($"OpenConfigDir failed: launcher refused {path}");
            }
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"OpenConfigDir failed: {ex.Message}");
        }
    }

    private void OnRefreshServiceStatusClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ServiceStatus = _serviceModeController.StatusText;
            _serviceModeController.UpdateServiceButtons();
        }
    }

    private void OnNavigateClick(object? sender, RoutedEventArgs e)
    {
        // 票 25：sender 为 NavButton 内部 PART_Button，页面键自 NavButton.PageTag 读取。
        if (sender is not Button button) return;
        var pageTag = (button.Parent as NavButton)?.PageTag;
        if (string.IsNullOrEmpty(pageTag))
        {
            return;
        }

        SetCurrentPage(pageTag);
    }

    private void SetCurrentPage(string page)
    {
        var normalized = string.Equals(page, "log", StringComparison.OrdinalIgnoreCase)
            ? "log"
            : string.Equals(page, "rules", StringComparison.OrdinalIgnoreCase)
                ? "rules"
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
        RulesPage.IsVisible = string.Equals(normalized, "rules", StringComparison.Ordinal);

        SetNavButtonActive(ConfigNavButton, string.Equals(normalized, "config", StringComparison.Ordinal));
        SetNavButtonActive(LogNavButton, string.Equals(normalized, "log", StringComparison.Ordinal));
        SetNavButtonActive(OpenServiceManagerTabButton, string.Equals(normalized, "service", StringComparison.Ordinal));
        SetNavButtonActive(RulesNavButton, string.Equals(normalized, "rules", StringComparison.Ordinal));
    }

    // 票 25：active 态唯一驱动 = NavButton.IsActive（内部映射 AppTheme Button.nav.active）。
    private static void SetNavButtonActive(NavButton button, bool isActive) => button.IsActive = isActive;

    private void OnSaveRulesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RulesPanel.SaveCustomRules();
        }
    }

    private void OnResetRulesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RulesPanel.ResetToDefaults();
        }
    }

    // 票 30：OnThemeVariantSelectionChanged/OnLogLevelSelectionChanged/OnLocaleSelectionChanged 已删除 ——
    // 三套 SelectionChanged 手动镜像收敛为 ComboBox SelectedIndex TwoWay 绑定 VM 索引属性，
    // 主题应用/SwitchLocale/LogEnabled 联动在 VM setter 承载（行为不变）。


    // ADR 0052 A3: Theme preset swatch click — dual-axis: ThemeId (preset) independent of ThemeVariant (light/dark)
    // 票 30：只剩事件转发 —— ThemeId 写回 VM（资源应用经 PropertyChanged 响应，防抖经 _configProperties 既有链路）。
    private void OnThemePresetSwatchClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var tag = (sender as RadioButton)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ThemeId = tag;
            }
        }
        catch
        {
            // prevent crash
        }
    }

    // 票 30：OnLogLevelSelectionChanged 已删除 —— 联动（LogEnabled/ShowDetailedEvents）迁入 MainWindowViewModel.LogLevelIndex setter。


    // 票 30：OnLocaleSelectionChanged 已删除 —— SwitchLocale 联动迁入 MainWindowViewModel.CurrentLocale setter。

    // 票 30：SyncLocaleComboSelection/SyncThemeVariantComboSelection/SyncLogLevelComboSelection 已删除 ——
    // ComboBox 选中态回填由 SelectedIndex TwoWay 绑定自动完成（get 侧归一化语义与原回填一致）。


    // ADR 0052 A3: Restore swatch checked state from config
    // 票 30：SyncThemeSwatchSelection/_pending/OnThemeSwatchListLoaded/TryApplyThemeSwatchSelection 已删除 ——
    // 色板选中态回填由 ConfigPage MultiBinding（VM.ThemeId ↔ 色板 ThemeId 比较）驱动，
    // 绑定系统天然处理容器延迟生成，无需 Loaded 重放 hack。

    // 票 30：ReadComboItemString 已删除 —— 全仓无调用者的死代码。

    private static readonly Dictionary<string, string> ThemeVariantTagToLocaleKey = new()
    {
        ["system"] = "theme.option.system",
        ["light"] = "theme.option.light",
        ["dark"] = "theme.option.dark",
    };

    private static readonly Dictionary<string, string> LogLevelTagToLocaleKey = new()
    {
        ["all"] = "loglevel.option.all",
        ["info"] = "loglevel.option.info",
        ["debug"] = "loglevel.option.debug",
        ["warn"] = "loglevel.option.warn",
        ["error"] = "loglevel.option.error",
    };

    private void RefreshI18nComboBoxItems()
    {
        var svc = LocalizationService.Instance;
        var configPage = ConfigPage;
        var logsPage = LogPage;
        RefreshComboBoxItems(configPage.ThemeVariantComboBoxControl, ThemeVariantTagToLocaleKey, svc);
        RefreshComboBoxItems(configPage.LogLevelComboBoxControl, LogLevelTagToLocaleKey, svc);
        // 票 05（ui-craft2）：LogsPage 工具条级别下拉同一套 Tag→key 映射 + SelectionBoxItem 缓存刷新。
        RefreshComboBoxItems(logsPage.LogLevelComboBoxControl, LogLevelTagToLocaleKey, svc);
        // Force Avalonia ComboBox SelectionBoxItem to re-render: setting Content
        // on ComboBoxItem does NOT propagate to the closed dropdown display
        // (SelectionBoxItemPresenter caches the selected item's content). The
        // industry pattern is to temporarily clear selection then restore it,
        // which forces the ComboBox to re-evaluate its SelectionBoxItem.
        ForceComboBoxSelectionBoxRefresh(configPage.ThemeVariantComboBoxControl);
        ForceComboBoxSelectionBoxRefresh(configPage.LogLevelComboBoxControl);
        ForceComboBoxSelectionBoxRefresh(logsPage.LogLevelComboBoxControl);
        // LocaleVariantComboBox items are native-language labels (not i18n keys),
        // so they don't change on locale switch — no refresh needed.
    }

    private static void ForceComboBoxSelectionBoxRefresh(ComboBox? combo)
    {
        if (combo?.Items is null) return;
        var selected = combo.SelectedItem;
        if (selected is null) return;
        // Temporarily clear selection to force SelectionBoxItem to drop its cached content
        combo.SelectedItem = null;
        // Restore selection — this forces ComboBox to re-render SelectionBoxItem
        // with the updated ComboBoxItem.Content value.
        combo.SelectedItem = selected;
    }

    private static void RefreshComboBoxItems(ComboBox? combo, Dictionary<string, string> tagToKey, LocalizationService svc)
    {
        if (combo?.Items is null) return;
        foreach (var item in combo.Items)
        {
            if (item is not ComboBoxItem comboItem) continue;
            var tag = comboItem.Tag?.ToString();
            if (tag is not null && tagToKey.TryGetValue(tag, out var key))
            {
                comboItem.Content = svc.Get(key);
            }
        }
    }

    /// <summary>
    /// Snapshot of RuntimeStatus text using current locale. Called from
    /// CultureChanged handler to refresh the left-top status text immediately
    /// without blocking on IPC (doesn't call IsPausedAsync). Uses the
    /// last-known paused state from the ViewModel's IsRuntimePausedSnapshot
    /// (cheap, sync, no IPC) so we can re-apply the localized status string
    /// in the new language without waiting for the ~1s background poll.
    /// </summary>
    private string RunModeStatusTextSnapshot()
    {
        if (_options is null)
        {
            var fallbackVm = DataContext as MainWindowViewModel;
            return fallbackVm?.RuntimeStatus ?? string.Empty;
        }

        var vm = DataContext as MainWindowViewModel;
        var isPaused = vm?.IsRuntimePausedSnapshot ?? false;
        return ServiceModeController.BuildRuntimeStatusText(_options.RuntimeKind, _options.GetServiceRuntimeState(), isPaused);
    }

    // 票 30：SyncThemeVariantComboSelection 已删除（SelectedIndex 绑定自动回填）。


    // 票 30：SyncThemeSwatchSelection/_pending/OnThemeSwatchListLoaded/TryApplyThemeSwatchSelection 已删除
    //（色板选中态由 MultiBinding 驱动）。

    // 票 30：SyncLogLevelComboSelection 已删除（SelectedIndex 绑定自动回填）。

    // 票 30：ReadThemeVariantSelection 已删除 —— 全仓无调用者的死代码。

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

    // ===== issue 06: 服务编排已抽取到 Services/ServiceModeController.cs =====
    // MainWindow 只保留点击转发 + IUiHost/IViewModelView 状态应用；编排状态机可单测。
    // 原编排方法族（OnInstallServiceClick 实现体、SwitchToDefaultModeAsync、
    // ShutdownTrayWorkerForServiceSwitchAsync 等）实现体已迁移，源断言 guard 同步迁移。

    private void OnInstallServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.InstallAsync();

    private void OnUninstallServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.UninstallAsync();

    private void OnStartServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.StartAsync();

    private void OnStopServiceClick(object? sender, RoutedEventArgs e) => _ = _serviceModeController.StopAsync();


    internal async Task ExitApplicationAsync()
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


    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 票 30：主题双轴（预设 ThemeId / 明暗 ThemeVariant）任一变化 → 社区主题资源重应用
        //（原 OnThemeVariantSelectionChanged / OnThemePresetSwatchClick 的视图侧应用迁至此处；
        // RequestedThemeVariant 仍由 VM ThemeVariant setter 承担）。幂等：启动/热重载的
        // 显式应用调用保留，重复应用结果一致。
        if (e.PropertyName is nameof(MainWindowViewModel.ThemeVariant) or nameof(MainWindowViewModel.ThemeId)
            && DataContext is MainWindowViewModel themeVm)
        {
            App.ApplyCommunityThemeResources(
                themeVm.ThemeId,
                applyDark: string.Equals(NormalizeThemeVariant(themeVm.ThemeVariant), "dark", StringComparison.OrdinalIgnoreCase));
        }

        if (!_configProperties.Contains(e.PropertyName ?? string.Empty))
            return;
        ScheduleDebouncedConfigApply();
    }


    // ADR 0052 A5: Restore sidebar width from config
    private void RestoreSidebarWidth(double width)
    {
        try
        {
            if (MainRootGrid is { } grid && grid.ColumnDefinitions.Count > 0)
            {
                var clamped = Math.Clamp(width, 170, 400);
                grid.ColumnDefinitions[0].Width = new GridLength(clamped);
            }
        }
        catch
        {
            // control not ready
        }
    }

    // ADR 0052 A5: Wire GridSplitter drag completion for persistence
    private void OnSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        try
        {
            if (MainRootGrid is { } grid && grid.ColumnDefinitions.Count > 0 && DataContext is MainWindowViewModel vm)
            {
                var width = Math.Clamp(grid.ColumnDefinitions[0].ActualWidth, 170, 400);
                vm.SidebarWidth = width;
                ScheduleDebouncedConfigApply();
            }
        }
        catch
        {
            // best-effort
        }
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
                QuarantineDirectory: vm.QuarantineDirectory,
                SidebarWidth: vm.SidebarWidth,
                ThemeId: vm.ThemeId);
            ConfigEditor.UpdateConfig(_options.ConfigPath, command);
            try
            {
                await _workerIpc.ReloadConfigAsync(_options.WorkerEndpointName, CancellationToken.None);
            }
            catch
            {
                vm.SaveStatus = LocalizationService.Instance.Get("msg.auto_saved_worker_down");
                _toastService.Show("config.save", ToastKind.Information, LocalizationService.Instance.Get("toast.config_saved_worker_down"));
                ScheduleSaveStatusClear();
                return;
            }
            ApplyRuntimeConfigToUiState();
            vm.SaveStatus = LocalizationService.Instance.Get("msg.auto_saved");
            _toastService.Show("config.save", ToastKind.Success, LocalizationService.Instance.Get("toast.config_saved"));
            ScheduleSaveStatusClear();
        }
        catch (Exception ex)
        {
            vm.SaveStatus = LocalizationService.Instance.Get("msg.save_failed_exception", ex.Message);
            _toastService.Show("config.save", ToastKind.Error, LocalizationService.Instance.Get("toast.config_save_failed"), ex.Message);
            ScheduleSaveStatusClear();
        }
    }


    // 票 30：OnApplyConfigClick 已删除 —— ADR 0037「应用配置」按钮移除后即为无引用死代码
    //（全仓 grep 仅守卫测试断言其存在）；守卫 MainWindowConfigHotReloadSourceTests 同步迁移为
    // 锁定防抖即时应用链（ScheduleDebouncedConfigApply → ApplyConfigImmediatelyAsync）。

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

    // 票 30：ApplyConfigForCurrentModeAsync 已删除 —— 全仓无调用者的死代码（防抖链路
    // ApplyConfigImmediatelyAsync 才是唯一 Worker ReloadConfig 入口）。

    private void ApplyRuntimeConfigToUiState()
    {
        if (_options is null)
        {
            return;
        }

        try
        {
            var cfg = LoadConfigOrDefault(_options.ConfigPath) ?? AppConfig.Default;
            // 票20 检查点 C：HideMainWindowOnStartup/HideTrayIcon/UseTrayIcon 写入收口至具名方法
            // （UpdateHideFlags 内含原「tray 模式下 UseTrayIcon = !HideTrayIcon」联动，语义逐字等价）。
            _options.UpdateHideFlags(cfg.Ui.HideMainWindowOnStartup, cfg.Ui.HideTrayIcon);
            var normalizedThemeVariant = NormalizeThemeVariant(cfg.Ui.ThemeVariant);

            if (DataContext is MainWindowViewModel vm)
            {
                vm.ExifToolPath = cfg.ExifTool.Path;
                vm.BackupEnabled = cfg.Backup.Enabled;
                vm.LogEnabled = cfg.Audit.DiagnosticMode;
                vm.HotFolderPath = cfg.Watch.HotFolder;
                vm.HideGuiOnStartup = cfg.Ui.HideMainWindowOnStartup;
                vm.HideTrayIcon = cfg.Ui.HideTrayIcon;
                vm.ThemeVariant = normalizedThemeVariant;
                vm.ThemeId = string.IsNullOrWhiteSpace(cfg.Ui.ThemeId) ? "catppuccin" : cfg.Ui.ThemeId;
                // 票 30：Sync*ComboSelection/SyncThemeSwatchSelection 回填调用删除 —— 选中态由绑定自动跟随 VM。
                App.ApplyCommunityThemeResources(vm.ThemeId, applyDark: string.Equals(NormalizeThemeVariant(vm.ThemeVariant), "dark", StringComparison.OrdinalIgnoreCase));
                vm.BackupDirectory = cfg.Backup.Directory;
                vm.AuditLogDirectory = cfg.Audit.LogDirectory;
                vm.LogLevel = cfg.Audit.LogLevel;
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

    // 票 31：PollVersionAsync / ApplyExifToolVersionFromIpcAsync 已迁移
    // WindowPollingHostedService（循环体/取消/投递语义逐字保持）；
    // NormalizeExifToolStatus 唯一权威仍在 ServiceModeController（票 30 结论保持）。
}

public partial class MainWindow
{

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
