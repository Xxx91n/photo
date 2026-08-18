using System.Text.Json;

namespace PhotoPrivacy.Core.Configuration;

/// <summary>
/// ADR 0053 M6d: Custom wipe rules persistent store.
/// Config path: config/rules.json
/// </summary>
public sealed class FormatRulesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string StorePath { get; }

    public FormatRulesStore(string configDir)
    {
        StorePath = Path.Combine(configDir, "rules.json");
    }

    public Dictionary<string, bool> LoadDefaults()
    {
        return new()
        {
            ["jpeg_strip_all"] = true,
            ["jpeg_preserve_icc"] = true,
            ["raw_strip_exif_xmp_iptc"] = true,
            ["video_strip_all_time"] = true,
            ["pdf_strip_all"] = true,
            ["pdf_requires_warning"] = true,
        };
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
