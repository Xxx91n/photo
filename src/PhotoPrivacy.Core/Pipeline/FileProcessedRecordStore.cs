using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class FileProcessedRecordStore : IProcessedRecordStore, IDisposable
{
    /// <summary>记录格式版本：2 起 key 为纯路径；无此标记的记录按历史指纹格式恢复。</summary>
    private const int CurrentKeyFormatVersion = 2;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _cache = new(ProcessedKeyBuilder.KeyComparer);
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

        var entry = new { key, ts = DateTimeOffset.UtcNow.ToString("O"), v = CurrentKeyFormatVersion };
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
                        var entry = new { key = kv.Key, ts = kv.Value.ToString("O"), v = CurrentKeyFormatVersion };
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
                        // #133 路线 2：key 口径收敛为路径粗粒度。旧记录是「路径|长度|mtimeTicks」指纹格式，
                        // 归一化为纯路径再入缓存，避免升级首轮整目录补扫把已清洗文件重复处理一遍。
                        // 同一路径可能有多条记录（重复处理后追加），保留最新时间戳，防止较早记录把仍在
                        // TTL 内的路径键提前过期（#151 审查）。
                        CacheRecovered(NormalizeLegacyKey(key, root), ts);
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

    /// <summary>
    /// 把无版本标记的历史指纹 key（路径|长度|mtimeTicks）归一化为纯路径。
    /// 带 v 标记的新记录 key 本身就是路径，原样返回，不再从尾部推断格式。
    /// </summary>
    private static string NormalizeLegacyKey(string key, JsonElement root)
    {
        // 无标记记录来自 #133 之前：正常形态一定带「|数字|数字」指纹，剥离后即路径。
        // 无标记且无指纹后缀的路径键按原样保留（升级前的中间态/异常记录）；
        // 无标记路径恰好以 |数字|数字 结尾时无法与历史指纹区分，按历史格式处理。
        // 新记录一律带 v 标记，不再进入这条歧义路径（POSIX 文件名允许 |）。
        if (root.TryGetProperty("v", out _))
        {
            return key;
        }

        var match = Regex.Match(key, @"\|[0-9]+\|[0-9]+$");
        return match.Success ? key[..match.Index] : key;
    }

    /// <summary>同一路径的多条恢复记录只保留最新时间戳，避免旧记录让路径键提前过期。</summary>
    private void CacheRecovered(string key, DateTimeOffset ts)
    {
        _cache.AddOrUpdate(key, ts, (_, existing) => ts > existing ? ts : existing);
    }
}
