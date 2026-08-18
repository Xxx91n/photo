using System.Collections.ObjectModel;
using PhotoPrivacy.Ui.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhotoPrivacy.Core.Configuration;
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
        ApplyLocaleFlowDirection();
    }

    public void ApplyLocaleFlowDirection()
    {
        UiFlowDirection = LocalizationService.Instance.IsRtl()
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;
    }
    private string _currentMode = "background";
    private string _runtimeStatus = LocalizationService.Instance.Get("status.running");
    private string _exifToolVersion = LocalizationService.Instance.Get("msg.exiftool_not_found");
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

    public RulesPanelViewModel RulesPanel { get; } =
        new RulesPanelViewModel(new FormatRulesStore(Path.Combine(AppContext.BaseDirectory, "config")));

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
        set => SetField(ref _currentLocale, value);
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

    public string ModeLabel => IsServiceMode ? LocalizationService.Instance.Get("mode.service") : LocalizationService.Instance.Get("mode.tray");

    public string StatusDotColor => IsRuntimeRunning ? "#22C55E" : "#9CA3AF";

    public string PauseResumeLabel => IsRuntimePaused ? LocalizationService.Instance.Get("btn.resume") : LocalizationService.Instance.Get("btn.pause");

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
