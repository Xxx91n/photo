namespace PhotoPrivacy.Core.ExifTool;

public enum WipeResult
{
    None,
    Skipped_NoMetadata,
    Skipped_NoClearable,

    /// <summary>
    /// 票 01 / ADR 0053 M6f：扩展名不在 wipe 格式族映射面（ExtensionFamilyMap）。
    /// 立即跳过：不进 ExifTool、不写协议块、不等 marker，由管道落 wipe_skipped_unknown 审计事件。
    /// </summary>
    UnknownFormat,

    Cleaned_NoOp,
    Cleaned_Modified
}
