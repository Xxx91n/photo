using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        // Notify indexer binding consumers (industry-standard WPF/Avalonia pattern for
        // Binding("[\"key\"]") on an INPC source: refreshing the indexer "Item[]" is what
        // makes every TextBlock/Button bound to this[string key] re-evaluate after SwitchLocale.
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item"));
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
                var dict = ParseLocaleJson(json);
                _locales[localeName] = MergeDicts(_locales.GetValueOrDefault(localeName), dict);
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
                var dict = ParseLocaleJson(json);
                _locales[localeName] = MergeDicts(_locales.GetValueOrDefault(localeName), dict);
            }
            catch
            {
                // Skip malformed locale files
            }
        }
    }

    /// <summary>
    /// Parse nested JSON locale into flat dot-path dictionary.
    /// e.g. {"nav": {"config": "Settings"}} → {"nav.config": "Settings"}
    /// Also supports flat keys (backward compatible with old format).
    /// </summary>
    private static Dictionary<string, string> ParseLocaleJson(string json)
    {
        var node = JsonNode.Parse(json);
        var dict = new Dictionary<string, string>();
        if (node is JsonValue val && val.TryGetValue<string>(out var flatStr))
        {
            // Old flat format: JSON was a plain string (unlikely but safe)
            var flat = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (flat is not null) return flat;
        }
        if (node is JsonObject obj)
        {
            FlattenNode(obj, string.Empty, dict);
        }
        else
        {
            // Fallback: old flat Dictionary<string,string> format
            var flat = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (flat is not null) return flat;
        }
        return dict;
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
        ["nav.service_manager"] = "服务管理器",
        ["btn.pause"] = "⏸ 暂停",
        ["btn.resume"] = "▶ 恢复",
        ["btn.save"] = "保存配置",
        ["btn.clear_logs"] = "清空日志",
        ["btn.browse"] = "浏览...",
        ["btn.open_config_dir"] = "打开配置目录",
        ["btn.ellipsis"] = "…",
        ["btn.add"] = "+",
        ["btn.remove"] = "-",
        ["status.running"] = "运行中",
        ["status.paused"] = "已暂停",
        ["status.stopped"] = "已停止",
        ["status.detecting"] = "检测中…",
        ["status.exiftool_not_found"] = "未找到 ExifTool",
        ["status.auto_detected"] = "已自动检测到",
        ["status.service_mode"] = "服务模式",
        ["status.tray_mode"] = "托盘模式",
        ["status.service_running"] = "服务运行中",
        ["status.service_stopped"] = "服务已停止",
        ["status.tray_paused"] = "托盘已暂停",
        ["status.tray_running"] = "托盘运行中",
        ["status.pause_service_unavailable"] = "暂停（服务模式不可用）",
        ["mode.tray"] = "托盘模式",
        ["mode.service"] = "服务模式",
        ["mode.background"] = "后台模式",
        ["settings.exiftool_path"] = "ExifTool 路径",
        ["settings.hot_folder"] = "监控目录",
        ["settings.backup"] = "备份",
        ["settings.backup_dir"] = "备份目录",
        ["settings.theme"] = "主题",
        ["settings.theme_preset"] = "主题预设",
        ["preset.catppuccin"] = "Catppuccin",
        ["preset.dracula"] = "Dracula",
        ["preset.nord"] = "Nord",
        ["preset.onedarkpro"] = "One Dark Pro",
        ["preset.tokyonight"] = "Tokyo Night",
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
        ["settings.enable_backup"] = "启用备份",
        ["settings.enable_backup_full"] = "启用备份",
        ["settings.audit_log_dir_label"] = "日志保存路径",
        ["service.status"] = "服务状态",
        ["service.install"] = "安装服务",
        ["service.uninstall"] = "卸载服务",
        ["service.start"] = "启动服务",
        ["service.stop"] = "停止服务",
        ["service.running"] = "运行中",
        ["service.stopped"] = "已停止",
        ["service.failed"] = "失败",
        ["service.section.status"] = "CURRENT STATUS",
        ["service.section.actions"] = "ACTIONS",
        ["service.row.install_start"] = "安装并启动服务",
        ["service.row.start_stop"] = "启动 / 停止服务",
        ["service.row.uninstall"] = "卸载服务",
        ["service.desc.install"] = "将 PhotoPrivacyWorker 注册为 Windows 系统服务",
        ["service.desc.start_stop"] = "手动控制已安装服务的运行状态",
        ["service.desc.uninstall"] = "从系统移除服务，切换为托盘模式运行",
        ["service.btn.install"] = "安装服务",
        ["service.btn.start"] = "启动",
        ["service.btn.stop"] = "停止",
        ["service.btn.refresh"] = "刷新",
        ["service.btn.uninstall"] = "卸载服务",
        ["service.status_format_skip"] = "{0} | 跳过：{1}",
        ["service.status_format_cancel"] = "{0} | 已取消：{1}",
        ["service.status_format_fail"] = "{0} | 失败：{1}",
        ["service.uninstalling"] = "正在卸载服务...",
        ["service.state.not_installed"] = "未安装",
        ["service.state.start_pending"] = "启动中",
        ["service.state.pause_pending"] = "暂停中",
        ["service.state.continue_pending"] = "恢复中",
        ["service.state.stop_pending"] = "停止中",
        ["service.state.unknown"] = "未知",
        ["service.state.not_installed_short"] = "未安装",
        ["service.msg.operation_success"] = "操作成功",
        ["service.msg.use_systemd"] = "Linux/macOS 请使用 systemd 管理服务",
        ["service.msg.must_use_worker"] = "安装服务必须使用 PhotoPrivacyWorker.exe",
        ["service.msg.invalid_target"] = "服务运行目标必须是 PhotoPrivacyWorker.exe",
        ["service.msg.path_empty"] = "路径不能为空",
        ["service.msg.path_invalid_chars"] = "路径包含不允许的字符: {0}",
        ["service.msg.path_must_be_absolute"] = "路径必须是绝对路径",
        ["service.msg.elevation_cancelled"] = "用户取消了管理员授权",
        ["service.exitcode.0"] = "操作成功",
        ["service.exitcode.5"] = "权限不足，请以管理员身份运行",
        ["service.exitcode.1053"] = "服务启动超时，请检查 ExifTool 路径和配置文件是否正确",
        ["service.exitcode.1055"] = "服务数据库被锁定，请稍后重试",
        ["service.exitcode.1056"] = "服务已在运行",
        ["service.exitcode.1058"] = "服务已被禁用",
        ["service.exitcode.1060"] = "服务不存在，请先安装",
        ["service.exitcode.1062"] = "服务未运行，无需停止",
        ["service.exitcode.1072"] = "服务已标记为删除，请重启系统后重试",
        ["service.exitcode.1073"] = "服务已存在，已自动执行先卸载再安装",
        ["service.exitcode.unknown"] = "未知错误（代码 {0}）",
        ["msg.saved"] = "配置已保存",
        ["msg.save_failed"] = "保存失败",
        ["msg.exiftool_not_found"] = "未找到 ExifTool",
        ["msg.worker_not_found"] = "未找到 Worker 可执行文件",
        ["msg.tray_still_running"] = "托盘 Worker 仍在运行，已取消服务启动，请稍后重试",
        ["msg.auto_saved"] = "✓ 已自动保存",
        ["msg.auto_saved_worker_down"] = "✓ 已自动保存（Worker 未运行）",
        ["msg.save_failed_exception"] = "✗ 保存失败：{0}",
        ["msg.invalid_exiftool_path"] = "✗ ExifTool 路径无效，请先选择正确的 exiftool.exe",
        ["msg.saved_worker_down"] = "✓ 已保存（Worker 未运行，下次启动生效）",
        ["msg.applied"] = "✓ 已应用",
        ["msg.apply_failed_exception"] = "✗ 应用失败：{0}",
        ["msg.config_apply_failed"] = "⚠ 配置应用失败",
        ["msg.worker_not_found_detail"] = "未找到 Worker 可执行文件（PhotoPrivacyWorker）",
        ["msg.tray_worker_running"] = "托盘 Worker 仍在运行，已取消服务启动，请稍后重试",
        ["config.section.path"] = "路径设置",
        ["config.section.behavior"] = "行为设置",
        ["config.section.logs"] = "日志设置",
        ["config.section.excluded"] = "排除监听列表",
        ["config.row.enable_backup"] = "启用备份",
        ["config.row.enable_quarantine"] = "启用隔离目录",
        ["config.desc.exiftool_path"] = "exiftool 可执行文件的完整路径",
        ["config.desc.hot_folder"] = "自动清理此目录下新增文件的元数据",
        ["config.desc.backup_enabled"] = "清理前将原文件复制到备份目录",
        ["config.desc.backup_dir"] = "留空默认使用监控目录/bak",
        ["config.desc.quarantine_enabled"] = "处理失败的文件移入此目录隔离",
        ["config.desc.quarantine_dir"] = "处理失败的文件存放路径",
        ["config.desc.hide_on_startup"] = "启动后仅在托盘显示，不弹出主窗口",
        ["config.desc.hide_tray"] = "在托盘模式下隐藏图标，仅保留后台处理",
        ["config.desc.theme"] = "可在系统/亮色/暗色之间自由切换",
        ["config.desc.language"] = "选择界面显示语言",
        ["config.desc.log_level"] = "info=标准日志，debug=详细调试日志",
        ["config.desc.audit_log_dir"] = "audit jsonl 文件的存放目录",
        ["config.desc.system_excluded"] = "日志、备份、隔离目录自动被排除在文件监听之外",
        ["config.desc.user_excluded"] = "可添加额外路径以排除监听",
        ["config.label.system_excluded"] = "系统自动排除目录（只读）",
        ["config.label.user_excluded"] = "用户自定义排除目录",
        ["theme.option.system"] = "跟随系统",
        ["theme.option.light"] = "浅色",
        ["theme.option.dark"] = "深色",
        ["loglevel.option.all"] = "ALL（全部）",
        ["loglevel.option.info"] = "INFO（标准）",
        ["loglevel.option.debug"] = "DEBUG（调试）",
        ["loglevel.option.warn"] = "WARN（警告）",
        ["loglevel.option.error"] = "ERROR（错误）",
        ["log.section.logs"] = "日志",
        ["log.btn.clear"] = "清空",
        ["tray.pause"] = "暂停",
        ["tray.resume"] = "恢复",
        ["tray.open_window"] = "打开主窗口",
        ["tray.exit"] = "退出",
        ["tray.tooltip"] = "PhotoPrivacy",
        ["dialog.select_exiftool"] = "选择 ExifTool 可执行文件",
        ["dialog.select_hot_folder"] = "选择监控目录",
        ["dialog.select_backup_dir"] = "选择备份目录",
        ["dialog.select_log_dir"] = "选择日志目录",
        ["dialog.select_quarantine_dir"] = "选择隔离目录",
        ["dialog.select_excluded_dir"] = "添加排除监听的目录",
            ["audit.exiftool_started"] = "ExifTool 启动",
        ["audit.service_started"] = "服务启动",
        ["audit.file_cleaned"] = "清理完成",
        ["audit.file_skipped"] = "已跳过",
        ["audit.file_detected"] = "检测到文件",
        ["audit.instance_conflict"] = "重复启动被拒",
        ["audit.file_failed"] = "清理失败",
    };

    private static Dictionary<string, string> BuiltInEn() => new()
    {
        ["app.title"] = "PhotoPrivacy",
        ["nav.config"] = "Settings",
        ["nav.logs"] = "Logs",
        ["nav.service"] = "Service",
        ["nav.service_manager"] = "Service Manager",
        ["btn.pause"] = "⏸ Pause",
        ["btn.resume"] = "▶ Resume",
        ["btn.save"] = "Save Config",
        ["btn.clear_logs"] = "Clear Logs",
        ["btn.browse"] = "Browse...",
        ["btn.open_config_dir"] = "Open config directory",
        ["btn.ellipsis"] = "…",
        ["btn.add"] = "+",
        ["btn.remove"] = "-",
        ["status.running"] = "Running",
        ["status.paused"] = "Paused",
        ["status.stopped"] = "Stopped",
        ["status.detecting"] = "Detecting…",
        ["status.exiftool_not_found"] = "ExifTool not found",
        ["status.auto_detected"] = "Auto-detected",
        ["status.service_mode"] = "Service Mode",
        ["status.tray_mode"] = "Tray Mode",
        ["status.service_running"] = "Service running",
        ["status.service_stopped"] = "Service stopped",
        ["status.tray_paused"] = "Tray paused",
        ["status.tray_running"] = "Tray running",
        ["status.pause_service_unavailable"] = "Pause (unavailable in service mode)",
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
        ["settings.enable_backup"] = "Enable backup",
        ["settings.enable_backup_full"] = "Enable backup",
        ["settings.audit_log_dir_label"] = "Log storage path",
        ["service.status"] = "Service Status",
        ["service.install"] = "Install Service",
        ["service.uninstall"] = "Uninstall Service",
        ["service.start"] = "Start Service",
        ["service.stop"] = "Stop Service",
        ["service.running"] = "Running",
        ["service.stopped"] = "Stopped",
        ["service.failed"] = "Failed",
        ["service.section.status"] = "CURRENT STATUS",
        ["service.section.actions"] = "ACTIONS",
        ["service.row.install_start"] = "Install & start service",
        ["service.row.start_stop"] = "Start / stop service",
        ["service.row.uninstall"] = "Uninstall service",
        ["service.desc.install"] = "Register PhotoPrivacyWorker as a Windows system service",
        ["service.desc.start_stop"] = "Manually control run state of the installed service",
        ["service.desc.uninstall"] = "Remove service from system, switch to tray mode",
        ["service.btn.install"] = "Install service",
        ["service.btn.start"] = "Start",
        ["service.btn.stop"] = "Stop",
        ["service.btn.refresh"] = "Refresh",
        ["service.btn.uninstall"] = "Uninstall service",
        ["service.status_format_skip"] = "{0} | Skipped: {1}",
        ["service.status_format_cancel"] = "{0} | Cancelled: {1}",
        ["service.status_format_fail"] = "{0} | Failed: {1}",
        ["service.uninstalling"] = "Uninstalling service...",
        ["service.state.not_installed"] = "Not installed",
        ["service.state.start_pending"] = "Starting",
        ["service.state.pause_pending"] = "Pausing",
        ["service.state.continue_pending"] = "Resuming",
        ["service.state.stop_pending"] = "Stopping",
        ["service.state.unknown"] = "Unknown",
        ["service.state.not_installed_short"] = "Not installed",
        ["service.msg.operation_success"] = "Operation succeeded",
        ["service.msg.use_systemd"] = "Use systemd to manage services on Linux/macOS",
        ["service.msg.must_use_worker"] = "Service installation requires PhotoPrivacyWorker.exe",
        ["service.msg.invalid_target"] = "Service target must be PhotoPrivacyWorker.exe",
        ["service.msg.path_empty"] = "Path cannot be empty",
        ["service.msg.path_invalid_chars"] = "Path contains invalid characters: {0}",
        ["service.msg.path_must_be_absolute"] = "Path must be absolute",
        ["service.msg.elevation_cancelled"] = "User cancelled administrator authorization",
        ["service.exitcode.0"] = "Operation succeeded",
        ["service.exitcode.5"] = "Access denied, please run as administrator",
        ["service.exitcode.1053"] = "Service start timeout, check ExifTool path and config file",
        ["service.exitcode.1055"] = "Service database locked, please retry later",
        ["service.exitcode.1056"] = "Service is already running",
        ["service.exitcode.1058"] = "Service is disabled",
        ["service.exitcode.1060"] = "Service does not exist, please install first",
        ["service.exitcode.1062"] = "Service is not running, no need to stop",
        ["service.exitcode.1072"] = "Service marked for deletion, please restart system and retry",
        ["service.exitcode.1073"] = "Service already exists, auto uninstalled then reinstalled",
        ["service.exitcode.unknown"] = "Unknown error (code {0})",
        ["msg.saved"] = "Configuration saved",
        ["msg.save_failed"] = "Save failed",
        ["msg.exiftool_not_found"] = "ExifTool not found",
        ["msg.worker_not_found"] = "Worker executable not found",
        ["msg.tray_still_running"] = "Tray Worker is still running. Service start cancelled, please retry later.",
        ["msg.auto_saved"] = "✓ Auto-saved",
        ["msg.auto_saved_worker_down"] = "✓ Auto-saved (Worker not running)",
        ["msg.save_failed_exception"] = "✗ Save failed: {0}",
        ["msg.invalid_exiftool_path"] = "✗ Invalid ExifTool path, please select correct exiftool.exe",
        ["msg.saved_worker_down"] = "✓ Saved (Worker not running, takes effect on next start)",
        ["msg.applied"] = "✓ Applied",
        ["msg.apply_failed_exception"] = "✗ Apply failed: {0}",
        ["msg.config_apply_failed"] = "⚠ Config apply failed",
        ["msg.worker_not_found_detail"] = "Worker executable not found (PhotoPrivacyWorker)",
        ["msg.tray_worker_running"] = "Tray Worker is still running. Service start cancelled, please retry later.",
        ["config.section.path"] = "Path settings",
        ["config.section.behavior"] = "Behavior settings",
        ["config.section.logs"] = "Log settings",
        ["config.section.excluded"] = "Exclude watch list",
        ["config.row.enable_backup"] = "Enable backup",
        ["config.row.enable_quarantine"] = "Enable quarantine",
        ["config.desc.exiftool_path"] = "Full path to the exiftool executable",
        ["config.desc.hot_folder"] = "Automatically clean metadata from new files in this directory",
        ["config.desc.backup_enabled"] = "Copy original files to backup dir before cleaning",
        ["config.desc.backup_dir"] = "Leave empty to use <hot_folder>/bak",
        ["config.desc.quarantine_enabled"] = "Move failed files into this directory for isolation",
        ["config.desc.quarantine_dir"] = "Path to store failed files",
        ["config.desc.hide_on_startup"] = "Show in tray only after startup, no main window popup",
        ["config.desc.hide_tray"] = "Hide tray icon in tray mode, keep background processing only",
        ["config.desc.theme"] = "Switch freely between system/light/dark",
        ["config.desc.language"] = "Select interface display language",
        ["config.desc.log_level"] = "info=standard logging, debug=verbose debugging logs",
        ["config.desc.audit_log_dir"] = "Directory where audit JSONL files are stored",
        ["config.desc.system_excluded"] = "Log, backup, and quarantine dirs are auto-excluded from file watching",
        ["config.desc.user_excluded"] = "Add additional paths to exclude from watching",
        ["config.label.system_excluded"] = "System auto-excluded directories (read-only)",
        ["config.label.user_excluded"] = "User-defined exclusion directories",
        ["theme.option.system"] = "Follow system",
        ["theme.option.light"] = "Light",
        ["theme.option.dark"] = "Dark",
        ["loglevel.option.all"] = "ALL (everything)",
        ["loglevel.option.info"] = "INFO (standard)",
        ["loglevel.option.debug"] = "DEBUG (verbose)",
        ["loglevel.option.warn"] = "WARN (warnings)",
        ["loglevel.option.error"] = "ERROR (errors)",
        ["log.section.logs"] = "Logs",
        ["log.btn.clear"] = "Clear",
        ["tray.pause"] = "Pause",
        ["tray.resume"] = "Resume",
        ["tray.open_window"] = "Open Main Window",
        ["tray.exit"] = "Exit",
        ["tray.tooltip"] = "PhotoPrivacy",
        ["dialog.select_exiftool"] = "Select ExifTool executable",
        ["dialog.select_hot_folder"] = "Select watch folder",
        ["dialog.select_backup_dir"] = "Select backup directory",
        ["dialog.select_log_dir"] = "Select log directory",
        ["dialog.select_quarantine_dir"] = "Select quarantine directory",
        ["dialog.select_excluded_dir"] = "Add directory to exclude from watching",
            ["audit.exiftool_started"] = "ExifTool started",
        ["audit.service_started"] = "Service started",
        ["audit.file_cleaned"] = "Cleaned",
        ["audit.file_skipped"] = "Skipped",
        ["audit.file_detected"] = "File detected",
        ["audit.instance_conflict"] = "Duplicate launch rejected",
        ["audit.file_failed"] = "Cleaning failed",
    };
}
