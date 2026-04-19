using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PhotoPrivacy.Ui.ViewModels;
using PhotoPrivacy.Ui.Views;

namespace PhotoPrivacy.Ui;

public partial class App : Application
{
    public static BackgroundUiOptions RuntimeOptions { get; set; } = BackgroundUiOptions.CreateFallback();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

            if (desktop.MainWindow is MainWindow window)
            {
                window.InitializeRuntime(RuntimeOptions);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
