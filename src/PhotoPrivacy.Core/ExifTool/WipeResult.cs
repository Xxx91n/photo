namespace PhotoPrivacy.Core.ExifTool;

public enum WipeResult
{
    None,
    Skipped_NoMetadata,
    Skipped_NoClearable,

    /// <summary>
    /// 扩展名不在 wipe 格式族映射面（ExtensionFamilyMap）。
    /// 立即跳过：不进 ExifTool、不写协议块、不等 marker，由管道落 wipe_skipped_unknown 审计事件。
    /// </summary>
    UnknownFormat,

    Cleaned_NoOp,
    Cleaned_Modified,

    /// <summary>
    /// ExifTool 明确说写不了：「0 image files updated」+「files weren't updated due to errors」
    /// （stderr 报写错误，文件一字未动；分片 MP4 即此形态，#268 实测）。管道按失败收场，
    /// 绝不许结成 Cleaned_Modified（#288）。
    /// </summary>
    Failed_WriteError
}
