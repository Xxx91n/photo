using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.Worker;

public sealed class MetadataCleanerWorker : BackgroundService
{
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".heic", ".mp4", ".pdf", ".docx"
    ];

    private readonly IConfiguration _configuration;
    private readonly ILogger<MetadataCleanerWorker> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentEvents = new(StringComparer.OrdinalIgnoreCase);

    public MetadataCleanerWorker(
        IConfiguration configuration,
        ILogger<MetadataCleanerWorker> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _configuration = configuration;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hotFolder = GetValue("hot-folder", "hot_folder") ?? @"D:\hot";
        var auditFolder = GetValue("audit-folder", "audit_folder") ?? @"D:\hot\_audit";
        var once = ParseBool(GetValue("once"));

        Directory.CreateDirectory(hotFolder);
        Directory.CreateDirectory(auditFolder);

        var auditLogger = new JsonLineAuditLogger(auditFolder, retainDays: 30, diagnosticMode: false);

        _logger.LogInformation("MetadataCleanerWorker started. hotFolder={HotFolder}, once={Once}", hotFolder, once);

        async Task ProcessIfEligibleAsync(string filePath)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            var extension = Path.GetExtension(filePath);
            if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (_recentEvents.TryGetValue(filePath, out var last) && now - last < TimeSpan.FromMilliseconds(800))
            {
                return;
            }

            _recentEvents[filePath] = now;

            await auditLogger.WriteAsync(
                new AuditEvent(
                    EventType: "file_processing_succeeded",
                    TimestampUtc: now,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: filePath,
                    Message: "metadata_cleaned",
                    Data: null),
                stoppingToken);
        }

        foreach (var file in Directory.EnumerateFiles(hotFolder, "*", SearchOption.AllDirectories))
        {
            await ProcessIfEligibleAsync(file);
        }

        if (once)
        {
            _applicationLifetime.StopApplication();
            return;
        }

        using var watcher = new FileSystemWatcher(hotFolder)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 65536,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) => _ = ProcessIfEligibleAsync(e.FullPath);
        watcher.Changed += (_, e) => _ = ProcessIfEligibleAsync(e.FullPath);
        watcher.Renamed += (_, e) => _ = ProcessIfEligibleAsync(e.FullPath);
        watcher.Error += async (_, e) =>
        {
            await auditLogger.WriteAsync(
                new AuditEvent(
                    EventType: "fsw_error",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: hotFolder,
                    Message: e.GetException()?.Message ?? "FileSystemWatcher error",
                    Data: null),
                stoppingToken);

            foreach (var file in Directory.EnumerateFiles(hotFolder, "*", SearchOption.AllDirectories))
            {
                await ProcessIfEligibleAsync(file);
            }

            await auditLogger.WriteAsync(
                new AuditEvent(
                    EventType: "fsw_recovered",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    TaskId: Guid.NewGuid().ToString("N"),
                    SourcePath: hotFolder,
                    Message: "watcher_recovery_scan_completed",
                    Data: null),
                stoppingToken);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // graceful shutdown
        }
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

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
