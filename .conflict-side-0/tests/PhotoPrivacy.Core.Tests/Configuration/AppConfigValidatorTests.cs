using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

public sealed class AppConfigValidatorTests
{
    [Fact]
    public void Validate_Should_Throw_When_ExifToolPath_Is_Not_Absolute()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { Path = "ExifTool.exe" }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Validate_Should_Throw_When_ExtraArgs_Contain_StayOpen()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { ExtraExifToolArgs = ["-stay_open", "true"] }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

    [Fact]
    public void Validate_Should_Throw_When_ExiftoolFiles_Directory_Missing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, "fake_exiftool.exe");
        File.WriteAllText(tempExe, "x");

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with { Path = tempExe }
            };

            Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Validate_Should_Not_Throw_When_DryRun_Enabled_And_ExifTool_Path_Missing()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = true,
                Path = @"D:\not-exist\ExifTool.exe"
            }
        };

        var ex = Record.Exception(() => AppConfigValidator.Validate(cfg));
        Assert.Null(ex);
    }
    [Fact]
    public void Validate_Should_Throw_When_HotFolder_Empty_And_Not_DryRun()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = false,
                Path = @"C:Program FilesExifToolexiftool.exe"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

}
