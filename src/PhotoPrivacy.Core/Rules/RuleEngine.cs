using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Rules;

public sealed class RuleEngine
{
    private readonly AppConfig _config;

    public RuleEngine(AppConfig config)
    {
        _config = config;
    }

    public RuleDecision Decide(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var allowedExtensions = _config.Rules.AllowedExtensions ?? [];
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new RuleDecision(false, "extension_not_allowed", null, false, null);
        }

        var excludedPatterns = _config.Rules.ExcludedPatterns ?? [];
        foreach (var pattern in excludedPatterns)
        {
            if (MatchesPattern(Path.GetFileName(sourcePath), pattern))
            {
                return new RuleDecision(false, "excluded_pattern", null, false, null);
            }
        }

        var outputPath = ResolveOutputPath(sourcePath);
        var createBackup = _config.Backup.Enabled;
        var backupPath = ResolveBackupPath(sourcePath, createBackup);

        if (createBackup
            && backupPath is not null
            && string.Equals(outputPath, backupPath, StringComparison.OrdinalIgnoreCase))
        {
            return new RuleDecision(true, "eligible", outputPath, false, null);
        }

        return new RuleDecision(true, "eligible", outputPath, createBackup, backupPath);
    }

    private string ResolveOutputPath(string sourcePath)
    {
        if (!string.Equals(_config.Rules.OutputMode, "fixed_directory", StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        var relative = Path.GetRelativePath(_config.Watch.HotFolder, sourcePath);
        return Path.Combine(_config.Rules.OutputDirectory, relative);
    }

    private string? ResolveBackupPath(string sourcePath, bool createBackup)
    {
        if (!createBackup)
        {
            return null;
        }

        // ADR 0045: if hot folder is empty, cannot resolve a meaningful backup dir
        if (string.IsNullOrWhiteSpace(_config.Watch.HotFolder))
        {
            return null;
        }

        var backup = _config.Backup;
        var backupDir = string.IsNullOrWhiteSpace(backup.Directory)
            ? Pipeline.BackupPathResolver.ResolveDefaultBackupDir(_config.Watch.HotFolder)
            : backup.Directory;

        // 票 03（A-006 / D-006）：镜像相对路径布局——备份根下按源文件相对监控根的
        // 子路径镜像，结构性消除跨子目录同名文件的备份互相覆盖；无法归位监控子树时
        // 回退 "_unsorted/文件名.短哈希" 兜底。防穿越/规范化/保留名由 Resolver 收口。
        return Pipeline.BackupPathResolver.ResolveBackupSlot(
            sourcePath, _config.Watch.HotFolder, backupDir, backup.Suffix);
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.Equals(pattern, "~$*", StringComparison.Ordinal))
        {
            return fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(pattern, "*.tmp", StringComparison.Ordinal))
        {
            return fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
