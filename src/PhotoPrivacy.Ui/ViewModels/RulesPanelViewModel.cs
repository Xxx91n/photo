using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Ui.ViewModels;

/// <summary>
/// ADR 0053 M6b-M6f: Rules Panel ViewModel with Expert mode gate.
/// BleachBit 5.1 pattern: ToggleSwitch + confirmation dialog, dangerous columns disabled in non-expert.
/// 票18: LoadRules backfills every group toggle from the store; SaveCustomRules persists all six toggles.
/// </summary>
public sealed class RulesPanelViewModel : INotifyPropertyChanged
{
    private readonly FormatRulesStore _store;
    private bool _isExpertMode;
    private string _searchFilter = string.Empty;
    private string _saveStatus = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpertMode
    {
        get => _isExpertMode;
        set
        {
            if (_isExpertMode != value)
            {
                _isExpertMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditDangerousColumns));
            }
        }
    }

    public bool CanEditDangerousColumns => _isExpertMode;

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter != value)
            {
                _searchFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    public string SaveStatus
    {
        get => _saveStatus;
        set => SetField(ref _saveStatus, value);
    }

    public ObservableCollection<FormatRuleRow> Rules { get; } = new();
    private readonly List<FormatRuleRow> _allRules = new();

    // 票 26（ADR 0062）: 规则空态占位开关 — ApplyFilter 收口处联动刷新（无事件订阅，防泄漏）。
    private bool _hasNoVisibleRules;
    public bool HasNoVisibleRules
    {
        get => _hasNoVisibleRules;
        private set => SetField(ref _hasNoVisibleRules, value);
    }

    // 票 06（ui-craft2 / 规范 §4 E5）：空态成因标志 —— 搜索过滤激活态；空态层按成因二分
    // 「真空 / 过滤无结果」双文案（atomcode 调研裁决二；同构 MainWindowViewModel.LogLevelFilterActive，票 05）。
    public bool SearchFilterActive => !string.IsNullOrEmpty(_searchFilter);

    public RulesPanelViewModel(FormatRulesStore store)
    {
        _store = store;
        LoadRules();
    }

    private void LoadRules()
    {
        var defaults = _store.Load();
        _allRules.Add(new FormatRuleRow("JPEG", WipeFormatFamily.Jpeg, "jpg jpeg jpe", stripAll: true, preserveIcc: true)
        {
            IsDangerous = false,
            IsEnabled = true,
        });
        _allRules.Add(new FormatRuleRow("RAW", WipeFormatFamily.Raw, "cr2 cr3 arw nef orf", stripExif: true, stripXmp: true, stripIptc: true)
        {
            IsDangerous = false,
            IsEnabled = true,
        });
        _allRules.Add(new FormatRuleRow("Video", WipeFormatFamily.Video, "mov mp4 m4v", stripAll: true, stripTime: true)
        {
            IsDangerous = true,
            // 票18: IsEnabled 不落盘；此前误读 RAW 族键 raw_strip_exif_xmp_iptc（键名错配），现按默认启用
            IsEnabled = true,
        });
        _allRules.Add(new FormatRuleRow("PDF", WipeFormatFamily.Pdf, "pdf", stripAll: true)
        {
            IsDangerous = true,
            RequiresUserWarning = true,
            IsEnabled = _isExpertMode,
        });
        _allRules.Add(new FormatRuleRow("EPS/PS", WipeFormatFamily.Eps, "eps ps ai", stripAll: true)
        {
            IsDangerous = true,
            RequiresUserWarning = true,
            IsEnabled = _isExpertMode,
        });
        BackfillFromStore(defaults);
        ApplyFilter();
    }

    /// <summary>票18: defaults 正确回填——每行逐组开关从存储值覆盖，缺键时保留行编码默认值。</summary>
    private void BackfillFromStore(Dictionary<string, bool> stored)
    {
        foreach (var row in _allRules)
        {
            row.StripAll = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "strip_all"), row.StripAll);
            row.PreserveIcc = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "preserve_icc"), row.PreserveIcc);
            row.StripExif = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "strip_exif"), row.StripExif);
            row.StripXmp = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "strip_xmp"), row.StripXmp);
            row.StripIptc = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "strip_iptc"), row.StripIptc);
            row.StripTime = stored.GetValueOrDefault(FormatRulesStore.Key(row.FamilyKey, "strip_time"), row.StripTime);
        }
    }

    private void ApplyFilter()
    {
        Rules.Clear();
        foreach (var row in _allRules)
        {
            if (string.IsNullOrEmpty(_searchFilter) ||
                row.Extensions.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                row.FamilyName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            {
                Rules.Add(row);
            }
        }
        HasNoVisibleRules = Rules.Count == 0;
        OnPropertyChanged(nameof(SearchFilterActive));
    }

    public void SaveCustomRules()
    {
        var rules = new Dictionary<string, bool>();
        foreach (var row in _allRules)
        {
            // 票18: 全部逐组开关持久化（此前仅 strip_all）；FamilyKey 取自枚举，杜绝 "eps/ps" 斜杠键
            rules[FormatRulesStore.Key(row.FamilyKey, "strip_all")] = row.StripAll;
            rules[FormatRulesStore.Key(row.FamilyKey, "preserve_icc")] = row.PreserveIcc;
            rules[FormatRulesStore.Key(row.FamilyKey, "strip_exif")] = row.StripExif;
            rules[FormatRulesStore.Key(row.FamilyKey, "strip_xmp")] = row.StripXmp;
            rules[FormatRulesStore.Key(row.FamilyKey, "strip_iptc")] = row.StripIptc;
            rules[FormatRulesStore.Key(row.FamilyKey, "strip_time")] = row.StripTime;
        }
        _store.Save(rules);
        SaveStatus = "Saved";
    }

    public void ResetToDefaults()
    {
        _store.ResetToDefaults();
        LoadRules();
        SaveStatus = "Reset to defaults";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class FormatRuleRow : INotifyPropertyChanged
{
    private bool _stripAll;
    private bool _preserveIcc;
    private bool _stripExif;
    private bool _stripXmp;
    private bool _stripIptc;
    private bool _stripTime;
    private bool _isEnabled = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FamilyName { get; }
    public WipeFormatFamily Family { get; }

    /// <summary>票18: rules.json 键前缀（枚举名小写），与 FormatRulesStore.Key 共用一套 schema。</summary>
    public string FamilyKey => Family.ToString().ToLowerInvariant();

    public string Extensions { get; }
    public bool IsDangerous { get; set; }
    public bool RequiresUserWarning { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public bool StripAll
    {
        get => _stripAll;
        set { if (SetField(ref _stripAll, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public bool PreserveIcc
    {
        get => _preserveIcc;
        set { if (SetField(ref _preserveIcc, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public bool StripExif
    {
        get => _stripExif;
        set { if (SetField(ref _stripExif, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public bool StripXmp
    {
        get => _stripXmp;
        set { if (SetField(ref _stripXmp, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public bool StripIptc
    {
        get => _stripIptc;
        set { if (SetField(ref _stripIptc, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public bool StripTime
    {
        get => _stripTime;
        set { if (SetField(ref _stripTime, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveArgs))); }
    }

    public string WarningLevel => (Family, RequiresUserWarning) switch
    {
        (WipeFormatFamily.Pdf, true) => "PDF",
        (WipeFormatFamily.Eps, true) => "EPS",
        _ => "",
    };

    public FormatRuleRow(string familyName, WipeFormatFamily family, string extensions,
        bool stripAll = false, bool preserveIcc = false, bool stripExif = false,
        bool stripXmp = false, bool stripIptc = false, bool stripTime = false)
    {
        FamilyName = familyName;
        Family = family;
        Extensions = extensions;
        _stripAll = stripAll;
        _preserveIcc = preserveIcc;
        _stripExif = stripExif;
        _stripXmp = stripXmp;
        _stripIptc = stripIptc;
        _stripTime = stripTime;
    }

    /// <summary>
    /// 票 19: family→命令复制表已删除。参数预览由 WipeRuleEngine（单一真相源）从本行勾选实时生成，
    /// 与 Worker 端擦除命令同源；勾选任一开关即真实改变命令（含 DataGrid 预览列与实际执行一致）。
    /// </summary>
    public string EffectiveArgs => WipeRuleEngine.BuildEffectiveArgs(
        Family, _stripAll, _preserveIcc, _stripExif, _stripXmp, _stripIptc, _stripTime);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
