namespace PhotoPrivacy.Core.ExifTool;

public enum WipeResult
{
    None,
    Skipped_NoMetadata,
    Skipped_NoClearable,
    Cleaned_NoOp,
    Cleaned_Modified
}
