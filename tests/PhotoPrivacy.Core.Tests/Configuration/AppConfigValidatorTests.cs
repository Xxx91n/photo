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
                // 票 02（承接票 01 首脑复核登记项）：原值是 drive-relative 形态的 verbatim 路径字面量
                //（盘符后的分隔符被吞），已改为不带盘符的字面量相对路径。
                Path = "ExifTool.exe"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };

        Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
    }

    // ---------------------------------------------------------------------------------------
    // 票 02（A-002）：空隔离/审计目录必须被拒（loader 已兜底，这里是第二层保险 + 可读错误）
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_Should_Throw_When_Quarantine_Directory_Is_Empty()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { DryRun = false },
            Quarantine = AppConfig.Default.Quarantine with { Directory = "" }
        };

        var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        Assert.Contains("quarantine.directory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Should_Throw_When_Audit_LogDirectory_Is_Blank()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { DryRun = false },
            Audit = AppConfig.Default.Audit with { LogDirectory = "   " }
        };

        var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        Assert.Contains("audit.log_directory", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 票 02（A-003）：排除清单告警——「不识别≠静默」（CollectWarnings 是告警的唯一出处）
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sub/dir/*.tmp")]
    [InlineData("sub\\dir\\*.tmp")]
    [InlineData("[abc].jpg")]
    [InlineData("{a,b}.jpg")]
    public void CollectWarnings_Should_Flag_Patterns_That_Cannot_Match_Or_Are_Unsupported(string pattern)
    {
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with { ExcludedPatterns = [pattern] }
        };

        var warnings = AppConfigValidator.CollectWarnings(cfg);

        Assert.Single(warnings);
        Assert.Contains("rules.excluded_patterns[0]", warnings[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("~$*")]
    [InlineData("*.tmp")]
    [InlineData("IMG_?.jpg")]
    [InlineData("a.jpg")]
    [InlineData("*")]
    public void CollectWarnings_Should_Be_Empty_For_Supported_Patterns(string pattern)
    {
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with { ExcludedPatterns = [pattern] }
        };

        Assert.Empty(AppConfigValidator.CollectWarnings(cfg));
    }

    [Fact]
    public void CollectWarnings_Should_Be_Empty_For_Default_Config_And_Empty_List()
    {
        Assert.Empty(AppConfigValidator.CollectWarnings(AppConfig.Default));

        var noPatterns = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with { ExcludedPatterns = [] }
        };
        Assert.Empty(AppConfigValidator.CollectWarnings(noPatterns));
    }

    [Fact]
    public void CollectWarnings_Should_Report_Every_Offending_Pattern_With_Its_Index()
    {
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                ExcludedPatterns = ["*.tmp", "bad/dir/*.tmp", ""]
            }
        };

        var warnings = AppConfigValidator.CollectWarnings(cfg);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("rules.excluded_patterns[1]", warnings[0], StringComparison.Ordinal);
        Assert.Contains("rules.excluded_patterns[2]", warnings[1], StringComparison.Ordinal);
    }

}
