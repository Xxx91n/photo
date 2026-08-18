namespace PhotoPrivacy.Core.ExifTool;

public interface IWipeStrategy
{
    WipeFormatFamily Family { get; }
    bool RequiresUserWarning { get; }
    string BuildEffectiveArgs();
}
