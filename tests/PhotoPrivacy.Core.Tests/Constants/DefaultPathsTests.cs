using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Tests.Constants;

public sealed class DefaultPathsTests
{
    [Fact]
    public void ExifToolPath_Should_Be_The_Required_Absolute_Path()
    {
        Assert.Equal(
            @"C:\Program Files\ExifTool\exiftool.exe",
            DefaultPaths.ExifToolPath);
    }
}
