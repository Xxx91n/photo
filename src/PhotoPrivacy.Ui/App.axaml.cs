using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
            UiDiagnosticLog.Write("App.OnFrameworkInitializationCompleted entered with desktop lifetime");
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

            if (desktop.MainWindow is MainWindow window)
            {
                UiDiagnosticLog.Write("MainWindow created; InitializeRuntime starting");
                window.InitializeRuntime(RuntimeOptions);
                UiDiagnosticLog.Write($"MainWindow.InitializeRuntime done. RuntimeKind={RuntimeOptions.RuntimeKind}, UseTrayIcon={RuntimeOptions.UseTrayIcon}, HideTrayIcon={RuntimeOptions.HideTrayIcon}, HideMainWindowOnStartup={RuntimeOptions.HideMainWindowOnStartup}");
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
                UiDiagnosticLog.Write("App forced initial MainWindow Show/Activate");
                RuntimeOptions.ShowMainWindow = () =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UiDiagnosticLog.Write("RuntimeOptions.ShowMainWindow invoked");
                        window.Show();
                        window.WindowState = WindowState.Normal;
                        window.Activate();
                    });
                };

            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
