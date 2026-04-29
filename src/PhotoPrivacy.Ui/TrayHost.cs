using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace PhotoPrivacy.Ui;

public sealed class TrayHost : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _pauseResumeItem;
    private readonly NativeMenuItem _openMainWindowItem;
    private readonly NativeMenuItem _exitItem;
    private readonly Window _window;
    private readonly BackgroundUiOptions _options;
    private readonly Func<Task> _exitAsync;
    private bool _allowWindowClose;

    public TrayHost(Window window, BackgroundUiOptions options, Func<Task> exitAsync)
    {
        _window = window;
        _options = options;
        _exitAsync = exitAsync;

        _pauseResumeItem = new NativeMenuItem("暂停");
        _pauseResumeItem.Click += (_, _) => TogglePauseResume();

        _openMainWindowItem = new NativeMenuItem("打开主窗口");
        _openMainWindowItem.Click += (_, _) => ShowMainWindow();

        _exitItem = new NativeMenuItem("退出");
        _exitItem.Click += async (_, _) => await _exitAsync();

        var menu = new NativeMenu
        {
            _pauseResumeItem,
            _openMainWindowItem,
            new NativeMenuItemSeparator(),
            _exitItem
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "PhotoPrivacy",
            Menu = menu,
            Icon = CreateDefaultIcon(),
            IsVisible = false
        };
        _trayIcon.Clicked += OnTrayClicked;
        UiDiagnosticLog.Write($"TrayHost created. IconLoaded={_trayIcon.Icon is not null}");

        _window.Closing += OnWindowClosing;
        UpdateMenu();
    }

    public void Dispose()
    {
        _window.Closing -= OnWindowClosing;
        _trayIcon.Clicked -= OnTrayClicked;
        _trayIcon.IsVisible = false;
    }

    public void Refresh()
    {
        UpdateMenu();
    }

    public bool IsVisible
    {
        get => _trayIcon.IsVisible;
        set
        {
            _trayIcon.IsVisible = value;
            UiDiagnosticLog.Write($"TrayHost.IsVisible set to {value}");
        }
    }

    public bool IconLoaded => _trayIcon.Icon is not null;

    public void AllowWindowClose()
    {
        _allowWindowClose = true;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose || !_trayIcon.IsVisible)
        {
            return;
        }

        e.Cancel = true;
        _window.Hide();
    }

    private void TogglePauseResume()
    {
        try
        {
            var paused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (paused)
            {
                _options.ResumeAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                _options.PauseAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            UpdateMenu();
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"TrayHost.TogglePauseResume failed: {ex.Message}");
            _pauseResumeItem.Header = "暂停";
        }
    }

    private void ShowMainWindow()
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
        _window.BringIntoView();
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void UpdateMenu()
    {
        try
        {
            var paused = _options.IsPausedAsync(CancellationToken.None).GetAwaiter().GetResult();
            _pauseResumeItem.Header = paused ? "恢复" : "暂停";
        }
        catch (Exception ex)
        {
            UiDiagnosticLog.Write($"TrayHost.UpdateMenu failed: {ex.Message}");
            _pauseResumeItem.Header = "暂停";
        }
    }

    private static WindowIcon? CreateDefaultIcon()
    {
        try
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-dot-16.png.base64");
            if (!File.Exists(assetPath))
            {
                UiDiagnosticLog.Write($"Tray icon asset missing: {assetPath}");
                return null;
            }

            var base64 = File.ReadAllText(assetPath).Trim();
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            return new WindowIcon(bitmap);
        }
        catch
        {
            UiDiagnosticLog.Write("Tray icon load failed");
            return null;
        }
    }
}
