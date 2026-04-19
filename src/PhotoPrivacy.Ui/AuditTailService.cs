using System.Text.Json;

namespace PhotoPrivacy.Ui;

public sealed class AuditTailService
{
    private readonly string _hotFolder;
    private readonly Action<AuditLogEntry> _onEntry;
    private readonly Func<bool> _includeDetailedEvents;
    private readonly Action<string?> _onExifToolExePathDetected;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private Task? _loopTask;
    private string _currentAuditFilePath;
    private long _lastPosition;

    public AuditTailService(
        string hotFolder,
        Action<AuditLogEntry> onEntry,
        Func<bool> includeDetailedEvents,
        Action<string?> onExifToolExePathDetected)
    {
        _hotFolder = hotFolder;
        _onEntry = onEntry;
        _includeDetailedEvents = includeDetailedEvents;
        _onExifToolExePathDetected = onExifToolExePathDetected;
        _currentAuditFilePath = BuildAuditPath(_hotFolder, DateTime.Today);
    }

    public static string BuildAuditPath(string hotFolder, DateTime day)
    {
        var dir = Path.Combine(hotFolder, "_audit");
        return Path.Combine(dir, $"audit-{day:yyyy-MM-dd}.jsonl");
    }

    public void Start()
    {
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
        TryReadNewLines();
    }

    public async Task StopAsync()
    {
        _cts.Cancel();

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
    }

    public static AuditLogEntry? ParseAuditLine(string line, bool includeDetailedEvents)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        var eventType = root.TryGetProperty("event_type", out var eventTypeEl)
            ? eventTypeEl.GetString() ?? "unknown"
            : "unknown";

        if (!includeDetailedEvents && string.Equals(eventType, "file_detected", StringComparison.OrdinalIgnoreCase))
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
        var expected = BuildAuditPath(_hotFolder, DateTime.Today);
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

            EmitIfAny(line);
        }

        lock (_gate)
        {
            _lastPosition = stream.Position;
        }
    }

    private void EmitIfAny(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("event_type", out var eventTypeEl)
                ? eventTypeEl.GetString() ?? "unknown"
                : "unknown";

            if (string.Equals(eventType, "exiftool_started", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("data", out var dataEl)
                && dataEl.ValueKind == JsonValueKind.Object
                && dataEl.TryGetProperty("exe_path", out var exePathEl)
                && exePathEl.ValueKind == JsonValueKind.String)
            {
                _onExifToolExePathDetected(exePathEl.GetString());
            }
        }
        catch
        {
            // ignore parse exception for detector path
        }

        var entry = ParseAuditLine(line, _includeDetailedEvents());
        if (entry is not null)
        {
            _onEntry(entry);
        }
    }

    private static (string Display, string ColorHex) MapEventStyle(string eventType)
    {
        return eventType switch
        {
            "exiftool_started" => ("🟢 ExifTool 启动", "#2E7D32"),
            "service_started" => ("🟢 服务启动", "#2E7D32"),
            "file_processing_succeeded" => ("✅ 清理完成", "#D8DEE9"),
            "file_skipped" => ("⏭ 已跳过", "#9E9E9E"),
            "file_detected" => ("🔍 检测到文件", "#90A4AE"),
            "instance_conflict" => ("⚠️ 重复启动被拒", "#F57C00"),
            "file_processing_failed" => ("❌ 清理失败", "#C62828"),
            _ => (eventType, "#D8DEE9")
        };
    }
}
