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
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = @"D:\clean"
            },
            Watch = AppConfig.Default.Watch with { HotFolder = @"D:\hot" }
        };

        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\album\a.jpg");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(@"D:\clean\album\a.jpg", decision.OutputPath);
    }

    [Fact]
    public void Decide_Should_Create_Bak_Path_When_Backup_Enabled()
    {
        var cfg = AppConfig.Default with
        {
            Backup = AppConfig.Default.Backup with { Enabled = true, Suffix = ".bak" },
            Watch = AppConfig.Default.Watch with { HotFolder = @"D:\hot" }
        };

        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(@"D:\hot\a.jpg");

        Assert.True(decision.CreateBackup);
        Assert.Equal(@"D:\hot\bak\a.jpg.bak", decision.BackupPath);
    }

    [Theory]
    [InlineData(".cr3")]
    [InlineData(".arw")]
    [InlineData(".nef")]
    [InlineData(".tiff")]
    [InlineData(".webp")]
    [InlineData(".avif")]
    [InlineData(".psd")]
    [InlineData(".ai")]
    [InlineData(".eps")]
    [InlineData(".heif")]
    [InlineData(".dng")]
    [InlineData(".raf")]
    [InlineData(".rw2")]
    [InlineData(".orf")]
    [InlineData(".mov")]
    [InlineData(".gif")]
    [InlineData(".heic")]
    [InlineData(".m4v")]
    [InlineData(".3gp")]
    [InlineData(".cr2")]
    [InlineData(".ori")]
    [InlineData(".3g2")]
    [InlineData(".sr2")]
    [InlineData(".mpo")]
    [InlineData(".x3f")]
    [InlineData(".crm")]
    [InlineData(".mie")]
    public void Decide_Should_Process_Writable_Extensions(string ext)
    {
        var cfg = AppConfig.Default;
        var engine = new RuleEngine(cfg);
        var decision = engine.Decide(Path.Combine(@"D:\hot", $"test{ext}"));

        Assert.True(decision.ShouldProcess);
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
        var decision = engine.Decide(@"D:hota.jpg");

        Assert.Null(decision.BackupPath);
    }

}
