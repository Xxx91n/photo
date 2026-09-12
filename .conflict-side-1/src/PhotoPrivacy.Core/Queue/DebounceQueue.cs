namespace PhotoPrivacy.Core.Queue;

public sealed class DebounceQueue
{
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public DebounceQueue(TimeSpan window, Func<DateTimeOffset> now)
    {
        _window = window;
        _now = now;
    }

    public void Enqueue(string path)
    {
        lock (_gate)
        {
            _events[path] = _now();
        }
    }

    public IReadOnlyList<string> PopReady()
    {
        var now = _now();
        var ready = new List<string>();

        lock (_gate)
        {
            foreach (var kv in _events.ToArray())
            {
                if (now - kv.Value >= _window)
                {
                    ready.Add(kv.Key);
                    _events.Remove(kv.Key);
                }
            }
        }

        return ready;
    }
}
