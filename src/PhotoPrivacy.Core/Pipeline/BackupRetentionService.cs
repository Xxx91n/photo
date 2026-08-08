namespace PhotoPrivacy.Core.Pipeline;

public static class BackupRetentionService
{
    public static void EnforceMaxSize(string backupDirectory, long maxSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            return;

        var files = Directory.EnumerateFiles(backupDirectory, "*" + ".bak", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.CreationTimeUtc)
            .ToList();

        var totalSize = files.Sum(f => f.Length);
        var removed = 0;

        while (totalSize > maxSizeBytes && files.Count > 0)
        {
            var oldest = files[0];
            files.RemoveAt(0);
            try
            {
                totalSize -= oldest.Length;
                oldest.Delete();
                removed++;
            }
            catch
            {
                // best effort
            }
        }
    }
}
