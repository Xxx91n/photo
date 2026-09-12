using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Rules;

/// <summary>
/// 票 19 检查点 B: 纯函数测试——给定规则集合 → 生成命令与勾选一致。
/// 同时锁定: 票 18 默认规则下，8 个格式族与 ADR 0053 M6a 命令表逐字一致（零行为漂移）。
/// </summary>
public sealed class WipeRuleEngineTests
{
    /// <summary>测试侧独立拼键（不信任 FormatRulesStore.Key），镜像票 18 canonical schema。</summary>
    private static string K(string family, string group) => family + "_" + group;

    private static Dictionary<string, bool> PanelDefaults()
    {
        var d = new Dictionary<string, bool>();
        Set(d, "jpeg", stripAll: true, preserveIcc: true);
        Set(d, "raw", stripAll: false, preserveIcc: false, stripExif: true, stripXmp: true, stripIptc: true);
        Set(d, "video", stripAll: true, preserveIcc: false, stripTime: true);
        Set(d, "pdf", stripAll: true, preserveIcc: false);
        Set(d, "eps", stripAll: true, preserveIcc: false);
        return d;
    }

    private static void Set(Dictionary<string, bool> d, string family,
        bool stripAll = false, bool preserveIcc = false, bool stripExif = false,
        bool stripXmp = false, bool stripIptc = false, bool stripTime = false)
    {
        d[K(family, "strip_all")] = stripAll;
        d[K(family, "preserve_icc")] = preserveIcc;
        d[K(family, "strip_exif")] = stripExif;
        d[K(family, "strip_xmp")] = stripXmp;
        d[K(family, "strip_iptc")] = stripIptc;
        d[K(family, "strip_time")] = stripTime;
    }

    [Theory]
    [InlineData(WipeFormatFamily.Jpeg, "-all= --icc_profile:all -tagsfromfile @ -colorspacetags")]
    [InlineData(WipeFormatFamily.Tiff, "-all= -CommonIFD0=")]
    [InlineData(WipeFormatFamily.Raw, "-exif:all= -xmp:all= -iptc:all= -icc_profile:all=")]
    [InlineData(WipeFormatFamily.Heic, "-all= --icc_profile:all")]
    [InlineData(WipeFormatFamily.Png, "-all=")]
    [InlineData(WipeFormatFamily.Video, "-All= -Time:All=")]
    [InlineData(WipeFormatFamily.Pdf, "-all=")]
    [InlineData(WipeFormatFamily.Eps, "-all=")]
    public void Resolve_WithoutRules_Reproduces_Adr0053_Commands(WipeFormatFamily family, string expected)
    {
        Assert.Equal(expected, WipeRuleEngine.Resolve(family, rules: null).EffectiveArgs);
    }

    [Theory]
    [InlineData(WipeFormatFamily.Jpeg, "-all= --icc_profile:all -tagsfromfile @ -colorspacetags")]
    [InlineData(WipeFormatFamily.Tiff, "-all= -CommonIFD0=")]
    [InlineData(WipeFormatFamily.Raw, "-exif:all= -xmp:all= -iptc:all= -icc_profile:all=")]
    [InlineData(WipeFormatFamily.Heic, "-all= --icc_profile:all")]
    [InlineData(WipeFormatFamily.Png, "-all=")]
    [InlineData(WipeFormatFamily.Video, "-All= -Time:All=")]
    [InlineData(WipeFormatFamily.Pdf, "-all=")]
    [InlineData(WipeFormatFamily.Eps, "-all=")]
    public void Resolve_WithPanelDefaults_Reproduces_Adr0053_Commands(WipeFormatFamily family, string expected)
    {
        Assert.Equal(expected, WipeRuleEngine.Resolve(family, PanelDefaults()).EffectiveArgs);
    }

    [Fact]
    public void StoreDefaults_Through_Engine_Produce_Legacy_Panel_Commands()
    {
        // 跨层缝合: FormatRulesStore.LoadDefaults()（票 18 schema）→ 引擎 → ADR 0053 面板族命令。
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var defaults = store.LoadDefaults();

        Assert.Equal("-all= --icc_profile:all -tagsfromfile @ -colorspacetags",
            WipeRuleEngine.Resolve(WipeFormatFamily.Jpeg, defaults).EffectiveArgs);
        Assert.Equal("-exif:all= -xmp:all= -iptc:all= -icc_profile:all=",
            WipeRuleEngine.Resolve(WipeFormatFamily.Raw, defaults).EffectiveArgs);
        Assert.Equal("-All= -Time:All=",
            WipeRuleEngine.Resolve(WipeFormatFamily.Video, defaults).EffectiveArgs);
        Assert.Equal("-all=", WipeRuleEngine.Resolve(WipeFormatFamily.Pdf, defaults).EffectiveArgs);
        Assert.Equal("-all=", WipeRuleEngine.Resolve(WipeFormatFamily.Eps, defaults).EffectiveArgs);
        // 非面板族（无存储键）回退安全默认。
        Assert.Equal("-all= -CommonIFD0=", WipeRuleEngine.Resolve(WipeFormatFamily.Tiff, defaults).EffectiveArgs);
        Assert.Equal("-all= --icc_profile:all", WipeRuleEngine.Resolve(WipeFormatFamily.Heic, defaults).EffectiveArgs);
        Assert.Equal("-all=", WipeRuleEngine.Resolve(WipeFormatFamily.Png, defaults).EffectiveArgs);
    }

    [Fact]
    public void Jpeg_PreserveIcc_Unchecked_Really_Changes_Command()
    {
        // 检查点 B: 取消勾选"保留 ICC" → ICC 保护段消失。
        var rules = PanelDefaults();
        rules[K("jpeg", "preserve_icc")] = false;

        var args = WipeRuleEngine.Resolve(WipeFormatFamily.Jpeg, rules).EffectiveArgs;

        Assert.Equal("-all= -tagsfromfile @ -colorspacetags", args);
        Assert.DoesNotContain("--icc_profile:all", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_GroupMode_Composes_Only_Checked_Groups()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("jpeg", "strip_all")] = false,
            [K("jpeg", "preserve_icc")] = true,
            [K("jpeg", "strip_exif")] = true,
            [K("jpeg", "strip_xmp")] = false,
            [K("jpeg", "strip_iptc")] = true,
            [K("jpeg", "strip_time")] = false,
        };

        Assert.Equal("-exif:all= -iptc:all=", WipeRuleEngine.Resolve(WipeFormatFamily.Jpeg, rules).EffectiveArgs);
    }

    [Fact]
    public void Raw_StripAll_Never_Emits_All_Equals()
    {
        // ADR 0053 安全不变量: RAW 全擦意图展开为全部安全组，永不 -all=（MakerNotes 毁渲染）。
        var rules = PanelDefaults();
        rules[K("raw", "strip_all")] = true;
        rules[K("raw", "strip_time")] = true;

        var args = WipeRuleEngine.Resolve(WipeFormatFamily.Raw, rules).EffectiveArgs;

        Assert.Equal("-exif:all= -xmp:all= -iptc:all= -time:all= -icc_profile:all=", args);
        Assert.DoesNotContain("-all=", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_PreserveIcc_Toggle_Controls_Icc_Arg_In_GroupMode()
    {
        // 全 6 键显式（缺键会回退族安全默认，无法隔离单开关语义）。
        var preserve = new Dictionary<string, bool>
        {
            [K("raw", "strip_all")] = false,
            [K("raw", "preserve_icc")] = true,
            [K("raw", "strip_exif")] = true,
            [K("raw", "strip_xmp")] = false,
            [K("raw", "strip_iptc")] = false,
            [K("raw", "strip_time")] = false,
        };
        var strip = new Dictionary<string, bool>
        {
            [K("raw", "strip_all")] = false,
            [K("raw", "preserve_icc")] = false,
            [K("raw", "strip_exif")] = true,
            [K("raw", "strip_xmp")] = false,
            [K("raw", "strip_iptc")] = false,
            [K("raw", "strip_time")] = false,
        };

        Assert.Equal("-exif:all=", WipeRuleEngine.Resolve(WipeFormatFamily.Raw, preserve).EffectiveArgs);
        Assert.Equal("-exif:all= -icc_profile:all=", WipeRuleEngine.Resolve(WipeFormatFamily.Raw, strip).EffectiveArgs);
    }

    [Fact]
    public void Missing_Keys_Fall_Back_To_Family_Safe_Defaults()
    {
        // 部分字典: 缺键回退族安全默认（RAW: strip_xmp/strip_iptc 默认 true）。
        var partial = new Dictionary<string, bool>
        {
            [K("raw", "strip_all")] = false,
            [K("raw", "preserve_icc")] = true,
            [K("raw", "strip_exif")] = true,
        };

        Assert.Equal("-exif:all= -xmp:all= -iptc:all=", WipeRuleEngine.Resolve(WipeFormatFamily.Raw, partial).EffectiveArgs);
    }

    [Fact]
    public void Video_GroupMode_Uses_Capital_Time_And_No_Icc()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("video", "strip_all")] = false,
            [K("video", "preserve_icc")] = false,
            [K("video", "strip_time")] = true,
        };

        Assert.Equal("-Time:All=", WipeRuleEngine.Resolve(WipeFormatFamily.Video, rules).EffectiveArgs);
    }

    [Fact]
    public void Video_StripAll_Unchecked_Time_Kept()
    {
        var rules = PanelDefaults();
        rules[K("video", "strip_time")] = false;

        Assert.Equal("-All=", WipeRuleEngine.Resolve(WipeFormatFamily.Video, rules).EffectiveArgs);
    }

    [Fact]
    public void Jpeg_GroupMode_Time_Uses_Lowercase()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("jpeg", "strip_all")] = false,
            [K("jpeg", "preserve_icc")] = true,
            [K("jpeg", "strip_time")] = true,
        };

        Assert.Equal("-time:all=", WipeRuleEngine.Resolve(WipeFormatFamily.Jpeg, rules).EffectiveArgs);
    }

    [Fact]
    public void Tiff_PreserveIcc_True_With_StripAll_Protects_Icc()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("tiff", "strip_all")] = true,
            [K("tiff", "preserve_icc")] = true,
        };

        Assert.Equal("-all= --icc_profile:all -CommonIFD0=", WipeRuleEngine.Resolve(WipeFormatFamily.Tiff, rules).EffectiveArgs);
    }

    [Fact]
    public void Empty_Selection_Known_Family_Skips()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("pdf", "strip_all")] = false,
            [K("pdf", "preserve_icc")] = true,
            [K("pdf", "strip_exif")] = false,
            [K("pdf", "strip_xmp")] = false,
            [K("pdf", "strip_iptc")] = false,
            [K("pdf", "strip_time")] = false,
        };

        var result = WipeRuleEngine.Resolve(WipeFormatFamily.Pdf, rules);

        Assert.Equal("no_rules", result.SkipReason);
        Assert.True(result.RequiresUserWarning);
    }

    [Fact]
    public void Unknown_Family_Skips_Regardless_Of_Rules()
    {
        var result = WipeRuleEngine.Resolve(WipeFormatFamily.Unknown, PanelDefaults());

        Assert.Equal(WipeFormatFamily.Unknown, result.Family);
        Assert.Equal("unknown_format", result.SkipReason);
    }

    [Fact]
    public void Pdf_And_Eps_Require_User_Warning()
    {
        Assert.True(WipeRuleEngine.RequiresUserWarning(WipeFormatFamily.Pdf));
        Assert.True(WipeRuleEngine.RequiresUserWarning(WipeFormatFamily.Eps));
        Assert.False(WipeRuleEngine.RequiresUserWarning(WipeFormatFamily.Jpeg));
        Assert.False(WipeRuleEngine.RequiresUserWarning(WipeFormatFamily.Raw));
    }

    [Fact]
    public void FileResolver_With_Rules_EndToEnd()
    {
        var rules = new Dictionary<string, bool>
        {
            [K("raw", "strip_all")] = false,
            [K("raw", "preserve_icc")] = true,
            [K("raw", "strip_exif")] = true,
            [K("raw", "strip_xmp")] = false,
            [K("raw", "strip_iptc")] = false,
            [K("raw", "strip_time")] = false,
        };

        var result = WipeStrategyResolver.Resolve(@"C:\photos\test.arw", rules);

        Assert.Equal(WipeFormatFamily.Raw, result.Family);
        Assert.Equal("-exif:all=", result.EffectiveArgs);
    }

    [Fact]
    public void BuildWipeTaskBlock_Reflects_Checkbox_Changes()
    {
        // 引擎路径端到端: 勾选保留 ICC=false → 真实命令块不含 ICC 保护段。
        var rules = PanelDefaults();
        rules[K("jpeg", "preserve_icc")] = false;

        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(@"D:\hot\a.jpg", "t1", rules);

        Assert.Contains("-all=", block, StringComparison.Ordinal);
        Assert.Contains("-tagsfromfile", block, StringComparison.Ordinal);
        Assert.DoesNotContain("--icc_profile:all", block, StringComparison.Ordinal);
        Assert.Contains("-overwrite_original", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWipeTaskBlock_Without_Rules_Keeps_Legacy_Command()
    {
        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(@"D:\hot\a.jpg", "t1");

        Assert.Contains("-all=", block, StringComparison.Ordinal);
        Assert.Contains("--icc_profile:all", block, StringComparison.Ordinal);
        Assert.Contains("-tagsfromfile", block, StringComparison.Ordinal);
    }

    private sealed class TempDirHolder : IDisposable
    {
        public string Path { get; }
        public TempDirHolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pp-wipengine-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
