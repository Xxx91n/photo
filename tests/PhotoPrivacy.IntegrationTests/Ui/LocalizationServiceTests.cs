using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoPrivacy.Core.Constants;
using PhotoPrivacy.Ui.Localization;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// ADR 0047 test closure — verify all 10 locale JSON files have identical key sets
/// after recursive flatten, so no language is missing keys at runtime.
/// Reads both embedded resources and on-disk files in ExeLocaleDirectory.
/// Plus runtime-switch tests that prove SwitchLocale actually mutates _currentStrings
/// and raises CultureChanged (industry-template regression guard).
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
        var asm = typeof(LocalizationService).Assembly;
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

    [Fact]
    public void SwitchLocale_To_English_Changes_Translated_Strings()
    {
        var svc = LocalizationService.Instance;
        try
        {
            svc.SwitchLocale("en");
            svc.SwitchLocale("zh-CN");
            var zhValue = svc.Get("nav.config");

            svc.SwitchLocale("en");
            var enValue = svc.Get("nav.config");

            Assert.NotEqual(zhValue, enValue);
            Assert.Equal("Settings", enValue);
            Assert.Equal("配置", zhValue);
        }
        finally
        {
            svc.SwitchLocale("zh-CN"); // restore default for other tests sharing the singleton
        }
    }

    [Fact]
    public void SwitchLocale_Raises_CultureChanged_Event()
    {
        var svc = LocalizationService.Instance;
        svc.SwitchLocale("zh-CN");
        string? captured = null;
        EventHandler<string> handler = (_, locale) => captured = locale;
        svc.CultureChanged += handler;
        try
        {
            svc.SwitchLocale("ja");
            Assert.Equal("ja", captured);
            Assert.Equal("ja", svc.CurrentLocale);
        }
        finally
        {
            svc.CultureChanged -= handler;
            svc.SwitchLocale("zh-CN");
        }
    }

    [Fact]
    public void SwitchLocale_Same_Locale_Does_Not_Raise_Event_Twice()
    {
        var svc = LocalizationService.Instance;
        svc.SwitchLocale("zh-CN");
        var raised = 0;
        EventHandler<string> handler = (_, _) => raised++;
        svc.CultureChanged += handler;
        try
        {
            svc.SwitchLocale("en");   // first switch: en != zh-CN -> raise
            Assert.Equal(1, raised);
            svc.SwitchLocale("en");   // same locale -> no raise (guard)
            Assert.Equal(1, raised);
            svc.SwitchLocale("ko");   // different -> raise
            Assert.Equal(2, raised);
        }
        finally
        {
            svc.CultureChanged -= handler;
            svc.SwitchLocale("zh-CN");
        }
    }

    [Fact]
    public void SwitchLocale_Raises_Indexer_PropertyChanged_For_Binding_Refresh()
    {
        // Industry-template regression guard: Avalonia's binding engine refreshes
        // every TextBlock.Text bound via {ex:Localize key} (which is Binding to
        // LocalizationService[key]) ONLY when the source raises PropertyChanged
        // for the indexer "Item[]" / "Item". Without this, runtime language switch
        // leaves AXAML text stale (the user-visible bug this test guards against).
        var svc = LocalizationService.Instance;
        svc.SwitchLocale("en"); // prime away from any prior state

        var raisedItemArray = 0;
        var raisedItem = 0;
        var raisedCurrentLocale = 0;
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == "Item[]") raisedItemArray++;
            if (e.PropertyName == "Item") raisedItem++;
            if (e.PropertyName == nameof(LocalizationService.CurrentLocale)) raisedCurrentLocale++;
        };
        svc.PropertyChanged += handler;
        try
        {
            svc.SwitchLocale("zh-CN"); // different from en -> must raise
            Assert.Equal(1, raisedItemArray);
            Assert.Equal(1, raisedItem);
            Assert.Equal(1, raisedCurrentLocale);
        }
        finally
        {
            svc.PropertyChanged -= handler;
            svc.SwitchLocale("zh-CN");
        }
    }

    [Fact]
    public void RuntimeStatus_Keys_Translate_Correctly_Across_Locales()
    {
        // Regression guard: "托盘运行中/Tray running" and "已暂停/Paused" must
        // be Get-able for every locale, so BuildRuntimeStatusText can produce
        // the right left-top status text on language switch without restart.
        var svc = LocalizationService.Instance;
        var locales = new[] { "zh-CN", "en", "ja", "ko", "de", "fr", "es", "pt", "ru", "ar" };
        try
        {
            foreach (var loc in locales)
            {
                svc.SwitchLocale(loc);
                var running = svc.Get("status.tray_running");
                var paused = svc.Get("status.tray_paused");
                Assert.False(string.IsNullOrEmpty(running), $"status.tray_running empty for {loc}");
                Assert.False(string.IsNullOrEmpty(paused), $"status.tray_paused empty for {loc}");
                // Must not fall back to the key itself for any locale
                Assert.NotEqual("status.tray_running", running);
                Assert.NotEqual("status.tray_paused", paused);
            }
        }
        finally
        {
            svc.SwitchLocale("zh-CN");
        }
    }

    [Fact]
    public void ComboBox_Option_Keys_Translate_Correctly_Across_Locales()
    {
        // Regression guard: theme and loglevel ComboBox item texts must translate
        // for every locale. Covers the AXAML change from ComboBoxItem.Content
        // to inner TextBlock.Text (the fix for "dropdown not refreshing on switch").
        var svc = LocalizationService.Instance;
        var keys = new[]
        {
            "theme.option.system", "theme.option.light", "theme.option.dark",
            "loglevel.option.all", "loglevel.option.info", "loglevel.option.debug",
            "loglevel.option.warn", "loglevel.option.error",
        };
        var locales = new[] { "zh-CN", "en", "ja", "ko", "de", "fr", "es", "pt", "ru", "ar" };
        try
        {
            foreach (var loc in locales)
            {
                svc.SwitchLocale(loc);
                foreach (var key in keys)
                {
                    var val = svc.Get(key);
                    Assert.False(string.IsNullOrEmpty(val), $"{key} empty for {loc}");
                    Assert.NotEqual(key, val);
                }
            }
        }
        finally
        {
            svc.SwitchLocale("zh-CN");
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
            var flat = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
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
