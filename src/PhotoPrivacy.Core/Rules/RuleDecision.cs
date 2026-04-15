namespace PhotoPrivacy.Core.Rules;

public sealed record RuleDecision(
    bool ShouldProcess,
    string Reason,
    string? OutputPath,
    bool CreateBackup,
    string? BackupPath);
