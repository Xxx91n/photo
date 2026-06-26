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
    private bool _disposed;

    public FileProcessedRecordStore(string directory, TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromDays(7);
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "processed.ndjson");

        var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        try
        {
            _writer = new StreamWriter(stream);
        }
        catch
        {
            // 如果 StreamWriter 创建失败，确保 FileStream 被释放
            stream.Dispose();
            throw;
        }

        try
        {
            RecoverFromLog();
        }
        catch
        {
            // 如果恢复失败，清理资源并重新抛出
            _writer.Dispose();
            throw;
        }

        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public bool IsProcessed(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        if (_disposed) return;
        _disposed = true;

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
                catch
                {
                    // 跳过格式错误的行
                }
            }
        }
        catch
        {
            // 最佳努力恢复，不阻塞初始化
        }
    }
}
