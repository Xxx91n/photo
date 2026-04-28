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
    private DebounceQueue? _debounceQueue;
    private RecentFingerprintCache? _recentFingerprintCache;
    private FswFolderWatcher? _watcher;
    private int _maxParallelDrain = 1;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private AppConfig? _currentConfig;

    public string CurrentExifToolVersion => _bridge?.VersionText ?? "unknown";

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
            return;
        }

        var printEffectiveConfig = ParseBool(GetValue("print-effective-config", "print_effective_config"));
        if (printEffectiveConfig)
        {
            var json = AppConfigJson.ToIndentedJson(config);
            Console.WriteLine(json);
            _applicationLifetime.StopApplication();
            return;
        }

        _audit = config.Audit.DiagnosticMode
            ? new JsonLineAuditLogger(config.Audit.LogDirectory, config.Audit.RetainDays, config.Audit.DiagnosticMode)
            : null;
        IAuditLogger auditLogger = _audit is not null ? _audit : new NoopAuditLogger();
        _bridge = CreateBridge(config, auditLogger);
        _pipeline = new FileTaskPipeline(config, new RuleEngine(config), _bridge, new LocalFileOperations(), auditLogger);
        _maxParallelDrain = Math.Max(1, Math.Min(config.ExifTool.MaxParallelDrain, config.ExifTool.StayOpenPoolSize));
        _debounceQueue = new DebounceQueue(TimeSpan.FromMilliseconds(config.Watch.DebounceMs), () => DateTimeOffset.UtcNow);
        _recentFingerprintCache = new RecentFingerprintCache(() => DateTimeOffset.UtcNow);

        await _bridge.StartAsync(stoppingToken);

        if (config.ExifTool.DryRun)
        {
            if (_audit is not null)
            {
                await _audit.WriteAsync(
                    new AuditEvent(
                        EventType: "exiftool_started",
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
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    EventType: "service_started",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: config.Watch.HotFolder,
                    Message: $"mode={(config.ExifTool.DryRun ? "dry-run" : "live")}",
                    Data: BuildServiceStartedData(autoExcluded)),
                stoppingToken);
        }

        foreach (var file in Directory.EnumerateFiles(config.Watch.HotFolder, "*", SearchOption.AllDirectories))
        {
            EnqueueIfNeeded(file);
        }

        var once = ParseBool(GetValue("once"));
        if (once)
        {
            await DrainOnceAsync(stoppingToken);
            _applicationLifetime.StopApplication();
            return;
        }

        _watcher.Start();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_runtimeControl.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                await DrainReadyItemsAsync(stoppingToken);
                await Task.Delay(200, stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {
            // graceful shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
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
        }

        if (_audit is not null)
        {
            using var auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _audit.WriteAsync(
                    new AuditEvent(
                        EventType: "service_stopped",
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

        await base.StopAsync(cancellationToken);
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

        Directory.CreateDirectory(config.Watch.HotFolder);
        Directory.CreateDirectory(config.Audit.LogDirectory);
        Directory.CreateDirectory(config.Quarantine.Directory);

        return config;
    }

    private IExifToolBridge CreateBridge(AppConfig config, IAuditLogger audit)
    {
        return PooledExifToolBridgeFactory.BuildFromConfig(config, audit);
    }

    public async Task ReloadConfigAsync()
    {
        await _reloadGate.WaitAsync();
        try
        {
            var config = LoadEffectiveConfig();
            AppConfigValidator.Validate(config);
            _currentConfig = config;
            await ApplyConfigAsync(config, CancellationToken.None);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ApplyConfigAsync(AppConfig config, CancellationToken token)
    {
        _watcher?.Stop();

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
        }

        Directory.CreateDirectory(config.Watch.HotFolder);
        Directory.CreateDirectory(config.Audit.LogDirectory);
        Directory.CreateDirectory(config.Quarantine.Directory);

        _audit = config.Audit.DiagnosticMode
            ? new JsonLineAuditLogger(config.Audit.LogDirectory, config.Audit.RetainDays, config.Audit.DiagnosticMode)
            : null;
        IAuditLogger auditLogger = _audit is not null ? _audit : new NoopAuditLogger();
        _bridge = CreateBridge(config, auditLogger);
        _pipeline = new FileTaskPipeline(config, new RuleEngine(config), _bridge, new LocalFileOperations(), auditLogger);
        _maxParallelDrain = Math.Max(1, Math.Min(config.ExifTool.MaxParallelDrain, config.ExifTool.StayOpenPoolSize));

        await _bridge.StartAsync(token);

        _watcher = new FswFolderWatcher(
            config,
            new DirectoryRecoveryScanner(config),
            auditLogger,
            path =>
            {
                EnqueueIfNeeded(path);
                return Task.CompletedTask;
            });

        _watcher.Start();
    }

    private void EnqueueIfNeeded(string path)
    {
        if (_debounceQueue is null || _recentFingerprintCache is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        var fingerprint = new FileFingerprint(info.Length, info.LastWriteTimeUtc);
        if (_recentFingerprintCache.ShouldSuppress(path, fingerprint, TimeSpan.FromSeconds(2)))
        {
            return;
        }

        _debounceQueue.Enqueue(path);
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
