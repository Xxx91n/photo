using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// ADR 0053 M6b-g: RulesPanelViewModel Expert gate + FormatRulesStore round-trip test 闭环.
/// </summary>
public sealed class RulesPanelViewModelTests
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "pp-rules-test-" + Guid.NewGuid().ToString("N")[..8]);

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
