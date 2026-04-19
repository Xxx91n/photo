namespace PhotoPrivacy.Ui;

public sealed class ExifToolVersionSnapshot
{
    private readonly Func<string> _readVersion;
    private string _lastValue = "unknown";

    public ExifToolVersionSnapshot(Func<string> readVersion)
    {
        _readVersion = readVersion;
    }

    public string ReadInitial()
    {
        _lastValue = Normalize(_readVersion());
        return _lastValue;
    }

    public string? TryReadChanged()
    {
        var current = Normalize(_readVersion());
        if (string.Equals(current, _lastValue, StringComparison.Ordinal))
        {
            return null;
        }

        _lastValue = current;
        return current;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim();
    }
}
