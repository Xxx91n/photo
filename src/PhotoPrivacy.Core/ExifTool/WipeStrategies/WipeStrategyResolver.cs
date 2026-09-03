using System.IO;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.ExifTool;

/// <summary>
/// ADR 0053 M6a: Per-format wipe strategy resolver.
/// Replaces ExifToolCommandBuilder.BuildWipeTaskBlock's universal '-all=' with per-format safe defaults.
/// Source: ExifTool FAQ #32 + Writer Limitations doc.
/// </summary>
public static class WipeStrategyResolver
{
    public static readonly IReadOnlyDictionary<string, WipeFormatFamily> ExtensionFamilyMap = new Dictionary<string, WipeFormatFamily>(StringComparer.OrdinalIgnoreCase)
    {
        // JPEG family
        ["jpg"] = WipeFormatFamily.Jpeg, ["jpeg"] = WipeFormatFamily.Jpeg,
        ["jpe"] = WipeFormatFamily.Jpeg, ["jps"] = WipeFormatFamily.Jpeg,
        ["jph"] = WipeFormatFamily.Jpeg, ["jhc"] = WipeFormatFamily.Jpeg,

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

        // PNG
        ["png"] = WipeFormatFamily.Png, ["apng"] = WipeFormatFamily.Png,

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

    /// <summary>票 19: 规则驱动 resolve——family 由扩展名表判定，命令由 WipeRuleEngine 从规则字典生成。</summary>
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

}
