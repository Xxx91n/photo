using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Ui.Localization;

/// <summary>
/// Lightweight JSON-based localization service.
/// Three-layer loader: embedded resource -> exe-same-dir -> user config dir (ascending priority).
/// Supports runtime language switching with PropertyChanged notification.
/// </summary>
public sealed class LocalizationService : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _locales = new();
    private string _currentLocale = "zh-CN";
    private string? _previousEffectiveLocale;
    private Dictionary<string, string> _currentStrings = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? CultureChanged;

    public string CurrentLocale => _currentLocale;
    public IReadOnlyList<string> AvailableLocales => _locales.Keys.OrderBy(k => k).ToList();

    private LocalizationService()
    {
        LoadAllLayers();
    }

    public void Initialize(string? locale = null)
    {
        var target = locale ?? DetectSystemLocale();
        SwitchLocale(target);
    }

    public void SwitchLocale(string locale)
    {
        if (locale == _previousEffectiveLocale) return;
        _previousEffectiveLocale = locale;

        if (_locales.TryGetValue(locale, out var strings))
        {
            _currentLocale = locale;
            _currentStrings = strings;
        }
        else if (_locales.TryGetValue("zh-CN", out var fallback))
        {
            _currentLocale = "zh-CN";
            _currentStrings = fallback;
        }

        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentLocale)));
        CultureChanged?.Invoke(this, locale);
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_currentStrings.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_locales.TryGetValue("zh-CN", out var zhStrings) && zhStrings.TryGetValue(key, out var zhValue))
        {
            return zhValue;
        }

        return key;
    }

    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    private static string DetectSystemLocale()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return culture switch
        {
            "zh-CN" or "zh-Hans" or "zh" => "zh-CN",
            "zh-TW" or "zh-Hant" => "zh-TW",
            "ja" or "ja-JP" => "ja",
            "ko" or "ko-KR" => "ko",
            "de" or "de-DE" => "de",
            "fr" or "fr-FR" => "fr",
            "es" or "es-ES" => "es",
            "ru" or "ru-RU" => "ru",
            "ar" or "ar-SA" or "ar-EG" => "ar",
            _ => "en"
        };
    }

    /// <summary>
    /// Three-layer loader (ascending priority):
    /// 1. Embedded resource (always available, bottom layer)
    /// 2. exe-same-dir Localization/Locales/
    /// 3. User config dir locales/ (per-user override, top priority)
    /// </summary>
    private void LoadAllLayers()
    {
        LoadEmbeddedLocales();
        LoadDiskLocales(LocalePathResolver.ExeLocaleDirectory);
        LoadDiskLocales(LocalePathResolver.UserLocaleDirectory);

        if (!_locales.ContainsKey("zh-CN"))
        {
            _locales["zh-CN"] = BuiltInZhCN();
        }
        if (!_locales.ContainsKey("en"))
        {
            _locales["en"] = BuiltInEn();
        }
    }

    private void LoadEmbeddedLocales()
    {
        var asm = typeof(LocalizationService).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var localeName = ExtractLocaleName(name);
            if (localeName is null) continue;
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null)
                {
                    _locales[localeName] = MergeDicts(_locales.GetValueOrDefault(localeName), dict);
                }
            }
            catch
            {
                // Skip malformed embedded locale
            }
        }
    }

    private void LoadDiskLocales(string localeDir)
    {
        if (!Directory.Exists(localeDir)) return;
        foreach (var file in Directory.GetFiles(localeDir, "*.json"))
        {
            var localeName = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null)
                {
                    _locales[localeName] = MergeDicts(_locales.GetValueOrDefault(localeName), dict);
                }
            }
            catch
            {
                // Skip malformed locale files
            }
        }
    }

    private static string? ExtractLocaleName(string resourceName)
    {
        var idx = resourceName.IndexOf("Locales.", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = resourceName.Substring(idx + "Locales.".Length);
        if (rest.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest.Substring(0, rest.Length - ".json".Length);
        }
        return rest;
    }

    private static Dictionary<string, string> MergeDicts(Dictionary<string, string>? baseDict, Dictionary<string, string> overlay)
    {
        var merged = new Dictionary<string, string>(baseDict ?? new());
        foreach (var kvp in overlay)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }

    private static readonly string[] RtlLocales = { "ar" };
    public bool IsRtl(string? locale = null) =>
        RtlLocales.Contains(locale ?? _currentLocale, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> BuiltInZhCN() => new()
    {
        ["app.title"] = "PhotoPrivacy",
        ["nav.config"] = "配置",
        ["nav.logs"] = "日志",
        ["nav.service"] = "服务管理",
        ["btn.pause"] = "⏸ 暂停",
        ["btn.resume"] = "▶ 恢复",
        ["btn.save"] = "保存配置",
        ["btn.clear_logs"] = "清空日志",
        ["btn.browse"] = "浏览...",
        ["status.running"] = "运行中",
        ["status.paused"] = "已暂停",
        ["status.stopped"] = "已停止",
        ["mode.tray"] = "托盘模式",
        ["mode.service"] = "服务模式",
        ["mode.background"] = "后台模式",
        ["msg.saved"] = "配置已保存",
        ["msg.save_failed"] = "保存失败",
        ["msg.exiftool_not_found"] = "未找到 ExifTool",
        ["msg.worker_not_found"] = "未找到 Worker 可执行文件",
        ["msg.tray_still_running"] = "托盘 Worker 仍在运行，已取消服务启动，请稍后重试",
    };

    private static Dictionary<string, string> BuiltInEn() => new()
    {
        ["app.title"] = "PhotoPrivacy",
        ["nav.config"] = "Settings",
        ["nav.logs"] = "Logs",
        ["nav.service"] = "Service",
        ["btn.pause"] = "⏸ Pause",
        ["btn.resume"] = "▶ Resume",
        ["btn.save"] = "Save Config",
        ["btn.clear_logs"] = "Clear Logs",
        ["btn.browse"] = "Browse...",
        ["status.running"] = "Running",
        ["status.paused"] = "Paused",
        ["status.stopped"] = "Stopped",
        ["mode.tray"] = "Tray Mode",
        ["mode.service"] = "Service Mode",
        ["mode.background"] = "Background Mode",
        ["msg.saved"] = "Configuration saved",
        ["msg.save_failed"] = "Save failed",
        ["msg.exiftool_not_found"] = "ExifTool not found",
        ["msg.worker_not_found"] = "Worker executable not found",
        ["msg.tray_still_running"] = "Tray Worker is still running. Service start cancelled, please retry later.",
    };
}
