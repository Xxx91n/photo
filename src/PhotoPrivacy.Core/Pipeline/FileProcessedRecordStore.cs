using System.Collections.Concurrent;
using System.Text.Json;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class FileProcessedRecordStore : IProcessedRecordStore, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly TimeSpan _ttl;
    private readonly StreamWriter _writer;
    private readonly Timer? _cleanupTimer;

    public FileProcessedRecordStore(string directory, TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromDays(7);
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "processed.ndjson");

        var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream);

        RecoverFromLog();

        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public bool IsProcessed(string key)
    {
        if (!_cache.TryGetValue(key, out var ts))
            return false;

        if (DateTimeOffset.UtcNow - ts > _ttl)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    public void MarkProcessed(string key)
    {
        _cache[key] = DateTimeOffset.UtcNow;
        var entry = new { key, ts = DateTimeOffset.UtcNow.ToString("O") };
        var json = JsonSerializer.Serialize(entry);
        lock (_writer)
        {
            _writer.WriteLine(json);
            _writer.Flush();
        }
    }

    public int Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - _ttl;
        var removed = 0;
        foreach (var kv in _cache)
        {
            if (kv.Value < cutoff && _cache.TryRemove(kv.Key, out _))
                removed++;
        }
        return removed;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        lock (_writer)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }

    private void RecoverFromLog()
    {
        try
        {
            if (!File.Exists(_filePath)) return;

            using var reader = new StreamReader(new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var cutoff = DateTimeOffset.UtcNow - _ttl;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var key = root.GetProperty("key").GetString();
                    var tsStr = root.GetProperty("ts").GetString();
                    if (key is null || tsStr is null) continue;

                    if (DateTimeOffset.TryParse(tsStr, out var ts) && ts >= cutoff)
                    {
                        _cache.TryAdd(key, ts);
                    }
                }
                catch { /* skip malformed lines */ }
            }
        }
        catch { /* best-effort recovery */ }
    }
}
