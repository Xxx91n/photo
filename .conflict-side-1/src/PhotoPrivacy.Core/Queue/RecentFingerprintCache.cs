namespace PhotoPrivacy.Core.Queue;

public sealed class RecentFingerprintCache
{
    // ponytail: hard cap 10000 entries. LRU via SeenAt. No new dependency — Dictionary + lock.
    // Upgrade path: if 10000 hot-files becomes common, use a linked-hash-map for O(1) eviction.
    public const int DefaultCapacity = 10000;
    private const int MaxCapacity = DefaultCapacity;

    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (FileFingerprint Fingerprint, DateTimeOffset SeenAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public RecentFingerprintCache(Func<DateTimeOffset> now)
    {
        _now = now;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _cache.Count;
            }
        }
    }

    public void Remember(string path, FileFingerprint fingerprint)
    {
        lock (_gate)
        {
            if (_cache.Count >= MaxCapacity && !_cache.ContainsKey(path))
            {
                EvictOldest();
            }
            _cache[path] = (fingerprint, _now());
        }
    }

    public bool ShouldSuppress(string path, FileFingerprint fingerprint, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(path, out var entry))
            {
                return false;
            }

            if (_now() - entry.SeenAt > ttl)
            {
                _cache.Remove(path);
                return false;
            }

            return entry.Fingerprint == fingerprint;
        }
    }

    /// <summary>
    /// Removes entries older than <paramref name="ttl"/>. Called periodically by worker cleanup timer.
    /// </summary>
    public void CleanupExpired(TimeSpan ttl)
    {
        lock (_gate)
        {
            var now = _now();
            var stale = _cache
                .Where(kvp => now - kvp.Value.SeenAt > ttl)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in stale)
            {
                _cache.Remove(key);
            }
        }
    }

    /// <summary>
    /// Removes oldest entries beyond <paramref name="max"/>. For emergencies or explicit size control.
    /// </summary>
    public void EvictExcess(int max)
    {
        lock (_gate)
        {
            while (_cache.Count > max)
            {
                EvictOldest();
            }
        }
    }

    private void EvictOldest()
    {
        if (_cache.Count == 0)
        {
            return;
        }
        // ponytail: O(n) scan for oldest. Acceptable at 10k cap & periodic cadence.
        // Upgrade: linked-hash-map for O(1) if hot-folder file count grows.
        var oldest = _cache.OrderBy(kvp => kvp.Value.SeenAt).First();
        _cache.Remove(oldest.Key);
    }
}
