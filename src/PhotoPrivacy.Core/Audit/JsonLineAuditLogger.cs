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
        Directory.CreateDirectory(_logDirectory);

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
        var baseName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}";
        var target = Path.Combine(_logDirectory, baseName + ".jsonl");

        if (File.Exists(target))
        {
            try
            {
                var info = new FileInfo(target);
                if (info.Length >= MaxFileSizeBytes)
                {
                    var seq = 1;
                    while (File.Exists(Path.Combine(_logDirectory, $"{baseName}_{seq}.jsonl")))
                        seq++;
                    target = Path.Combine(_logDirectory, $"{baseName}_{seq}.jsonl");
                }
            }
            catch
            {
                // best effort check
            }
        }

        return target;
    }

    public void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retainDays);
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "audit-*.jsonl"))
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }

        EnforceRetainedFileCountLimit();
    }

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
            try { oldest.Delete(); } catch { }
        }
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
