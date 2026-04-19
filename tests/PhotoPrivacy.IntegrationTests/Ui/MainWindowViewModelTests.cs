using System.ComponentModel;
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
}
