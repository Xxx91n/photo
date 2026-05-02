using System.Collections.Concurrent;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class InMemoryProcessedRecordStore : IProcessedRecordStore
{
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.OrdinalIgnoreCase);

    public bool IsProcessed(string key)
    {
        return _processed.ContainsKey(key);
    }

    public void MarkProcessed(string key)
    {
        _processed.TryAdd(key, 0);
    }
}
