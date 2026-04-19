using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Views;

public partial class MainWindow : Window
{
    private AuditTailService? _auditTail;
    private TrayHost? _trayHost;
    private ExifToolVersionSnapshot? _versionSnapshot;
    private CancellationTokenSource? _versionPollCts;
    private Task? _versionPollTask;
    private BackgroundUiOptions? _options;
    private readonly ServiceManager _serviceManager = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    public void InitializeRuntime(BackgroundUiOptions options)
    {
        _options = options;
        var effectiveConfig = LoadConfigOrDefault(options.ConfigPath);
        var hotFolder = effectiveConfig?.Watch.HotFolder;
        var exifToolPathFromConfig = effectiveConfig?.ExifTool.Path;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMode = "background";
            _versionSnapshot = new ExifToolVersionSnapshot(options.GetExifToolVersion);
            vm.ExifToolVersion = NormalizeExifToolStatus(_versionSnapshot.ReadInitial());
            vm.ShowDetailedEvents = false;
            vm.RuntimeStatus = options.IsPaused() ? "已暂停" : "运行中";
            vm.ShowServiceManagerTab = OperatingSystem.IsWindows();
            vm.ServiceStatus = _serviceManager.GetStatusText();
        }

        ServiceManagerTab.IsVisible = OperatingSystem.IsWindows();

        PauseResumeButton.Click += OnPauseResumeClick;
        ClearLogsButton.Click += OnClearLogsClick;
        OpenConfigDirButton.Click += OnOpenConfigDirClick;
        RefreshServiceStatusButton.Click += OnRefreshServiceStatusClick;

        InstallServiceButton.Click += OnInstallServiceClick;
        UninstallServiceButton.Click += OnUninstallServiceClick;
        StartServiceButton.Click += OnStartServiceClick;
        StopServiceButton.Click += OnStopServiceClick;

        _auditTail = new AuditTailService(
            hotFolder: hotFolder ?? Path.GetDirectoryName(options.AuditDirectory) ?? AppContext.BaseDirectory,
            onEntry: entry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext is MainWindowViewModel innerVm)
                    {
                        innerVm.AppendLog(entry);
                    }
                });
            },
            includeDetailedEvents: () => (DataContext as MainWindowViewModel)?.ShowDetailedEvents ?? false,
            onExifToolExePathDetected: exePath => _ = ResolveExifToolVersionAsync(exePath ?? exifToolPathFromConfig));

        _auditTail.Start();
        _ = ResolveExifToolVersionAsync(exifToolPathFromConfig);

        _versionPollCts = new CancellationTokenSource();
        _versionPollTask = Task.Run(() => PollVersionAsync(_versionPollCts.Token), _versionPollCts.Token);

        _trayHost = new TrayHost(this, options, ExitApplicationAsync);
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

    protected override async void OnClosed(EventArgs e)
    {
        if (_versionPollCts is not null)
        {
            await _versionPollCts.CancelAsync();
            _versionPollCts.Dispose();
            _versionPollCts = null;
        }

        if (_versionPollTask is not null)
        {
            try
            {
                await _versionPollTask;
            }
            catch (OperationCanceledException)
            {
                // expected when window closes
            }

            _versionPollTask = null;
        }

        if (_auditTail is not null)
        {
            await _auditTail.StopAsync();
        }

        _trayHost?.Dispose();
        base.OnClosed(e);
    }

    private void OnPauseResumeClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null)
        {
            return;
        }

        if (_options.IsPaused())
        {
            _options.Resume();
        }
        else
        {
            _options.Pause();
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.RuntimeStatus = _options.IsPaused() ? "已暂停" : "运行中";
        }

        PauseResumeButton.Content = _options.IsPaused() ? "恢复" : "暂停";
        _trayHost?.Refresh();
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearLogs();
        }
    }

    private void OnOpenConfigDirClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null)
        {
            return;
        }

        var path = Path.GetDirectoryName(_options.ConfigPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OpenDirectory(path);
    }

    private void OnRefreshServiceStatusClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ServiceStatus = _serviceManager.GetStatusText();
        }
    }

    private void OnInstallServiceClick(object? sender, RoutedEventArgs e)
    {
        if (_options is null || string.IsNullOrWhiteSpace(_options.SelfExecutablePath))
        {
            return;
        }

        var result = _serviceManager.Install(_options.SelfExecutablePath, _options.ConfigPath);
        ApplyServiceResult(result);
    }

    private void OnUninstallServiceClick(object? sender, RoutedEventArgs e)
    {
        var result = _serviceManager.Uninstall();
        ApplyServiceResult(result);
    }

    private void OnStartServiceClick(object? sender, RoutedEventArgs e)
    {
        var result = _serviceManager.Start();
        ApplyServiceResult(result);
    }

    private void OnStopServiceClick(object? sender, RoutedEventArgs e)
    {
        var result = _serviceManager.Stop();
        ApplyServiceResult(result);
    }

    private void ApplyServiceResult(ServiceCommandResult result)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var status = _serviceManager.GetStatusText();
        vm.ServiceStatus = result.Status switch
        {
            ServiceCommandStatus.Success => $"{status} | {result.Message}",
            ServiceCommandStatus.Skipped => $"{status} | 跳过：{result.Message}",
            ServiceCommandStatus.ElevationCancelled => $"{status} | 已取消：{result.Message}",
            ServiceCommandStatus.Failed => $"{status} | 失败：{result.Message}",
            _ => status
        };
    }

    private async Task ExitApplicationAsync()
    {
        if (_options is not null)
        {
            await _options.ExitAsync();
        }

        _trayHost?.AllowWindowClose();
        Close();
    }

    private async Task PollVersionAsync(CancellationToken token)
    {
        if (_versionSnapshot is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            var changed = _versionSnapshot.TryReadChanged();
            if (changed is null)
            {
                continue;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = NormalizeExifToolStatus(changed);
                }
            });
        }
    }

    private static string NormalizeExifToolStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "未找到 ExifTool";
        }

        var text = raw.Trim();
        return text.StartsWith("ExifTool", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"ExifTool v{text} ✓";
    }

    private async Task ResolveExifToolVersionAsync(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = "未找到 ExifTool";
                }
            });
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-ver",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("failed to start exiftool");
            }

            var version = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var text = string.IsNullOrWhiteSpace(version)
                ? "未找到 ExifTool"
                : $"ExifTool v{version.Trim()} ✓";

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = text;
                }
            });
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ExifToolVersion = "未找到 ExifTool";
                }
            });
        }
    }

    private static void OpenDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = path,
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = path,
                UseShellExecute = true
            });
        }
    }
}
