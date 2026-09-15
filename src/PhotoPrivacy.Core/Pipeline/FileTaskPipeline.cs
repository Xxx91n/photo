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

        // 方案五：这些变量需要在 try/finally 作用域内可见
        string? tempPath = null;
        bool needsTempRoute = false;
        string effectiveTarget = sourcePath;
        string target = sourcePath;

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

            target = decision.OutputPath;
            if (!string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _fileOperations.Copy(sourcePath, target, overwrite: true);
            }

            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                _fileOperations.EnsureDirectory(targetDir);
            }

            // ── 方案五：临时文件路由 ──────────────────────────────────────────
            // 触发条件：ExifTool 操作目标就是原文件本身，且本次需要创建备份。
            // 此时把操作目标换成临时副本，让 sourcePath 保持只读直到备份完成。
            var isInPlace = string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase);
            needsTempRoute = isInPlace && decision.CreateBackup && decision.BackupPath is not null;

            effectiveTarget = target;

            if (needsTempRoute)
            {
                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    "PP_" + Path.GetRandomFileName() + Path.GetExtension(sourcePath));
                _fileOperations.Copy(sourcePath, tempPath, overwrite: true);
                effectiveTarget = tempPath;
            }
            // ─────────────────────────────────────────────────────────────────────

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

                    var result = await _bridge.WipeMetadataAsync(effectiveTarget, cancellationToken);

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

                        case WipeResult.UnknownFormat:
                            // 票 01（A-001 / A-009）：未知格式的可见跳过事件——全链路可消费。
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "wipe_skipped_unknown",
                                    AuditLevel.Warn,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    "unsupported_format",
                                    new Dictionary<string, string>
                                    {
                                        ["extension"] = Path.GetExtension(sourcePath),
                                        ["reason"] = "not_in_wipe_family_map"
                                    }),
                                cancellationToken);
                            return;

                        case WipeResult.Cleaned_NoOp:
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_skipped",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    "wipe_noop_file_unchanged",
                                    null),
                                cancellationToken);
                            if (processedKey is not null)
                            {
                                _processedStore.MarkProcessed(processedKey);
                            }
                            return;

                        case WipeResult.Cleaned_Modified:
                            if (decision.CreateBackup && decision.BackupPath is not null)
                            {
                                var backupDir = Path.GetDirectoryName(decision.BackupPath);
                                if (!string.IsNullOrWhiteSpace(backupDir))
                                {
                                    _fileOperations.EnsureDirectory(backupDir);
                                }

                                if (needsTempRoute)
                                {
                                    // sourcePath 仍是原始状态，直接备份
                                    await _fileOperations.AtomicCopyAsync(sourcePath, decision.BackupPath!, overwrite: true, cancellationToken).ConfigureAwait(false);
                                    // 再把清除后的临时文件移回原路径，完成"原地清除"的最终效果
                                    _fileOperations.Move(tempPath!, target);
                                    tempPath = null; // 标记已消费，finally 块不用清理
                                }
                                else
                                {
                                    // target != sourcePath 的正常路径，原逻辑不变
                                    await _fileOperations.AtomicCopyAsync(sourcePath, decision.BackupPath!, overwrite: true, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            else if (needsTempRoute)
                            {
                                // 需要路由但不需要备份（理论上 needsTempRoute 里已含 CreateBackup 检查，
                                // 这里作为防御性分支：把清除后的临时文件移回原路径）
                                _fileOperations.Move(tempPath!, target);
                                tempPath = null;
                            }

                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_processing_succeeded",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    $"attempt={attempt};wipe_result=Cleaned_Modified",
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

                    // 方案五：异常时清理已损坏的临时文件，重试时会重新创建
                    if (needsTempRoute && tempPath is not null)
                    {
                        try { File.Delete(tempPath); } catch { /* 清理失败不影响重试 */ }
                        tempPath = null;
                    }

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

                        // 方案五：重试前重新创建临时文件副本
                        if (needsTempRoute)
                        {
                            tempPath = Path.Combine(
                                Path.GetTempPath(),
                                "PP_" + Path.GetRandomFileName() + Path.GetExtension(sourcePath));
                            _fileOperations.Copy(sourcePath, tempPath, overwrite: true);
                            effectiveTarget = tempPath;
                        }
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
            // 方案五：若临时文件未被消费（异常中途退出），清理残留
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch { /* 清理失败不影响主流程 */ }
            }

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
