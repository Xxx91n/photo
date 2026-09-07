using System.Collections.ObjectModel;
using PhotoPrivacy.Ui.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui.Services;
using System.IO;

namespace PhotoPrivacy.Ui.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public string Title { get; } = "PhotoPrivacy";

    private Avalonia.Media.FlowDirection _uiFlowDirection = Avalonia.Media.FlowDirection.LeftToRight;

    public Avalonia.Media.FlowDirection UiFlowDirection
    {
        get => _uiFlowDirection;
        set => SetField(ref _uiFlowDirection, value);
    }

    public void RefreshLocaleDependent()
    {
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(PauseResumeLabel));
        OnPropertyChanged(nameof(PauseResumeContent));
        ApplyLocaleFlowDirection();
    }

    public void ApplyLocaleFlowDirection()
    {
        UiFlowDirection = _localization.IsRtl()
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;
    }
    private readonly LocalizationService _localization;

    // 票 29（架构恢复第七轮）：构造注入 —— LocalizationService 经组合根容器注入
    //（检查点 B：静态单例收敛第一例，容器与 Instance 为同一实例）。
    // 可选参数默认回落 Instance，保留既有无参调用/测试兼容（行为不变）。
    public MainWindowViewModel(LocalizationService? localization = null, FormatRulesStore? rulesStore = null)
    {
        _localization = localization ?? LocalizationService.Instance;
        _runtimeStatus = _localization.Get("status.running");
        _exifToolVersion = _localization.Get("msg.exiftool_not_found");
        RulesPanel = new RulesPanelViewModel(rulesStore ?? new FormatRulesStore(Path.Combine(AppContext.BaseDirectory, "config")));
    }

    private string _currentMode = "background";
    private string _runtimeStatus;
    private string _exifToolVersion;
    private string _serviceStatus = "N/A";
    private bool _showServiceManagerTab;
    private bool _showDetailedEvents;
    private string _exifToolPath = string.Empty;
    private bool _backupEnabled;
    private bool _logEnabled;
    private string _hotFolderPath = string.Empty;
    private bool _hideGuiOnStartup = true;
    private bool _hideTrayIcon;
    private string _currentPage = "config";
    private string _themeVariant = "system";
    private string _currentLocale = "zh-CN";
    private string _saveStatus = string.Empty;
    private string _backupDirectory = string.Empty;
    private string _auditLogDirectory = string.Empty;
    private string _logLevel = "info";
    private string _exifToolPathHint = string.Empty;
    private bool _quarantineEnabled = true;
    private string _quarantineDirectory = string.Empty;
    private ObservableCollection<string> _userExcludedDirectories = [];
    private ObservableCollection<string> _systemAutoExcludedDirectories = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentMode
    {
        get => _currentMode;
        set
        {
            if (SetField(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(ModeColor));
                OnPropertyChanged(nameof(ModeLabel));
            }
        }
    }

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        set
        {
            if (SetField(ref _runtimeStatus, value))
            {
                OnPropertyChanged(nameof(StatusDotColor));
                OnPropertyChanged(nameof(PauseResumeLabel));
                OnPropertyChanged(nameof(PauseResumeContent));
            }
        }
    }

    public string ExifToolVersion
    {
        get => _exifToolVersion;
        set => SetField(ref _exifToolVersion, value);
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set
        {
            if (SetField(ref _serviceStatus, value))
            {
                OnPropertyChanged(nameof(ServiceStatusDotColor));
            }
        }
    }

    public bool ShowServiceManagerTab
    {
        get => _showServiceManagerTab;
        set => SetField(ref _showServiceManagerTab, value);
    }

    // 票 24（ADR 0061）：服务管理页按钮可用性单一真相源 — adapter 直写按钮清零，页面经绑定消费。
    private ServiceButtonState _serviceButtons = ServiceButtonStates.AllDisabled;
    public ServiceButtonState ServiceButtons
    {
        get => _serviceButtons;
        set
        {
            if (SetField(ref _serviceButtons, value))
            {
                OnPropertyChanged(nameof(ServiceButtons.InstallEnabled));
                OnPropertyChanged(nameof(ServiceButtons.UninstallEnabled));
                OnPropertyChanged(nameof(ServiceButtons.StartEnabled));
                OnPropertyChanged(nameof(ServiceButtons.StopEnabled));
            }
        }
    }

    public bool ShowDetailedEvents
    {
        get => _showDetailedEvents;
        set => SetField(ref _showDetailedEvents, value);
    }

    public string ExifToolPath
    {
        get => _exifToolPath;
        set => SetField(ref _exifToolPath, value);
    }

    public bool BackupEnabled
    {
        get => _backupEnabled;
        set => SetField(ref _backupEnabled, value);
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set => SetField(ref _logEnabled, value);
    }

    public string HotFolderPath
    {
        get => _hotFolderPath;
        set => SetField(ref _hotFolderPath, value);
    }

    public bool HideGuiOnStartup
    {
        get => _hideGuiOnStartup;
        set => SetField(ref _hideGuiOnStartup, value);
    }

    public bool HideTrayIcon
    {
        get => _hideTrayIcon;
        set => SetField(ref _hideTrayIcon, value);
    }

    public string CurrentPage
    {
        get => _currentPage;
        set => SetField(ref _currentPage, value);
    }

    public RulesPanelViewModel RulesPanel { get; }

    public string ThemeVariant
    {
        get => _themeVariant;
        set
        {
            if (SetField(ref _themeVariant, value))
            {
                var normalized = value?.ToLowerInvariant();
                if (Avalonia.Application.Current is not null)
                {
                    Avalonia.Application.Current.RequestedThemeVariant = normalized switch
                    {
                        "dark" => Avalonia.Styling.ThemeVariant.Dark,
                        "light" => Avalonia.Styling.ThemeVariant.Light,
                        _ => Avalonia.Styling.ThemeVariant.Default
                    };
                }
            }
        }
    }

    // 票 30（架构恢复第七轮）：三套手动镜像收敛 —— ComboBox 选中态经 SelectedIndex TwoWay 绑定
    // 由 VM 索引属性驱动（spec 研究输入 Q1：设置单一真相源在 VM）。get 侧归一化与原
    // Sync*ComboSelection 回填语义一致（OrdinalIgnoreCase 匹配，未知值回落默认项）；
    // set 侧仅承载用户交互路径的联动副作用（启动/热重载直接赋 string 属性，不触发）。
    public int ThemeVariantIndex
    {
        get => NormalizeThemeVariantValue(_themeVariant) switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        set => ThemeVariant = value switch
        {
            1 => "light",
            2 => "dark",
            _ => "system"
        };
    }

    // ADR 0052 A3: Theme preset ID (independent of variant axis) — atomcode research: dual-axis design
    private string _themeId = "catppuccin";
    public string ThemeId
    {
        get => _themeId;
        set => SetField(ref _themeId, value);
    }

    // ADR 0052 A5: Sidebar width for persistence
    private double _sidebarWidth = 200.0;
    public double SidebarWidth
    {
        get => _sidebarWidth;
        set => SetField(ref _sidebarWidth, value);
    }

    public string CurrentLocale
    {
        get => _currentLocale;
        set
        {
            if (SetField(ref _currentLocale, value) && value is not null)
            {
                // 票 30：SwitchLocale 联动迁入 setter（原 OnLocaleSelectionChanged 手动镜像）；
                // ADR 0044 决策 8 / ADR 0047 A6 持久化链路「启动时读取 → Initialize(locale)」由此贯通，
                // config ui.locale 与系统语言不一致时 UI 文案随 config（SwitchLocale 自带防重入幂等）。
                _localization.SwitchLocale(value);
            }
        }
    }

    // 票 30：语言下拉选中态绑定 —— 顺序与 ConfigPage.axaml 的 10 个 ComboBoxItem 一致；
    // 未知 locale 回落 0（zh-CN），与原 SyncLocaleComboSelection 的 SelectedIndex=0 兜底一致。
    public static readonly string[] LocaleOrder = ["zh-CN", "en", "ja", "ko", "de", "fr", "es", "pt", "ru", "ar"];

    public int CurrentLocaleIndex
    {
        get
        {
            var idx = Array.FindIndex(LocaleOrder, l => string.Equals(l, _currentLocale, StringComparison.OrdinalIgnoreCase));
            return idx < 0 ? 0 : idx;
        }
        set => CurrentLocale = value >= 0 && value < LocaleOrder.Length ? LocaleOrder[value] : LocaleOrder[0];
    }

    public string SaveStatus
    {
        get => _saveStatus;
        set => SetField(ref _saveStatus, value);
    }

    public string BackupDirectory
    {
        get => _backupDirectory;
        set => SetField(ref _backupDirectory, value);
    }

    public string AuditLogDirectory
    {
        get => _auditLogDirectory;
        set => SetField(ref _auditLogDirectory, value);
    }

    public string LogLevel
    {
        get => _logLevel;
        set => SetField(ref _logLevel, value);
    }

    // 票 30：日志级别下拉选中态绑定 —— 顺序与 ConfigPage.axaml 的 5 个 ComboBoxItem 一致；
    // 未知级别回落 1（info），与原 SyncLogLevelComboSelection 的 SelectedIndex=1 兜底一致。
    // set 侧联动 LogEnabled/ShowDetailedEvents（原 OnLogLevelSelectionChanged 行为迁入；
    // 启动/热重载直接赋 LogLevel string，不触发联动 —— 行为不变）。
    public int LogLevelIndex
    {
        get => (_logLevel ?? string.Empty).ToLowerInvariant() switch
        {
            "all" => 0,
            "info" => 1,
            "debug" => 2,
            "warn" => 3,
            "error" => 4,
            _ => 1
        };
        set
        {
            var level = value switch
            {
                0 => "all",
                2 => "debug",
                3 => "warn",
                4 => "error",
                _ => "info"
            };
            LogLevel = level;
            LogEnabled = level is "all" or "debug";
            ShowDetailedEvents = level is "all" or "debug";
        }
    }

    private static string NormalizeThemeVariantValue(string? value)
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

    // ADR 0052 A6: Path hint properties for Watermark display
    public string HotFolderPathHint => PhotoPrivacy.Core.Constants.DefaultPaths.DefaultHotFolder;
    public string BackupDirectoryHint => PhotoPrivacy.Core.Constants.DefaultPaths.DefaultBackupDirectory;
    public string QuarantineDirectoryHint => PhotoPrivacy.Core.Constants.DefaultPaths.DefaultQuarantineDirectory;
    public string AuditLogDirectoryHint => PhotoPrivacy.Core.Constants.DefaultPaths.DefaultAuditLogDirectory;

    public string ExifToolPathHint
    {
        get => _exifToolPathHint;
        set
        {
            if (SetField(ref _exifToolPathHint, value))
            {
                OnPropertyChanged(nameof(HasExifToolHint));
            }
        }
    }

    public bool QuarantineEnabled
    {
        get => _quarantineEnabled;
        set => SetField(ref _quarantineEnabled, value);
    }

    public string QuarantineDirectory
    {
        get => _quarantineDirectory;
        set => SetField(ref _quarantineDirectory, value);
    }

    public ObservableCollection<string> UserExcludedDirectories => _userExcludedDirectories;

    public ObservableCollection<string> SystemAutoExcludedDirectories => _systemAutoExcludedDirectories;

    public bool HasExifToolHint => !string.IsNullOrWhiteSpace(_exifToolPathHint);

    public string ModeColor => IsServiceMode ? "#3B82F6" : "#22C55E";

    public string ModeLabel => IsServiceMode ? _localization.Get("mode.service") : _localization.Get("mode.tray");

    public string StatusDotColor => IsRuntimeRunning ? "#22C55E" : "#9CA3AF";

    public string PauseResumeLabel => IsRuntimePaused ? _localization.Get("btn.resume") : _localization.Get("btn.pause");

    // 票 24（ADR 0061）：暂停/恢复按钮可用性 + 派生文案（服务模式禁用并显示不可用文案）— 单一真相源在 VM。
    private bool _pauseResumeAvailable = true;
    public bool PauseResumeAvailable
    {
        get => _pauseResumeAvailable;
        set
        {
            if (SetField(ref _pauseResumeAvailable, value))
            {
                OnPropertyChanged(nameof(PauseResumeContent));
            }
        }
    }

    public string PauseResumeContent => PauseResumeAvailable
        ? PauseResumeLabel
        : _localization.Get("status.pause_service_unavailable");

    public string ServiceStatusDotColor
    {
        get
        {
            var text = _serviceStatus;
            if (text.Contains("Running", StringComparison.OrdinalIgnoreCase)
                || text.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                return "#22C55E";
            }

            if (text.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }

            if (text.Contains("stop", StringComparison.OrdinalIgnoreCase)
                || text.Contains("stopped", StringComparison.OrdinalIgnoreCase)
                || text.Contains("pending", StringComparison.OrdinalIgnoreCase))
            {
                return "#F59E0B";
            }

            return "#9CA3AF";
        }
    }

    public ObservableCollection<AuditLogEntry> LogEntries { get; } = [];

    // 票 26（ADR 0062）: 空审计流占位开关 — 在 Append/Clear 收口处联动刷新（无事件订阅，防泄漏）。
    private bool _hasNoLogs = true;
    public bool HasNoLogs
    {
        get => _hasNoLogs;
        private set => SetField(ref _hasNoLogs, value);
    }

    private bool IsServiceMode =>
        string.Equals(_currentMode, "service", StringComparison.OrdinalIgnoreCase)
        || _currentMode.Contains("service", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Public snapshot of the last-known paused state, used by MainWindow's
    /// CultureChanged handler to re-localize RuntimeStatus without IPC.
    /// Mirrors the private IsRuntimePaused derived from the current
    /// _runtimeStatus string content.
    /// </summary>
    public bool IsRuntimePausedSnapshot => IsRuntimePaused;

    private bool IsRuntimePaused =>
        _runtimeStatus.Contains("paused", StringComparison.OrdinalIgnoreCase)
        || _runtimeStatus.Contains("pause", StringComparison.OrdinalIgnoreCase);

    private bool IsRuntimeRunning =>
        _runtimeStatus.Contains("running", StringComparison.OrdinalIgnoreCase)
        || _runtimeStatus.Contains("run", StringComparison.OrdinalIgnoreCase);

    public void AppendLog(AuditLogEntry entry)
    {
        LogEntries.Insert(0, entry);
        HasNoLogs = false;
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    /// <summary>
    /// 批量追加日志条目，减少 CollectionChanged 事件次数，避免 UI 线程洪泛。
    /// 新条目插入头部，超出上限时从尾部批量移除。
    /// </summary>
    public void AppendLogBatch(IReadOnlyList<AuditLogEntry> entries)
    {
        if (entries.Count == 0) return;

        // 逆序插入头部，保持最新在前
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            LogEntries.Insert(0, entries[i]);
        }
        HasNoLogs = false;

        // 批量裁剪尾部
        var excess = LogEntries.Count - 500;
        if (excess > 0)
        {
            for (var i = 0; i < excess; i++)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        }
    }

    public void ClearLogs()
    {
        LogEntries.Clear();
        HasNoLogs = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
