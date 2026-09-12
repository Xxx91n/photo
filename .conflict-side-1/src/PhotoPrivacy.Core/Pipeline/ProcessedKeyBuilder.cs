namespace PhotoPrivacy.Core.Pipeline;

public static class ProcessedKeyBuilder
{
    public static string Build(string path, long fileLength, DateTime lastWriteTimeUtc)
    {
        var normalized = Path.GetFullPath(path);
        return $"{normalized}|{fileLength}|{lastWriteTimeUtc.Ticks}";
    }
}
