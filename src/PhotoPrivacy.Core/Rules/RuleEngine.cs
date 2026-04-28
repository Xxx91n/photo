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
        var backupPath = ResolveBackupPath(sourcePath, createBackup, _config.Backup);

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

    private static string? ResolveBackupPath(string sourcePath, bool createBackup, BackupOptions backup)
    {
        if (!createBackup)
        {
            return null;
        }

        var backupDir = string.IsNullOrWhiteSpace(backup.Directory)
            ? string.Empty
            : backup.Directory;

        var fileName = Path.GetFileName(sourcePath);
        var backupFileName = fileName + backup.Suffix;

        if (string.IsNullOrWhiteSpace(backupDir))
        {
            return Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".", backupFileName);
        }

        return Path.Combine(backupDir, backupFileName);
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
