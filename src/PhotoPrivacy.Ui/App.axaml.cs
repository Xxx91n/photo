using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using PhotoPrivacy.Core.Configuration;
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

                var config = LoadConfigOrDefault(RuntimeOptions.ConfigPath);
                if (config is not null)
                {
                    Application.Current!.RequestedThemeVariant = config.Ui.ThemeVariant switch
                    {
                        "dark" => ThemeVariant.Dark,
                        "light" => ThemeVariant.Light,
                        _ => ThemeVariant.Default
                    };
                }

                TrySetWindowIcon(window);

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

    private static void TrySetWindowIcon(Window window)
    {
        try
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-dot-16.png.base64");
            if (!File.Exists(assetPath))
            {
                return;
            }

            var base64 = File.ReadAllText(assetPath).Trim();
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            window.Icon = new WindowIcon(bitmap);
        }
        catch
        {
            // best-effort icon
        }
    }
}
