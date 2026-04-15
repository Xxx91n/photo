namespace PhotoPrivacy.Core.Queue;

public sealed class RecentFingerprintCache
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (FileFingerprint Fingerprint, DateTimeOffset SeenAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public RecentFingerprintCache(Func<DateTimeOffset> now)
    {
        _now = now;
    }

    public void Remember(string path, FileFingerprint fingerprint)
    {
        lock (_gate)
        {
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
}
