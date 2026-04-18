using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Watcher;

public static class WatchPathFilter
{
    public const string ServiceStartedDataKey = "已自动排除的子目录列表";

    public static IReadOnlyList<string> ResolveAutoExcludedSubdirectories(AppConfig config)
    {
        var hotFolder = NormalizePath(config.Watch.HotFolder);
        var candidates = new[]
        {
            config.Audit.LogDirectory,
            config.Quarantine.Directory
        };

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

    private static bool IsSameOrSubPath(string path, string directory)
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
