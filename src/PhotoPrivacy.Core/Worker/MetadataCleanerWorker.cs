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

    /// <summary>
    /// 串行化「drain 一轮」与「拆卸重建」（#85）：重载在拆卸 store/audit 前必须先等当前一轮 drain 结束，
    /// 否则在途 <c>HandleAsync</c> 会用到已释放实例；同时 <c>PopReady</c> 已弹出的路径也不会因
    /// <c>_pipeline</c> 被置空而丢失（不重入队）。
    /// </summary>
    private readonly SemaphoreSlim _drainGate = new(1, 1);

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
            try
            {
                while (string.IsNullOrWhiteSpace(_currentConfig?.Watch.HotFolder))
                {
                    await _standbySignal.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown while the worker is waiting for first-run configuration.
                return;
            }
            catch (ObjectDisposedException)
            {
                // StopAsync disposes the signal before the host cancels; re-entering the wait after that
                // throws synchronously. That is shutdown too — must not fall through into the processing
                // loop, which would spin against a torn-down runtime until the cancellation lands.
                return;
            }

            await RunProcessingLoopAsync(stoppingToken);
            return;
        }

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

        // Skip enumeration if hot folder is empty — prevents ArgumentException
        if (!string.IsNullOrWhiteSpace(config.Watch.HotFolder) && Directory.Exists(config.Watch.HotFolder))
        {
            foreach (var file in Directory.EnumerateFiles(config.Watch.HotFolder, "*", SearchOption.AllDirectories))
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

        _watcher.Start();
        _appliedWatchFolder = config.Watch.HotFolder;

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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer?.Dispose();
        _watcher?.Stop();

        // IMPORTANT: the host-provided cancellationToken here is already cancelled
        // (or has a very short timeout) by the time StopAsync is called after
        // StopApplication(). Using it directly causes bridge.StopAsync and
        // audit.WriteAsync to be skipped immediately, leaving the process
        // hanging until the host forces a timeout. Use independent tokens instead.

        if (_bridge is not null)
        {
            using var bridgeCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await _bridge.StopAsync(bridgeCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Bridge stop timed out after 4 s, continuing shutdown.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bridge stop threw unexpectedly, continuing shutdown.");
            }
            (_bridge as IDisposable)?.Dispose();
            _bridge = null;
        }

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

        // 与 ApplyConfigAsync 同一口径（#85）：等当前一轮 drain 结束再释放，避免在途 HandleAsync 用到
        // 已释放实例。停机路径用有界等待（4 s）：拿不到闸也只降级继续，不让停机被长任务拖死。
        var drainIdle = await _drainGate.WaitAsync(TimeSpan.FromSeconds(4));
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
            if (drainIdle)
            {
                _drainGate.Release();
            }
        }

        _reloadGate.Dispose();
        _standbySignal.Dispose();

        await base.StopAsync(cancellationToken);
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

       return config;
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
        await _reloadGate.WaitAsync();
        try
        {
            var config = LoadEffectiveConfig();
            await ApplyConfigAsync(config, CancellationToken.None);
            if (_standbySignal.CurrentCount == 0)
            {
                _standbySignal.Release();
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

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
    private async Task RebuildRuntimeAsync(AppConfig config, CancellationToken token)
    {
        var previousConfig = _currentConfig;
        if (previousConfig is null)
        {
            await BuildRuntimeAsync(config, token);
            return;
        }

        try
        {
            await BuildRuntimeAsync(config, token);
        }
        catch (Exception ex)
        {
            // Design(#112): 重载重建失败必须用上一份已生效配置回滚，不得停在「活着但不再监控」形态。
            try
            {
                await BuildRuntimeAsync(previousConfig, token);
            }
            catch (Exception rollbackEx)
            {
                SafeLog.Try(() => _logger.LogError(
                    rollbackEx,
                    "配置重载失败且回滚到上一份生效配置也失败：{Message}",
                    ex.Message));
                throw new AggregateException(
                    "重载失败，且用上一份生效配置回滚失败；worker 已无可用运行时，需再次重载或重启",
                    ex,
                    rollbackEx);
            }

            SafeLog.Try(() => _logger.LogWarning(ex, "配置重载失败，已回滚到上一份生效配置"));
            throw new InvalidOperationException($"{ex.Message}（已回滚到上一份生效配置）", ex);
        }
    }

    private async Task BuildRuntimeAsync(AppConfig config, CancellationToken token)
    {
        // 拆卸窗口的起点：旧 watcher 一停，这期间新建的文件既不会有 FSW 事件，又会落进新轮询兜底的
        // 初始快照，只能靠补扫捞回来（见下方 handoff 兜底）。
        var handoffStartUtc = DateTime.UtcNow;

        // 首扫判定看「上一个完整建成的范围」：重建中途抛异常时收口目录已切到新目录，只看收口目录
        // 会让下一次重载同目录跳过首扫，那目录里的既有文件就永远漏扫。深度键也算范围——
        // false→true 时必须补扫新放开的子目录，否则那些既有文件永远不会被处理。
        var scopeChanged = !SamePath(_appliedWatchFolder, config.Watch.HotFolder)
            || _currentConfig?.Watch.IncludeSubdirectories != config.Watch.IncludeSubdirectories;

        // 去抖队列的复用看「当前收口目录」：换了目录必须丢弃旧目录压在窗口内的在途路径，否则它们会被
        // 新 pipeline 照常清除元数据。窗口未变才复用——#85 只护住「已弹队」那一段，拆卸时若把队列连同
        // 窗口内的路径一起丢掉，新 watcher 的轮询初始快照会把这些文件当既有文件，从此永不再报。
        var debounceWindowUnchanged = _debounceQueue is not null
            && SamePath(_activeWatchFolder, config.Watch.HotFolder)
            && _currentConfig?.Watch.DebounceMs == config.Watch.DebounceMs;

        // 收口目录先切到新配置：此后旧 watcher 与轮询兜底的在途回调都按新目录过滤，不会漏进新队列。
        _activeWatchFolder = config.Watch.HotFolder;

        _watcher?.Stop();
        _watcher = null;
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;

        if (!debounceWindowUnchanged)
        {
            _debounceQueue = null;
            _recentFingerprintCache = null;
        }

        if (_bridge is not null)
        {
            using var bridgeCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await _bridge.StopAsync(bridgeCts.Token);
            }
            catch
            {
                // best-effort
            }
            (_bridge as IDisposable)?.Dispose();
            _bridge = null;
       }

        // 重建前先摘空引用并释放旧实例的常驻句柄：FileProcessedRecordStore 以 FileShare.Read
        // 常驻持有 processed.ndjson 的写句柄，JsonLineAuditLogger 持有后台消费任务；旧实例不释放
        // 则新 store 打开同一文件必然自我冲突（IOException「being used by another process」，
        // 单实例、任何模式皆然），ReloadConfig 从未真正生效。先置空 pipeline/audit 让 drain 循环
        // 走空转分支，再释放（释放失败只吞，不阻断重建）。
        // Design(#85): 重载重建前必须释放旧 store/audit 的常驻句柄；失败只降级不阻断重建。
        _pipeline = null;
        var retiredStore = _processedRecordStore;
        _processedRecordStore = null;
        var retiredAudit = _audit;
        _audit = null;
        DisposeQuietly(retiredStore);
        DisposeQuietly(retiredAudit);

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

        _watcher = new FswFolderWatcher(
            config,
            new DirectoryRecoveryScanner(config),
            auditLogger,
            path =>
            {
                EnqueueIfNeeded(path);
                return Task.CompletedTask;
            },
            logger: _logger);

        _autoExcludedSubdirectories = _watcher.GetAutoExcludedSubdirectories();

        // 先起 watcher 再扫描：扫描期间新建的文件若没被 FSW 盯住，会落进轮询兜底的初始快照，从此永不上报。
        // Design(#100): 重载首扫之前必须先起 watcher。
        _watcher.Start();

        // 其余重载不整目录补扫：审计目录一改，新的去重记录表是空的，把既有文件全排一遍就会重处理，
        // fixed_directory 产物可被覆盖。
        // Design(#100): 重载首扫只在「监控范围被启用」或队列被重建（在途去抖项已随旧队列丢弃）时全量补扫。
        if (Directory.Exists(config.Watch.HotFolder))
        {
            // 扫描深度取自 include_subdirectories：FSW 按这个键建监听，越界扫到的子目录文件规则引擎并不过滤。
            var searchOption = config.Watch.IncludeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var fullSweep = scopeChanged || !debounceWindowUnchanged;
            // 非全量补扫时只捞拆卸窗口内新建的文件：它们没有 FSW 事件，又会被新轮询的初始快照当成既有文件。
            // 时间戳粒度有差异（FAT 到 2 s），往前留余量——漏捞等于元数据静默留存，宁可多捞。
            // 余量窗口内已处理的文件通常被去重记录挡住；但同一次重载若换了审计目录，去重表是空的，
            // 它们会被重处理（fixed_directory 下可能覆盖输出）。保留 mtime 的写入也会被余量跳过。残余见 #114。
            var handoffFloorUtc = handoffStartUtc.AddSeconds(-2);
            foreach (var path in Directory.EnumerateFiles(config.Watch.HotFolder, "*", searchOption))
            {
                if (fullSweep || File.GetLastWriteTimeUtc(path) >= handoffFloorUtc)
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

        public IReadOnlyList<string> ScanAll(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return [];
            }

            return Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
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
