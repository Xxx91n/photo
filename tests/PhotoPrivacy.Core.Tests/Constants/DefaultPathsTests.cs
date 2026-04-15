using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Tests.Constants;

public sealed class DefaultPathsTests
{
    [Fact]
    public void ExifToolPath_Should_Be_The_Required_Absolute_Path()
    {
        Assert.Equal(
            @"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe",
            DefaultPaths.ExifToolPath);
    }
}
