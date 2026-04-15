namespace PhotoPrivacy.Core.Queue;

public sealed class InFlightRegistry
{
    private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool TryEnter(string path)
    {
        lock (_gate)
        {
            return _set.Add(path);
        }
    }

    public void Exit(string path)
    {
        lock (_gate)
        {
            _set.Remove(path);
        }
    }
}
