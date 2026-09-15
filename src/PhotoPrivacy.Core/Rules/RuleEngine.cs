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

        var fileName = Path.GetFileName(sourcePath);
        var backupFileName = fileName + backup.Suffix;

        return Path.Combine(backupDir, backupFileName);
    }

    /// <summary>
    /// 票 02（A-003）：真通配匹配——<c>*</c> 匹配任意长度（含空）序列，<c>?</c> 匹配恰好一个字符，
    /// 其余字符按字面量匹配；整串锚定（非子串包含），大小写口径与收敛前两个字面量分支一致
    /// （OrdinalIgnoreCase）。匹配对象是文件名（<see cref="Path.GetFileName(string)"/>），
    /// 故模式里的路径分隔符永远匹配不到——该情形由 <c>AppConfigValidator.CollectWarnings</c>
    /// 以告警形式暴露（不识别≠静默）。
    /// 实现用两指针 + 星号回退法：O(n·m) 上界、无正则、无回溯爆炸面（模式来自用户配置）。
    /// </summary>
    private static bool MatchesPattern(string fileName, string pattern)
    {
        var nameIndex = 0;
        var patternIndex = 0;
        var starPatternIndex = -1;
        var starNameIndex = 0;

        while (nameIndex < fileName.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || EqualsIgnoringCase(pattern[patternIndex], fileName[nameIndex])))
            {
                nameIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPatternIndex = patternIndex;
                starNameIndex = nameIndex;
                patternIndex++;
                continue;
            }

            if (starPatternIndex >= 0)
            {
                patternIndex = starPatternIndex + 1;
                starNameIndex++;
                nameIndex = starNameIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool EqualsIgnoringCase(char left, char right)
        => char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
