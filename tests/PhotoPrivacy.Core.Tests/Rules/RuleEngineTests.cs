using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    [Fact]
    public void Decide_Should_Process_When_Extension_Is_Allowed()
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\a.jpg");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(@"D:\hot\a.jpg", decision.OutputPath);
    }

    [Fact]
    public void Decide_Should_Use_Fixed_Output_Directory_When_Configured()
    {
        // 票 28 返修：Path.GetRelativePath/Combine 平台分隔符随 OS——用平台中立构造断言
        var hot = Path.Combine(Path.GetTempPath(), "pp-re-hot");
        var clean = Path.Combine(Path.GetTempPath(), "pp-re-clean");
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = clean
            },
            Watch = AppConfig.Default.Watch with { HotFolder = hot }
        };

        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(hot, "album", "a.jpg"));

        Assert.True(decision.ShouldProcess);
        Assert.Equal(Path.Combine(clean, "album", "a.jpg"), decision.OutputPath);
    }

    [Fact]
    public void Decide_Should_Create_Bak_Path_When_Backup_Enabled()
    {
        // 票 28 返修：备份目录 = <hotFolder>/bak（BackupPathResolver），断言平台中立构造
        var hot = Path.Combine(Path.GetTempPath(), "pp-re-hot");
        var cfg = AppConfig.Default with
        {
            Backup = AppConfig.Default.Backup with { Enabled = true, Suffix = ".bak" },
            Watch = AppConfig.Default.Watch with { HotFolder = hot }
        };

        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(hot, "a.jpg"));

        Assert.True(decision.CreateBackup);
        Assert.Equal(Path.Combine(hot, "bak", "a.jpg.bak"), decision.BackupPath);
    }

    [Theory]
    [InlineData(".cr3")]
    [InlineData(".arw")]
    [InlineData(".nef")]
    [InlineData(".tiff")]
    [InlineData(".avif")]
    [InlineData(".ai")]
    [InlineData(".eps")]
    [InlineData(".heif")]
    [InlineData(".dng")]
    [InlineData(".raf")]
    [InlineData(".rw2")]
    [InlineData(".orf")]
    [InlineData(".mov")]
    [InlineData(".heic")]
    [InlineData(".m4v")]
    [InlineData(".3gp")]
    [InlineData(".cr2")]
    [InlineData(".3g2")]
    [InlineData(".sr2")]
    public void Decide_Should_Process_Writable_Extensions(string ext)
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(@"D:\hot", $"test{ext}"));

        Assert.True(decision.ShouldProcess);
    }

    /// <summary>
    /// 票 01 宣称收敛（D-005.1）：以下扩展名原先在 allowed_extensions 里但不在
    /// WipeStrategyResolver.ExtensionFamilyMap 映射面——即“宣称支持但没有擦除策略”。
    /// 收敛后默认不再处理（fail-closed）；即使被用户旧配置放进来，ExifToolBridge 也会立即跳过
    /// 并落 wipe_skipped_unknown 审计（见 UnsupportedFormatPipelineTests）。
    /// </summary>
    [Theory]
    [InlineData(".webp")]
    [InlineData(".psd")]
    [InlineData(".gif")]
    [InlineData(".ori")]
    [InlineData(".mpo")]
    [InlineData(".x3f")]
    [InlineData(".crm")]
    [InlineData(".mie")]
    public void Decide_Should_Reject_Extensions_Outside_Wipe_Family_Map(string ext)
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(@"D:\hot", $"test{ext}"));

        Assert.False(decision.ShouldProcess);
        Assert.Equal("extension_not_allowed", decision.Reason);
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    [InlineData(".wav")]
    [InlineData(".ogg")]
    [InlineData(".zip")]
    [InlineData(".exe")]
    [InlineData(".avi")]
    [InlineData(".mkv")]
    [InlineData(".html")]
    [InlineData(".txt")]
    [InlineData(".svg")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData(".docx")]
    [InlineData(".xls")]
    public void Decide_Should_Reject_NonWritable_Extensions(string ext)
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(@"D:\hot", $"test{ext}"));

        Assert.False(decision.ShouldProcess);
        Assert.Equal("extension_not_allowed", decision.Reason);
    }
    [Fact]
    public void Decide_Should_Return_Null_BackupPath_When_HotFolder_Empty()
    {
        var cfg = AppConfig.Default with
        {
            Backup = AppConfig.Default.Backup with { Enabled = true },
            Watch = AppConfig.Default.Watch with { HotFolder = "" }
        };
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\a.jpg");

        Assert.Null(decision.BackupPath);
    }

    // ---------------------------------------------------------------------------------------
    // 票 02（A-003）：排除清单真通配（* / ?）行为测试。
    // 语义边界 = 纯文件名匹配（与收敛前一致）；大小写口径 = OrdinalIgnoreCase（与收敛前一致）；
    // 断言全部走 RuleEngine.Decide 的公共行为（Reason 字段），不读源码文本。
    // 注意：allowed_extensions 先于 excluded_patterns 判定，故探针文件名的扩展名必须在放行面内，
    // 否则会提前以 extension_not_allowed 返回、根本走不到排除清单（helper 已显式放行 .jpg/.tmp）。
    // ---------------------------------------------------------------------------------------

    private static RuleDecision DecideWithPatterns(string fileName, params string[] patterns)
    {
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                AllowedExtensions = [".jpg", ".tmp"],
                ExcludedPatterns = patterns
            }
        };

        return new RuleEngine(cfg).Decide(Path.Combine(@"D:\hot", fileName));
    }

    [Theory]
    [InlineData("~$draft.jpg", true)]
    [InlineData("~$.jpg", true)]
    [InlineData("draft~$.jpg", false)]
    public void Decide_Should_Keep_Legacy_Tilde_Dollar_Star_Semantics(string fileName, bool shouldExclude)
    {
        var decision = DecideWithPatterns(fileName, "~$*");

        Assert.Equal(!shouldExclude, decision.ShouldProcess);
        Assert.Equal(shouldExclude ? "excluded_pattern" : "eligible", decision.Reason);
    }

    [Fact]
    public void Decide_Should_Keep_Legacy_Star_Tmp_Semantics()
    {
        var excluded = DecideWithPatterns("a.tmp", "*.tmp");
        Assert.False(excluded.ShouldProcess);
        Assert.Equal("excluded_pattern", excluded.Reason);

        // 对照：同一文件名不被另一条既有字面量命中
        var control = DecideWithPatterns("a.tmp", "~$*");
        Assert.True(control.ShouldProcess);
        Assert.Equal("eligible", control.Reason);
    }

    [Theory]
    [InlineData("IMG_1.jpg", true)]
    [InlineData("IMG_12.jpg", false)]
    [InlineData("IMG_.jpg", false)]
    public void Decide_Should_Match_Single_Char_Wildcard(string fileName, bool shouldExclude)
    {
        var decision = DecideWithPatterns(fileName, "IMG_?.jpg");

        Assert.Equal(!shouldExclude, decision.ShouldProcess);
        Assert.Equal(shouldExclude ? "excluded_pattern" : "eligible", decision.Reason);
    }

    [Theory]
    [InlineData("IMG_0001.jpg", true)]
    [InlineData("IMG_.jpg", true)]
    [InlineData("XIMG_0001.jpg", false)]
    public void Decide_Should_Match_Star_In_Any_Position(string fileName, bool shouldExclude)
    {
        var decision = DecideWithPatterns(fileName, "IMG_*.jpg");

        Assert.Equal(!shouldExclude, decision.ShouldProcess);
        Assert.Equal(shouldExclude ? "excluded_pattern" : "eligible", decision.Reason);
    }

    [Fact]
    public void Decide_Should_Match_Excluded_Pattern_Case_Insensitively()
    {
        var decision = DecideWithPatterns("a.jpg", "*.JPG");

        Assert.False(decision.ShouldProcess);
        Assert.Equal("excluded_pattern", decision.Reason);
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("aa.jpg", false)]
    [InlineData("a.jpg.jpg", false)]
    public void Decide_Should_Anchor_Excluded_Pattern_To_The_Whole_File_Name(string fileName, bool shouldExclude)
    {
        var decision = DecideWithPatterns(fileName, "a.jpg");

        Assert.Equal(!shouldExclude, decision.ShouldProcess);
        Assert.Equal(shouldExclude ? "excluded_pattern" : "eligible", decision.Reason);
    }

}
