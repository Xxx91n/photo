using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoPrivacy.Ui.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public string Title { get; } = "PhotoPrivacy";
    private string _currentMode = "background";
    private string _runtimeStatus = "运行中";
    private string _exifToolVersion = "未找到 ExifTool";
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
    private string _saveStatus = string.Empty;
    private string _backupDirectory = string.Empty;
    private string _auditLogDirectory = string.Empty;
    private string _logLevel = "info";
    private string _exifToolPathHint = string.Empty;
    private bool _quarantineEnabled = true;
    private string _quarantineDirectory = @"D:\hot\_quarantine";
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

    public string ModeLabel => IsServiceMode ? "服务模式" : "托盘模式";

    public string StatusDotColor => IsRuntimeRunning ? "#22C55E" : "#9CA3AF";

    public string PauseResumeLabel => IsRuntimePaused ? "▶ 恢复" : "⏸ 暂停";

    public string ServiceStatusDotColor
    {
        get
        {
            var text = _serviceStatus;
            if (text.Contains("Running", StringComparison.OrdinalIgnoreCase)
                || text.Contains("运行", StringComparison.OrdinalIgnoreCase))
            {
                return "#22C55E";
            }

            if (text.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("失败", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }

            if (text.Contains("stop", StringComparison.OrdinalIgnoreCase)
                || text.Contains("停止", StringComparison.OrdinalIgnoreCase)
                || text.Contains("pending", StringComparison.OrdinalIgnoreCase))
            {
                return "#F59E0B";
            }

            return "#9CA3AF";
        }
    }

    public ObservableCollection<AuditLogEntry> LogEntries { get; } = [];

    private bool IsServiceMode =>
        _currentMode.Contains("服务", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_currentMode, "service", StringComparison.OrdinalIgnoreCase);

    private bool IsRuntimePaused =>
        _runtimeStatus.Contains("暂停", StringComparison.OrdinalIgnoreCase)
        || _runtimeStatus.Contains("paused", StringComparison.OrdinalIgnoreCase);

    private bool IsRuntimeRunning =>
        _runtimeStatus.Contains("运行", StringComparison.OrdinalIgnoreCase)
        || _runtimeStatus.Contains("running", StringComparison.OrdinalIgnoreCase);

    public void AppendLog(AuditLogEntry entry)
    {
        LogEntries.Insert(0, entry);
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
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
