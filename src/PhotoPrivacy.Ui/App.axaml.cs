using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
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
                    // Dual-axis separation (ADR 0052 A3, commit cab8d55):
                    //   theme_variant (axis: system/light/dark) -> RequestedThemeVariant
                    //   theme_id (axis: catppuccin/dracula/nord/tokyonight/onedarkpro) -> ResourceDictionary swap
                    // Legacy compatibility: old configs that stored a preset ID inside theme_variant
                    // (e.g. "catppuccin") are mapped to Dark so upgrades do not flip to Light.
                    var themeVariant = config.Ui.ThemeVariant switch
                    {
                        "system" or "" or null => ThemeVariant.Default,
                        "light" => ThemeVariant.Light,
                        "dark" or "nord" or "catppuccin" or "dracula" or "tokyonight" or "onedarkpro" => ThemeVariant.Dark,
                        _ => ThemeVariant.Default
                    };
                    Application.Current!.RequestedThemeVariant = themeVariant;
                    var startupThemeId = string.IsNullOrWhiteSpace(config.Ui.ThemeId) ? "catppuccin" : config.Ui.ThemeId;
                    ApplyCommunityThemeResources(startupThemeId, applyDark: themeVariant != ThemeVariant.Light);
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

    internal static void ApplyCommunityThemeResources(string themeName, bool applyDark)
    {
        try
        {
            var resources = Application.Current!.Resources;
            // AtomCode research 2026-08-18: index-replace Fallback-C (Avalonia #9691)
            // Do NOT call MergedDictionaries.Clear() — that disconnects resources and
            // crashes DataGrid rendering (root cause of Rules page crash).
            // DesignTokens stays at index 0; community theme occupies index 1.
            while (resources.MergedDictionaries.Count > 2)
            {
                resources.MergedDictionaries.RemoveAt(resources.MergedDictionaries.Count - 1);
            }
            while (resources.MergedDictionaries.Count < 2)
            {
                resources.MergedDictionaries.Add(new ResourceDictionary());
            }

            if (!applyDark)
            {
                // Light mode: replace slot 1 with empty dict so Semi Light theme renders.
                resources.MergedDictionaries[1] = new ResourceDictionary();
                return;
            }

            var themeFile = themeName switch
            {
                "nord" => "Themes/NordDark.axaml",
                "catppuccin" => "Themes/Catppuccin.axaml",
                "dracula" => "Themes/Dracula.axaml",
                "tokyonight" => "Themes/TokyoNight.axaml",
                "onedarkpro" => "Themes/OneDarkPro.axaml",
                _ => null
            };
            if (themeFile is null)
            {
                resources.MergedDictionaries[1] = new ResourceDictionary();
                return;
            }

            var uri = new Uri("avares://PhotoPrivacy.Ui/" + themeFile);
            var include = new ResourceInclude(baseUri: null) { Source = uri };
            resources.MergedDictionaries[1] = include;
        }
        catch (Exception ex)
        {
            // best-effort: if theme resource fails to load, fall back to default Semi theme
            UiDiagnosticLog.Write($"ApplyCommunityThemeResources failed for themeName={themeName}, applyDark={applyDark}: {ex.Message}");
        }
    }
}
