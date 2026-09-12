using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Ui.Localization;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 票 31（架构恢复第七轮）：版本轮询与服务状态轮询宿主服务 — 与 ADR 0025 心跳
///（ConnectionStateService）同一心智模型的 IHostedService 承载。两个轮询循环原为
/// MainWindow.InitializeRuntime 内的 Task.Run 拉起 + OnClosed 手工取消/等待；现由
/// StartAsync/StopAsync 收口，MainWindow 只剩 Activate/Start/StopAsync 三行接线。
/// 行为语义逐字保持（循环体、周期、取消、Dispatcher 投递路径与原实现一致）。
/// 服务状态轮询的循环体本就住在 ServiceModeController.PollServiceModeTransitionAsync
///（issue 06），本类只接管其生命周期（拉起/取消/等待），编排零改动。
/// 装配破环：不直接依赖 ServiceModeController（其构造需要 MainWindow 视图适配器，
/// 经 MainWindow 构造注入会成 MS DI 死环），改由窗口构造完 Controller 后经
/// AttachServiceModePoll 委托挂载。
/// </summary>
public sealed class WindowPollingHostedService : IHostedService
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<Func<CancellationToken, Task<string>>>? _versionSourceFactory;
    private Func<CancellationToken, Task>? _serviceModePoll;
    private ExifToolVersionSnapshot? _versionSnapshot;
    private CancellationTokenSource? _versionPollCts;
    private Task? _versionPollTask;
    private CancellationTokenSource? _serviceModePollCts;
    private Task? _serviceModePollTask;

    /// <summary>
    /// 生产构造（组合根）。versionSourceFactory 惰性读取 App.RuntimeOptions.GetExifToolVersionAsync
    /// 的当前值 —— 与原 MainWindow.InitializeRuntime 在窗口就绪时才建 snapshot 的时序语义一致。
    /// </summary>
    public WindowPollingHostedService(
        MainWindowViewModel viewModel,
        Func<Func<CancellationToken, Task<string>>> versionSourceFactory)
    {
        _viewModel = viewModel;
        _versionSourceFactory = versionSourceFactory;
    }

    /// <summary>测试构造：直接注入版本读取委托，绕过 App.RuntimeOptions 静态。</summary>
    internal WindowPollingHostedService(
        MainWindowViewModel viewModel,
        Func<CancellationToken, Task<string>> versionSource)
    {
        _viewModel = viewModel;
        _versionSourceFactory = () => versionSource;
    }

    /// <summary>
    /// 服务模式轮询委托挂载（MainWindow 构造内调用，破 DI 环：见类注释）。
    /// </summary>
    public void AttachServiceModePoll(Func<CancellationToken, Task> poll)
    {
        _serviceModePoll = poll;
    }

    /// <summary>
    /// 由 MainWindow.InitializeRuntime 调用（窗口就绪后）：建立版本快照并拉起两个轮询。
    /// 与原实现的相对时序一致 —— 版本轮询在 AuditTail 启动后、服务模式轮询在窗口可见性策略后。
    /// </summary>
    public void Activate()
    {
        _versionSnapshot = new ExifToolVersionSnapshot(_versionSourceFactory?.Invoke());
        _versionPollCts = new CancellationTokenSource();
        _versionPollTask = Task.Run(() => PollVersionAsync(_versionPollCts.Token), _versionPollCts.Token);
        if (_serviceModePoll is not null)
        {
            _serviceModePollCts = new CancellationTokenSource();
            _serviceModePollTask = Task.Run(
                () => _serviceModePoll(_serviceModePollCts.Token),
                _serviceModePollCts.Token);
        }
    }

    /// <summary>原 MainWindow.ApplyExifToolVersionFromIpcAsync 迁入：一次性 IPC 版本读取 + UI 投递。</summary>
    public async Task ApplyExifToolVersionFromIpcAsync(Func<CancellationToken, Task<string>> getExifToolVersionAsync)
    {
        try
        {
            var version = await getExifToolVersionAsync(CancellationToken.None).ConfigureAwait(false);
            var text = ServiceModeController.NormalizeExifToolStatus(version);
            Dispatcher.UIThread.Post(() => _viewModel.ExifToolVersion = text);
        }
        catch
        {
            Dispatcher.UIThread.Post(
                () => _viewModel.ExifToolVersion = LocalizationService.Instance.Get("status.exiftool_not_found"));
        }
    }

    // 原 MainWindow.PollVersionAsync 迁入（票 31）：1s 周期 + TryReadChangedAsync 变化检测 +
    // Dispatcher.UIThread.Post 投递，循环体逐字保持（仅 DataContext as 换为构造注入的 _viewModel）。
    private async Task PollVersionAsync(CancellationToken token)
    {
        if (_versionSnapshot is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            var changed = await _versionSnapshot.TryReadChangedAsync(token).ConfigureAwait(false);
            if (changed is null)
            {
                continue;
            }

            Dispatcher.UIThread.Post(() =>
            {
                _viewModel.ExifToolVersion = ServiceModeController.NormalizeExifToolStatus(changed);
            });
        }
    }

    // === IHostedService 生命周期 ===

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 轮询依赖窗口就绪（版本源委托 + Controller Attach），InitializeRuntime 显式 Activate；
        // 此处不拉起，避免窗口构造前空转（与原 Task.Run 拉起时序一致）。
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 原 MainWindow.OnClosed 轮询释放段迁入：先版本后服务模式（顺序保持），
        // CancelAsync → Dispose → null，任务 await 容忍 OperationCanceledException。
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

        if (_serviceModePollCts is not null)
        {
            await _serviceModePollCts.CancelAsync();
            _serviceModePollCts.Dispose();
            _serviceModePollCts = null;
        }

        if (_serviceModePollTask is not null)
        {
            try
            {
                await _serviceModePollTask;
            }
            catch (OperationCanceledException)
            {
                // expected on close
            }

            _serviceModePollTask = null;
        }
    }
}
