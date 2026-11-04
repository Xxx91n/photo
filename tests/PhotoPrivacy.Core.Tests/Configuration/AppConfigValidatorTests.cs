using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Constants;

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

    // #33: 目录式覆盖解析出的 exe 绝对路径，仍按既有口径校验伴随目录（Windows exiftool_files / Unix lib）。
    [Fact]
    public void Validate_Should_Accept_Directory_Override_With_Companion_Directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, exeName), "stub");
        Directory.CreateDirectory(Path.Combine(dir, OperatingSystem.IsWindows() ? "exiftool_files" : "lib"));

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with
                {
                    Path = DefaultPaths.ResolveExifToolExecutable(dir)
                }
            };

            AppConfigValidator.Validate(cfg);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // #33: 目录内缺伴随运行时目录时，启用监控必须 fail-closed，而不是延迟到 stay_open 桥接启动期。
    [Fact]
    public void Validate_Should_Reject_Directory_Override_Without_Companion_Directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, exeName), "stub");

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with
                {
                    Path = DefaultPaths.ResolveExifToolExecutable(dir)
                }
            };

            Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
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
    public void Validate_Should_Allow_Empty_HotFolder_For_FirstRun_Standby()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = false,
                // （承接 首脑复核登记项）：原值是 drive-relative 形态的 verbatim 路径字面量
                //（盘符后的分隔符被吞），已改为不带盘符的字面量相对路径。
                Path = "ExifTool.exe"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };

        var ex = Record.Exception(() => AppConfigValidator.Validate(cfg));
        Assert.Null(ex);
    }

    // ---------------------------------------------------------------------------------------
    // 校验分层（Design(#108)）：无条件层在待机/ dry-run 形态同样执行；
    // 监控资源层只在启用监控时 fail-closed。
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 本票的核心收窄：空监控目录不再整体 return，禁用参数这类与监控无关的项必须照常拒。
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_ExtraArgs_Forbidden_Even_If_HotFolder_Is_Empty()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = false,
                ExtraExifToolArgs = ["-stay_open"]
            },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };

        var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        Assert.Contains("extra_exiftool_args", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// dry-run 以前是整体早退，禁用参数也被一并跳过；分层后该判断上移到 dry-run 之前。
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_ExtraArgs_Forbidden_Even_In_DryRun()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = true,
                ExtraExifToolArgs = ["-execute"]
            }
        };

        var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
        Assert.Contains("extra_exiftool_args", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 待机态只推迟监控资源校验：缺 ExifTool、缺 exiftool_files 都不阻断待机，
    /// 否则裸机连待机都进不去（#100）。启用监控时它们仍 fail-closed——见下面的启用形态用例。
    /// </summary>
    [Fact]
    public void Validate_Should_Not_Evaluate_Monitor_Resources_While_HotFolder_Is_Empty()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = false,
                Path = Path.Combine(Path.GetTempPath(), "no-such-exiftool", "exiftool.exe")
            },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };

        Assert.Null(Record.Exception(() => AppConfigValidator.Validate(cfg)));
    }

    /// <summary>
    /// 同一份配置一旦启用监控（有目录），监控资源校验立刻 fail-closed——这是待机→启用链路的拒绝侧。
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_Monitor_Resources_Missing_And_HotFolder_Is_Set()
    {
        var tempHot = Path.Combine(Path.GetTempPath(), "pp-hot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempHot);

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with
                {
                    DryRun = false,
                    Path = Path.Combine(Path.GetTempPath(), "no-such-exiftool", "exiftool.exe")
                },
                Watch = AppConfig.Default.Watch with { HotFolder = tempHot },
                Backup = AppConfig.Default.Backup with { Enabled = false }
            };

            var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
            Assert.Contains("exiftool.path not found", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempHot, recursive: true);
        }
    }

    [Fact]
    public void Validate_Should_Throw_When_Backup_Directory_Equals_HotFolder()
    {
        var tempHot = Path.Combine(Path.GetTempPath(), "pp-hot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempHot);
        File.WriteAllText(Path.Combine(tempHot, "exiftool.exe"), "x");
        // 两个平台的伴随运行时目录都建：Windows 要求 exiftool_files，Unix 要求同级 lib/
        // （见 Design(#108) 的分层与 IsSystemBinDirectory 例外），否则本用例会先撞运行时目录缺失。
        Directory.CreateDirectory(Path.Combine(tempHot, "exiftool_files"));
        Directory.CreateDirectory(Path.Combine(tempHot, "lib"));

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with
                {
                    DryRun = false,
                    Path = Path.Combine(tempHot, "exiftool.exe")
                },
                Watch = AppConfig.Default.Watch with { HotFolder = tempHot },
                Backup = AppConfig.Default.Backup with { Enabled = true, Directory = tempHot }
            };

            var ex = Assert.Throws<AppConfigValidationException>(() => AppConfigValidator.Validate(cfg));
            Assert.Contains("备份目录", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempHot, recursive: true);
        }
    }

    /// <summary>
    /// 符号链接安装：<c>bin/exiftool</c> 链接到另一目录，<c>lib/</c> 在目标旁边。
    /// 防的具体回潮形态（#108 审查）：按链接所在目录查运行时目录，把可用安装误判成装坏。
    /// </summary>
    [Fact]
    public void Validate_Should_Accept_Symlinked_ExifTool_With_Lib_Beside_The_Target()
    {
        // Windows 建符号链接需要特权（开发者模式/管理员），且 dotnet 测试在 CI 只跑 Linux；
        // 这条断言在 Windows 上跳过，不赌宿主机权限。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "pp-link-" + Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "ExifTool");
        var linkDirectory = Path.Combine(root, "bin");
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(linkDirectory);

        var target = Path.Combine(targetDirectory, "exiftool");
        File.WriteAllText(target, "#!/bin/sh\n");
        Directory.CreateDirectory(Path.Combine(targetDirectory, "lib"));

        var link = Path.Combine(linkDirectory, "exiftool");
        _ = File.CreateSymbolicLink(link, target);

        try
        {
            var cfg = AppConfig.Default with
            {
                ExifTool = AppConfig.Default.ExifTool with { DryRun = false, Path = link },
                Watch = AppConfig.Default.Watch with { HotFolder = linkDirectory },
                Backup = AppConfig.Default.Backup with { Enabled = false }
            };

            Assert.Null(Record.Exception(() => AppConfigValidator.Validate(cfg)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 空隔离/审计目录必须被拒（loader 已兜底，这里是第二层保险 + 可读错误）
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
    // 排除清单告警——「不识别≠静默」（CollectWarnings 是告警的唯一出处）
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
