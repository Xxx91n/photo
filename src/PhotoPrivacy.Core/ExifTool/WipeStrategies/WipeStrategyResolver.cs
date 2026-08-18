using System.IO;

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
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return new WipeStrategyResult(WipeFormatFamily.Unknown, EffectiveArgs: string.Empty, RequiresUserWarning: true, SkipReason: "unknown_format");
        }

        var key = ext.StartsWith('.') ? ext[1..] : ext;
        if (!ExtensionFamilyMap.TryGetValue(key, out var family))
        {
            return new WipeStrategyResult(WipeFormatFamily.Unknown, EffectiveArgs: string.Empty, RequiresUserWarning: true, SkipReason: "unknown_format");
        }

        return family switch
        {
            WipeFormatFamily.Jpeg => new WipeStrategyResult(family,
                "-all= --icc_profile:all -tagsfromfile @ -colorspacetags",
                RequiresUserWarning: false),
            WipeFormatFamily.Tiff => new WipeStrategyResult(family,
                "-all= -CommonIFD0=",
                RequiresUserWarning: false),
            WipeFormatFamily.Raw => new WipeStrategyResult(family,
                "-exif:all= -xmp:all= -iptc:all= -icc_profile:all=",
                RequiresUserWarning: false),
            WipeFormatFamily.Heic => new WipeStrategyResult(family,
                "-all= --icc_profile:all",
                RequiresUserWarning: false),
            WipeFormatFamily.Png => new WipeStrategyResult(family,
                "-all=",
                RequiresUserWarning: false),
            WipeFormatFamily.Video => new WipeStrategyResult(family,
                "-All= -Time:All=",
                RequiresUserWarning: false),
            WipeFormatFamily.Pdf => new WipeStrategyResult(family,
                "-all=",
                RequiresUserWarning: true),
            WipeFormatFamily.Eps => new WipeStrategyResult(family,
                "-all=",
                RequiresUserWarning: true),
            _ => new WipeStrategyResult(WipeFormatFamily.Unknown, EffectiveArgs: string.Empty, RequiresUserWarning: true, SkipReason: "unknown_format"),
        };
    }
}
