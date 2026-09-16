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

        // 票 03（A-006 / D-006）：镜像相对路径布局后备份分散于子目录树，
        // 旁路版本（名字.时间戳.bak）与 _unsorted 兜底槽位均以 .bak 结尾，
        // 必须 AllDirectories 递归才能兑现 ADR 0006 的 size/TTL 兜底。
        var files = EnumerateBackupFiles(backupDirectory);
        DeleteExpiredTtl(files, retainDays);
        EnforceSizeLimit(files, maxSizeBytes);
        CleanupEmptyDirectories(backupDirectory);
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
            var files = EnumerateBackupFiles(backupDirectory);
            DeleteExpiredTtl(files, retainDays);
            EnforceSizeLimit(files, maxSizeBytes);
            CleanupEmptyDirectories(backupDirectory);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>递归枚举备份根下所有 .bak 备份（含镜像子目录、旁路版本、_unsorted 兜底）。</summary>
    private static List<FileInfo> EnumerateBackupFiles(string backupDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(backupDirectory, "*.bak", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();
        }
        catch
        {
            // 权限/竞态导致的枚举失败按空集处理——retention 是尽力而为的兜底，不反噬主流程。
            return new List<FileInfo>();
        }
    }

    /// <summary>
    /// 自底向上清理镜像布局产生的空目录（retention 删完文件后残留）。
    /// 不删除备份根本身；best-effort，失败不影响主流程。
    /// </summary>
    private static void CleanupEmptyDirectories(string backupDirectory)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(backupDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // 目录可能正被并发写入/枚举——跳过即可。
                }
            }
        }
        catch
        {
            // 同 EnumerateBackupFiles：兜底尽力而为。
        }
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
