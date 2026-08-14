using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;

using PhotoPrivacy.Ui.Localization;
namespace PhotoPrivacy.Ui;

public sealed class AuditTailService
{
    private readonly string _logDirectory;
    private readonly Action<IReadOnlyList<AuditLogEntry>> _onBatch;
    private readonly Func<string> _getLogLevel;
    private readonly Action<string?>? _onExifToolExePathDetected;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly ConcurrentQueue<AuditLogEntry> _pendingEntries = new();
    private readonly ConcurrentQueue<string> _pendingExePaths = new();
    private readonly DispatcherTimer _flushTimer;

    private FileSystemWatcher? _watcher;
    private Task? _loopTask;
    private string _currentAuditFilePath;
    private long _lastPosition;

    public AuditTailService(
        string logDirectory,
        Action<IReadOnlyList<AuditLogEntry>> onBatch,
        Func<string> getLogLevel,
        Action<string?>? onExifToolExePathDetected = null)
    {
        _logDirectory = logDirectory;
        _onBatch = onBatch;
        _getLogLevel = getLogLevel;
        _onExifToolExePathDetected = onExifToolExePathDetected;
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
        _flushTimer.Start();

        // ADR 0037: start at file end — only show NEW events from this session, not history.
        SkipToCurrentEnd();
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

        stream.Position = startPos;
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            BufferLine(line);
        }

        lock (_gate)
        {
            _lastPosition = stream.Position;
        }
    }

    private void BufferLine(string line)
    {
        var exePath = TryExtractExifToolExePath(line);
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            _pendingExePaths.Enqueue(exePath);
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

        if (_pendingEntries.IsEmpty)
        {
            return;
        }

        var batch = new List<AuditLogEntry>();
        while (_pendingEntries.TryDequeue(out var entry))
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
