using Avalonia;
using Avalonia.Controls;

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
            IsVisible = true
        };

        _window.Closing += OnWindowClosing;
        UpdateMenu();
    }

    public void Dispose()
    {
        _window.Closing -= OnWindowClosing;
        _trayIcon.IsVisible = false;
    }

    public void Refresh()
    {
        UpdateMenu();
    }

    public void AllowWindowClose()
    {
        _allowWindowClose = true;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose)
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

    private void UpdateMenu()
    {
        _pauseResumeItem.Header = _options.IsPaused() ? "恢复" : "暂停";
    }
}
