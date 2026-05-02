namespace PhotoPrivacy.Core.ExifTool;

public enum WipeResult
{
    None,
    Skipped_NoMetadata,
    Skipped_NoClearable,
    Cleaned_NoBackup,
    Cleaned_WithBackup
}
