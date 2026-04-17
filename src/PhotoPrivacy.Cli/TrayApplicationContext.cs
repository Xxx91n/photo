using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Cli;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IHost _host;
    private readonly IRuntimeControl _runtimeControl;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly SynchronizationContext _uiContext;

    public TrayApplicationContext(IHost host, IRuntimeControl runtimeControl)
    {
        _host = host;
        _runtimeControl = runtimeControl;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _pauseResumeItem = new ToolStripMenuItem("暂停", null, (_, _) => TogglePauseResume());
        var exitItem = new ToolStripMenuItem("退出", null, async (_, _) => await ExitAsync());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "PhotoPrivacy Cleaner",
            ContextMenuStrip = menu
        };

        var lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopped.Register(OnHostApplicationStopped);

        UpdateMenuText();
        _ = _host.StartAsync();
    }

    private void OnHostApplicationStopped()
    {
        _uiContext.Post(_ =>
        {
            if (_notifyIcon.Visible)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            ExitThread();
        }, null);
    }

    private void TogglePauseResume()
    {
        if (_runtimeControl.IsPaused)
        {
            _runtimeControl.Resume();
        }
        else
        {
            _runtimeControl.Pause();
        }

        UpdateMenuText();
    }

    private void UpdateMenuText()
    {
        _pauseResumeItem.Text = _runtimeControl.IsPaused ? "继续" : "暂停";
    }

    private async Task ExitAsync()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        await _host.StopAsync();
        _host.Dispose();

        ExitThread();
    }
}
