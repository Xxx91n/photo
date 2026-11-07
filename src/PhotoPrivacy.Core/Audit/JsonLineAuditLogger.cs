using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.Channels;

namespace PhotoPrivacy.Core.Audit;

public sealed class JsonLineAuditLogger : IAuditLogger, IDisposable
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100MB
    private const int RetainedFileCountLimit = 31;
    private const int BatchSize = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    private readonly string _logDirectory;
    private readonly int _retainDays;
    private readonly bool _diagnosticMode;
    private AuditLevel _minimumWriteLevel;
    private readonly Channel<string> _channel;
    private readonly Task _backgroundTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private string? _activeRolloverTarget;
    private string? _activeRolloverDate;

    public JsonLineAuditLogger(
        string logDirectory,
        int retainDays,
        bool diagnosticMode,
        AuditLevel minimumWriteLevel = AuditLevel.Debug)
    {
        _logDirectory = logDirectory;
        _retainDays = retainDays;
        _diagnosticMode = diagnosticMode;
       _minimumWriteLevel = minimumWriteLevel;
        // ponytail: empty logDirectory (config.sample.json defaults to "") would throw
        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
           Directory.CreateDirectory(_logDirectory);
        }

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _backgroundTask = Task.Run(ConsumeLoopAsync);
    }

    public void SetMinimumWriteLevel(AuditLevel level) => _minimumWriteLevel = level;

    public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent.Level < _minimumWriteLevel)
            return ValueTask.CompletedTask;

        var payload = new
        {
            event_type = auditEvent.EventType,
            level = auditEvent.Level.ToString().ToUpperInvariant(),
            timestamp_utc = auditEvent.TimestampUtc.ToUniversalTime().ToString("O"),
            task_id = auditEvent.TaskId,
            source_path_masked = PathMasker.Mask(auditEvent.SourcePath),
            source_path_hash = PathMasker.StableHash(auditEvent.SourcePath),
            message = auditEvent.Message,
            data = auditEvent.Data,
            diagnostic_mode = _diagnosticMode
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        if (_channel.Writer.TryWrite(json))
            return ValueTask.CompletedTask;

        // Channel completed (disposing)
        return ValueTask.CompletedTask;
    }

    private async Task ConsumeLoopAsync()
    {
        var batch = new List<string>(BatchSize);
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < BatchSize && reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, _cts.Token).ConfigureAwait(false);
                }
            }

            // Drain any remaining items after the loop exits (Dispose calls TryComplete)
            batch.Clear();
            while (reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count >= BatchSize)
                {
                    await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown — drain remaining
            batch.Clear();
            while (reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count >= BatchSize)
                {
                    await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task FlushBatchAsync(List<string> batch, CancellationToken cancellationToken)
    {
        var target = ResolveRolloverTarget();
        using var writer = new StreamWriter(
            new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.Read),
            leaveOpen: false);

        foreach (var json in batch)
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveRolloverTarget()
    {
        // Design(#208): 审计文件名按本地「今天」命名，与 IPC 回填、UI tail 同口径——UTC+8 00:00–07:59 时窗内不再把行写进「昨天」名文件。
        var baseName = AuditLogFileNames.BaseName(DateTime.Now);
        var baseTarget = Path.Combine(_logDirectory, baseName + ".jsonl");

        // 已记住当前活动目标：同日、文件仍存在且未满 → 续写同一文件，避免每批次重复探测/新建分卷。
        if (_activeRolloverTarget is not null
            && _activeRolloverDate == baseName
            && !FileExistsAndAtLimit(_activeRolloverTarget))
        {
            return _activeRolloverTarget;
        }

        // 基础名未满 → 续写基础名。
        if (!FileExistsAndAtLimit(baseTarget))
        {
            _activeRolloverTarget = baseTarget;
            _activeRolloverDate = baseName;
            return baseTarget;
        }

        // 基础名已满 → 复用第一个「不存在或未满」的分卷：未满续写，仅当连续分卷都已满才 seq+1 新建。
        var seq = 1;
        string shard;
        while (true)
        {
            shard = Path.Combine(_logDirectory, $"{baseName}_{seq}.jsonl");
            if (!File.Exists(shard) || !FileExistsAndAtLimit(shard))
            {
                break;
            }
            seq++;
        }

        _activeRolloverTarget = shard;
        _activeRolloverDate = baseName;
        return shard;
    }

    private static bool FileExistsAndAtLimit(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length >= MaxFileSizeBytes;
        }
        catch
        {
            // best effort：探测失败按未满处理，续写既有文件
            return false;
        }
    }

    public void CleanupExpired()
    {
        // Design(#208): 清理按文件创建时间（UTC 表示）判龄、按 audit-*.jsonl 全局枚举计数，与文件名日期无关。Design(#231): 清理双闸=判龄删除 + 31 文件计数上限（分卷计入）；retain_days<=0 视为不做年龄清理，绝不因退化配置清空审计面（对齐 BackupRetentionService.DeleteExpiredTtl 的 <=0 兜底）。
        if (_retainDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retainDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "audit-*.jsonl"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 不变量④ 降级必留痕：单文件被占用/已消失不炸穿清理链，留待下个周期重试，失败以审计事件留痕。
                        TraceDeleteFailed(file);
                    }
                }
            }
        }

        EnforceRetainedFileCountLimit();
    }

    // Design(#231): 31 文件计数上限（glob audit-*.jsonl，分卷计入）是磁盘占用保护兜底；到上限后最旧创建时间先删——即使文件仍在 retain_days 窗口内也会被淘汰，属记录在案的静默淘汰。
    private void EnforceRetainedFileCountLimit()
    {
        var files = Directory.EnumerateFiles(_logDirectory, "audit-*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.CreationTimeUtc)
            .ToList();

        while (files.Count > RetainedFileCountLimit)
        {
            var oldest = files[0];
            files.RemoveAt(0);
            try { oldest.Delete(); } catch { TraceDeleteFailed(oldest.FullName); }
        }
    }

    // 不变量④ 降级必留痕：年龄闸与计数闸共用——删除失败以审计事件留痕（Warn），留待下个清理周期重试。
    private void TraceDeleteFailed(string path)
    {
        WriteAsync(new AuditEvent(
            EventType: "audit_cleanup_delete_failed",
            Level: AuditLevel.Warn,
            TimestampUtc: DateTimeOffset.UtcNow,
            TaskId: "audit",
            SourcePath: Path.GetFileName(path),
            Message: "audit cleanup: delete failed, will retry next cycle",
            Data: null), CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // best effort drain
        }

        _cts.Cancel();
        _cts.Dispose();
    }
}
