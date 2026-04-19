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
        set => _trayIcon.IsVisible = value;
    }

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
        if (_options.IsPaused())
        {
            _options.Resume();
        }
        else
        {
            _options.Pause();
        }

        UpdateMenu();
    }

    private void ShowMainWindow()
    {
        _window.Show();
        _window.Activate();
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void UpdateMenu()
    {
        _pauseResumeItem.Header = _options.IsPaused() ? "恢复" : "暂停";
    }

    private static WindowIcon? CreateDefaultIcon()
    {
        try
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-dot-16.png.base64");
            if (!File.Exists(assetPath))
            {
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
            return null;
        }
    }
}
