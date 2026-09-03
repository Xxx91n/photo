using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Rules;

/// <summary>
/// 票 19（架构恢复第五轮）: 擦除命令单一真相源。
/// 给定格式族 + 逐组规则开关（strip_all / preserve_icc / strip_exif / strip_xmp / strip_iptc / strip_time，
/// 键名 schema 与 FormatRulesStore.Key 一致），生成 ExifTool 参数串。
/// 取代 ADR 0053 M6a 的 WipeStrategyResolver per-family 硬编码命令 switch 与
/// FormatRuleRow.EffectiveArgs 复制表：规则面板勾选自此真实改变 ExifTool 命令。
/// 规则字典缺键/缺族时回退 ADR 0053 安全默认（ExifTool FAQ #32 + Writer Limitations）。
/// </summary>
public static class WipeRuleEngine
{
    /// <summary>逐组开关组名（与 FormatRulesStore 键 schema 一致）。</summary>
    public static readonly string[] Groups = { "strip_all", "preserve_icc", "strip_exif", "strip_xmp", "strip_iptc", "strip_time" };

    /// <summary>格式族 → rules.json familyKey（无斜杠，与 FormatRulesStore 键 schema 一致）。</summary>
    public static string FamilyKey(WipeFormatFamily family) => family switch
    {
        WipeFormatFamily.Jpeg => "jpeg",
        WipeFormatFamily.Tiff => "tiff",
        WipeFormatFamily.Raw => "raw",
        WipeFormatFamily.Heic => "heic",
        WipeFormatFamily.Png => "png",
        WipeFormatFamily.Video => "video",
        WipeFormatFamily.Pdf => "pdf",
        WipeFormatFamily.Eps => "eps",
        _ => "unknown",
    };

    /// <summary>是否要求用户警告（PDF 永不真正删除、EPS 原生标签不可删）。</summary>
    public static bool RequiresUserWarning(WipeFormatFamily family)
        => family is WipeFormatFamily.Pdf or WipeFormatFamily.Eps;

    /// <summary>
    /// ADR 0053 M6a 安全默认（逐组开关取值）。规则字典缺键/缺族时回退本表；
    /// 面板族（jpeg/raw/video/pdf/eps）的取值与 FormatRulesStore.LoadDefaults 一致。
    /// </summary>
    private static (bool StripAll, bool PreserveIcc, bool StripExif, bool StripXmp, bool StripIptc, bool StripTime) FamilyFallback(WipeFormatFamily family) => family switch
    {
        WipeFormatFamily.Jpeg => (true, true, false, false, false, false),
        WipeFormatFamily.Tiff => (true, false, false, false, false, false),
        WipeFormatFamily.Raw => (false, false, true, true, true, false),
        WipeFormatFamily.Heic => (true, true, false, false, false, false),
        WipeFormatFamily.Png => (true, false, false, false, false, false),
        WipeFormatFamily.Video => (true, false, false, false, false, true),
        WipeFormatFamily.Pdf => (true, false, false, false, false, false),
        WipeFormatFamily.Eps => (true, false, false, false, false, false),
        _ => (false, false, false, false, false, false),
    };

    /// <summary>规则字典驱动（bridge/存储路径）。缺键回退 FamilyFallback。</summary>
    public static WipeStrategyResult Resolve(WipeFormatFamily family, IReadOnlyDictionary<string, bool>? rules)
    {
        if (family is WipeFormatFamily.Unknown)
        {
            return new WipeStrategyResult(WipeFormatFamily.Unknown, EffectiveArgs: string.Empty, RequiresUserWarning: true, SkipReason: "unknown_format");
        }

        var fallback = FamilyFallback(family);
        var familyKey = FamilyKey(family);
        bool Rule(string group, bool fb) =>
            rules is not null && rules.TryGetValue(FormatRulesStore.Key(familyKey, group), out var value) ? value : fb;

        return Resolve(
            family,
            Rule("strip_all", fallback.StripAll),
            Rule("preserve_icc", fallback.PreserveIcc),
            Rule("strip_exif", fallback.StripExif),
            Rule("strip_xmp", fallback.StripXmp),
            Rule("strip_iptc", fallback.StripIptc),
            Rule("strip_time", fallback.StripTime));
    }

    /// <summary>逐组开关直驱（UI 面板行路径）——勾选真实改变命令的纯函数核心。</summary>
    public static WipeStrategyResult Resolve(
        WipeFormatFamily family,
        bool stripAll,
        bool preserveIcc,
        bool stripExif,
        bool stripXmp,
        bool stripIptc,
        bool stripTime)
    {
        if (family is WipeFormatFamily.Unknown)
        {
            return new WipeStrategyResult(WipeFormatFamily.Unknown, EffectiveArgs: string.Empty, RequiresUserWarning: true, SkipReason: "unknown_format");
        }

        var args = BuildEffectiveArgs(family, stripAll, preserveIcc, stripExif, stripXmp, stripIptc, stripTime);
        if (args.Length == 0)
        {
            // 已知族但无任何可执行组：跳过擦除（空参数会让 exiftool 空跑重写文件）。
            return new WipeStrategyResult(family, EffectiveArgs: string.Empty, RequiresUserWarning: RequiresUserWarning(family), SkipReason: "no_rules");
        }

        return new WipeStrategyResult(family, args, RequiresUserWarning(family));
    }

    /// <summary>规则开关 → ExifTool 参数串（确定性顺序，join 单空格）。</summary>
    public static string BuildEffectiveArgs(
        WipeFormatFamily family,
        bool stripAll,
        bool preserveIcc,
        bool stripExif,
        bool stripXmp,
        bool stripIptc,
        bool stripTime)
    {
        var parts = new List<string>();

        if (stripAll)
        {
            switch (family)
            {
                case WipeFormatFamily.Jpeg:
                    parts.Add("-all=");
                    if (preserveIcc)
                    {
                        parts.Add("--icc_profile:all");
                    }
                    parts.Add("-tagsfromfile");
                    parts.Add("@");
                    parts.Add("-colorspacetags");
                    break;
                case WipeFormatFamily.Tiff:
                    parts.Add("-all=");
                    if (preserveIcc)
                    {
                        parts.Add("--icc_profile:all");
                    }
                    parts.Add("-CommonIFD0=");
                    break;
                case WipeFormatFamily.Heic:
                    parts.Add("-all=");
                    if (preserveIcc)
                    {
                        parts.Add("--icc_profile:all");
                    }
                    break;
                case WipeFormatFamily.Raw:
                    // ADR 0053 安全不变量：RAW 永不 -all=（MakerNotes 删后毁渲染），全擦意图展开为全部安全组。
                    parts.Add("-exif:all=");
                    parts.Add("-xmp:all=");
                    parts.Add("-iptc:all=");
                    if (stripTime)
                    {
                        parts.Add("-time:all=");
                    }
                    if (!preserveIcc)
                    {
                        parts.Add("-icc_profile:all=");
                    }
                    break;
                case WipeFormatFamily.Video:
                    parts.Add("-All=");
                    if (stripTime)
                    {
                        parts.Add("-Time:All=");
                    }
                    break;
                default:
                    parts.Add("-all=");
                    break;
            }

            return string.Join(" ", parts);
        }

        // 逐组模式：按确定性顺序追加已勾选组。
        if (stripExif)
        {
            parts.Add("-exif:all=");
        }
        if (stripXmp)
        {
            parts.Add("-xmp:all=");
        }
        if (stripIptc)
        {
            parts.Add("-iptc:all=");
        }
        if (!preserveIcc && family is not WipeFormatFamily.Video)
        {
            // preserve_icc=false 即 ICC 可删；Video 族无 ICC 剥离语义（ADR 0053 M6a）。
            parts.Add("-icc_profile:all=");
        }
        if (stripTime)
        {
            parts.Add(family is WipeFormatFamily.Video ? "-Time:All=" : "-time:all=");
        }

        return string.Join(" ", parts);
    }
}
