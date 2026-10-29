using System.Collections.Concurrent;
using System.Text.Json;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class FileProcessedRecordStore : IProcessedRecordStore, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly TimeSpan _ttl;
    private StreamWriter _writer;
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    /// <summary>压缩后无法恢复可写句柄时置位：此后只在内存去重，不再写盘（#85 审查）。</summary>
    private bool _persistenceFailed;

   public FileProcessedRecordStore(string directory, TimeSpan? ttl = null)
   {
       _ttl = ttl ?? TimeSpan.FromDays(7);
        // ponytail: empty directory (config.sample.json defaults to "") would throw
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(AppContext.BaseDirectory, "_audit");
        }
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

        // 定时回调不得把异常抛回线程池：Dispose 不等待已排队的回调（#85 重载会提高触发频率），
        // 回调内 Cleanup 在已释放实例上抛 ObjectDisposedException 会终结进程。
        _cleanupTimer = new Timer(
            _ =>
            {
                try
                {
                    Cleanup();
                }
                catch
                {
                    // best-effort：清理失败不影响写入与重载路径。
                }
            },
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
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

        // 压缩失败后已显式标记不可写：只在内存里去重，不再写盘（避免写进已释放的 writer）。
        if (_persistenceFailed)
        {
            return;
        }

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
        // 定时回调路径：实例已释放时静默返回，不抛 ObjectDisposedException（旧形态会终结进程，#85）。
        if (_disposed)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - _ttl;
        var removed = 0;
        foreach (var kv in _cache)
        {
            if (kv.Value < cutoff && _cache.TryRemove(kv.Key, out _))
                removed++;
        }

        Compact();
        return removed;
    }

    private void Compact()
    {
        lock (_writer)
        {
            _writer.Flush();
            _writer.Dispose();

            var tmpPath = _filePath + ".compact.tmp";
            try
            {
                using (var compactWriter = new StreamWriter(new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None)))
                {
                    foreach (var kv in _cache)
                    {
                        var entry = new { key = kv.Key, ts = kv.Value.ToString("O") };
                        var json = JsonSerializer.Serialize(entry);
                        compactWriter.WriteLine(json);
                    }
                    compactWriter.Flush();
                }

                File.Move(tmpPath, _filePath, overwrite: true);
            }
            finally
            {
                // 无论压缩成败都要恢复可写句柄，否则后续 MarkProcessed 会写进已释放的 writer；
                // 恢复失败则显式标记为不可写（只降级为「内存去重」），不把异常留给下次写入。
                TryDeleteQuietly(tmpPath);
                TryReopen();
            }
        }
    }

    private void TryReopen()
    {
        try
        {
            var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream);
            _persistenceFailed = false;
        }
        catch
        {
            _persistenceFailed = true;
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort：残留的 .compact.tmp 不影响主文件。
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        lock (_writer)
        {
            if (!_persistenceFailed)
            {
                _writer.Flush();
            }

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
