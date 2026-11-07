using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Queue;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Rules;
using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Worker;

public sealed class MetadataCleanerWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetadataCleanerWorker> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IRuntimeControl _runtimeControl;

    private JsonLineAuditLogger? _audit;
    private IExifToolBridge? _bridge;
    private FileTaskPipeline? _pipeline;
    private FileProcessedRecordStore? _processedRecordStore;
    private DebounceQueue? _debounceQueue;
    private RecentFingerprintCache? _recentFingerprintCache;
    private FswFolderWatcher? _watcher;
    private int _maxParallelDrain = 1;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly SemaphoreSlim _standbySignal = new(0, 1);
    // 停机信号（#117）：待机等待必须能被 StopAsync 显式打断。仅 Dispose 不会让挂起的
    // WaitAsync 完成（.NET 10 的 SemaphoreSlim.Dispose 只清空等待队列、不完成等待者），
    // 待机分支用它与宿主 stoppingToken 链接的令牌等待，StopAsync 先 Cancel 再收口。
    private readonly CancellationTokenSource _standbyStopCts = new();

    /// <summary>
    /// 停机-重载生命周期竞态（#126）的总开关：StopAsyncCore 第一句置位，此后重载一律拒绝——
    /// 不再创建任何运行时（bridge/watcher/store/audit/timer），也不再触碰共享实例。
    /// volatile：重载路径在多个检查点读它，停机线程写它；检查点之后到发布字段为止保持纯同步。
    /// </summary>
    // Design(#82): 停机开始后重载不再创建运行时，按契约回 reload_failed（#126，D7 协议不变）；
    // 本标记挂在总开关上——该决策由重载路径的四处检查点共同执行。
    private volatile bool _stopping;

    /// <summary>
    /// 重载在停机开始后的可预期终点（#126）：停机取消它，让还卡在 <c>_reloadGate.WaitAsync</c>
    /// 上的重载立刻收敛（回 <c>reload_failed</c>），而不是吊到进程退出。不 Dispose 本源——
    /// Cancel 之后不再有新等待者（<see cref="_stopping"/> 已挡住入口），Dispose 抢跑反而会把
    /// 已注册的取消回调变成悬空（同 .NET 10 信号量 Dispose 语义）。
    /// </summary>
    private readonly CancellationTokenSource _reloadStopCts = new();

    /// <summary>
    /// 串行化「drain 一轮」与「拆卸重建」（#85）：重载在拆卸 store/audit 前必须先等当前一轮 drain 结束，
    /// 否则在途 <c>HandleAsync</c> 会用到已释放实例；同时 <c>PopReady</c> 已弹出的路径也不会因
    /// <c>_pipeline</c> 被置空而丢失（不重入队）。
    /// </summary>
    private readonly SemaphoreSlim _drainGate = new(1, 1);
    // CodeRabbit #437：StopAsync 幂等串行化。host 关闭会多次调用 StopAsync（见 StopAsync 注释），
    // 用锁 + 共享完成任务保证第二次调用只等待、不重跑 Cancel/Dispose 等清理步骤。
    private readonly object _stopLock = new();
    private Task? _stopTask;

    private Timer? _cleanupTimer;
    private AppConfig? _currentConfig;

    /// <summary>
    /// 当前在跑的运行时（watcher/队列）归属哪个监控目录。拆卸期间旧 watcher 与轮询兜底仍可能回调，
    /// 入队一律按这个目录收口，避免用户已经放弃的目录里的文件被新 pipeline 清除。
    /// </summary>
    private string _activeWatchFolder = string.Empty;

    /// <summary>
    /// 上一个「完整建成」的监控目录。重建中途抛异常时 <see cref="_activeWatchFolder"/> 已经切到新目录
    /// 而这里仍是旧目录，下一次重载同一个目录因此会重跑首扫，不至于把没建完的目录永远漏扫。
    /// </summary>
    private string _appliedWatchFolder = string.Empty;

    private IReadOnlyList<string> _autoExcludedSubdirectories = Array.Empty<string>();

    public string CurrentExifToolVersion => _bridge?.VersionText ?? ExifToolVersionText.Unknown;
    public string CurrentWatchDirectory => _currentConfig?.Watch.HotFolder ?? string.Empty;

    /// <summary>
    /// 测试缝：在「拆卸窗口」内执行一段代码（旧 watcher 已停、新 watcher 未起）。
    ///
    /// 这段窗口真实耗时只有毫秒级，靠时间投放文件撞不中；handoff 兜底要可重复地验，
    /// 必须有个确定的落点（#114）。两种用法：往窗口里投放文件，或把窗口撑长。
    /// 生产路径不赋值，为 null 时不产生任何行为。重载失败回滚（#112）会再走一遍建运行时，
    /// 因此钩子可能被触发两次；回滚那趟恒全量补扫，钩里投放的文件不经差集判定。
    /// </summary>
    // Design(#82): 拆卸窗口的起止由注入钩子划定（生产不赋值），测试不得靠 sleep 猜窗口边界（#114）。
    public Func<Task>? TeardownWindowHookAsync { get; set; }

    /// <summary>
    /// 驱动 ExifTool 版本解析（#86）。
    ///
    /// 桥自 M4 起惰性启动（首个文件才拉进程），于是首个文件到达前 <see cref="CurrentExifToolVersion"/>
    /// 恒为 <see cref="ExifToolVersionText.Unknown"/>——即使 ExifTool 就在配置路径上且完全可用。
    /// IPC handler 在状态快照仍为 unknown 时调用本方法：未启动则冷启动（启动流程自带版本探针），
    /// 已启动但那次探针超时则补探。失败（路径失效、进程起不来、探针超时）原样上抛，由调用方记录降级。
    /// </summary>
    // Design(#82): 版本解析按需触发（IPC 状态请求驱动），不引入启动期 eager start，也不进每文件热路径。
    public async Task EnsureExifToolVersionAsync(CancellationToken cancellationToken)
    {
        var bridge = _bridge;
        if (bridge is null)
        {
            return;
        }

        // 停机期间不得再拉起/重启桥：StopAsync 之后新起的进程无人回收（子进程泄漏）。
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _applicationLifetime.ApplicationStopping);

        await bridge.EnsureVersionAsync(linked.Token);
    }

    public MetadataCleanerWorker(
        IConfiguration configuration,
        ILogger<MetadataCleanerWorker> logger,
        IHostApplicationLifetime applicationLifetime,
        IRuntimeControl runtimeControl)
    {
        _configuration = configuration;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _runtimeControl = runtimeControl;
    }

    /// <summary>
    /// Worker 主流程：加载并校验配置 → 空监控目录进待机（等重载配置）→ 建运行时（目录、审计、桥）
    /// → 先起 watcher 再首扫（once 模式只「枚举→drain→退出」，不起 watcher）→ 进入处理循环。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AppConfig config;
        try
        {
            config = LoadEffectiveConfig();
            AppConfigValidator.Validate(config);
        }
        catch (AppConfigValidationException ex)
        {
           _logger.LogError(ex, "Configuration validation failed: {Message}", ex.Message);
           Environment.ExitCode = 1;
           _applicationLifetime.StopApplication();
           Environment.Exit(1);
           return;
        }

        // 校验通过即登记当前配置：GetStatus 的 watchDirectory 由此立刻反映真实目录，
        // 不再在建目录/首次扫描期间把已配置好的 Worker 误报成「监控目录待配置」。
        _currentConfig = config;
        _activeWatchFolder = config.Watch.HotFolder;

        var printEffectiveConfig = ParseBool(GetValue("print-effective-config", "print_effective_config"));
        if (printEffectiveConfig)
        {
            var json = AppConfigJson.ToIndentedJson(config);
            Console.WriteLine(json);
            _applicationLifetime.StopApplication();
            return;
        }

        CleanupStaleTempFiles();
        var once = ParseBool(GetValue("once"));
        if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            if (once)
            {
                _logger.LogError("The --once mode requires a configured watch folder");
                Environment.ExitCode = 1;
                _applicationLifetime.StopApplication();
                return;
            }

            _logger.LogWarning("Worker is ready but no watch folder is configured; waiting for configuration reload");
            // 待机等待必须能被停机显式打断（#117）：仅 Dispose 不会让挂起的 WaitAsync 完成——
            // .NET 10 的 SemaphoreSlim.Dispose 只清空等待队列、不完成等待者，Dispose + catch 兜不住
            // 首次配置待机这条路径。把宿主 stoppingToken 与本 Worker 停机信号链接起来，两条停机路径
            // （宿主停止 / StopAsync）都收敛到下面的 OperationCanceledException 分支。
            using var standbyCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _standbyStopCts.Token);
            try
            {
                while (string.IsNullOrWhiteSpace(_currentConfig?.Watch.HotFolder))
                {
                    await _standbySignal.WaitAsync(standbyCts.Token);
                }
            }
            catch (OperationCanceledException) when (standbyCts.IsCancellationRequested)
            {
                // Normal shutdown while the worker is waiting for first-run configuration.
                return;
            }
            catch (ObjectDisposedException)
            {
                // 防御兜底：若信号在链接令牌被观察前已被 Dispose，重入等待会同步抛
                // ObjectDisposedException。同样是停机，不得落入处理循环。
                return;
            }

            await RunProcessingLoopAsync(stoppingToken);
            return;
        }

        // 待机分支已在上面返回：只有真正启用监控才创建目录（#124）。
        EnsureConfiguredDirectories(config);

        _audit = new JsonLineAuditLogger(config.Audit.LogDirectory, config.Audit.RetainDays, config.Audit.DiagnosticMode, AuditLevelParser.Parse(config.Audit.LogLevel));
        IAuditLogger auditLogger = _audit is not null ? _audit : new NoopAuditLogger();
        // 启动路径的软告警落点（排除清单不可匹配/未支持模式）。
        await EmitConfigWarningsAsync(config, stoppingToken);
        _bridge = CreateBridge(config, auditLogger);
        _processedRecordStore = new FileProcessedRecordStore(config.Audit.LogDirectory);
        _pipeline = new FileTaskPipeline(config, new RuleEngine(config), _bridge, new LocalFileOperations(), auditLogger, _processedRecordStore);
        _maxParallelDrain = Math.Max(1, Math.Min(config.ExifTool.MaxParallelDrain, config.ExifTool.StayOpenPoolSize));

        _cleanupTimer = new Timer(_ => PerformPeriodicCleanup(config), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        _debounceQueue = new DebounceQueue(TimeSpan.FromMilliseconds(config.Watch.DebounceMs), () => DateTimeOffset.UtcNow);
        _recentFingerprintCache = new RecentFingerprintCache(() => DateTimeOffset.UtcNow);

        if (config.ExifTool.DryRun)
        {
            if (_audit is not null)
            {
                await _audit.WriteAsync(
                    new AuditEvent(
                        EventType: "exiftool_started",
                        Level: AuditLevel.Info,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        TaskId: Guid.NewGuid().ToString("N"),
                        SourcePath: config.ExifTool.Path,
                        Message: "dry-run bridge started",
                        Data: null),
                    stoppingToken);
            }
        }

        _watcher = new FswFolderWatcher(
            config,
            new DirectoryRecoveryScanner(config),
            auditLogger,
            path =>
            {
                EnqueueIfNeeded(path);
                return Task.CompletedTask;
            });

        var autoExcluded = _watcher.GetAutoExcludedSubdirectories();
        _autoExcludedSubdirectories = autoExcluded;
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    EventType: "service_started",
                    Level: AuditLevel.Info,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: config.Watch.HotFolder,
                    Message: $"mode={(config.ExifTool.DryRun ? "dry-run" : "live")}",
                    Data: BuildServiceStartedData(autoExcluded)),
                stoppingToken);
        }

        // 先起 watcher 再扫描（与重载首扫同一时序）：扫描期间新建的文件若没被 FSW 盯住，
        // 会落进轮询兜底的初始快照，从此永不上报。once 模式没有监控运行时，保持「枚举→清理→退出」的既有时序。
        // Design(#82): 启动首扫之前必须先起 watcher，与重载首扫同一时序（once 模式无监控运行时，除外；#109 修复后定稿，残余见 #82 域内）。
        if (!once)
        {
            _watcher.Start();
        }

        _appliedWatchFolder = config.Watch.HotFolder;

        if (!string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            foreach (var file in WatchFolderEnumeration.EnumerateFiles(config.Watch.HotFolder, config.Watch.IncludeSubdirectories))
            {
                EnqueueIfNeeded(file);
            }
        }

        if (once)
        {
            await DrainOnceAsync(stoppingToken);
            _applicationLifetime.StopApplication();
            return;
        }

        // Log inotify limits on Linux (non-blocking diagnostic)
        var inotifyDiag = InotifyMonitor.CheckAndWarn(_logger);
        if (inotifyDiag is not null && _audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    EventType: "inotify_limits",
                    Level: AuditLevel.Info,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: config.Watch.HotFolder,
                    Message: inotifyDiag,
                    Data: null),
                stoppingToken);
        }

        await RunProcessingLoopAsync(stoppingToken);
    }

    private async Task RunProcessingLoopAsync(CancellationToken stoppingToken)
    {
        // Main loop with exponential backoff on transient failures.
        // Unrecoverable exceptions (config, bridge start) still propagate and stop the host.
        const int maxConsecutiveFailures = 10;
        var consecutiveFailures = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_runtimeControl.IsPaused)
                    {
                        await Task.Delay(200, stoppingToken);
                        continue;
                    }

                    // 一轮 drain 与重载拆卸互斥（#85）：拆卸必须等在途处理结束，弹队的路径也不会丢。
                    await _drainGate.WaitAsync(stoppingToken);
                    try
                    {
                        await DrainReadyItemsAsync(stoppingToken);
                    }
                    finally
                    {
                        _drainGate.Release();
                    }

                    await Task.Delay(200, stoppingToken);

                    // Reset backoff on successful iteration
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    _logger.LogError(ex,
                        "Transient error in worker loop ({Count}/{Max}): {Message}",
                        consecutiveFailures, maxConsecutiveFailures, ex.Message);

                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        _logger.LogCritical(
                            "Too many consecutive failures ({Count}), stopping host.",
                            consecutiveFailures);
                        _applicationLifetime.StopApplication();
                        Environment.Exit(1);
                        break;
                    }

                    // Exponential backoff: 1s, 2s, 4s, 8s, 16s, ... capped at 60s
                    var backoffMs = Math.Min(1000 * (1 << (consecutiveFailures - 1)), 60_000);
                    _logger.LogWarning("Backing off for {Ms}ms before retry.", backoffMs);
                    await Task.Delay(backoffMs, stoppingToken);
                }
            }
        }
        catch (TaskCanceledException)
        {
            // graceful shutdown
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // 幂等（CodeRabbit #437）：host 关闭会在 ShutdownCoordinator 的 app.StopAsync 与
        // RunAsync 路径（ApplicationStopping 后 host.StopAsync(None)）各调用一次 StopAsync；
        // 若两路都执行完整清理，第二次会撞已释放的 _standbyStopCts（Cancel/Dispose 抛
        // ObjectDisposedException）导致 host 判定关机失败。串行化：首次调用创建关闭任务，
        // 后续调用复用同一任务等待其完成。
        lock (_stopLock)
        {
            _stopTask ??= StopAsyncCore(cancellationToken);
            return _stopTask;
        }
    }

    private async Task StopAsyncCore(CancellationToken cancellationToken)
    {
        // #126：停机起点必须排在一切等待之前。置位后重载路径的各检查点（ReloadConfigAsync 入口/
        // 闸内、ApplyConfigAsync 拿 drain 闸后、BuildRuntimeAsync 建运行时前）都会拒绝继续——
        // 「停机清理之后再重建运行时」与「重载对已释放 gate 调用 Release」两个缺陷形态由此断根。
        // 取消 _reloadStopCts 让还卡在 _reloadGate 等待上的重载立刻收敛，不吊死后面的有界清理。
        _stopping = true;
        _reloadStopCts.Cancel();

        // #117：停机必须先显式打断待机等待。.NET 10 的 SemaphoreSlim.Dispose 只清空等待队列、
        // 不会让挂起的 WaitAsync 完成（不会向等待者抛 ObjectDisposedException），只靠 Dispose + catch
        // 兜不住「首次配置待机」这条路径；先 Cancel 让 ExecuteAsync 的待机分支立刻收敛，
        // 后续有界清理与 base.StopAsync 才能按预算完成。
        _standbyStopCts.Cancel();

        // IMPORTANT: the host-provided cancellationToken here is already cancelled
        // (or has a very short timeout) by the time StopAsync is called after
        // StopApplication(). Using it directly causes bridge.StopAsync and
        // audit.WriteAsync to be skipped immediately, leaving the process
        // hanging until the host forces a timeout. Use independent tokens instead.

        // 第一遍运行时拆卸：回收停机开始那一刻在册的实例。
        await StopRuntimeAsync();

        if (_audit is not null)
        {
            using var auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _audit.WriteAsync(
                    new AuditEvent(
                        EventType: "service_stopped",
                        Level: AuditLevel.Info,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        TaskId: Guid.NewGuid().ToString("N"),
                        SourcePath: string.Empty,
                        Message: "worker stopped",
                        Data: null),
                    auditCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Audit write timed out on shutdown, entry may be missing.");
            }
        }

        // 与 ApplyConfigAsync 同一口径（#85）：等当前一轮 drain 结束，共享实例（store/audit）只在
        // 持 drain 闸时释放——在途处理或重载重建还持闸时动手 Dispose 正是本票「清理与重建互踩」
        // 的原始形态；#126 起拿不到闸不再强拆，留给进程退出收尾。
        var drainIdle = await _drainGate.WaitAsync(TimeSpan.FromSeconds(4));

        // #126：第二遍运行时拆卸，无论是否拿到 drain 闸都执行——重载的同步发布段（含首扫枚举，
        // 大目录可拖秒级）可能吃满 4 s 预算，拿不到闸就跳过会把已发布的 bridge/watcher/timer 留到
        // 进程退出。无闸回收是安全的：watcher/timer/bridge 不被在途 drain 与重载发布段再引用
        // （发布段用局部变量建、末尾发布），与入口第一遍同语义（停桥本就发生在闸外）。
        await StopRuntimeAsync();

        if (drainIdle)
        {
            try
            {
                _audit?.Dispose();
                _audit = null;
                _pipeline = null;
                DisposeQuietly(_processedRecordStore);
                _processedRecordStore = null;
            }
            finally
            {
                _drainGate.Release();
            }
        }
        else
        {
            _logger.LogWarning(
                "Shutdown did not acquire the drain gate within 4 s; shared instances are left to process exit (#126).");
        }

        // #126：_reloadGate 不 Dispose。停机后仍可能有重载在它上面等待（_reloadStopCts 负责收敛）
        // 或持闸收尾（finally Release）——Dispose 只清空等待队列、不完成等待者，Release 会命中
        // ObjectDisposedException。信号量无非托管资源，不新进等待者已由 _stopping 保证，进程退出收尾。

        await base.StopAsync(cancellationToken);

        // CodeRabbit #357/#456：base.StopAsync 对 ExecuteTask 的等待受调用方 token 影响——token 过期会
        // 提前返回，ExecuteTask 可能尚未完成。这里补一次**有界且不抛**的收敛检查：既确保 ExecuteAsync
        // 的待机分支（观察到取消后）真正收敛，又不让被阻塞的启动清扫无限拖住停机，也不把 ExecuteTask
        // 的 fault 传播到停机路径（收敛状态已由 base.StopAsync 按既有语义处理）。
        // 预算内未收敛则跳过 Dispose（信号量不释放就不会有 .NET 10 的 Dispose 竞态），
        // 残留句柄由进程退出收尾。
        var converged = ExecuteTask is null;
        if (ExecuteTask is not null)
        {
            converged = await Task.WhenAny(ExecuteTask, Task.Delay(TimeSpan.FromSeconds(4))) == ExecuteTask;
        }

        if (converged)
        {
            // 待机等待必须在 ExecuteTask 收敛后才 Dispose：#117 修复（先 Cancel）与 .NET 10 的
            // SemaphoreSlim.Dispose（只清等待队列、不完成等待者）之间存在竞态——若 Dispose 抢在
            // 取消回调把等待者移出队列之前执行，等待者会被永久挂起。先让 ExecuteTask 收敛再释放。
            _standbySignal.Dispose();
            _standbyStopCts.Dispose();
        }
        else
        {
            _logger.LogWarning(
                "ExecuteTask did not converge within 4 s on shutdown; skipping signal disposal to avoid the Dispose race (#117/#357/#456).");
        }
    }

    /// <summary>
    /// 停机路径的运行时拆卸（timer/watcher/bridge），停机会调用两遍（#126）：
    /// 第一遍在 <see cref="StopAsyncCore"/> 入口，回收停机开始时刻在册的实例；
    /// 第二遍在 drain 闸等待之后，回收重载在停机开始后、到停机收尾为止这段时间重建并发布的
    /// 实例（「清理后不重建」的镜像面：重建了也要能被收回来）。重载被 <c>_stopping</c> 拒绝后
    /// 不会重建，第二遍对已收走的字段是空操作。
    /// </summary>
    private async Task StopRuntimeAsync()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        _watcher?.Stop();
        _watcher = null;

        // 先摘引用再做有界停止：第二遍拆卸因此不会重复停同一个实例；若重载恰在发布段重建了
        // 新实例，这里摘引用前读到的仍是当时的最新值。
        var bridge = _bridge;
        if (bridge is null)
        {
            return;
        }

        _bridge = null;
        await StopBridgeAsync(bridge);
    }

    /// <summary>
    /// 有界停桥并释放：重载拆卸与停机拆卸共用同一形态（4 s CTS → StopAsync → Dispose），
    /// 拆卸失败只记日志不阻断调用方（停机继续收尾、重载按失败结果呈现）。
    /// </summary>
    private async Task StopBridgeAsync(IExifToolBridge bridge)
    {
        using var bridgeCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await bridge.StopAsync(bridgeCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Bridge stop timed out after 4 s, continuing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bridge stop threw unexpectedly, continuing.");
        }

        (bridge as IDisposable)?.Dispose();
    }

    /// <summary>
    /// 释放旧实例的常驻句柄（#85）：失败静默吞掉，不阻断重载/停机路径——
    /// 释放失败的真实后果由调用方（重载结果）呈现。
    /// </summary>
    private static void DisposeQuietly(IDisposable? disposable)
    {
        if (disposable is not null)
        {
            SafeLog.Try(disposable.Dispose);
        }
    }

    /// <summary>
    /// Scan %TEMP% for stale PP_ prefixed temp files left by prior crashes.
    /// FileTaskPipeline creates %TEMP%/PP_*.ext temp copies; if a crash interrupts the
    /// temp-route, these linger. Safe to delete: they are only transient copies.
    /// ponytail: best-effort, ignore errors (file may be locked by another active session).
    /// </summary>
    private static void CleanupStaleTempFiles()
    {
        try
        {
            var tempDir = Path.GetTempPath();
            foreach (var file in Directory.EnumerateFiles(tempDir, "PP_*", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    private async void PerformPeriodicCleanup(AppConfig config)
    {
        try
        {
            _audit?.CleanupExpired();
        }
        catch
        {
            // best effort
        }

        try
        {
            // Bound fingerprint cache. Periodic cleanup of stale entries
            // plus eviction when capacity exceeded. Prevents unbounded growth on hot folders.
            _recentFingerprintCache?.CleanupExpired(TimeSpan.FromMinutes(10));
        }
        catch
        {
            // best effort
        }

        try
        {
            var backupDir = string.IsNullOrWhiteSpace(config.Backup.Directory)
                ? Pipeline.BackupPathResolver.ResolveDefaultBackupDir(config.Watch.HotFolder)
                : config.Backup.Directory;
            await BackupRetentionService.EnforceMaxSizeAsync(
                backupDir, config.Backup.MaxSizeMb * 1024L * 1024L, config.Backup.RetainDays,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    // Design(#82): 配置加载纯读——LoadEffectiveConfig 不在返回前创建 hot/audit/quarantine；目录只在运行时真正启用监控时创建。
    private AppConfig LoadEffectiveConfig()
    {
        var configPath = GetValue("config")
            ?? Path.Combine(AppContext.BaseDirectory, "config", "config.json");

        AppConfig config;
        if (File.Exists(configPath))
        {
            config = AppConfigLoader.Load(configPath);
        }
        else
        {
            config = AppConfig.Default;
        }

        var hotFolder = GetValue("hot-folder", "hot_folder");
        var auditFolder = GetValue("audit-folder", "audit_folder");
        var dryRunArg = GetValue("dry-run", "dry_run");
        var stayOpenPoolSizeArg = GetValue("stay-open-pool-size", "stay_open_pool_size");
        var maxParallelDrainArg = GetValue("max-parallel-drain", "max_parallel_drain");

        if (!string.IsNullOrWhiteSpace(hotFolder))
        {
            config = config with { Watch = config.Watch with { HotFolder = hotFolder } };
        }

        if (!string.IsNullOrWhiteSpace(auditFolder))
        {
            config = config with { Audit = config.Audit with { LogDirectory = auditFolder } };
        }

        if (!string.IsNullOrWhiteSpace(dryRunArg))
        {
            config = config with { ExifTool = config.ExifTool with { DryRun = ParseBool(dryRunArg) } };
        }

        if (!string.IsNullOrWhiteSpace(stayOpenPoolSizeArg))
        {
            config = config with
            {
                ExifTool = config.ExifTool with
                {
                    StayOpenPoolSize = ParseInt(stayOpenPoolSizeArg, "stay_open_pool_size")
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(maxParallelDrainArg))
        {
            config = config with
            {
                ExifTool = config.ExifTool with
                {
                    MaxParallelDrain = ParseInt(maxParallelDrainArg, "max_parallel_drain")
                }
           };
       }

       return config;
    }

    /// <summary>
    /// 只在运行时真正启用监控时创建配置里的目录（#124）：待机态不创建这些目录。
    /// </summary>
    // Design(#82): 目录只在真正启用监控时创建；待机（含重载回待机）不创建。
    private static void EnsureConfiguredDirectories(AppConfig config)
    {
        // ponytail: empty config paths (config.sample.json defaults to "") would throw
        // ArgumentException on Directory.CreateDirectory — skip if blank.
        if (!string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            Directory.CreateDirectory(config.Watch.HotFolder);
        }
        if (!string.IsNullOrWhiteSpace(config.Audit.LogDirectory))
        {
            Directory.CreateDirectory(config.Audit.LogDirectory);
        }
        if (!string.IsNullOrWhiteSpace(config.Quarantine.Directory))
        {
            Directory.CreateDirectory(config.Quarantine.Directory);
        }
    }

    private IExifToolBridge CreateBridge(AppConfig config, IAuditLogger audit)
    {
        // 擦除引擎由 rules.json 驱动（单一真相源），与 UI 规则面板同一 FormatRulesStore 存储；
        // 规则快照在 bridge 构建时载入（初启与 ApplyConfig 重建时刷新），缺文件时 Load 返回默认规则。
        var rulesStore = new FormatRulesStore(Path.Combine(AppContext.BaseDirectory, "config"));
        return PooledExifToolBridgeFactory.BuildFromConfig(config, audit, rulesStore.Load());
    }

    /// <summary>
    /// 重载配置并重建运行期组件。
    ///
    /// 失败必须上抛：IPC 通道据此回 <c>ok:false</c> 带原因（<c>WorkerIpcRequestHandler</c>），
    /// SIGHUP 通道自带 catch（<c>Program.cs</c>）。禁止再在方法内吞掉异常——
    /// 旧形态把「重建失败、旧 watcher 已停」报成 <c>ok:true</c>，热重载从未真正生效（#85）。
    /// </summary>
    public async Task ReloadConfigAsync()
    {
        // #126：停机开始后重载一律拒绝——不建运行时、不碰任何 gate。IPC 侧由既有 catch 回
        // ok:false（reload_failed: ...），SIGHUP 侧由既有 catch 记日志；协议字段与状态位不变（D7）。
        if (_stopping)
        {
            throw new InvalidOperationException(StoppingReloadRejection);
        }

        // 等闸挂上停机令牌（#126）：停机会取消 _reloadStopCts，让还没拿到闸的重载立刻可预期地
        // 结束，而不是吊在 WaitAsync 上直到进程退出。本闸停机不 Dispose（见 StopAsyncCore），
        // 等待者不会撞 ObjectDisposedException。
        await _reloadGate.WaitAsync(_reloadStopCts.Token);
        try
        {
            // 拿到闸后复检（#126）：停机可能在等闸期间开始。拒绝路径未触碰任何运行时，finally 正常释放。
            if (_stopping)
            {
                throw new InvalidOperationException(StoppingReloadRejection);
            }

            var config = LoadEffectiveConfig();
            await ApplyConfigAsync(config, CancellationToken.None);
            if (!_stopping && _standbySignal.CurrentCount == 0)
            {
                _standbySignal.Release();
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    /// <summary>停机开始后的重载拒绝原因（#126）：经 IPC 映射为 <c>reload_failed: ...</c>，文案可定位到时序。</summary>
    private const string StoppingReloadRejection = "reload_rejected: worker 正在停机，本次重载未应用";

    /// <summary>
    /// Validate current config without applying. Returns (valid, errorMessage).
    /// </summary>
    public (bool valid, string? error) TryValidateConfig()
    {
        try
        {
            var config = LoadEffectiveConfig();
            AppConfigValidator.Validate(config);
            return (true, null);
        }
        catch (AppConfigValidationException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// （不变量①④）：把 validator 的排除清单告警落成可见信号——
    /// 结构化日志 + <c>config_warning</c> 审计事件（UI 审计视图对未知事件类型回退显示原事件名）。
    /// <see cref="AppConfigValidator.Validate"/> 负责 fail-closed 的硬拒，
    /// 本方法负责「不阻断但绝不静默」的软路径：不可匹配/未支持的模式必须留痕。
    /// </summary>
    private async Task EmitConfigWarningsAsync(AppConfig config, CancellationToken token)
    {
        foreach (var warning in AppConfigValidator.CollectWarnings(config))
        {
            _logger.LogWarning("配置告警：{Warning}", warning);

            if (_audit is null)
            {
                continue;
            }

            await _audit.WriteAsync(
                new AuditEvent(
                    EventType: "config_warning",
                    Level: AuditLevel.Warn,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: config.Watch.HotFolder,
                    Message: warning,
                    Data: null),
                token);
        }
    }

    /// <summary>
    /// 拆卸前快照没建成、本次重建退回整目录补扫这件事的留痕（不变量①④：降级不许只进日志）。
    /// 写失败只记日志，不反噬重建——重建半途而废的代价比少一条告警大得多（#85 口径）。
    /// </summary>
    private async Task EmitHandoffDegradedAsync(AppConfig config, string reason)
    {
        var audit = _audit;
        if (audit is null)
        {
            return;
        }

        try
        {
            // 与停机留痕同口径：2 s 有界等待，等不到就降级成只记日志。重载路径传进来的 token 恒为
            // CancellationToken.None（ReloadConfigAsync / SIGHUP 都不带停机令牌），这里不必再链一个。
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await audit.WriteAsync(
                new AuditEvent(
                    EventType: "config_warning",
                    Level: AuditLevel.Warn,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: config.Watch.HotFolder,
                    Message: $"重载拆卸前的目录快照未建成（{reason}），本次改走整目录补扫：已处理文件可能被重处理。",
                    Data: null),
                bounded.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拆卸快照降级的审计事件未写出。");
        }
    }

    private async Task ApplyConfigAsync(AppConfig config, CancellationToken token)
    {
        try
        {
            AppConfigValidator.Validate(config);
        }
        catch (AppConfigValidationException)
        {
            throw;
        }

        // reload 路径的软告警落点（写进当前生效的审计日志）。
        await EmitConfigWarningsAsync(config, token);

        // 拆卸与重建期间持 drain 闸（#85）：等在途一轮处理结束，避免在途 HandleAsync 用到已释放的
        // store/audit；也避免 PopReady 已弹出的路径因 _pipeline 被置空而丢失（该路径不重入队）。
        // 等待有界：超时即放弃本次重载（旧配置与组件保持原样），不带着在途处理强拆。
        if (!await _drainGate.WaitAsync(TimeSpan.FromSeconds(8)))
        {
            throw new InvalidOperationException("reload_busy: 处理中的文件未在 8 s 内结束，本次重载已取消（旧配置仍生效）");
        }

        try
        {
            // 拿到 drain 闸后复检停机（#126）：停机的共享实例清理同样持闸——此刻拿到闸，说明停机
            // 清理要么尚未开始（晚于本次重载，会等本次重建完成再收走），要么已结束（本重载必须
            // 在创建任何运行时之前放弃）。拒绝路径未动任何组件，旧配置原样生效。
            if (_stopping)
            {
                throw new InvalidOperationException(StoppingReloadRejection);
            }

            // 拿到 drain 闸后再建目录（#124）：reload_busy 放弃本次重载时不留下新配置的目录；
            // 创建失败仍在拆卸/回滚之前，旧运行时原样保留。
            if (!string.IsNullOrWhiteSpace(config.Watch.HotFolder))
            {
                EnsureConfiguredDirectories(config);
            }

            await RebuildRuntimeAsync(config, token);
        }
        finally
        {
            _drainGate.Release();
        }
    }

    /// <summary>
    /// 重建运行期组件；重建失败时用上一份已生效配置把旧运行时建回来（#112）。
    ///
    /// 拆卸（停 watcher、释放 store/audit）发生在重建之前，因此进入拆卸之后的任何抛错都会让
    /// worker 停在「进程活着但不再监控原目录」的形态。失败必须回滚：旧配置当时能建成，重建它
    /// 通常也能成功；回滚成功再把原异常上抛为 <c>reload_failed</c>（IPC 契约不变）。
    /// 不能改成「先建新运行时再切换」：新旧 store/audit 对同一 processed.ndjson 与审计文件各持
    /// 写句柄，旧实例未释放时新建必然自我冲突（#85 根因），事务化需要改存储共享模型，不在本票。
    /// </summary>
    // Design(#82): 重载重建失败必须回滚到上一份生效配置（#112），不得停在「活着但不再监控」形态。
    private async Task RebuildRuntimeAsync(AppConfig config, CancellationToken token)
    {
        var previousConfig = _currentConfig;
        if (previousConfig is null)
        {
            await BuildRuntimeAsync(config, rebuildDebounceState: false, token);
            return;
        }

        try
        {
            await BuildRuntimeAsync(config, rebuildDebounceState: false, token);
        }
        catch (Exception ex)
        {
            // #126：停机已开始时不再回滚——回滚就是再建一遍运行时，与「停机后不重建」直接冲突；
            // 原样上抛由调用方按 reload_failed 收口，停机路径的第二遍拆卸负责收走已发布实例。
            if (_stopping)
            {
                throw;
            }

            // 回滚强制重建去抖队列/指纹缓存：失败尝试可能已按新配置的 DebounceMs 换过队列，
            // 「队列非空」不代表它属于上一份配置；复用会让回滚后的窗口变成新配置的值，并丢掉
            // 旧窗口里的在途路径。重建队列同时触发全量补扫，由旧的去重表挡住已处理文件。
            try
            {
                await BuildRuntimeAsync(previousConfig, rebuildDebounceState: true, token);
            }
            catch (Exception rollbackEx)
            {
                SafeLog.Try(() => _logger.LogError(
                    rollbackEx,
                    "配置重载失败且回滚到上一份生效配置也失败：原错误={ReloadError}；回滚错误={RollbackError}",
                    ex.Message,
                    rollbackEx.Message));
                // AggregateException.Message 会追加内层异常消息，再手拼会让 IPC 文案重复两段。
                throw new AggregateException(
                    "重载失败，且用上一份生效配置回滚未完成；worker 可能已无可用运行时，需再次重载或重启确认",
                    ex,
                    rollbackEx);
            }

            SafeLog.Try(() => _logger.LogWarning(ex, "配置重载失败，已回滚到上一份生效配置"));
            throw new InvalidOperationException($"{ex.Message}（已回滚到上一份生效配置）", ex);
        }
    }

    /// <summary>
    /// 建一遍运行期组件：拆卸旧实例（停 watcher、释放 store/audit），按配置重建并在之后做首扫
    /// 或拆卸窗口差集回收（fullSweep / handoffBaseline 决定）。失败语义与回滚由调用方
    /// <see cref="RebuildRuntimeAsync"/> 负责；首扫与快照的深度一律走 <see cref="WatchFolderEnumeration"/>。
    /// </summary>
    private async Task BuildRuntimeAsync(
        AppConfig config,
        bool rebuildDebounceState,
        CancellationToken token)
    {
        // 首扫判定看「上一个完整建成的范围」：重建中途抛异常时收口目录已切到新目录，只看收口目录
        // 会让下一次重载同目录跳过首扫，那目录里的既有文件就永远漏扫。深度键也算范围——
        // false→true 时必须补扫新放开的子目录，否则那些既有文件永远不会被处理。
        var scopeChanged = !SamePath(_appliedWatchFolder, config.Watch.HotFolder)
            || _currentConfig?.Watch.IncludeSubdirectories != config.Watch.IncludeSubdirectories;

        // 去抖队列的复用看「当前收口目录」：换了目录必须丢弃旧目录压在窗口内的在途路径，否则它们会被
        // 新 pipeline 照常清除元数据。窗口未变才复用——#85 只护住「已弹队」那一段，拆卸时若把队列连同
        // 窗口内的路径一起丢掉，新 watcher 的轮询初始快照会把这些文件当既有文件，从此永不再报。
        // 回滚（rebuildDebounceState）一律重建：失败尝试可能已把队列换成新配置的窗口，复用它会让
        // 回滚后的去抖窗口仍是新值，旧窗口里的在途路径也不会被补扫回来。
        var debounceWindowUnchanged = !rebuildDebounceState
            && _debounceQueue is not null
            && SamePath(_activeWatchFolder, config.Watch.HotFolder)
            && _currentConfig?.Watch.DebounceMs == config.Watch.DebounceMs;

        var fullSweep = scopeChanged || !debounceWindowUnchanged;

        // 非全量补扫的重载只回收「拆卸窗口内发生变化」的文件：快照必须在旧 watcher 停下之前取——一停就没有
        // FSW 事件，新轮询兜底的初始快照又会把窗口内的文件当成既有文件，从此永不上报。
        // 差集比的是 (长度, mtime)：只看 mtime 余量的话，保留源时间戳的写入躲得过判定，判定多捞到的已处理
        // 文件又只能靠去重表挡住——同一次重载换了审计目录时新表是空的，多捞的就是重处理。
        // 空基准只是「本次走全量补扫」的占位：差集判定只在非全量补扫那条分支上才发生。
        // Design(#82): 非全量补扫的回收判据是拆卸前后的目录快照差集，不用 mtime 余量、不靠去重表兜多捞（#114）；快照没建成与回滚（#112）两条退路仍走全量补扫，残余就记在这里。
        var handoffBaseline = new Dictionary<string, FileFingerprint>(PathKeyComparer);
        if (!fullSweep)
        {
            try
            {
                handoffBaseline = CaptureWatchFolderSnapshot(config);
            }
            catch (Exception ex)
            {
                // 记录的取舍：差集没有基准可用，退回整目录补扫——多捞的已处理文件此时只能靠去重表兜
                // （审计目录同批改时新表为空，会重处理）。选它是因为另一头漏捞等于元数据静默留存。
                _logger.LogWarning(ex, "重载拆卸前的目录快照未建成，本次重建改走整目录补扫。");
                fullSweep = true;
                await EmitHandoffDegradedAsync(config, ex.Message);
            }
        }

        // 收口目录先切到新配置：此后旧 watcher 与轮询兜底的在途回调都按新目录过滤，不会漏进新队列。
        _activeWatchFolder = config.Watch.HotFolder;

        _watcher?.Stop();
        _watcher = null;
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;

        // 拆卸窗口（旧 watcher 已停、新 watcher 未起）从这里开始，到下面 _watcher.Start() 结束。
        // 基准比窗口起点取得更早，所以「基准→Stop」之间落地的改动会被 FSW 与差集各报一次，
        // 由去重队列与 2 s 指纹抑制压掉重复——多报一次无所谓，漏报等于元数据静默留存。
        if (TeardownWindowHookAsync is { } teardownHook)
        {
            await teardownHook();
        }

        if (!debounceWindowUnchanged)
        {
            _debounceQueue = null;
            _recentFingerprintCache = null;
        }

        if (_bridge is not null)
        {
            var retiredBridge = _bridge;
            _bridge = null;
            await StopBridgeAsync(retiredBridge);
       }

        // 重建前先摘空引用并释放旧实例的常驻句柄：FileProcessedRecordStore 以 FileShare.Read
        // 常驻持有 processed.ndjson 的写句柄，JsonLineAuditLogger 持有后台消费任务；旧实例不释放
        // 则新 store 打开同一文件必然自我冲突（IOException「being used by another process」，
        // 单实例、任何模式皆然），ReloadConfig 从未真正生效。先置空 pipeline/audit 让 drain 循环
        // 走空转分支，再释放（释放失败只吞，不阻断重建）。
        // Design(#82): 重载重建前必须释放旧 store/audit 的常驻句柄；失败只降级不阻断重建。
        _pipeline = null;
        var retiredStore = _processedRecordStore;
        _processedRecordStore = null;
        var retiredAudit = _audit;
        _audit = null;
        DisposeQuietly(retiredStore);
        DisposeQuietly(retiredAudit);

        // 建运行时前的最后一道复检（#126）：拆卸段有 await（桥停止有界 4 s 等），停机可能在拆卸
        // 期间开始——在创建任何新运行时之前放弃。此处之后到发布字段为止是纯同步代码，且全程持
        // drain 闸，停机不持闸收不走它们——但停机的第二遍拆卸在闸等待之后**必定执行**，会把
        // 已发布的实例收走，不留越过停机的运行时资源。
        if (_stopping)
        {
            throw new InvalidOperationException(StoppingReloadRejection);
        }

       if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
       {
           _autoExcludedSubdirectories = Array.Empty<string>();
           _appliedWatchFolder = string.Empty;
           _currentConfig = config;
           return;
       }

       _cleanupTimer = new Timer(_ => PerformPeriodicCleanup(config), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        if (!debounceWindowUnchanged)
        {
            _debounceQueue = new DebounceQueue(TimeSpan.FromMilliseconds(config.Watch.DebounceMs), () => DateTimeOffset.UtcNow);
            _recentFingerprintCache = new RecentFingerprintCache(() => DateTimeOffset.UtcNow);
        }
       _audit = new JsonLineAuditLogger(config.Audit.LogDirectory, config.Audit.RetainDays, config.Audit.DiagnosticMode, AuditLevelParser.Parse(config.Audit.LogLevel));
        IAuditLogger auditLogger = _audit is not null ? _audit : new NoopAuditLogger();
        _bridge = CreateBridge(config, auditLogger);
        _processedRecordStore = new FileProcessedRecordStore(config.Audit.LogDirectory);
        _pipeline = new FileTaskPipeline(config, new RuleEngine(config), _bridge, new LocalFileOperations(), auditLogger, _processedRecordStore);
        _maxParallelDrain = Math.Max(1, Math.Min(config.ExifTool.MaxParallelDrain, config.ExifTool.StayOpenPoolSize));

        // ponytail: M4 lazy - do NOT eager StartAsync here; WipeMetadataAsync calls
        // EnsureStartedAsync internally, so ExifTool only spawns when first file arrives.
        // This was originally `await _bridge.StartAsync(token);`, which blocked startup; startup now avoids that.

        // 用局部变量建、再发布到字段（#126）：停机的第一遍拆卸不持 drain 闸，会清空 _watcher——
        // 若发布段直接写字段再解引用，「赋值 → Start()」之间被清空即 NRE。局部变量保证发布段的
        // 自身引用不依赖字段；发布出去的实例由停机第二遍拆卸收走。
        var watcher = new FswFolderWatcher(
            config,
            new DirectoryRecoveryScanner(config),
            auditLogger,
            path =>
            {
                EnqueueIfNeeded(path);
                return Task.CompletedTask;
            },
            logger: _logger);

        _autoExcludedSubdirectories = watcher.GetAutoExcludedSubdirectories();
        _watcher = watcher;

        // 先起 watcher 再扫描：扫描期间新建的文件若没被 FSW 盯住，会落进轮询兜底的初始快照，从此永不上报。
        // Design(#82): 重载首扫之前必须先起 watcher（#100）。
        watcher.Start();

        // 其余重载不整目录补扫：审计目录一改，新的去重记录表是空的，把既有文件全排一遍就会重处理，
        // fixed_directory 产物可被覆盖。例外是回滚（rebuildDebounceState）：失败尝试从拆卸到抛错之间
        // 没有任何事件源盯着目录，空窗可达数秒，而差集只覆盖本次这一趟，故回滚一律全量补扫——
        // 已处理文件由回滚重建的旧去重表挡住。
        // 非全量补扫时只捞差集，多一个都不捞：窗口外已处理过的文件即便 mtime 很新也不该再进队列。
        // 残余：窗口内被原地改写、长度与 mtime 都没变的写入判不出来（保留时间戳的同名覆盖），
        // 那种写入不留任何可比对的元数据，本路径判不出来；只有后续范围变更触发整目录补扫才会再碰到它。
        // Design(#82): 重载首扫只在「监控范围被启用」或队列被重建（在途去抖项已随旧队列丢弃）时全量补扫（#100）。
        if (Directory.Exists(config.Watch.HotFolder))
        {
            foreach (var path in WatchFolderEnumeration.EnumerateFiles(config.Watch.HotFolder, config.Watch.IncludeSubdirectories))
            {
                if (fullSweep || ChangedInsideHandoffWindow(handoffBaseline, path))
                {
                    EnqueueIfNeeded(path);
                }
            }
        }

        _currentConfig = config;
        _appliedWatchFolder = config.Watch.HotFolder;
    }

    private void EnqueueIfNeeded(string path)
    {
        // 取一次本地引用再用：重载拆卸会把这两个字段置空，而 FSW/轮询回调是 fire-and-forget，
        // 守门与使用之间被置空就会抛 NullReferenceException——那个异常吞在未观察的任务里，文件从此不再入队。
        var queue = _debounceQueue;
        var fingerprintCache = _recentFingerprintCache;
        if (queue is null || fingerprintCache is null)
        {
            return;
        }

        // 只收当前监控目录内的路径：拆卸下来的旧 watcher 与轮询兜底不会等回调跑完就返回，
        // 用户已经放弃的目录若继续入队，新 pipeline 会照常清除范围外的文件。
        var activeWatchFolder = _activeWatchFolder;
        if (!string.IsNullOrWhiteSpace(activeWatchFolder)
            && !WatchPathFilter.IsSameOrSubPath(path, activeWatchFolder))
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        if (_autoExcludedSubdirectories.Count > 0
            && WatchPathFilter.ShouldSkipPath(path, _autoExcludedSubdirectories))
        {
            return;
        }

        var info = new FileInfo(path);
        var fingerprint = new FileFingerprint(info.Length, info.LastWriteTimeUtc);
        if (fingerprintCache.ShouldSuppress(path, fingerprint, TimeSpan.FromSeconds(2)))
        {
            return;
        }

        queue.Enqueue(path);
    }

    /// <summary>
    /// 目录是否同一处。Windows 大小写不敏感、类 Unix 大小写敏感——这里比较的是「要不要重扫/要不要丢弃在途路径」，
    /// 平台判错会少扫一次或把一个目录的文件当成另一个目录的。
    /// </summary>
    private static bool SamePath(string left, string right)
        => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// 快照字典的路径键比较器，与 <see cref="SamePath"/> 同一口径：Windows 大小写不敏感、类 Unix 敏感。
    /// 在大小写敏感的文件系统上一律 OrdinalIgnoreCase 会让 Photo.jpg 与 photo.jpg 撞成一个键，
    /// 差集于是把没变的那个当成变了多捞一次。
    /// </summary>
    private static StringComparer PathKeyComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// 拆卸起点上的监控目录基准：路径 → (长度, 最后写入时间)，深度取 include_subdirectories，
    /// 与重建后的首扫同一口径——两份名单口径不一致，差集就会把范围外的文件当成窗口内新落地的。
    /// 目录尚不存在时返回空基准：此后枚举到的都算新落地。
    /// </summary>
    private static Dictionary<string, FileFingerprint> CaptureWatchFolderSnapshot(AppConfig config)
    {
        var snapshot = new Dictionary<string, FileFingerprint>(PathKeyComparer);
        foreach (var path in WatchFolderEnumeration.EnumerateFiles(config.Watch.HotFolder, config.Watch.IncludeSubdirectories))
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                snapshot[path] = new FileFingerprint(info.Length, info.LastWriteTimeUtc);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 这个文件是不是拆卸窗口内落地的：基准里没有这个路径，或长度/最后写入时间与基准不同。
    /// </summary>
    private static bool ChangedInsideHandoffWindow(Dictionary<string, FileFingerprint> baseline, string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            // 枚举到之后、取指纹之前被删掉：没有东西要处理。
            return false;
        }

        return !baseline.TryGetValue(path, out var seenBeforeTeardown)
            || seenBeforeTeardown != new FileFingerprint(info.Length, info.LastWriteTimeUtc);
    }

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            await DrainReadyItemsAsync(cancellationToken);
            await Task.Delay(150, cancellationToken);
        }
    }

    private async Task DrainReadyItemsAsync(CancellationToken cancellationToken)
    {
        if (_debounceQueue is null || _pipeline is null || _recentFingerprintCache is null)
        {
            return;
        }

        var ready = _debounceQueue.PopReady();

        if (_maxParallelDrain <= 1)
        {
            foreach (var path in ready)
            {
                await ProcessReadyPathAsync(path, cancellationToken);
            }

            return;
        }

        using var gate = new SemaphoreSlim(_maxParallelDrain);
        var tasks = new List<Task>(ready.Count);
        foreach (var path in ready)
        {
            await gate.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessReadyPathAsync(path, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessReadyPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_pipeline is null || _recentFingerprintCache is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        await _pipeline.HandleAsync(path, cancellationToken);

        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        _recentFingerprintCache.Remember(path, new FileFingerprint(info.Length, info.LastWriteTimeUtc));
    }

    private string? GetValue(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string value, string optionName)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new AppConfigValidationException($"{optionName} must be an integer");
        }

        return parsed;
    }

    private static IReadOnlyDictionary<string, string>? BuildServiceStartedData(IReadOnlyList<string> autoExcludedSubdirectories)
    {
        if (autoExcludedSubdirectories.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            [WatchPathFilter.ServiceStartedDataKey] = string.Join(";", autoExcludedSubdirectories)
        };
    }

    private sealed class DirectoryRecoveryScanner : IRecoveryScanner
    {
        private readonly AppConfig _config;

        public DirectoryRecoveryScanner(AppConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// FSW 出错恢复后的补扫：深度跟随 <c>include_subdirectories</c>（#109），只收扩展名白名单内的文件。
        /// </summary>
        public IReadOnlyList<string> ScanAll(string rootPath)
        {
            return WatchFolderEnumeration
                .EnumerateFiles(rootPath, _config.Watch.IncludeSubdirectories)
                .Where(path => IsAllowed(path, _config.Rules.AllowedExtensions ?? []))
                .ToArray();
        }

        private static bool IsAllowed(string path, string[] allowedExtensions)
        {
            var extension = Path.GetExtension(path);
            return allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
    }
}
