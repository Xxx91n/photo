using System.IO;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.ExifTool;

/// <summary>
/// Per-format wipe strategy resolver.
/// Replaces ExifToolCommandBuilder.BuildWipeTaskBlock's universal '-all=' with per-format safe defaults.
/// Source: ExifTool FAQ #32 + Writer Limitations doc.
/// </summary>
public static class WipeStrategyResolver
{
    public static readonly IReadOnlyDictionary<string, WipeFormatFamily> ExtensionFamilyMap = new Dictionary<string, WipeFormatFamily>(StringComparer.OrdinalIgnoreCase)
    {
        // JPEG family
        // 删除 jhc 死条目——它从不在 allowed_extensions 里，映射面上永远不可达
        // （FormatCoverageReconciliationTests 以 mapped ⊆ allowed 钉死“无死条目”）。
        ["jpg"] = WipeFormatFamily.Jpeg, ["jpeg"] = WipeFormatFamily.Jpeg,
        ["jpe"] = WipeFormatFamily.Jpeg, ["jps"] = WipeFormatFamily.Jpeg,
        ["jph"] = WipeFormatFamily.Jpeg,

        // TIFF/DNG
        ["tif"] = WipeFormatFamily.Tiff, ["tiff"] = WipeFormatFamily.Tiff,
        ["dng"] = WipeFormatFamily.Tiff,

        // RAW
        ["cr2"] = WipeFormatFamily.Raw, ["cr3"] = WipeFormatFamily.Raw,
        ["arw"] = WipeFormatFamily.Raw, ["nef"] = WipeFormatFamily.Raw,
        ["orf"] = WipeFormatFamily.Raw, ["raf"] = WipeFormatFamily.Raw,
        ["rw2"] = WipeFormatFamily.Raw, ["pef"] = WipeFormatFamily.Raw,
        ["srw"] = WipeFormatFamily.Raw, ["sr2"] = WipeFormatFamily.Raw,

        // HEIC family
        ["heic"] = WipeFormatFamily.Heic, ["heif"] = WipeFormatFamily.Heic,
        ["hif"] = WipeFormatFamily.Heic, ["avif"] = WipeFormatFamily.Heic,

        // PNG / WebP（issue #8：WebP 实测 -all= 往返安全，ICC 保留与 PNG 同引擎）
        ["png"] = WipeFormatFamily.Png, ["apng"] = WipeFormatFamily.Png,
        ["webp"] = WipeFormatFamily.Png,
        // GIF（issue #8：部分清理——Comment/XMP/ICC 可删；C2PA/PlainText/MIDI 删不掉，
        // 命令与 PNG 同款，禁止塞 -jumbf:all= 装作删 C2PA；见研究文档 §3.3 与
        // GifC2paPreservationTests）
        ["gif"] = WipeFormatFamily.Png,

        // PSD（issue #8：部分清理——EXIF/XMP/IPTC/ICC（IRB 内）可删；版权/URL/分辨率/
        // 打印参数/缩略图/图层内容删不掉；命令与 PNG 同款，42 图层真实样本实测图层段
        // 字节级不变；见研究文档 §3.4）
        ["psd"] = WipeFormatFamily.Png,

        // 视频六个扩展名继续留着（#8 拍板，#268 本机实测）：iPhone 录像的 GPS / 设备
        // 信息、C2PA 官方样本的 JUMBF 都删得掉，画面数据字节不变、全量解码通过；
        // 官方自己说视频是「删掉大部分顶层元数据」，所以按部分清理处理。
        // 分片 MP4（moof）ExifTool 13.61 写不了；产品的 stay_open 通道只看 {ready}，
        // 目前会把这类文件误记成已清理（审计谎报，待另票修），见研究文档 §12.3。
        // MOV/MP4
        ["mov"] = WipeFormatFamily.Video, ["mp4"] = WipeFormatFamily.Video,
        ["m4v"] = WipeFormatFamily.Video, ["qt"] = WipeFormatFamily.Video,
        ["3gp"] = WipeFormatFamily.Video, ["3g2"] = WipeFormatFamily.Video,

        // PDF
        ["pdf"] = WipeFormatFamily.Pdf,

        // EPS/PS/AI
        ["eps"] = WipeFormatFamily.Eps, ["ps"] = WipeFormatFamily.Eps,
        ["ai"] = WipeFormatFamily.Eps,
    };

    public static WipeStrategyResult Resolve(string filePath)
        => WipeRuleEngine.Resolve(ResolveFamily(filePath), rules: null);

    /// <summary> 规则驱动 resolve——family 由扩展名表判定，命令由 WipeRuleEngine 从规则字典生成。</summary>
    public static WipeStrategyResult Resolve(string filePath, IReadOnlyDictionary<string, bool>? rules)
        => WipeRuleEngine.Resolve(ResolveFamily(filePath), rules);

    /// <summary>扩展名 → 格式族（未知扩展/无扩展 → Unknown）。</summary>
    public static WipeFormatFamily ResolveFamily(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return WipeFormatFamily.Unknown;
        }

        var key = ext.StartsWith('.') ? ext[1..] : ext;
        return ExtensionFamilyMap.TryGetValue(key, out var family) ? family : WipeFormatFamily.Unknown;
    }

    /// <summary>
    /// 「部分清理」标注（issue #8 拍板：删不掉的尾巴要在审计里如实写明，不许谎报清干净）。
    /// 返回 null 表示该格式按宽线属清得干净的族，无需标注。新增部分清理格式时必须同步此表。
    /// </summary>
    public static string? PartialCleanNote(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return null;
        }

        var key = (ext.StartsWith('.') ? ext[1..] : ext).ToLowerInvariant();
        return key switch
        {
            "gif" => "部分清理：C2PA(JUMBF)、Plain Text、MIDI 块保留（ExifTool 删不掉）",
            "psd" => "部分清理：版权标记、URL、分辨率、打印参数、缩略图、图层内容保留",
            "mov" or "mp4" or "m4v" or "qt" or "3gp" or "3g2"
                => "部分清理：视频只删得掉大部分顶层信息，编码器指纹还在；拍摄日期改成 0，不影响播放",
            _ => null,
        };
    }

}
