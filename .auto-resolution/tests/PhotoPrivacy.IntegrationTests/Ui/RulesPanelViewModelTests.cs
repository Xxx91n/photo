using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// ADR 0053 M6b-g: RulesPanelViewModel Expert gate + FormatRulesStore round-trip test 闭环.
/// 票18: 全逐组开关（strip_all/preserve_icc/strip_exif/strip_xmp/strip_iptc/strip_time）
/// 的 Save→Load 往返忠实 + 键名错配修复 + defaults 回填锁定。
/// </summary>
public sealed class RulesPanelViewModelTests
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "pp-rules-test-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly string[] GroupKeys =
    {
        "strip_all", "preserve_icc", "strip_exif", "strip_xmp", "strip_iptc", "strip_time",
    };

    // 测试侧独立拼键，不信任被测实现自己的 Key() 拼装
    private static string FamKey(FormatRuleRow row) => row.Family.ToString().ToLowerInvariant();

    [Fact]
    public void ExpertGate_NonExpert_CannotEditDangerousColumns()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm = new RulesPanelViewModel(store);
        Assert.False(vm.IsExpertMode);
        Assert.False(vm.CanEditDangerousColumns);
    }

    [Fact]
    public void ExpertGate_TogglingExpert_RaisesPropertyChanged()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm = new RulesPanelViewModel(store);
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        vm.IsExpertMode = true;
        Assert.Contains("IsExpertMode", changedProps);
        Assert.Contains("CanEditDangerousColumns", changedProps);
        Assert.True(vm.CanEditDangerousColumns);
    }

    [Fact]
    public void SearchFilter_Filters_ByExtension()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm = new RulesPanelViewModel(store);
        var initialCount = vm.Rules.Count;

        vm.SearchFilter = "jpg";
        var filtered = vm.Rules.Count;
        Assert.True(filtered > 0);
        Assert.True(filtered <= initialCount);
    }

    [Fact]
    public void RulesStore_RoundTrip_SaveLoadReset()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);

        var defaults = store.Load();
        Assert.True(defaults["jpeg_strip_all"]);

        var custom = new Dictionary<string, bool>(defaults) { ["jpeg_strip_all"] = false };
        store.Save(custom);

        var loaded = store.Load();
        Assert.False(loaded["jpeg_strip_all"]);

        store.ResetToDefaults();
        var reset = store.Load();
        Assert.True(reset["jpeg_strip_all"]);
    }

    [Fact]
    public void RulesStore_LoadingMissingFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-empty-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new FormatRulesStore(dir);
            var loaded = store.Load();
            Assert.True(loaded.Count > 0);
            Assert.True(loaded["jpeg_strip_all"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // --- 票18: 全逐组开关往返 ---

    [Fact]
    public void RulesStore_RoundTrip_FullSchemaSaveLoadIdentical()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);

        var original = new Dictionary<string, bool>();
        var families = new[] { "jpeg", "raw", "video", "pdf", "eps" };
        for (var i = 0; i < families.Length; i++)
        {
            for (var j = 0; j < GroupKeys.Length; j++)
            {
                original[families[i] + "_" + GroupKeys[j]] = (i + j) % 2 == 0;
            }
        }

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void LoadDefaults_CoversEveryPanelFamilyAndGroup()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var defaults = store.LoadDefaults();

        foreach (var family in new[] { "jpeg", "raw", "video", "pdf", "eps" })
        {
            foreach (var group in GroupKeys)
            {
                Assert.True(defaults.ContainsKey(family + "_" + group), "missing default key " + family + "_" + group);
            }
        }
        Assert.Equal(30, defaults.Count);
        Assert.All(defaults.Keys, k => Assert.DoesNotContain("/", k));
    }

    [Fact]
    public void SaveCustomRules_PersistsEveryGroupToggleForEveryRow()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm = new RulesPanelViewModel(store);

        for (var i = 0; i < vm.Rules.Count; i++)
        {
            var row = vm.Rules[i];
            row.StripAll = i % 2 == 0;
            row.PreserveIcc = i % 3 == 0;
            row.StripExif = i % 2 == 1;
            row.StripXmp = i % 3 == 1;
            row.StripIptc = i % 2 == 0 && i > 0;
            row.StripTime = i % 3 == 2;
        }

        vm.SaveCustomRules();

        var stored = store.Load();
        Assert.Equal(vm.Rules.Count * GroupKeys.Length, stored.Count);
        Assert.All(stored.Keys, k => Assert.DoesNotContain("/", k));
        for (var i = 0; i < vm.Rules.Count; i++)
        {
            var row = vm.Rules[i];
            Assert.Equal(row.StripAll, stored[FamKey(row) + "_strip_all"]);
            Assert.Equal(row.PreserveIcc, stored[FamKey(row) + "_preserve_icc"]);
            Assert.Equal(row.StripExif, stored[FamKey(row) + "_strip_exif"]);
            Assert.Equal(row.StripXmp, stored[FamKey(row) + "_strip_xmp"]);
            Assert.Equal(row.StripIptc, stored[FamKey(row) + "_strip_iptc"]);
            Assert.Equal(row.StripTime, stored[FamKey(row) + "_strip_time"]);
        }
    }

    [Fact]
    public void SaveThenNewViewModel_RestoresEveryToggle()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);
        var vm1 = new RulesPanelViewModel(store);

        for (var i = 0; i < vm1.Rules.Count; i++)
        {
            var row = vm1.Rules[i];
            row.StripAll = i % 2 == 1;
            row.PreserveIcc = i % 2 == 0;
            row.StripExif = i % 3 == 2;
            row.StripXmp = i % 3 == 0;
            row.StripIptc = i % 2 == 1 && i < 4;
            row.StripTime = i % 3 == 1;
        }
        vm1.SaveCustomRules();

        var vm2 = new RulesPanelViewModel(store);
        Assert.Equal(vm1.Rules.Count, vm2.Rules.Count);
        for (var i = 0; i < vm1.Rules.Count; i++)
        {
            var a = vm1.Rules[i];
            var b = vm2.Rules.Single(r => r.Family == a.Family);
            Assert.Equal(a.StripAll, b.StripAll);
            Assert.Equal(a.PreserveIcc, b.PreserveIcc);
            Assert.Equal(a.StripExif, b.StripExif);
            Assert.Equal(a.StripXmp, b.StripXmp);
            Assert.Equal(a.StripIptc, b.StripIptc);
            Assert.Equal(a.StripTime, b.StripTime);
        }
    }

    [Fact]
    public void LoadRules_BackfillsRowValuesFromStoredFile_WithoutCrossFamilyLeak()
    {
        using var dir = new TempDirHolder();
        var store = new FormatRulesStore(dir.Path);

        var seeded = store.LoadDefaults();
        seeded["video_strip_all"] = false;
        seeded["jpeg_strip_xmp"] = true;
        store.Save(seeded);

        var vm = new RulesPanelViewModel(store);

        var video = vm.Rules.Single(r => r.Family == WipeFormatFamily.Video);
        Assert.False(video.StripAll);
        Assert.True(video.IsEnabled);
        var raw = vm.Rules.Single(r => r.Family == WipeFormatFamily.Raw);
        Assert.True(raw.StripExif);
        var jpeg = vm.Rules.Single(r => r.Family == WipeFormatFamily.Jpeg);
        Assert.True(jpeg.StripXmp);
        Assert.True(jpeg.StripAll);
    }

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
}
