namespace PhotoPrivacy.Core.Pipeline;

/// <summary>
/// Single source of truth for default backup directory resolution.
/// Both RuleEngine.ResolveBackupPath and MetadataCleanerWorker call this to avoid drift
/// (previous bug: RuleEngine used "bak", Worker used "_backup" → retention never matched actual backups).
/// </summary>
public static class BackupPathResolver
{
    public const string DefaultBackupDirName = "bak";

    public static string ResolveDefaultBackupDir(string hotFolder) =>
        Path.Combine(hotFolder, DefaultBackupDirName);
}
