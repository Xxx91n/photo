using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

/// <summary>
/// WipeStrategyResolver per-format safe defaults test 闭环.
/// Source: ExifTool FAQ #32 + Writer Limitations doc + per-format safe-default table.
/// </summary>
public sealed class WipeStrategyResolverTests
{
    [Theory]
    [InlineData("photo.jpg", WipeFormatFamily.Jpeg)]
    [InlineData("photo.JPEG", WipeFormatFamily.Jpeg)]
    [InlineData("scan.tiff", WipeFormatFamily.Tiff)]
    [InlineData("raw.dng", WipeFormatFamily.Tiff)]
    [InlineData("shot.cr3", WipeFormatFamily.Raw)]
    [InlineData("img.heic", WipeFormatFamily.Heic)]
    [InlineData("icon.png", WipeFormatFamily.Png)]
    [InlineData("anim.webp", WipeFormatFamily.Png)]
    [InlineData("loop.gif", WipeFormatFamily.Png)]
    [InlineData("clip.mov", WipeFormatFamily.Video)]
    [InlineData("doc.pdf", WipeFormatFamily.Pdf)]
    [InlineData("vector.eps", WipeFormatFamily.Eps)]
    public void Resolve_Recognized_Extension_Returns_Correct_Family(string fileName, WipeFormatFamily expected)
    {
        var result = WipeStrategyResolver.Resolve(@"C:\photos\" + fileName);
        Assert.Equal(expected, result.Family);
        Assert.Null(result.SkipReason);
        Assert.False(string.IsNullOrEmpty(result.EffectiveArgs));
    }

    [Fact]
    public void Resolve_Jpeg_Uses_Safe_ColorSpaceTags_Pattern()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\photos\test.jpg");
        // ExifTool FAQ #32: -all= --icc_profile:all -tagsfromfile @ -colorspacetags
        Assert.Contains("-all=", result.EffectiveArgs);
        Assert.Contains("--icc_profile:all", result.EffectiveArgs);
        Assert.Contains("-colorspacetags", result.EffectiveArgs);
        Assert.False(result.RequiresUserWarning);
    }

    [Fact]
    public void Resolve_Raw_Only_Strips_Subdirs_Not_IFD0()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\photos\test.arw");
        // Conservative: -exif:all= -xmp:all= -iptc:all= -icc_profile:all= (not -all=)
        Assert.Contains("-exif:all=", result.EffectiveArgs);
        Assert.Contains("-xmp:all=", result.EffectiveArgs);
        Assert.DoesNotContain("-all=", result.EffectiveArgs);
        Assert.False(result.RequiresUserWarning);
    }

    [Fact]
    public void Resolve_Video_Uses_Capital_A_And_Time_Stripping()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\videos\clip.mp4");
        // MOV/MP4: -All= -Time:All= (capital A required first)
        Assert.Contains("-All=", result.EffectiveArgs);
        Assert.Contains("-Time:All=", result.EffectiveArgs);
    }

    [Fact]
    public void Resolve_PDF_Requires_User_Warning()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\docs\report.pdf");
        Assert.True(result.RequiresUserWarning);
    }

    [Fact]
    public void Resolve_EPS_Requires_User_Warning()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\art\logo.eps");
        Assert.True(result.RequiresUserWarning);
    }

    [Fact]
    public void Resolve_Gif_Uses_Png_Command_And_Never_Fakes_Jumbf_Deletion()
    {
        // 防的是：GIF 的 C2PA 在 ExifTool 13.61 删不掉（-jumbf:all= 实测 0 updated
        // 静默不动）；若有人往 GIF 命令里塞 -jumbf:all= 装作删了 C2PA，审计就撒谎。
        // 关联 issue #8。
        var result = WipeStrategyResolver.Resolve(@"C:\photos\loop.gif");

        Assert.Equal(WipeFormatFamily.Png, result.Family);
        Assert.Equal("-all= --icc_profile:all", result.EffectiveArgs);
        Assert.DoesNotContain("-jumbf", result.EffectiveArgs, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Unknown_Extension_Returns_SkipResult()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\data\file.xyz");
        Assert.Equal(WipeFormatFamily.Unknown, result.Family);
        Assert.NotNull(result.SkipReason);
        Assert.Equal("unknown_format", result.SkipReason);
    }

    [Fact]
    public void Resolve_No_Extension_Returns_SkipResult()
    {
        var result = WipeStrategyResolver.Resolve(@"C:\data\Makefile");
        Assert.Equal(WipeFormatFamily.Unknown, result.Family);
        Assert.NotNull(result.SkipReason);
    }
}
