using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class FileTaskPipeline
{
    private readonly AppConfig _config;
    private readonly RuleEngine _ruleEngine;
    private readonly IExifToolBridge _bridge;
    private readonly IFileOperations _fileOperations;
    private readonly IAuditLogger _audit;
    private readonly IProcessedRecordStore _processedStore;

    private readonly HashSet<string> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileTaskPipeline(
        AppConfig config,
        RuleEngine ruleEngine,
        IExifToolBridge bridge,
        IFileOperations fileOperations,
        IAuditLogger audit,
        IProcessedRecordStore processedStore)
    {
        _config = config;
        _ruleEngine = ruleEngine;
        _bridge = bridge;
        _fileOperations = fileOperations;
        _audit = audit;
        _processedStore = processedStore;
    }

    public async Task HandleAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!TryEnterInflight(sourcePath))
        {
            return;
        }

        try
        {
            var processedKey = TryBuildProcessedKey(sourcePath);

            if (processedKey is not null && _processedStore.IsProcessed(processedKey))
            {
                await _audit.WriteAsync(
                    new AuditEvent(
                        "file_skipped",
                        AuditLevel.Info,
                        DateTimeOffset.UtcNow,
                        Guid.NewGuid().ToString("N"),
                        sourcePath,
                        "already_processed",
                        null),
                    cancellationToken);
                return;
            }

            await _audit.WriteAsync(
                new AuditEvent(
                    "file_detected",
                    AuditLevel.Debug,
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString("N"),
                    sourcePath,
                    "debounced file entered pipeline",
                    null),
                cancellationToken);

            var decision = _ruleEngine.Decide(sourcePath);
            if (!decision.ShouldProcess || decision.OutputPath is null)
            {
                await _audit.WriteAsync(
                    new AuditEvent("file_skipped", AuditLevel.Info, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, decision.Reason, null),
                    cancellationToken);
                return;
            }

            var target = decision.OutputPath;
            if (!string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _fileOperations.Copy(sourcePath, target, overwrite: true);
            }

            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                _fileOperations.EnsureDirectory(targetDir);
            }

            for (var attempt = 1; attempt <= _config.Retry.MaxAttempts; attempt++)
            {
                try
                {
                    await _audit.WriteAsync(
                        new AuditEvent(
                            "file_processing_started",
                            AuditLevel.Debug,
                            DateTimeOffset.UtcNow,
                            Guid.NewGuid().ToString("N"),
                            sourcePath,
                            $"attempt={attempt}",
                            null),
                        cancellationToken);

                    var result = await _bridge.WipeMetadataAsync(target, cancellationToken);

                    switch (result)
                    {
                        case WipeResult.Skipped_NoMetadata:
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_skipped",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    "no_metadata_found",
                                    null),
                                cancellationToken);
                            return;

                        case WipeResult.Skipped_NoClearable:
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_skipped",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    "no_clearable_metadata",
                                    null),
                                cancellationToken);
                            return;

                        case WipeResult.Cleaned_NoBackup:
                        case WipeResult.Cleaned_WithBackup:
                            if (decision.CreateBackup && decision.BackupPath is not null)
                            {
                                var backupDir = Path.GetDirectoryName(decision.BackupPath);
                                if (!string.IsNullOrWhiteSpace(backupDir))
                                {
                                    _fileOperations.EnsureDirectory(backupDir);
                                }

                                _fileOperations.Copy(sourcePath, decision.BackupPath, overwrite: true);
                            }

                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_processing_succeeded",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    $"attempt={attempt};wipe_result={result}",
                                    null),
                                cancellationToken);
                            if (processedKey is not null)
                            {
                                _processedStore.MarkProcessed(processedKey);
                            }
                            return;

                        default:
                            throw new InvalidOperationException($"Unexpected wipe result: {result}");
                    }
                }
                catch (Exception ex)
                {
                    await _audit.WriteAsync(
                        new AuditEvent("file_processing_failed", AuditLevel.Error, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, ex.Message, null),
                        cancellationToken);

                    if (attempt < _config.Retry.MaxAttempts)
                    {
                        var delaySeconds = _config.Retry.BackoffSeconds[
                            Math.Min(attempt - 1, _config.Retry.BackoffSeconds.Length - 1)];

                        await _audit.WriteAsync(
                            new AuditEvent(
                                "file_retry_scheduled",
                                AuditLevel.Warn,
                                DateTimeOffset.UtcNow,
                                Guid.NewGuid().ToString("N"),
                                sourcePath,
                                $"next_attempt={attempt + 1};delay_seconds={delaySeconds}",
                                null),
                            cancellationToken);

                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                }
            }

            if (_config.Quarantine.Enabled)
            {
                _fileOperations.EnsureDirectory(_config.Quarantine.Directory);
                var failedFilePath = target;
                var destination = Path.Combine(_config.Quarantine.Directory, Path.GetFileName(failedFilePath));
                _fileOperations.Move(failedFilePath, destination);
                await _audit.WriteAsync(
                    new AuditEvent("file_quarantined", AuditLevel.Warn, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, destination, null),
                    cancellationToken);
            }
        }
        finally
        {
            ExitInflight(sourcePath);
        }
    }

    private bool TryEnterInflight(string path)
    {
        lock (_gate)
        {
            return _inflight.Add(path);
        }
    }

    private void ExitInflight(string path)
    {
        lock (_gate)
        {
            _inflight.Remove(path);
        }
    }

    private static string? TryBuildProcessedKey(string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists)
            {
                return null;
            }

            return ProcessedKeyBuilder.Build(sourcePath, info.Length, info.LastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }
}
