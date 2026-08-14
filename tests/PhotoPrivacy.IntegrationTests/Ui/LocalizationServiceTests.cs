using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// ADR 0047 test closure — verify all 10 locale JSON files have identical key sets
/// after recursive flatten, so no language is missing keys at runtime.
/// Reads both embedded resources and on-disk files in ExeLocaleDirectory.
/// </summary>
public sealed class LocalizationServiceTests
{
    private static readonly string[] ExpectedLocales =
    [
        "ar", "de", "en", "es", "fr", "ja", "ko", "pt", "ru", "zh-CN"
    ];

    [Fact]
    public void All_Ten_Locales_Present_On_Disk()
    {
        var dir = LocalePathResolver.ExeLocaleDirectory;
        Assert.True(Directory.Exists(dir), $"Locale dir not found: {dir}");
        foreach (var locale in ExpectedLocales)
        {
            var file = Path.Combine(dir, locale + ".json");
            Assert.True(File.Exists(file), $"Missing locale file: {file}");
        }
    }

    [Fact]
    public void All_Locales_Have_Identical_Flat_Key_Sets_As_English()
    {
        var dir = LocalePathResolver.ExeLocaleDirectory;
        var enKeys = FlattenKeys(File.ReadAllText(Path.Combine(dir, "en.json")));
        Assert.NotEmpty(enKeys);

        foreach (var locale in ExpectedLocales)
        {
            if (locale == "en") continue;
            var content = File.ReadAllText(Path.Combine(dir, locale + ".json"));
            var keys = FlattenKeys(content);
            Assert.NotEmpty(keys);

            var missing = enKeys.Except(keys).OrderBy(k => k).ToList();
            var extra = keys.Except(enKeys).OrderBy(k => k).ToList();

            Assert.True(missing.Count == 0,
                $"{locale}: missing {missing.Count} keys vs en: [{string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? "..." : "")}]");
            Assert.True(extra.Count == 0,
                $"{locale}: extra {extra.Count} keys vs en: [{string.Join(", ", extra.Take(10))}{(extra.Count > 10 ? "..." : "")}]");
        }
    }

    [Fact]
    public void Embedded_Resources_Contain_All_Ten_Locales()
    {
        var asm = typeof(PhotoPrivacy.Ui.Localization.LocalizationService).Assembly;
        var resourceNames = asm.GetManifestResourceNames()
            .Where(n => n.Contains("Locales.", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json"))
            .ToList();

        foreach (var locale in ExpectedLocales)
        {
            var expected = $"Locales.{locale}.json";
            Assert.True(resourceNames.Any(r => r.EndsWith(expected, StringComparison.OrdinalIgnoreCase)),
                $"Embedded resource not found: {expected}. Found: [{string.Join(", ", resourceNames)}]");
        }

        var enResource = resourceNames.First(r => r.EndsWith("Locales.en.json", StringComparison.OrdinalIgnoreCase));
        var enKeys = FlattenKeys(ReadResourceText(asm, enResource));

        foreach (var locale in ExpectedLocales)
        {
            if (locale == "en") continue;
            var resName = resourceNames.First(r => r.EndsWith($"Locales.{locale}.json", StringComparison.OrdinalIgnoreCase));
            var keys = FlattenKeys(ReadResourceText(asm, resName));

            var missing = enKeys.Except(keys).ToList();
            var extra = keys.Except(enKeys).ToList();

            Assert.True(missing.Count == 0,
                $"embedded {locale}: missing {missing.Count} keys: [{string.Join(", ", missing.Take(10))}]");
            Assert.True(extra.Count == 0,
                $"embedded {locale}: extra {extra.Count} keys: [{string.Join(", ", extra.Take(10))}]");
        }
    }

    private static HashSet<string> FlattenKeys(string json)
    {
        var node = JsonNode.Parse(json);
        var dict = new Dictionary<string, string>();
        if (node is JsonObject obj)
        {
            FlattenNode(obj, string.Empty, dict);
        }
        else
        {
            var flat = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (flat is not null)
                foreach (var k in flat.Keys) dict[k] = flat[k];
        }
        return dict.Keys.ToHashSet();
    }

    private static void FlattenNode(JsonObject obj, string prefix, Dictionary<string, string> dict)
    {
        foreach (var kvp in obj)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
            if (kvp.Value is JsonValue val && val.TryGetValue<string>(out var s))
            {
                dict[key] = s;
            }
            else if (kvp.Value is JsonObject child)
            {
                FlattenNode(child, key, dict);
            }
        }
    }

    private static string ReadResourceText(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
