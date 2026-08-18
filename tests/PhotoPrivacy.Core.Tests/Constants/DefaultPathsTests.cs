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

    // ADR 0055 A4: DefaultHotFolder must be app-local (BaseDirectory/hot), not user's system pictures
    [Fact]
    public void DefaultHotFolder_Should_Be_App_Local_Hot_Directory()
    {
        var expected = System.IO.Path.Combine(AppContext.BaseDirectory, "hot");
        Assert.Equal(expected, DefaultPaths.DefaultHotFolder);
    }

    // ADR 0055 A4: DefaultBackupDirectory subdir must be "bak" (aligned with BackupPathResolver.DefaultBackupDirName)
    [Fact]
    public void DefaultBackupDirectory_Should_Use_Bak_Subdir_Aligned_With_BackupPathResolver()
    {
        var expected = System.IO.Path.Combine(DefaultPaths.DefaultHotFolder, "bak");
        Assert.Equal(expected, DefaultPaths.DefaultBackupDirectory);
    }

}
