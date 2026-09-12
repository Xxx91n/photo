using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 19（规则引擎单一真相源）UI 侧闭环:
/// FormatRuleRow.EffectiveArgs 复制表删除后，面板预览 = WipeRuleEngine（单一真相源）实时生成；
/// 勾选真实改变命令并触发 PropertyChanged；Save→Load→引擎 与面板预览一致（单一真相源端到端）。
/// </summary>
public sealed class RulesPanelEffectiveArgsTests
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "pp-rules19-" + Guid.NewGuid().ToString("N")[..8]);

    private sealed class TempDirHolder : IDisposable
    {
        public string Path { get; }
        public TempDirHolder()
        {
            Path = TempDir;
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Row_EffectiveArgs_Matches_Engine_Output()
    {
        using var dir = new TempDirHolder();
        var vm = new RulesPanelViewModel(new FormatRulesStore(dir.Path));

        foreach (var row in vm.Rules)
        {
            var expected = WipeRuleEngine.BuildEffectiveArgs(
                row.Family, row.StripAll, row.PreserveIcc, row.StripExif, row.StripXmp, row.StripIptc, row.StripTime);
            Assert.Equal(expected, row.EffectiveArgs);
        }
    }

    [Fact]
    public void Row_Defaults_Reproduce_Adr0053_Commands()
    {
        using var dir = new TempDirHolder();
        var vm = new RulesPanelViewModel(new FormatRulesStore(dir.Path));

        Assert.Equal("-all= --icc_profile:all -tagsfromfile @ -colorspacetags",
            vm.Rules.Single(r => r.Family == WipeFormatFamily.Jpeg).EffectiveArgs);
        Assert.Equal("-exif:all= -xmp:all= -iptc:all= -icc_profile:all=",
            vm.Rules.Single(r => r.Family == WipeFormatFamily.Raw).EffectiveArgs);
        Assert.Equal("-All= -Time:All=",
            vm.Rules.Single(r => r.Family == WipeFormatFamily.Video).EffectiveArgs);
        Assert.Equal("-all=",
            vm.Rules.Single(r => r.Family == WipeFormatFamily.Pdf).EffectiveArgs);
    }

    [Fact]
    public void Unchecking_PreserveIcc_Really_Changes_Command_And_Notifies()
    {
        using var dir = new TempDirHolder();
        var vm = new RulesPanelViewModel(new FormatRulesStore(dir.Path));
        var jpeg = vm.Rules.Single(r => r.Family == WipeFormatFamily.Jpeg);

        var notified = new List<string>();
        jpeg.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        jpeg.PreserveIcc = false;

        Assert.Contains(nameof(FormatRuleRow.EffectiveArgs), notified);
        Assert.Equal("-all= -tagsfromfile @ -colorspacetags", jpeg.EffectiveArgs);
        Assert.DoesNotContain("--icc_profile:all", jpeg.EffectiveArgs, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_Load_Engine_Matches_Panel_Preview()
    {
        // 单一真相源端到端: 面板勾选 → SaveCustomRules → rules.json → store.Load → 引擎，与预览一致。
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm = new RulesPanelViewModel(store);

        vm.Rules.Single(r => r.Family == WipeFormatFamily.Jpeg).PreserveIcc = false;
        vm.Rules.Single(r => r.Family == WipeFormatFamily.Video).StripTime = false;
        vm.SaveCustomRules();

        var stored = store.Load();
        foreach (var row in vm.Rules)
        {
            var engineResult = WipeRuleEngine.Resolve(row.Family, stored);
            Assert.Equal(row.EffectiveArgs, engineResult.EffectiveArgs);
        }

        // 引擎路径（bridge 实际调用形态）同样与预览一致。
        Assert.Equal("-all= -tagsfromfile @ -colorspacetags",
            WipeStrategyResolver.Resolve(@"C:\photos\a.jpg", stored).EffectiveArgs);
        Assert.Equal("-All=",
            WipeStrategyResolver.Resolve(@"C:\videos\b.mp4", stored).EffectiveArgs);
    }

    [Fact]
    public void Toggle_Group_Switches_Change_Raw_Preview()
    {
        using var dir = new TempDirHolder();
        var vm = new RulesPanelViewModel(new FormatRulesStore(dir.Path));
        var raw = vm.Rules.Single(r => r.Family == WipeFormatFamily.Raw);

        raw.StripIptc = false;
        Assert.Equal("-exif:all= -xmp:all= -icc_profile:all=", raw.EffectiveArgs);

        raw.StripExif = false;
        raw.StripXmp = false;
        Assert.Equal("-icc_profile:all=", raw.EffectiveArgs);
    }
}
