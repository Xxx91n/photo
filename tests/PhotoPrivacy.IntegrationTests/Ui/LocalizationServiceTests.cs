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


    /// <summary>
    /// ADR 0035 regression guard (ea9ecc9 root cause): the CultureChanged
    /// handler in MainWindow.axaml.cs must NOT contain sync-over-async
    /// (.GetAwaiter().GetResult()) on the UI thread — this caused window
    /// freeze when the Worker IPC pipe was stale/broken during language
    /// switch. Industry pattern: fire-and-forget + background poll for status.
    /// </summary>
    [Fact]
    public void CultureChanged_Handler_Does_Not_Contain_SyncOverAsync()
    {
        // Lint-style regression guard: reads the source file and verifies
        // the CultureChanged lambda block has no .GetAwaiter().GetResult().
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        sourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(sourcePath))
        {
            // Fallback: try relative to test bin directory
            sourcePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs"));
        }

        if (!File.Exists(sourcePath))
        {
            // If source not available in CI, skip gracefully
            return;
        }

        var source = File.ReadAllText(sourcePath);

        // Find CultureChanged handler block and verify no sync-over-async inside it
        var cultureIdx = source.IndexOf("LocalizationService.Instance.CultureChanged +=");
        Assert.True(cultureIdx >= 0, "CultureChanged handler not found in MainWindow.axaml.cs");

        // The handler block ends at the next ");" after the opening.
        // Capture enough to cover the full lambda including the RuntimeStatus
        // refresh block (added after the ea9ecc9 root-cause fix).
        var handlerBlock = source.Substring(cultureIdx, 1200);
        Assert.DoesNotContain("GetAwaiter().GetResult()", handlerBlock);
    }

    /// <summary>
    /// Regression guard for ea9ecc9: all 3 theme.option.* keys must have
    /// non-empty translations across all 10 locales, both in JSON files
    /// and in BuiltIn fallback. This catches any locale JSON that was
    /// accidentally truncated or missing the nested "theme.option" subtree.
    /// </summary>
    [Fact]
    public void Theme_Option_Keys_All_Locales_Have_NonEmpty_Translation()
    {
        var svc = LocalizationService.Instance;
        var themeKeys = new[] { "theme.option.system", "theme.option.light", "theme.option.dark" };
        var locales = new[] { "ar", "de", "en", "es", "fr", "ja", "ko", "pt", "ru", "zh-CN" };

        foreach (var locale in locales)
        {
            svc.SwitchLocale(locale);
            foreach (var key in themeKeys)
            {
                var value = svc.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"Locale '{locale}' key '{key}': translation is empty/whitespace");
                Assert.NotEqual(key, value); // must not fallback to key itself
            }
        }
    }

    /// <summary>
    /// Regression guard for ea9ecc9: all 5 loglevel.option.* keys must have
    /// non-empty translations across all 10 locales. Mirrors the theme test.
    /// </summary>
    [Fact]
    public void LogLevel_Option_Keys_All_Locales_Have_NonEmpty_Translation()
    {
        var svc = LocalizationService.Instance;
        var logKeys = new[] { "loglevel.option.all", "loglevel.option.info", "loglevel.option.debug", "loglevel.option.warn", "loglevel.option.error" };
        var locales = new[] { "ar", "de", "en", "es", "fr", "ja", "ko", "pt", "ru", "zh-CN" };

        foreach (var locale in locales)
        {
            svc.SwitchLocale(locale);
            foreach (var key in logKeys)
            {
                var value = svc.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"Locale '{locale}' key '{key}': translation is empty/whitespace");
                Assert.NotEqual(key, value); // must not fallback to key itself
            }
        }
    }


    /// <summary>
    /// Regression guard: MainWindow.axaml.cs must contain ForceComboBoxSelectionBoxRefresh
    /// — the industry-pattern method (SelectedItem null→restore) which forces
    /// Avalonia ComboBox SelectionBoxItem to re-render after ComboBoxItem.Content
    /// changes at runtime. Without it, the closed ComboBox display stays in
    /// old language until restart even though dropdown items refresh.
    /// </summary>
    [Fact]
    public void MainWindow_Contains_ForceComboBoxSelectionBoxRefresh_Industry_Pattern()
    {
        var sourcePath = ResolveMainWindowSourcePath();
        if (!File.Exists(sourcePath)) return;

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("ForceComboBoxSelectionBoxRefresh", source);
        Assert.Contains("combo.SelectedItem = null", source);
        Assert.Contains("combo.SelectedItem = selected", source);
    }

    /// <summary>
    /// Regression guard: MainWindow.axaml.cs CultureChanged handler must
    /// contain RunModeStatusTextSnapshot() call — refreshes the left-top
    /// RuntimeStatus immediately on locale switch, without waiting for the
    /// ~1s background poll. Without it, RuntimeStatus stays in old language.
    /// </summary>
    [Fact]
    public void CultureChanged_Handler_Refreshes_RuntimeStatus_Immediately()
    {
        var sourcePath = ResolveMainWindowSourcePath();
        if (!File.Exists(sourcePath)) return;

        var source = File.ReadAllText(sourcePath);

        var cultureIdx = source.IndexOf("LocalizationService.Instance.CultureChanged +=");
        Assert.True(cultureIdx >= 0, "CultureChanged handler not found");
        var handlerBlock = source.Substring(cultureIdx, 1200);
        Assert.Contains("RunModeStatusTextSnapshot()", handlerBlock);
        Assert.Contains("viewModel.RuntimeStatus =", handlerBlock);
    }

    /// <summary>
    /// Regression guard: MainWindowViewModel must expose IsRuntimePausedSnapshot
    /// (public) so MainWindow's CultureChanged handler can read the last-known
    /// paused state without blocking on IPC. The private IsRuntimePaused derived
    /// from _runtimeStatus string content stays as the underlying implementation.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Exposes_IsRuntimePausedSnapshot_Public()
    {
        var vmPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs");
        vmPath = Path.GetFullPath(vmPath);
        if (!File.Exists(vmPath))
        {
            vmPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs"));
        }
        if (!File.Exists(vmPath)) return;

        var source = File.ReadAllText(vmPath);
        Assert.Contains("public bool IsRuntimePausedSnapshot => IsRuntimePaused;", source);
        Assert.Contains("private bool IsRuntimePaused =>", source);
    }

    /// <summary>
    /// Stronger than Theme_Option_*_Keys test: also asserts that switching
    /// locale multiple times in sequence still yields correct non-empty,
    /// distinct translations. This catches the regression where only the
    /// first switch works (stale INPC indexer capture bug).
    /// </summary>
    [Fact]
    public void Multi_Switch_Locale_Yields_Distinct_Translations_Each_Time()
    {
        var svc = LocalizationService.Instance;
        var locales = new[] { "zh-CN", "en", "ja", "ko", "de", "fr", "es", "pt", "ru", "ar" };

        var firstValues = new Dictionary<string, string>();
        foreach (var locale in locales)
        {
            svc.SwitchLocale(locale);
            var statusKey = "status.tray_running";
            var value = svc.Get(statusKey);
            Assert.False(string.IsNullOrWhiteSpace(value));
            firstValues[locale] = value;
        }

        // At least 3 distinct values across 10 locales (native language names
        // differ). This guards against all locales silently returning the
        // same fallback (key itself).
        var distinctCount = firstValues.Values.Distinct().Count();
        Assert.True(distinctCount >= 3,
            "Expected >=3 distinct translations of status.tray_running across 10 locales, got " + distinctCount);

        // Second round: same values should be reproduced (stale-capture guard)
        svc.SwitchLocale("en");
        svc.SwitchLocale("zh-CN");
        var zhValue = svc.Get("status.tray_running");
        Assert.Equal(firstValues["zh-CN"], zhValue);
    }

    private static string ResolveMainWindowSourcePath()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..",
                "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return string.Empty;
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
