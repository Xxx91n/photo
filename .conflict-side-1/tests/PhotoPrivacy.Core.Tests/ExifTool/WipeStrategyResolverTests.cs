using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

/// <summary>
/// ADR 0053 M6a-g: WipeStrategyResolver per-format safe defaults test 闭环.
/// Source: ExifTool FAQ #32 + Writer Limitations doc + ADR 0053 M6a table.
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
