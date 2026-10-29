namespace PhotoPrivacy.Core.ExifTool;

/// <summary>
/// exifToolVersion 上报值的哨兵文本（#86）。
///
/// 语义：`unknown` = 「版本尚未解析出来」——桥未启动、版本探针超时或探针失败。
/// 它是 IPC 字段 <c>exifToolVersion</c> 的既有降级值，不是错误码；消费方据此判断
/// 「还没探到」而不是「ExifTool 不存在」。此处收敛字面量，避免桥/Worker/IPC 三处各写一份。
/// </summary>
public static class ExifToolVersionText
{
    /// <summary>版本未知时的上报值（ExifTool 可用性另行判定）。</summary>
    public const string Unknown = "unknown";

    /// <summary>上报值是否仍表示「版本尚未解析」。</summary>
    public static bool IsUnknown(string? versionText)
        => string.Equals(versionText, Unknown, StringComparison.Ordinal);
}
