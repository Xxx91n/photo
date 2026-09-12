using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 票 27：ConfigFileWatcher — 监听 config.json 磁盘变更（手改/外部工具/备份回滚），
/// 防抖 500ms 后回调 <see cref="onReload"/>，让 UI 把当前内存 VM 替换为磁盘最新值，
/// 消除"权威配置到底有没有生效"的疑虑.
///
/// 设计要点:
/// - 只用 FSW 事件(Changed/Created/Renamed)，不轮询，与 ADR 0037 + 票 24 文件监听纪律一致.
/// - 防抖窗口 500ms(与 ConfigEditor 自动应用同窗口)，防止编辑器多次写入触发风暴.
/// - 提供 <see cref="SuppressNextReload"/> 接口：UI 自己 SaveConfig 后写入磁盘会触发 FSW，
///   此时调用方主动抑制下一次 reload，避免 UI → 磁盘 → UI 死循环覆盖用户未确认改动.
/// - 抑制标志在防抖 *触发时* 消费（而非事件到达时），让单次写盘触发的多次 Changed 事件
///   在同一防抖窗口内被合并并整体抑制，避免 FSW 多事件反弹.
/// - 目录不存在时 Start() 安全降级（Watcher 不会启动）.
/// </summary>
public sealed class ConfigFileWatcher : IDisposable
{
    private readonly string _configPath;
    private readonly string _watchDirectory;
    private readonly string _fileName;
    private readonly Action _onReload;
    private readonly ILogger _logger;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _fsw;
    private CancellationTokenSource? _debounceCts;
    private Task? _debounceTask;
    private bool _suppressNextReload;

    public ConfigFileWatcher(
        string configPath,
        Action onReload,
        TimeSpan? debounce = null,
        ILogger? logger = null)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _onReload = onReload ?? throw new ArgumentNullException(nameof(onReload));
        _watchDirectory = Path.GetDirectoryName(configPath) ?? string.Empty;
        _fileName = Path.GetFileName(configPath);
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        _logger = logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(_watchDirectory))
        {
            _logger.LogWarning("ConfigFileWatcher: config path has no directory, skipping watch ({Path})", _configPath);
            return;
        }

        try
        {
            Directory.CreateDirectory(_watchDirectory);
            _fsw = new FileSystemWatcher(_watchDirectory, _fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };
            _fsw.Changed += OnFileSystemEvent;
            _fsw.Created += OnFileSystemEvent;
            _fsw.Renamed += OnFileSystemEvent;
            _fsw.Error += OnWatcherError;
            _logger.LogDebug("ConfigFileWatcher started for {Path} (debounce={DebounceMs}ms)", _configPath, _debounce.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigFileWatcher failed to start for {Path}", _configPath);
        }
    }

    /// <summary>
    /// 票 27 抑制 UI 自身保存后的 FSW 反弹：调用方在写盘前调用一次，
    /// 下一次防抖窗口内的所有 reload 会被丢弃（合并抑制），避免 UI → 磁盘 → UI 死循环.
    /// </summary>
    public void SuppressNextReload()
    {
        lock (_gate)
        {
            _suppressNextReload = true;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        CancellationToken token;
        lock (_gate)
        {
            // 重置防抖窗口：每次新事件都取消旧的延迟任务、起一个新的计时窗口.
            // suppress 标志不立刻消费——等防抖窗口到期再统一判断，避免单次写盘
            // 触发的多次 Changed 事件被分到两个窗口、第二次反弹 reload.
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _debounceTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, token).ConfigureAwait(false);

                bool suppress;
                lock (_gate)
                {
                    suppress = _suppressNextReload;
                    _suppressNextReload = false;
                }

                if (suppress)
                {
                    _logger.LogDebug("ConfigFileWatcher suppressed self-write reload for {Path}", _configPath);
                    return;
                }

                _onReload();
            }
            catch (TaskCanceledException)
            {
                // debounce reset — ignore
            }
        }, token);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "ConfigFileWatcher FSW error");
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }
        }
        catch
        {
            // ignore shutdown races
        }

        if (_fsw is not null)
        {
            try
            {
                _fsw.EnableRaisingEvents = false;
                _fsw.Dispose();
            }
            catch
            {
                // ignore
            }
            _fsw = null;
        }
    }
}
