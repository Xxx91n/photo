using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Pipeline;

namespace PhotoPrivacy.Core.Watcher;

public static class WatchPathFilter
{
    public const string ServiceStartedDataKey = "已自动排除的子目录列表";

   public static IReadOnlyList<string> ResolveAutoExcludedSubdirectories(AppConfig config)
   {
        if (string.IsNullOrWhiteSpace(config.Watch.HotFolder))
        {
            return Array.Empty<string>();
        }

       var hotFolder = NormalizePath(config.Watch.HotFolder);
       var candidates = new List<string?>
       {
           config.Audit.LogDirectory,
           config.Quarantine.Directory,
       };
       if (!string.IsNullOrWhiteSpace(config.Backup.Directory))
       {
           candidates.Add(config.Backup.Directory);
       }
        var defaultBackupDir = Pipeline.BackupPathResolver.ResolveDefaultBackupDir(config.Watch.HotFolder);
        candidates.Add(defaultBackupDir);
        if (config.Watch.AutoExcludedDirectories is { Length: > 0 })
        {
            candidates.AddRange(config.Watch.AutoExcludedDirectories);
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalizedCandidate = NormalizePath(candidate);
            if (!IsStrictSubdirectory(normalizedCandidate, hotFolder))
            {
                continue;
            }

            excluded.Add(normalizedCandidate);
        }

        return excluded
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool ShouldSkipPath(string path, IReadOnlyList<string> excludedDirectories)
    {
        if (excludedDirectories.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        foreach (var excludedDirectory in excludedDirectories)
        {
            if (IsSameOrSubPath(normalizedPath, excludedDirectory))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStrictSubdirectory(string candidateDirectory, string parentDirectory)
    {
        if (string.Equals(candidateDirectory, parentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(parentDirectory, candidateDirectory);
        return IsSubPathRelative(relative);
    }

    /// <summary>
    /// 票 03（A-006）：公开的组件级边界判定——备份镜像槽位归位（BackupPathResolver）与
    /// 监控排除（ShouldSkipPath）共用同一实现：GetRelativePath + ".." / 根路径检测，
    /// 禁止裸字符串 startswith（"/a/repo" vs "/a/repo-backup" 兄弟目录陷阱）。
    /// 入参须为已 NormalizePath 的绝对路径。
    /// </summary>
    public static bool TryGetSubPathRelative(string parentDirectory, string candidate, out string? relative)
    {
        var computed = Path.GetRelativePath(parentDirectory, candidate);
        if (IsSubPathRelative(computed))
        {
            relative = computed;
            return true;
        }

        relative = null;
        return false;
    }

    public static bool IsSameOrSubPath(string path, string directory)
    {
        if (string.Equals(path, directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(directory, path);
        return IsSubPathRelative(relative);
    }

    private static bool IsSubPathRelative(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(relativePath, "..", StringComparison.Ordinal))
        {
            return false;
        }

        if (relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        return !Path.IsPathRooted(relativePath);
    }

   private static string NormalizePath(string path)
   {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
       var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root)
            && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
