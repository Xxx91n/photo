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
                            // 未知格式的可见跳过事件——全链路可消费。
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
                                    // sourcePath 仍是原始状态，直接备份（冲突分流见 WriteBackupAsync）
                                    await WriteBackupAsync(sourcePath, decision.BackupPath!, cancellationToken).ConfigureAwait(false);
                                    // 再把清除后的临时文件移回原路径，完成"原地清除"的最终效果
                                    _fileOperations.Move(tempPath!, target);
                                    tempPath = null; // 标记已消费，finally 块不用清理
                                }
                                else
                                {
                                    // target != sourcePath 的正常路径，原逻辑不变（冲突分流见 WriteBackupAsync）
                                    await WriteBackupAsync(sourcePath, decision.BackupPath!, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            else if (needsTempRoute)
                            {
                                // 需要路由但不需要备份（理论上 needsTempRoute 里已含 CreateBackup 检查，
                                // 这里作为防御性分支：把清除后的临时文件移回原路径）
                                _fileOperations.Move(tempPath!, target);
                                tempPath = null;
                            }

                            // issue #8：部分清理族（gif/psd/视频）的成功事件必须带尾巴标注，
                            // 不许让 GIF 和 JPEG 在审计里不可区分。
                            var partialNote = WipeStrategyResolver.PartialCleanNote(sourcePath);
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_processing_succeeded",
                                    AuditLevel.Info,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    $"attempt={attempt};wipe_result=Cleaned_Modified",
                                    partialNote is null
                                        ? null
                                        : new Dictionary<string, string> { ["partial_clean"] = partialNote }),
                                cancellationToken);
                            if (processedKey is not null)
                            {
                                _processedStore.MarkProcessed(processedKey);
                            }
                            return;

                        case WipeResult.Failed_WriteError:
                            // #288：ExifTool 明确说写不了（「0 image files updated」+「files weren't
                            // updated due to errors」，分片 MP4 即此形态）——文件一字未动，绝不许按
                            // file_processing_succeeded 收场。确定性错误，重试必然同样失败，不进重试，
                            // 直接按设置进隔离区；原文件保持未处理，不写 processed 记录。
                            await _audit.WriteAsync(
                                new AuditEvent(
                                    "file_processing_failed",
                                    AuditLevel.Error,
                                    DateTimeOffset.UtcNow,
                                    Guid.NewGuid().ToString("N"),
                                    sourcePath,
                                    "wipe_result=Failed_WriteError;exiftool_refused_to_write",
                                    null),
                                cancellationToken);
                            await QuarantineAsync(target, sourcePath, cancellationToken);
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

            await QuarantineAsync(target, sourcePath, cancellationToken);
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

    /// <summary>
    /// 按设置把失败文件挪进隔离区并写 <c>file_quarantined</c> 审计事件；未启用隔离时只跳过。
    /// 重试耗尽与 #288 的 ExifTool 写错误两条失败路径共用。终态收场：隔离自身的失败不向调用方
    /// 抛——否则写错误分支会被拖进擦除重试（CodeRabbit #294 意见 1），单独落
    /// <c>file_quarantine_failed</c> 让失败看得见。隔离目标绝不覆盖已隔离文件（CodeRabbit #294
    /// 意见 2：LocalFileOperations.Move 目标存在会先删后移，同名隔离会丢先前隔离的原件，
    /// 且写错误分支没有备份、丢了就没了）：槽位被占时按备份侧车的既有模式另存
    /// <c>名字.时间戳.扩展名</c>，同秒碰撞加 <c>-N</c> 序号。
    /// </summary>
    private async Task QuarantineAsync(string failedFilePath, string sourcePath, CancellationToken cancellationToken)
    {
        if (!_config.Quarantine.Enabled)
        {
            return;
        }

        try
        {
            _fileOperations.EnsureDirectory(_config.Quarantine.Directory);
            var destination = Path.Combine(_config.Quarantine.Directory, Path.GetFileName(failedFilePath));
            if (File.Exists(destination))
            {
                destination = BackupPathResolver.ResolveSidecarPath(
                    destination,
                    Path.GetExtension(failedFilePath),
                    DateTimeOffset.UtcNow,
                    File.Exists);
            }

            _fileOperations.Move(failedFilePath, destination);
            await _audit.WriteAsync(
                new AuditEvent("file_quarantined", AuditLevel.Warn, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, destination, null),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 关停取消不得记成隔离失败——原样上抛，让取消语义回到调用方。
            throw;
        }
        catch (Exception ex)
        {
            // 终态收场：隔离失败只单独记账，不回灌进擦除重试；file_quarantined 事件缺席本身就是
            // 「没隔离成」的信号，这里再给一条带原因的 Error 让用户看得见。
            await _audit.WriteAsync(
                new AuditEvent("file_quarantine_failed", AuditLevel.Error, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath,
                    $"quarantine_target={failedFilePath};{ex.Message}", null),
                cancellationToken);
        }
    }

    /// <summary>
    /// 备份冲突分流——绝不静默覆盖：
    /// 1. 源或槽位为符号链接/ReparsePoint → 拒绝并写可见告警（fail-closed，Borg create 语义）；
    /// 2. 槽位不存在 → 直接原子写入；
    /// 3. 槽位存在且内容相同 → 跳过（幂等，承接"同文件重处理保留最新"的合法语义）；
    /// 4. 槽位存在且内容不同 → 另存一份为 <c>名字.时间戳.扩展名</c>（绝不覆盖）。
    /// </summary>
    private async Task WriteBackupAsync(string sourcePath, string backupSlot, CancellationToken cancellationToken)
    {
        if (BackupPathResolver.IsReparsePoint(sourcePath) || BackupPathResolver.IsReparsePoint(backupSlot))
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    "backup_skipped",
                    AuditLevel.Warn,
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString("N"),
                    sourcePath,
                    "reparse_point_refused",
                    new Dictionary<string, string> { ["backup_path"] = backupSlot }),
                cancellationToken);
            return;
        }

        var backupDir = Path.GetDirectoryName(backupSlot);
        if (!string.IsNullOrWhiteSpace(backupDir))
        {
            _fileOperations.EnsureDirectory(backupDir);
        }

        var slotExists = File.Exists(backupSlot);
        if (!slotExists)
        {
            // 槽位空闲：原子写入，不存在覆盖问题。
            await _fileOperations.AtomicCopyAsync(sourcePath, backupSlot, overwrite: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (BackupPathResolver.ContentsEqual(sourcePath, backupSlot))
        {
            // 同槽位+内容相同 → 跳过（幂等，不另存一份）。
            await _audit.WriteAsync(
                new AuditEvent(
                    "backup_skipped",
                    AuditLevel.Info,
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString("N"),
                    sourcePath,
                    "identical_content_slot_kept",
                    new Dictionary<string, string> { ["backup_path"] = backupSlot }),
                cancellationToken);
            return;
        }

        // 槽位存在且内容分歧 → 另存一份（绝不静默覆盖）。
        var sidecar = BackupPathResolver.ResolveSidecarPath(
            backupSlot, _config.Backup.Suffix, DateTimeOffset.UtcNow, File.Exists);
        await _fileOperations.AtomicCopyAsync(sourcePath, sidecar, overwrite: false, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEvent(
                "backup_sidecar_versioned",
                AuditLevel.Info,
                DateTimeOffset.UtcNow,
                Guid.NewGuid().ToString("N"),
                sourcePath,
                "content_diverged_sidecar_written",
                new Dictionary<string, string>
                {
                    ["slot"] = backupSlot,
                    ["sidecar"] = sidecar,
                }),
            cancellationToken);
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

            // #133 路线 2：去重键收敛为路径粗粒度，同一路径已有记录即视为已处理（不再比对长度/mtime 指纹）。
            return ProcessedKeyBuilder.Build(sourcePath);
        }
        catch
        {
            return null;
        }
    }
}
