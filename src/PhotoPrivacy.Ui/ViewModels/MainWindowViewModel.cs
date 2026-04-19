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

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentMode
    {
        get => _currentMode;
        set => SetField(ref _currentMode, value);
    }

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        set => SetField(ref _runtimeStatus, value);
    }

    public string ExifToolVersion
    {
        get => _exifToolVersion;
        set => SetField(ref _exifToolVersion, value);
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetField(ref _serviceStatus, value);
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

    public ObservableCollection<AuditLogEntry> LogEntries { get; } = [];

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
