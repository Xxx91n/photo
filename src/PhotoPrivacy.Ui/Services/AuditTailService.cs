using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;

using PhotoPrivacy.Ui.Localization;
namespace PhotoPrivacy.Ui.Services;

public sealed class AuditTailService
{
    private readonly string _logDirectory;
    private readonly Action<IReadOnlyList<AuditLogEntry>> _onBatch;
    private readonly Func<string> _getLogLevel;
    private readonly Action<string?>? _onExifToolExePathDetected;
    private readonly Func<CancellationToken, Task<string[]?>>? _backfillFetcher;
    private readonly TimeSpan _backfillRetryInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly ConcurrentQueue<AuditLogEntry> _pendingEntries = new();
    private readonly ConcurrentQueue<AuditLogEntry> _backfillPendingEntries = new();
    private readonly ConcurrentQueue<string> _pendingExePaths = new();
    private readonly HashSet<string> _deliveredLines = new(StringComparer.Ordinal);
    private readonly Queue<string> _deliveredOrder = new();
    private readonly DispatcherTimer _flushTimer;

    // 原始行级去重记忆上限：覆盖 backfill 50 行 + FSW 窗口增量，防长期运行内存增长。
    private const int DeliveredMemoryCap = 512;

    private FileSystemWatcher? _watcher;
    private Task? _loopTask;
    private Task? _backfillTask;
    private string _currentAuditFilePath;
    private long _lastPosition;
    // B04: all reads/writes of this flag take _gate — reads via the BackfillSucceeded
    // getter, the write inside EnqueueBackfillLines' _gate section (atomic with the
    // delivery claim). No lock-free access remains, so future concurrent fetchers are safe.
    private bool _backfillSucceeded;
    // Stall detection for the tail: the held partial line from the previous poll plus the
    // file length at that time. Same length + same partial line = writer stopped appending,
    // so the partial line is complete content and must be delivered instead of starved.
    private long _stallCheckLength;
    private string? _stallCheckPartial;

    public AuditTailService(
        string logDirectory,
        Action<IReadOnlyList<AuditLogEntry>> onBatch,
        Func<string> getLogLevel,
        Action<string?>? onExifToolExePathDetected = null,
        Func<CancellationToken, Task<string[]?>>? backfillFetcher = null,
        TimeSpan? backfillRetryInterval = null)
    {
        _logDirectory = logDirectory;
        _onBatch = onBatch;
        _getLogLevel = getLogLevel;
        _onExifToolExePathDetected = onExifToolExePathDetected;
        _backfillFetcher = backfillFetcher;
        _backfillRetryInterval = backfillRetryInterval ?? TimeSpan.FromSeconds(5);
        _currentAuditFilePath = BuildAuditPath(_logDirectory, DateTime.Today);

        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, OnFlushTick);
    }

    public static string BuildAuditPath(string logDirectory, DateTime day)
    {
        return Path.Combine(logDirectory, $"audit-{day:yyyy-MM-dd}.jsonl");
    }

   public void Start()
   {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

       var auditDir = Path.GetDirectoryName(_currentAuditFilePath)!;
       Directory.CreateDirectory(auditDir);

        // ADR 0037: establish the start-of-session watermark BEFORE any watcher/loop/backfill
        // starts, so only content that existed before Start() is skipped; everything appended
        // afterwards is guaranteed to be picked up by the tail or the backfill dedup memory.
        SkipToCurrentEnd();

        _watcher = new FileSystemWatcher(auditDir, "audit-*.jsonl")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        _watcher.Changed += (_, _) => TryReadNewLines();
        _watcher.Created += (_, _) => TryReadNewLines();
        _watcher.Renamed += (_, _) => TryReadNewLines();

        _loopTask = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        if (_backfillFetcher is not null)
        {
            // ADR 0046: backfill 收口在服务内部 — 失败静默重试，绝不杀 UI。
            _backfillTask = Task.Run(() => BackfillLoopAsync(_cts.Token), _cts.Token);
        }
        _flushTimer.Start();
    }

    /// <summary>
    /// Skip to the end of the current audit file so TryReadNewLines only catches future lines.
    /// Used both on startup (don't replay history) and when the user clears logs.
    /// </summary>
    public void SkipToCurrentEnd()
    {
        lock (_gate)
        {
            if (File.Exists(_currentAuditFilePath))
            {
                using var stream = new FileStream(_currentAuditFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _lastPosition = stream.Length;
            }
            else
            {
                _lastPosition = 0;
            }
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        _flushTimer.Stop();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_backfillTask is not null)
        {
            try
            {
                await _backfillTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore shutdown races
            }
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore shutdown races
            }
        }

        FlushPending();
    }

    /// <summary>
    /// ADR 0037: user cleared the log view. Drain in-flight batches, reset the tail position to
    /// file end, but KEEP the delivered-lines dedup memory so cleared history cannot reappear
    /// via a later backfill or a tail re-read of the same raw lines.
    /// </summary>
    public void NotifyLogsCleared()
    {
        while (_pendingEntries.TryDequeue(out _))
        {
        }

        while (_backfillPendingEntries.TryDequeue(out _))
        {
        }

        SkipToCurrentEnd();
    }

    public static AuditLogEntry? ParseAuditLine(string line, string logLevel)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var eventType = root.TryGetProperty("event_type", out var eventTypeEl)
                ? eventTypeEl.GetString() ?? "unknown"
                : "unknown";

            var level = root.TryGetProperty("level", out var levelEl)
                ? levelEl.GetString() ?? "INFO"
                : "INFO";

            if (!ShouldInclude(level, logLevel))
            {
                return null;
            }

            var timestampUtc = root.TryGetProperty("timestamp_utc", out var tsEl)
                ? tsEl.GetString()
                : null;
            var localTime = DateTimeOffset.TryParse(timestampUtc, out var parsedTs)
                ? parsedTs.ToLocalTime().ToString("HH:mm:ss")
                : DateTime.Now.ToString("HH:mm:ss");

            var sourcePathMasked = root.TryGetProperty("source_path_masked", out var srcEl)
                ? srcEl.GetString() ?? string.Empty
                : string.Empty;

            var message = root.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? string.Empty
                : string.Empty;

            var (displayEvent, colorHex) = MapEventStyle(eventType);
            return new AuditLogEntry(
                TimeText: localTime,
                EventType: eventType,
                DisplayEvent: displayEvent,
                SourcePathMasked: sourcePathMasked,
                Message: message,
                ColorHex: colorHex);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldInclude(string eventLevelText, string uiLogLevel)
    {
        int eventLevel = eventLevelText.ToLowerInvariant() switch
        {
            "debug" => 0,
            "info"  => 1,
            "warn"  => 2,
            "error" => 3,
            _       => 1
        };

        int minDisplay = uiLogLevel.ToLowerInvariant() switch
        {
            "all"   => 0,
            "debug" => 0,
            "info"  => 1,
            "warn"  => 2,
            "error" => 3,
            _       => 1
        };

        return eventLevel >= minDisplay;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            RotateIfDayChanged();
            TryReadNewLines();
        }
    }

    private void RotateIfDayChanged()
    {
        var expected = BuildAuditPath(_logDirectory, DateTime.Today);
        if (string.Equals(expected, _currentAuditFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            _currentAuditFilePath = expected;
            _lastPosition = 0;
        }
    }

    private void TryReadNewLines()
    {
        string filePath;
        long startPos;

        lock (_gate)
        {
            filePath = _currentAuditFilePath;
            startPos = _lastPosition;
        }

        if (!File.Exists(filePath))
        {
            return;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (startPos > stream.Length)
        {
            startPos = 0;
        }

        // Safe watermark = offset just past the final newline (0 if none). A writer mid-
        // append leaves a trailing partial line; committing past it would permanently skip
        // the rest of that line once the write lands. The partial line is held below the
        // watermark and re-read in full on a later poll.
        var completeEnd = FindLastNewlineBoundary(stream);

        stream.Position = startPos;
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.Peek() >= 0)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }
            lines.Add(line);
        }

        // Without a trailing newline the final ReadLine result is the partial trailing
        // line: hold it back (it stays below the watermark). A held partial line is
        // delivered as soon as a later poll finds the file length unchanged — the writer
        // has stopped appending, so waiting longer would starve a complete line forever
        // (the stall-delivery path also covers files that simply end without a newline).
        var completeCount = stream.Length > completeEnd ? lines.Count - 1 : lines.Count;
        for (var i = 0; i < completeCount; i++)
        {
            BufferLine(lines[i]);
        }

        var heldPartial = stream.Length > completeEnd && lines.Count == completeCount + 1
            ? lines[^1]
            : null;
        lock (_gate)
        {
            if (heldPartial is not null && stream.Length == _stallCheckLength && string.Equals(heldPartial, _stallCheckPartial, StringComparison.Ordinal))
            {
                // Same partial line and no growth since the previous poll: the writer is
                // done. Deliver it (ParseAuditLine drops it if truly truncated) and move
                // the watermark past it so it can never be starved.
                BufferLine(heldPartial);
                completeEnd = stream.Length;
                _stallCheckLength = 0;
                _stallCheckPartial = null;
            }
            else if (heldPartial is not null)
            {
                _stallCheckLength = stream.Length;
                _stallCheckPartial = heldPartial;
            }
            else
            {
                _stallCheckLength = 0;
                _stallCheckPartial = null;
            }

            _lastPosition = Math.Max(_lastPosition, completeEnd);
        }
    }
    /// <summary>Offset just past the final '\n' in the stream (0 if the file has none).</summary>
    private static long FindLastNewlineBoundary(FileStream stream)
    {
        var length = stream.Length;
        if (length == 0)
        {
            return 0;
        }

        var buffer = new byte[1024];
        var scanFrom = length;
        while (scanFrom > 0)
        {
            var chunk = (int)Math.Min(buffer.Length, scanFrom);
            scanFrom -= chunk;
            lock (stream)
            {
                stream.Position = scanFrom;
                var read = 0;
                while (read < chunk)
                {
                    var n = stream.Read(buffer, read, chunk - read);
                    if (n == 0)
                    {
                        break;
                    }
                    read += n;
                }

                for (var i = read - 1; i >= 0; i--)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        return scanFrom + i + 1;
                    }
                }
            }
        }

        return 0;
    }

    /// <summary>Reads <c>_backfillSucceeded</c> under _gate (B04): the flag is only
    /// meaningful in relation to _gate-guarded delivery, so observe it under the same lock.</summary>
    private bool BackfillSucceeded
    {
        get
        {
            lock (_gate)
            {
                return _backfillSucceeded;
            }
        }
    }

    private async Task BackfillLoopAsync(CancellationToken token)
    {
        // ADR 0046: fetch once when the worker becomes reachable; failures stay silent.
        // `_backfillSucceeded` is set atomically under _gate together with the backfill
        // delivery claim so the tail can never re-deliver lines the backfill already
        // showed; the loop reads it through BackfillSucceeded (also under _gate, B04).
        while (!token.IsCancellationRequested && !BackfillSucceeded)
        {
            string[]? lines = null;
            try
            {
                lines = await _backfillFetcher(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // ADR 0035: worker unreachable (IOException/SocketException) — silent retry.
            }

            if (lines is not null)
            {
                // Fetcher contract: non-null = worker reachable (empty array allowed);
                // null = unreachable/failed — retry silently. EnqueueBackfillLines sets
                // `_backfillSucceeded` under _gate on success (B04), so no unlocked write here.
                EnqueueBackfillLines(lines);
            }

            if (!BackfillSucceeded && !token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_backfillRetryInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void EnqueueBackfillLines(string[] lines)
    {
        var merged = MergeBackfillLines(lines);
        // ADR 0035: resolve the UI log level OUTSIDE _gate — the getter may marshal to the
        // UI thread, and the UI thread can block on _gate (SkipToCurrentEnd/TryReadNewLines).
        var logLevel = _getLogLevel();
        lock (_gate)
        {
            foreach (var line in merged)
            {
                // Lines already delivered by the tail are dropped here; the remainder is
                // claimed atomically against any concurrent tail read (same _gate section).
                // No position watermark is advanced here: the tail owns everything from its
                // own start-of-session watermark, so a line written between the worker's
                // snapshot and this claim is still read by the tail instead of being lost.
                if (_deliveredLines.Contains(line))
                {
                    continue;
                }

                var entry = ParseAuditLine(line, logLevel);
                if (entry is null)
                {
                    continue;
                }

                ClaimLine(line);
                _backfillPendingEntries.Enqueue(entry);
            }

            // B04: success flag under the same _gate section as the delivery claim — set
            // only after every fetched line is recorded (dedup memory), so a future
            // concurrent fetcher can never observe "succeeded" mid-delivery.
            _backfillSucceeded = true;
        }
    }

    /// <summary>Caller must hold _gate. Records a raw line as delivered, evicting the oldest
    /// claim once the memory cap is reached so long sessions cannot grow without bound.</summary>
    private void ClaimLine(string line)
    {
        _deliveredOrder.Enqueue(line);
        _deliveredLines.Add(line);
        while (_deliveredOrder.Count > DeliveredMemoryCap)
        {
            var oldest = _deliveredOrder.Dequeue();
            _deliveredLines.Remove(oldest);
        }
    }

    /// <summary>
    /// Merge raw backfill lines: drop blanks, dedupe exact raw lines, stable-sort by
    /// timestamp_utc ascending; lines without a parseable timestamp keep input order at the end.
    /// </summary>
    public static string[] MergeBackfillLines(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<(string Line, DateTimeOffset? Ts)>();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw))
            {
                continue;
            }

            DateTimeOffset? ts = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("timestamp_utc", out var tsEl) &&
                    tsEl.GetString() is { } tsText &&
                    DateTimeOffset.TryParse(tsText, out var parsed))
                {
                    ts = parsed;
                }
            }
            catch
            {
                // not JSON — keep with no timestamp
            }

            unique.Add((raw, ts));
        }

        return unique
            .Select((item, index) => (item, index))
            .OrderBy(tuple => tuple.item.Ts.HasValue ? 0 : 1)
            .ThenBy(tuple => tuple.item.Ts ?? DateTimeOffset.MaxValue)
            .ThenBy(tuple => tuple.index)
            .Select(tuple => tuple.item.Line)
            .ToArray();
    }

    private void BufferLine(string line)
    {
        var exePath = TryExtractExifToolExePath(line);
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            _pendingExePaths.Enqueue(exePath);
        }

        lock (_gate)
        {
            if (_deliveredLines.Contains(line))
            {
                return;
            }
            ClaimLine(line);
        }

        var entry = ParseAuditLine(line, _getLogLevel());
        if (entry is not null)
        {
            _pendingEntries.Enqueue(entry);
        }
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        FlushPending();
    }

    private void FlushPending()
    {
        while (_pendingExePaths.TryDequeue(out var exePath))
        {
            _onExifToolExePathDetected?.Invoke(exePath);
        }

        FlushQueue(_pendingEntries);
        FlushQueue(_backfillPendingEntries);
    }

    private void FlushQueue(ConcurrentQueue<AuditLogEntry> queue)
    {
        if (queue.IsEmpty)
        {
            return;
        }

        var batch = new List<AuditLogEntry>();
        while (queue.TryDequeue(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count > 0)
        {
            _onBatch(batch);
        }
    }

    public static string? TryExtractExifToolExePath(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("event_type", out var eventTypeEl)
                ? eventTypeEl.GetString() ?? "unknown"
                : "unknown";

            if (!string.Equals(eventType, "exiftool_started", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!dataEl.TryGetProperty("exe_path", out var exePathEl) || exePathEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return exePathEl.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static (string Display, string ColorHex) MapEventStyle(string eventType)
    {
        return eventType switch
        {
            "exiftool_started" => ($"🟢 {LocalizationService.Instance.Get("audit.exiftool_started")}", "#2E7D32"),
            "service_started" => ($"🟢 {LocalizationService.Instance.Get("audit.service_started")}", "#2E7D32"),
            "file_processing_succeeded" => ($"✅ {LocalizationService.Instance.Get("audit.file_cleaned")}", "#D8DEE9"),
            "file_skipped" => ($"⏭ {LocalizationService.Instance.Get("audit.file_skipped")}", "#9E9E9E"),
            "file_detected" => ($"🔍 {LocalizationService.Instance.Get("audit.file_detected")}", "#90A4AE"),
            "instance_conflict" => ($"⚠️ {LocalizationService.Instance.Get("audit.instance_conflict")}", "#F57C00"),
            "file_processing_failed" => ($"❌ {LocalizationService.Instance.Get("audit.file_failed")}", "#C62828"),
            _ => (eventType, "#D8DEE9")
        };
    }
}
