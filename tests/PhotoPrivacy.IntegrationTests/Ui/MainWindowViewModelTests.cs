using System.ComponentModel;
using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ViewModel_Should_Implement_NotifyPropertyChanged()
    {
        var vm = new MainWindowViewModel();

        Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
    }

    [Fact]
    public void Setting_ExifToolVersion_Should_Raise_PropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string>();
        var notify = Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
        notify.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        vm.ExifToolVersion = "13.40";

        Assert.Contains(nameof(MainWindowViewModel.ExifToolVersion), raised);
    }

    [Fact]
    public void ShowDetailedEvents_Should_Default_To_False_And_Raise_PropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string>();
        var notify = Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
        notify.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        Assert.False(vm.ShowDetailedEvents);

        vm.ShowDetailedEvents = true;

        Assert.True(vm.ShowDetailedEvents);
        Assert.Contains(nameof(MainWindowViewModel.ShowDetailedEvents), raised);
    }

    [Fact]
    public void AppendLog_Should_Insert_Newest_At_Top()
    {
        var vm = new MainWindowViewModel();
        vm.AppendLog(new AuditLogEntry("08:00:00", "file_detected", "🔍 检测到文件", "a", "first", "#000"));
        vm.AppendLog(new AuditLogEntry("08:00:01", "file_processing_succeeded", "✅ 清理完成", "b", "second", "#000"));

        Assert.Equal("second", vm.LogEntries[0].Message);
        Assert.Equal("first", vm.LogEntries[1].Message);
    }

    [Fact]
    public void ConfigFields_Should_Raise_PropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string>();
        var notify = Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
        notify.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

       vm.ExifToolPath = @"C:\Program Files\ExifTool\exiftool.exe";
        vm.BackupEnabled = true;
        vm.LogEnabled = true;
        vm.HotFolderPath = @"D:\hot";
        vm.HideGuiOnStartup = false;
        vm.HideGuiOnStartup = true;
        vm.HideTrayIcon = true;

        Assert.Contains(nameof(MainWindowViewModel.ExifToolPath), raised);
        Assert.Contains(nameof(MainWindowViewModel.BackupEnabled), raised);
        Assert.Contains(nameof(MainWindowViewModel.LogEnabled), raised);
        Assert.Contains(nameof(MainWindowViewModel.HotFolderPath), raised);
        Assert.Contains(nameof(MainWindowViewModel.HideGuiOnStartup), raised);
        Assert.Contains(nameof(MainWindowViewModel.HideTrayIcon), raised);
    }

    [Fact]
    public void NewUiProperties_Should_Raise_PropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        vm.CurrentPage = "log";
        vm.ThemeVariant = "dark";
        vm.SaveStatus = "已保存，待应用";

        Assert.Contains(nameof(MainWindowViewModel.CurrentPage), raised);
        Assert.Contains(nameof(MainWindowViewModel.ThemeVariant), raised);
        Assert.Contains(nameof(MainWindowViewModel.SaveStatus), raised);
    }
}
