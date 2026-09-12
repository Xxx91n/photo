namespace PhotoPrivacy.Ui;

public sealed class ExifToolVersionSnapshot
{
    private readonly Func<CancellationToken, Task<string>> _readVersionAsync;
    private string _lastValue = "";

    public ExifToolVersionSnapshot(Func<CancellationToken, Task<string>> readVersionAsync)
    {
        _readVersionAsync = readVersionAsync;
    }

    public async Task<string?> TryReadChangedAsync(CancellationToken token)
    {
        string current;
        try
        {
            current = Normalize(await _readVersionAsync(token).ConfigureAwait(false));
        }
        catch
        {
            current = "unknown";
        }

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
