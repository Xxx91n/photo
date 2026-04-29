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
}
