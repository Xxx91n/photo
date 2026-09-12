using System.Collections.Concurrent;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class InMemoryProcessedRecordStore : IProcessedRecordStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processed = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;

    public InMemoryProcessedRecordStore(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromDays(7);
    }

    public bool IsProcessed(string key)
    {
        if (!_processed.TryGetValue(key, out var ts))
            return false;

        if (DateTimeOffset.UtcNow - ts > _ttl)
        {
            _processed.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    public void MarkProcessed(string key)
    {
        _processed[key] = DateTimeOffset.UtcNow;
    }

    public int Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - _ttl;
        var removed = 0;
        foreach (var kv in _processed)
        {
            if (kv.Value < cutoff && _processed.TryRemove(kv.Key, out _))
                removed++;
        }
        return removed;
    }
}
