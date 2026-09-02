using System.Text.Json;

namespace PhotoPrivacy.Core.Configuration;

/// <summary>
/// ADR 0053 M6d: Custom wipe rules persistent store.
/// Config path: config/rules.json
/// 票18: canonical schema — one "{family}_{group}" bool key per metadata group
/// (strip_all / preserve_icc / strip_exif / strip_xmp / strip_iptc / strip_time)
/// for every panel family, so Save→Load round-trips all group toggles, not just strip_all.
/// </summary>
public sealed class FormatRulesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Per-family coded defaults, mirroring the rules panel row defaults (ADR 0053 M6).</summary>
    private static readonly (string FamilyKey, bool StripAll, bool PreserveIcc, bool StripExif, bool StripXmp, bool StripIptc, bool StripTime)[] FamilyDefaults =
    {
        ("jpeg", true, true, false, false, false, false),
        ("raw", false, false, true, true, true, false),
        ("video", true, false, false, false, false, true),
        ("pdf", true, false, false, false, false, false),
        ("eps", true, false, false, false, false, false),
    };

    /// <summary>rules.json key for one family/group toggle.</summary>
    public static string Key(string familyKey, string group) => $"{familyKey}_{group}";

    public string StorePath { get; }

    public FormatRulesStore(string configDir)
    {
        StorePath = Path.Combine(configDir, "rules.json");
    }

    public Dictionary<string, bool> LoadDefaults()
    {
        var defaults = new Dictionary<string, bool>();
        foreach (var (familyKey, stripAll, preserveIcc, stripExif, stripXmp, stripIptc, stripTime) in FamilyDefaults)
        {
            defaults[Key(familyKey, "strip_all")] = stripAll;
            defaults[Key(familyKey, "preserve_icc")] = preserveIcc;
            defaults[Key(familyKey, "strip_exif")] = stripExif;
            defaults[Key(familyKey, "strip_xmp")] = stripXmp;
            defaults[Key(familyKey, "strip_iptc")] = stripIptc;
            defaults[Key(familyKey, "strip_time")] = stripTime;
        }
        return defaults;
    }

    public Dictionary<string, bool> Load()
    {
        if (!File.Exists(StorePath))
        {
            return LoadDefaults();
        }
        try
        {
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json, JsonOpts) ?? LoadDefaults();
        }
        catch
        {
            return LoadDefaults();
        }
    }

    public void Save(Dictionary<string, bool> rules)
    {
        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(rules, JsonOpts);
        // ponytail: atomic write (temp + rename)
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, StorePath, overwrite: true);
    }

    public void ResetToDefaults()
    {
        if (File.Exists(StorePath))
        {
            File.Delete(StorePath);
        }
    }
}
