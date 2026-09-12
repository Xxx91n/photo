namespace PhotoPrivacy.Core.Pipeline;

public static class BackupRetentionService
{
    /// <summary>
    /// Synchronous retention (kept for test compat). Prefers EnforceMaxSizeAsync in hot paths.
    /// Single-pass O(n): TTL first, then size cap.
    /// </summary>
    public static void EnforceMaxSize(string backupDirectory, long maxSizeBytes, int retainDays = 0)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            return;

        var files = Directory.EnumerateFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .ToList();

        DeleteExpiredTtl(files, retainDays);

        EnforceSizeLimit(files, maxSizeBytes);
    }

    /// <summary>
    /// Async retention (hot path). Does not block thread pool.
    /// </summary>
    public static async Task EnforceMaxSizeAsync(
        string backupDirectory, long maxSizeBytes, int retainDays,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            return;

        await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .ToList();

            DeleteExpiredTtl(files, retainDays);

            EnforceSizeLimit(files, maxSizeBytes);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteExpiredTtl(List<FileInfo> files, int retainDays)
    {
        if (retainDays <= 0) return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retainDays);
        for (var i = files.Count - 1; i >= 0; i--)
        {
            if (files[i].CreationTimeUtc < cutoff.UtcDateTime)
            {
                TryDelete(files[i]);
                files.RemoveAt(i);
            }
        }
    }

    private static void EnforceSizeLimit(List<FileInfo> files, long maxSizeBytes)
    {
        var remaining = files.OrderBy(f => f.CreationTimeUtc).ToList();
        var totalSize = remaining.Sum(f => f.Length);

        while (totalSize > maxSizeBytes && remaining.Count > 0)
        {
            var oldest = remaining[0];
            remaining.RemoveAt(0);
            totalSize -= oldest.Length;
            TryDelete(oldest);
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // best effort; file may be locked or already deleted
        }
    }
}
