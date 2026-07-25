using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace PhotoPrivacy.Ui.Localization;

/// <summary>
/// Lightweight JSON-based localization service.
/// Loads locale files from Localization/Locales/{lang}.json at startup.
/// Supports runtime language switching with PropertyChanged notification.
/// </summary>
public sealed class LocalizationService : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _locales = new();
    private string _currentLocale = "zh-CN";
    private Dictionary<string, string> _currentStrings = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLocale => _currentLocale;

    public IReadOnlyList<string> AvailableLocales => _locales.Keys.OrderBy(k => k).ToList();

    private LocalizationService()
    {
        LoadEmbeddedLocales();
    }

    public void Initialize(string? locale = null)
    {
        var target = locale ?? DetectSystemLocale();
        SwitchLocale(target);
    }

    public void SwitchLocale(string locale)
    {
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
        CultureChanged?.Invoke(this, _currentLocale);
    }

    public event EventHandler<string>? CultureChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_currentStrings.TryGetValue(key, out var value))
        {
            return value;
        }

        // Fallback to zh-CN
        if (_locales.TryGetValue("zh-CN", out var zhStrings) && zhStrings.TryGetValue(key, out var zhValue))
        {
            return zhValue;
        }

        return key; // Return key itself as last resort
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
            _ => "en"
        };
    }

    private void LoadEmbeddedLocales()
    {
        var localeDir = Path.Combine(AppContext.BaseDirectory, "Localization", "Locales");
        if (!Directory.Exists(localeDir))
        {
            // Fallback: try relative to assembly
            localeDir = Path.Combine(Path.GetDirectoryName(typeof(LocalizationService).Assembly.Location)!, "Localization", "Locales");
        }

        if (Directory.Exists(localeDir))
        {
            foreach (var file in Directory.GetFiles(localeDir, "*.json"))
            {
                var localeName = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var json = File.ReadAllText(file);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict is not null)
                    {
                        _locales[localeName] = dict;
                    }
                }
                catch
                {
                    // Skip malformed locale files
                }
            }
        }

        // Always ensure zh-CN and en exist as built-in fallback
        if (!_locales.ContainsKey("zh-CN"))
        {
            _locales["zh-CN"] = BuiltInZhCN();
        }
        if (!_locales.ContainsKey("en"))
        {
            _locales["en"] = BuiltInEn();
        }
    }

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
        ["settings.exiftool_path"] = "ExifTool 路径",
        ["settings.hot_folder"] = "监控目录",
        ["settings.backup"] = "备份",
        ["settings.backup_dir"] = "备份目录",
        ["settings.theme"] = "主题",
        ["settings.language"] = "语言",
        ["settings.font"] = "字体",
        ["settings.font_size"] = "字体大小",
        ["settings.hide_on_startup"] = "启动时隐藏主窗口",
        ["settings.hide_tray"] = "隐藏托盘图标",
        ["settings.quarantine"] = "隔离区",
        ["settings.quarantine_dir"] = "隔离目录",
        ["settings.audit_log_dir"] = "审计日志目录",
        ["settings.log_level"] = "日志级别",
        ["settings.excluded_dirs"] = "排除目录",
        ["settings.system_excluded"] = "系统自动排除",
        ["service.status"] = "服务状态",
        ["service.install"] = "安装服务",
        ["service.uninstall"] = "卸载服务",
        ["service.start"] = "启动服务",
        ["service.stop"] = "停止服务",
        ["service.running"] = "运行中",
        ["service.stopped"] = "已停止",
        ["service.failed"] = "失败",
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
        ["settings.exiftool_path"] = "ExifTool Path",
        ["settings.hot_folder"] = "Watch Folder",
        ["settings.backup"] = "Backup",
        ["settings.backup_dir"] = "Backup Directory",
        ["settings.theme"] = "Theme",
        ["settings.language"] = "Language",
        ["settings.font"] = "Font",
        ["settings.font_size"] = "Font Size",
        ["settings.hide_on_startup"] = "Hide window on startup",
        ["settings.hide_tray"] = "Hide tray icon",
        ["settings.quarantine"] = "Quarantine",
        ["settings.quarantine_dir"] = "Quarantine Directory",
        ["settings.audit_log_dir"] = "Audit Log Directory",
        ["settings.log_level"] = "Log Level",
        ["settings.excluded_dirs"] = "Excluded Directories",
        ["settings.system_excluded"] = "System Auto-Excluded",
        ["service.status"] = "Service Status",
        ["service.install"] = "Install Service",
        ["service.uninstall"] = "Uninstall Service",
        ["service.start"] = "Start Service",
        ["service.stop"] = "Stop Service",
        ["service.running"] = "Running",
        ["service.stopped"] = "Stopped",
        ["service.failed"] = "Failed",
        ["msg.saved"] = "Configuration saved",
        ["msg.save_failed"] = "Save failed",
        ["msg.exiftool_not_found"] = "ExifTool not found",
        ["msg.worker_not_found"] = "Worker executable not found",
        ["msg.tray_still_running"] = "Tray Worker is still running. Service start cancelled, please retry later.",
    };
}
