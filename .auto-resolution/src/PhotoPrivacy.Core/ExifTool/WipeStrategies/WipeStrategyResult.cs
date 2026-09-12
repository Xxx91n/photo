namespace PhotoPrivacy.Core.ExifTool;

public sealed record WipeStrategyResult(
    WipeFormatFamily Family,
    string EffectiveArgs,
    bool RequiresUserWarning,
    string? SkipReason = null);
