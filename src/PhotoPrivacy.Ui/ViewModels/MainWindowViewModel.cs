using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoPrivacy.Ui.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public string Title { get; } = "PhotoPrivacy";
    private string _currentMode = "background";
    private string _runtimeStatus = "运行中";
    private string _exifToolVersion = "unknown";
    private string _serviceStatus = "N/A";
    private bool _showServiceManagerTab;

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

    public ObservableCollection<string> LogLines { get; } = [];

    public void AppendLog(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > 500)
        {
            LogLines.RemoveAt(0);
        }
    }

    public void ClearLogs()
    {
        LogLines.Clear();
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
