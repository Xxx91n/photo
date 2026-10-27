//! 新栈 i18n：键值资产复用旧 UI `Localization/Locales/*.json`（#60）。
//!
//! 机制按 Rust 惯例重做，但语义与旧 UI `LocalizationService` 等效：
//! - 三层加载（升序优先级）：内嵌资源 → exe 同目录 `Localization/Locales/` → 用户数据目录 `locales/`；
//! - 缺键兜底链：当前语言 → `zh-CN` → 键名本身；
//! - 嵌套 JSON 展平为点路径键（`{"nav":{"config":"…"}}` → `nav.config`）；
//! - en 为对齐基准：10 语言键集与 en.json 全等（测试守护）。
//!
//! 渲染层每帧按 `config.ui.locale` 草稿取词，语言 chips 点击即改草稿，
//! 因此切换即时生效（不重启）；落盘仍走配置页统一的防抖保存。

use serde_json::Value;
use std::collections::BTreeMap;
use std::path::PathBuf;

/// 内嵌底层：编译期打进二进制，文件缺失/损坏时 UI 仍有完整文案（旧 UI 内嵌资源层的等价物）。
const EMBEDDED: &[(&str, &str)] = &[
    ("ar", include_str!("../locales/ar.json")),
    ("de", include_str!("../locales/de.json")),
    ("en", include_str!("../locales/en.json")),
    ("es", include_str!("../locales/es.json")),
    ("fr", include_str!("../locales/fr.json")),
    ("ja", include_str!("../locales/ja.json")),
    ("ko", include_str!("../locales/ko.json")),
    ("pt", include_str!("../locales/pt.json")),
    ("ru", include_str!("../locales/ru.json")),
    ("zh-CN", include_str!("../locales/zh-CN.json")),
];

/// 缺键兜底链的中间层语言，与旧 UI `LocalizationService.Get` 的 zh-CN 兜底一致。
pub const FALLBACK_LOCALE: &str = "zh-CN";

/// 旧 UI `LocalePathResolver.ExeLocaleDirectory` 的等价物。
fn exe_locale_directory() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?;
    Some(exe.parent()?.join("Localization").join("Locales"))
}

/// 旧 UI `LocalePathResolver.GetUserAppDataDir()/locales` 的等价物：
/// Windows `%LOCALAPPDATA%\PhotoPrivacy`、macOS `~/Library/Application Support/PhotoPrivacy`、
/// Linux `$XDG_DATA_HOME`（缺省 `~/.local/share`）下的 `PhotoPrivacy/locales`。
fn user_locale_directory() -> Option<PathBuf> {
    if cfg!(windows) {
        let local_app_data = std::env::var_os("LOCALAPPDATA")?;
        Some(
            PathBuf::from(local_app_data)
                .join("PhotoPrivacy")
                .join("locales"),
        )
    } else if cfg!(target_os = "macos") {
        let home = std::env::var_os("HOME")?;
        Some(
            PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("PhotoPrivacy")
                .join("locales"),
        )
    } else {
        let base = match std::env::var_os("XDG_DATA_HOME") {
            Some(dir) if !dir.is_empty() => PathBuf::from(dir),
            _ => {
                let home = std::env::var_os("HOME")?;
                PathBuf::from(home).join(".local").join("share")
            }
        };
        Some(base.join("PhotoPrivacy").join("locales"))
    }
}

/// 展平嵌套 locale JSON 为点路径键值；只收字符串叶子（与旧 UI `FlattenNode` 一致）。
fn flatten(json: &str) -> BTreeMap<String, String> {
    fn walk(prefix: &str, value: &Value, out: &mut BTreeMap<String, String>) {
        match value {
            Value::String(text) => {
                out.insert(prefix.to_string(), text.clone());
            }
            Value::Object(map) => {
                for (key, child) in map {
                    let path = if prefix.is_empty() {
                        key.clone()
                    } else {
                        format!("{prefix}.{key}")
                    };
                    walk(&path, child, out);
                }
            }
            _ => {}
        }
    }

    let mut out = BTreeMap::new();
    // Windows 编辑器常写 UTF-8 BOM；serde_json 不认 BOM，先剥掉（与 config 读取同一坑）。
    let json = json.strip_prefix('\u{feff}').unwrap_or(json);
    if let Ok(value) = serde_json::from_str::<Value>(json) {
        walk("", &value, &mut out);
    }
    out
}

/// 全部语言的键值目录。启动时加载一次，之后只读。
pub struct Catalog {
    locales: BTreeMap<String, BTreeMap<String, String>>,
}

impl Catalog {
    /// 三层加载：内嵌 → exe 同目录 → 用户数据目录（后者键值覆盖前者，升序优先级）。
    /// 单个磁盘文件损坏时跳过该文件，内嵌层保底（旧 UI `LoadDiskLocales` 的容错一致）。
    pub fn load() -> Self {
        let mut catalog = Self::embedded();
        for directory in [exe_locale_directory(), user_locale_directory()] {
            let Some(directory) = directory else { continue };
            let Ok(entries) = std::fs::read_dir(&directory) else {
                continue;
            };
            for entry in entries.flatten() {
                let path = entry.path();
                if path.extension().and_then(|ext| ext.to_str()) != Some("json") {
                    continue;
                }
                let Some(locale) = path.file_stem().and_then(|stem| stem.to_str()) else {
                    continue;
                };
                if let Ok(json) = std::fs::read_to_string(&path) {
                    catalog.merge_locale(locale, &json);
                }
            }
        }
        catalog
    }

    /// 仅内嵌层；测试与无文件系统场景使用。
    pub fn embedded() -> Self {
        let mut catalog = Self {
            locales: BTreeMap::new(),
        };
        for (locale, json) in EMBEDDED {
            catalog.merge_locale(locale, json);
        }
        catalog
    }

    fn merge_locale(&mut self, locale: &str, json: &str) {
        self.locales
            .entry(locale.to_string())
            .or_default()
            .extend(flatten(json));
    }

    /// 缺键兜底链：当前语言 → zh-CN → 键名本身（旧 UI `Get` 的语义）。
    pub fn get(&self, locale: &str, key: &str) -> String {
        if let Some(value) = self
            .locales
            .get(locale)
            .and_then(|strings| strings.get(key))
        {
            return value.clone();
        }
        if let Some(value) = self
            .locales
            .get(FALLBACK_LOCALE)
            .and_then(|strings| strings.get(key))
        {
            return value.clone();
        }
        key.to_string()
    }

    /// 带占位符取词：`{0}`…`{n}` 按序替换（旧 UI `Get(key, args)` 的 `{0}` 语义）。
    pub fn get_format(&self, locale: &str, key: &str, args: &[&str]) -> String {
        format_template(&self.get(locale, key), args)
    }

    /// 当前语言是否含该键（用于「有翻译才替换」的调用点，如服务退出码）。
    pub fn contains(&self, locale: &str, key: &str) -> bool {
        self.locales
            .get(locale)
            .is_some_and(|strings| strings.contains_key(key))
    }
}

/// `{0}`…`{n}` 占位符替换；参数不足时保留占位符原文。
pub fn format_template(template: &str, args: &[&str]) -> String {
    let mut out = template.to_string();
    for (index, arg) in args.iter().enumerate() {
        out = out.replace(&format!("{{{index}}}"), arg);
    }
    out
}

/// RTL 语言判定；当前 10 语言中只有 ar（旧 UI `RtlLocales` 一致）。
pub fn is_rtl(locale: &str) -> bool {
    locale.eq_ignore_ascii_case("ar")
}

/// UI 用到的全部键。集中成常量让拼写错误变成编译错误，
/// `used_keys_are_covered_by_en` 测试再保证每个常量在 en 里真实存在。
pub mod keys {
    pub const APP_TITLE: &str = "app.title";
    pub const APP_SUBTITLE: &str = "app.subtitle";
    pub const NAV_OVERVIEW: &str = "nav.overview";
    pub const NAV_CONFIG: &str = "nav.config";
    pub const NAV_LOGS: &str = "nav.logs";
    pub const NAV_SERVICE: &str = "nav.service";
    pub const NAV_RULES: &str = "nav.rules";
    pub const BTN_BROWSE: &str = "btn.browse";
    pub const COMMON_NONE: &str = "common.none";
    pub const STATUS_CONNECTED: &str = "status.connected";
    pub const STATUS_CONNECTING: &str = "status.connecting";
    pub const STATUS_RECONNECTING: &str = "status.reconnecting";
    pub const STATUS_DISCONNECTED: &str = "status.disconnected";
    pub const STATUS_SAVING: &str = "status.saving";
    pub const STATUS_SAVED_RELOADED: &str = "status.saved_reloaded";
    pub const STATUS_SAVED_WORKER_DOWN: &str = "status.saved_worker_down";
    pub const STATUS_CONFIG_ERROR: &str = "status.config_error";
    pub const STATUS_AUTO_DETECTED: &str = "status.auto_detected";
    pub const MSG_SAVE_FAILED_EXCEPTION: &str = "msg.save_failed_exception";
    pub const CONFIG_SECTION_PATH: &str = "config.section.path";
    pub const CONFIG_SECTION_BEHAVIOR: &str = "config.section.behavior";
    pub const CONFIG_SECTION_EXCLUDED: &str = "config.section.excluded";
    pub const CONFIG_SECTION_LOGS: &str = "config.section.logs";
    pub const CONFIG_PLACEHOLDER_EXIFTOOL_PATH: &str = "config.placeholder.exiftool_path";
    pub const CONFIG_PLACEHOLDER_HOT_FOLDER: &str = "config.placeholder.hot_folder";
    pub const CONFIG_PLACEHOLDER_BACKUP_DIR: &str = "config.placeholder.backup_dir";
    pub const CONFIG_PLACEHOLDER_QUARANTINE_DIR: &str = "config.placeholder.quarantine_dir";
    pub const CONFIG_PLACEHOLDER_AUDIT_LOG_DIR: &str = "config.placeholder.audit_log_dir";
    pub const CONFIG_EMPTY_USER_EXCLUDED: &str = "config.empty.user_excluded";
    pub const CONFIG_BTN_REMOVE_DIR: &str = "config.btn.remove_dir";
    pub const CONFIG_BTN_ADD_FOLDER: &str = "config.btn.add_folder";
    pub const SETTINGS_EXIFTOOL_PATH: &str = "settings.exiftool_path";
    pub const SETTINGS_HOT_FOLDER: &str = "settings.hot_folder";
    pub const SETTINGS_BACKUP_DIR: &str = "settings.backup_dir";
    pub const SETTINGS_QUARANTINE_DIR: &str = "settings.quarantine_dir";
    pub const SETTINGS_HIDE_ON_STARTUP: &str = "settings.hide_on_startup";
    pub const SETTINGS_HIDE_TRAY: &str = "settings.hide_tray";
    pub const SETTINGS_THEME: &str = "settings.theme";
    pub const SETTINGS_LANGUAGE: &str = "settings.language";
    pub const SETTINGS_LOG_LEVEL: &str = "settings.log_level";
    pub const SETTINGS_AUDIT_LOG_DIR: &str = "settings.audit_log_dir";
    pub const CONFIG_ROW_ENABLE_BACKUP: &str = "config.row.enable_backup";
    pub const CONFIG_ROW_ENABLE_QUARANTINE: &str = "config.row.enable_quarantine";
    pub const CONFIG_DESC_EXIFTOOL_PATH: &str = "config.desc.exiftool_path";
    pub const CONFIG_DESC_HOT_FOLDER: &str = "config.desc.hot_folder";
    pub const CONFIG_DESC_BACKUP_ENABLED: &str = "config.desc.backup_enabled";
    pub const CONFIG_DESC_BACKUP_DIR: &str = "config.desc.backup_dir";
    pub const CONFIG_DESC_QUARANTINE_ENABLED: &str = "config.desc.quarantine_enabled";
    pub const CONFIG_DESC_QUARANTINE_DIR: &str = "config.desc.quarantine_dir";
    pub const CONFIG_DESC_HIDE_ON_STARTUP: &str = "config.desc.hide_on_startup";
    pub const CONFIG_DESC_HIDE_TRAY: &str = "config.desc.hide_tray";
    pub const CONFIG_DESC_THEME: &str = "config.desc.theme";
    pub const CONFIG_DESC_LANGUAGE: &str = "config.desc.language";
    pub const CONFIG_DESC_LOG_LEVEL: &str = "config.desc.log_level";
    pub const CONFIG_DESC_AUDIT_LOG_DIR: &str = "config.desc.audit_log_dir";
    pub const CONFIG_DESC_SYSTEM_EXCLUDED: &str = "config.desc.system_excluded";
    pub const CONFIG_DESC_USER_EXCLUDED: &str = "config.desc.user_excluded";
    pub const CONFIG_LABEL_SYSTEM_EXCLUDED: &str = "config.label.system_excluded";
    pub const CONFIG_LABEL_USER_EXCLUDED: &str = "config.label.user_excluded";
    pub const DIALOG_SELECT_EXIFTOOL: &str = "dialog.select_exiftool";
    pub const DIALOG_SELECT_FOLDER: &str = "dialog.select_folder";
    pub const DIALOG_SELECT_EXCLUDED_DIR: &str = "dialog.select_excluded_dir";
    pub const LOGLEVEL_CHIP_ALL: &str = "loglevel.chip.all";
    pub const LOGLEVEL_CHIP_DEBUG: &str = "loglevel.chip.debug";
    pub const LOGLEVEL_CHIP_INFO: &str = "loglevel.chip.info";
    pub const LOGLEVEL_CHIP_WARN: &str = "loglevel.chip.warn";
    pub const LOGLEVEL_CHIP_ERROR: &str = "loglevel.chip.error";
    pub const LOG_SUBTITLE: &str = "log.subtitle";
    pub const LOG_BTN_CLEAR: &str = "log.btn.clear";
    pub const LOG_EMPTY_TITLE: &str = "log.empty_title";
    pub const LOG_EMPTY_DESC: &str = "log.empty_desc";
    pub const LOG_EMPTY_FILTERED_TITLE: &str = "log.empty_filtered_title";
    pub const LOG_EMPTY_FILTERED_DESC: &str = "log.empty_filtered_desc";
    pub const LOG_ENTRIES_COUNT: &str = "log.entries_count";
    pub const LOG_COL_TIME: &str = "log.col.time";
    pub const LOG_COL_EVENT: &str = "log.col.event";
    pub const LOG_COL_SOURCE: &str = "log.col.source";
    pub const LOG_COL_MESSAGE: &str = "log.col.message";
    pub const OVERVIEW_WORKER_SECTION: &str = "overview.worker_section";
    pub const OVERVIEW_WORKER_MODE: &str = "overview.worker_mode";
    pub const OVERVIEW_REPORTED_MODE: &str = "overview.reported_mode";
    pub const OVERVIEW_SERVICE_IPC: &str = "overview.service_ipc";
    pub const OVERVIEW_PROTOCOL: &str = "overview.protocol";
    pub const OVERVIEW_EXIFTOOL: &str = "overview.exiftool";
    pub const OVERVIEW_ROUND_TRIPS: &str = "overview.round_trips";
    pub const OVERVIEW_LAST_ROUND_TRIP: &str = "overview.last_round_trip";
    pub const OVERVIEW_NO_ROUND_TRIP: &str = "overview.no_round_trip";
    pub const OVERVIEW_REACHABLE: &str = "overview.reachable";
    pub const OVERVIEW_UNREACHABLE: &str = "overview.unreachable";
    pub const MODE_BACKGROUND: &str = "mode.background";
    pub const MODE_SERVICE: &str = "mode.service";
    pub const THEME_OPTION_SYSTEM: &str = "theme.option.system";
    pub const THEME_OPTION_LIGHT: &str = "theme.option.light";
    pub const THEME_OPTION_DARK: &str = "theme.option.dark";
    pub const SERVICE_SECTION: &str = "service.section.service";
    pub const SERVICE_STATUS: &str = "service.status";
    pub const SERVICE_ROW_START_STOP: &str = "service.row.start_stop";
    pub const SERVICE_DESC_APPLYING: &str = "service.desc.applying";
    pub const SERVICE_DESC_REFRESH_3S: &str = "service.desc.refresh_3s";
    pub const SERVICE_BTN_INSTALL: &str = "service.btn.install";
    pub const SERVICE_BTN_INSTALL_SHORT: &str = "service.btn.install_short";
    pub const SERVICE_BTN_START: &str = "service.btn.start";
    pub const SERVICE_BTN_STOP: &str = "service.btn.stop";
    pub const SERVICE_BTN_REFRESH: &str = "service.btn.refresh";
    pub const SERVICE_BTN_UNINSTALL: &str = "service.btn.uninstall";
    pub const SERVICE_BTN_UNINSTALL_SHORT: &str = "service.btn.uninstall_short";
    pub const SERVICE_DESC_INSTALL: &str = "service.desc.install";
    pub const SERVICE_DESC_START_STOP: &str = "service.desc.start_stop";
    pub const SERVICE_DESC_UNINSTALL: &str = "service.desc.uninstall";
    pub const SERVICE_RESULT_SUCCESS: &str = "service.result.success";
    pub const SERVICE_RESULT_SKIPPED: &str = "service.result.skipped";
    pub const SERVICE_RESULT_CANCELLED: &str = "service.result.cancelled";
    pub const SERVICE_RESULT_FAILED: &str = "service.result.failed";
    pub const SERVICE_STATE_RUNNING: &str = "service.state.running";
    pub const SERVICE_STATE_NOT_INSTALLED: &str = "service.state.not_installed";
    pub const SERVICE_STATE_START_PENDING: &str = "service.state.start_pending";
    pub const SERVICE_STATE_PAUSE_PENDING: &str = "service.state.pause_pending";
    pub const SERVICE_STATE_PAUSED: &str = "service.state.paused";
    pub const SERVICE_STATE_CONTINUE_PENDING: &str = "service.state.continue_pending";
    pub const SERVICE_STATE_STOPPED: &str = "service.state.stopped";
    pub const SERVICE_STATE_STOP_PENDING: &str = "service.state.stop_pending";
    pub const SERVICE_STATE_UNKNOWN: &str = "service.state.unknown";
    pub const RULES_PANEL_SUBTITLE: &str = "rules.panel.subtitle";
    pub const RULES_UNSAVED: &str = "rules.unsaved";
    pub const RULES_EXPERT_MODE: &str = "rules.expert.mode";
    pub const RULES_EXPERT_HIDES_NOTE: &str = "rules.expert.hides_note";
    pub const RULES_EXPERT_INFOBAR: &str = "rules.expert.infobar";
    pub const RULES_STORED_IN: &str = "rules.stored_in";
    pub const RULES_CAUTION: &str = "rules.caution";
    pub const RULES_SAVE_FAILED: &str = "rules.save_failed";
    pub const RULES_RESET_FAILED: &str = "rules.reset_failed";
    pub const RULES_BTN_SAVE: &str = "rules.btn.save";
    pub const RULES_BTN_RESET: &str = "rules.btn.reset";
    pub const TOAST_RULES_SAVED: &str = "toast.rules_saved";
    pub const TOAST_RULES_RESET: &str = "toast.rules_reset";
    pub const RULES_COL_FAMILY: &str = "rules.col.family";
    pub const RULES_COL_EXTENSIONS: &str = "rules.col.extensions";
    pub const RULES_COL_WARNING: &str = "rules.col.warning";
    pub const RULES_COL_STRIP_ALL: &str = "rules.col.strip_all";
    pub const RULES_COL_PRESERVE_ICC: &str = "rules.col.preserve_icc";
    pub const RULES_COL_STRIP_EXIF: &str = "rules.col.strip_exif";
    pub const RULES_COL_STRIP_XMP: &str = "rules.col.strip_xmp";
    pub const RULES_COL_STRIP_IPTC: &str = "rules.col.strip_iptc";
    pub const RULES_COL_STRIP_TIME: &str = "rules.col.strip_time";
    pub const RULES_FAMILY_JPEG: &str = "rules.family.jpeg";
    pub const RULES_FAMILY_RAW: &str = "rules.family.raw";
    pub const RULES_FAMILY_VIDEO: &str = "rules.family.video";
    pub const RULES_FAMILY_PDF: &str = "rules.family.pdf";
    pub const RULES_FAMILY_EPS: &str = "rules.family.eps";
    pub const TRAY_OPEN_WINDOW: &str = "tray.open_window";
    pub const TRAY_EXIT: &str = "tray.exit";
    pub const TRAY_TOOLTIP: &str = "tray.tooltip";
}

#[cfg(test)]
mod tests {
    use super::*;

    /// AC1：10 语言键集与 en.json 全等（键值复用，含内嵌兜底语义等效）。
    #[test]
    fn all_ten_locales_have_exactly_the_en_key_set() {
        let catalog = Catalog::embedded();
        let en = catalog.locales.get("en").expect("en must be embedded");
        assert_eq!(catalog.locales.len(), 10, "必须恰好 10 语言");
        for (locale, strings) in &catalog.locales {
            let missing: Vec<&String> = en
                .keys()
                .filter(|key| !strings.contains_key(*key))
                .collect();
            let extra: Vec<&String> = strings
                .keys()
                .filter(|key| !en.contains_key(*key))
                .collect();
            assert!(
                missing.is_empty() && extra.is_empty(),
                "{locale} 键集与 en 不全等：缺 {missing:?} 多 {extra:?}"
            );
        }
    }

    /// 内嵌层是兜底底线：任何键在任何语言的值都必须是非空字符串。
    #[test]
    fn embedded_values_are_all_non_empty_strings() {
        let catalog = Catalog::embedded();
        for (locale, strings) in &catalog.locales {
            for (key, value) in strings {
                assert!(!value.trim().is_empty(), "{locale}.{key} 是空值");
            }
        }
    }

    #[test]
    fn nested_json_flattens_to_dot_paths() {
        let strings = flatten(r#"{"nav": {"config": "Settings"}, "flat": "x"}"#);
        assert_eq!(
            strings.get("nav.config").map(String::as_str),
            Some("Settings")
        );
        assert_eq!(strings.get("flat").map(String::as_str), Some("x"));
    }

    #[test]
    fn malformed_json_yields_empty_layer_instead_of_panic() {
        assert!(flatten("{ not json").is_empty());
    }

    #[test]
    fn missing_key_falls_back_to_zh_cn_then_key_name() {
        let mut catalog = Catalog {
            locales: BTreeMap::new(),
        };
        catalog.merge_locale("xx", r#"{"a.present": "xx 值"}"#);
        catalog.merge_locale(
            FALLBACK_LOCALE,
            r#"{"a.present": "zh 值", "a.only_zh": "zh 兜底"}"#,
        );
        assert_eq!(catalog.get("xx", "a.present"), "xx 值");
        assert_eq!(catalog.get("xx", "a.only_zh"), "zh 兜底");
        assert_eq!(
            catalog.get("xx", "a.missing_everywhere"),
            "a.missing_everywhere"
        );
    }

    #[test]
    fn placeholders_are_replaced_in_order_and_missing_args_kept() {
        assert_eq!(
            format_template("Saved · Worker not reloaded: {0}", &["timeout"]),
            "Saved · Worker not reloaded: timeout"
        );
        assert_eq!(format_template("{0} + {1}", &["a"]), "a + {1}");
        assert_eq!(format_template("no args", &[]), "no args");
    }

    #[test]
    fn get_format_uses_current_locale_value() {
        let catalog = Catalog::embedded();
        assert_eq!(
            catalog.get_format("en", "log.entries_count", &["3"]),
            "3 entries"
        );
        assert_eq!(
            catalog.get_format("zh-CN", "log.entries_count", &["3"]),
            "3 条"
        );
    }

    #[test]
    fn only_arabic_is_rtl() {
        // 防：RTL 误判会整页镜像非 RTL 语言（旧 UI RtlLocales 只有 ar）。
        assert!(is_rtl("ar"));
        assert!(is_rtl("AR"));
        assert!(!is_rtl("en"));
        assert!(!is_rtl("zh-CN"));
        assert!(!is_rtl("fa"));
    }

    #[test]
    fn every_declared_key_exists_in_en() {
        // 防：keys 常量与 locale 资产漂移——常量拼错键名时这里立即失败。
        let catalog = Catalog::embedded();
        let en = catalog.locales.get("en").expect("en must be embedded");
        const ALL: &[&str] = &[
            keys::APP_TITLE,
            keys::APP_SUBTITLE,
            keys::NAV_OVERVIEW,
            keys::NAV_CONFIG,
            keys::NAV_LOGS,
            keys::NAV_SERVICE,
            keys::NAV_RULES,
            keys::BTN_BROWSE,
            keys::COMMON_NONE,
            keys::STATUS_CONNECTED,
            keys::STATUS_CONNECTING,
            keys::STATUS_RECONNECTING,
            keys::STATUS_DISCONNECTED,
            keys::STATUS_SAVING,
            keys::STATUS_SAVED_RELOADED,
            keys::STATUS_SAVED_WORKER_DOWN,
            keys::STATUS_CONFIG_ERROR,
            keys::STATUS_AUTO_DETECTED,
            keys::MSG_SAVE_FAILED_EXCEPTION,
            keys::CONFIG_SECTION_PATH,
            keys::CONFIG_SECTION_BEHAVIOR,
            keys::CONFIG_SECTION_EXCLUDED,
            keys::CONFIG_SECTION_LOGS,
            keys::CONFIG_PLACEHOLDER_EXIFTOOL_PATH,
            keys::CONFIG_PLACEHOLDER_HOT_FOLDER,
            keys::CONFIG_PLACEHOLDER_BACKUP_DIR,
            keys::CONFIG_PLACEHOLDER_QUARANTINE_DIR,
            keys::CONFIG_PLACEHOLDER_AUDIT_LOG_DIR,
            keys::CONFIG_EMPTY_USER_EXCLUDED,
            keys::CONFIG_BTN_REMOVE_DIR,
            keys::CONFIG_BTN_ADD_FOLDER,
            keys::SETTINGS_EXIFTOOL_PATH,
            keys::SETTINGS_HOT_FOLDER,
            keys::SETTINGS_BACKUP_DIR,
            keys::SETTINGS_QUARANTINE_DIR,
            keys::SETTINGS_HIDE_ON_STARTUP,
            keys::SETTINGS_HIDE_TRAY,
            keys::SETTINGS_THEME,
            keys::SETTINGS_LANGUAGE,
            keys::SETTINGS_LOG_LEVEL,
            keys::SETTINGS_AUDIT_LOG_DIR,
            keys::CONFIG_ROW_ENABLE_BACKUP,
            keys::CONFIG_ROW_ENABLE_QUARANTINE,
            keys::CONFIG_DESC_EXIFTOOL_PATH,
            keys::CONFIG_DESC_HOT_FOLDER,
            keys::CONFIG_DESC_BACKUP_ENABLED,
            keys::CONFIG_DESC_BACKUP_DIR,
            keys::CONFIG_DESC_QUARANTINE_ENABLED,
            keys::CONFIG_DESC_QUARANTINE_DIR,
            keys::CONFIG_DESC_HIDE_ON_STARTUP,
            keys::CONFIG_DESC_HIDE_TRAY,
            keys::CONFIG_DESC_THEME,
            keys::CONFIG_DESC_LANGUAGE,
            keys::CONFIG_DESC_LOG_LEVEL,
            keys::CONFIG_DESC_AUDIT_LOG_DIR,
            keys::CONFIG_DESC_SYSTEM_EXCLUDED,
            keys::CONFIG_DESC_USER_EXCLUDED,
            keys::CONFIG_LABEL_SYSTEM_EXCLUDED,
            keys::CONFIG_LABEL_USER_EXCLUDED,
            keys::DIALOG_SELECT_EXIFTOOL,
            keys::DIALOG_SELECT_FOLDER,
            keys::DIALOG_SELECT_EXCLUDED_DIR,
            keys::LOGLEVEL_CHIP_ALL,
            keys::LOGLEVEL_CHIP_DEBUG,
            keys::LOGLEVEL_CHIP_INFO,
            keys::LOGLEVEL_CHIP_WARN,
            keys::LOGLEVEL_CHIP_ERROR,
            keys::LOG_SUBTITLE,
            keys::LOG_BTN_CLEAR,
            keys::LOG_EMPTY_TITLE,
            keys::LOG_EMPTY_DESC,
            keys::LOG_EMPTY_FILTERED_TITLE,
            keys::LOG_EMPTY_FILTERED_DESC,
            keys::LOG_ENTRIES_COUNT,
            keys::LOG_COL_TIME,
            keys::LOG_COL_EVENT,
            keys::LOG_COL_SOURCE,
            keys::LOG_COL_MESSAGE,
            keys::OVERVIEW_WORKER_SECTION,
            keys::OVERVIEW_WORKER_MODE,
            keys::OVERVIEW_REPORTED_MODE,
            keys::OVERVIEW_SERVICE_IPC,
            keys::OVERVIEW_PROTOCOL,
            keys::OVERVIEW_EXIFTOOL,
            keys::OVERVIEW_ROUND_TRIPS,
            keys::OVERVIEW_LAST_ROUND_TRIP,
            keys::OVERVIEW_NO_ROUND_TRIP,
            keys::OVERVIEW_REACHABLE,
            keys::OVERVIEW_UNREACHABLE,
            keys::MODE_BACKGROUND,
            keys::MODE_SERVICE,
            keys::THEME_OPTION_SYSTEM,
            keys::THEME_OPTION_LIGHT,
            keys::THEME_OPTION_DARK,
            keys::SERVICE_SECTION,
            keys::SERVICE_STATUS,
            keys::SERVICE_DESC_APPLYING,
            keys::SERVICE_ROW_START_STOP,
            keys::SERVICE_DESC_REFRESH_3S,
            keys::SERVICE_BTN_INSTALL,
            keys::SERVICE_BTN_INSTALL_SHORT,
            keys::SERVICE_BTN_START,
            keys::SERVICE_BTN_STOP,
            keys::SERVICE_BTN_REFRESH,
            keys::SERVICE_BTN_UNINSTALL,
            keys::SERVICE_BTN_UNINSTALL_SHORT,
            keys::SERVICE_DESC_INSTALL,
            keys::SERVICE_DESC_START_STOP,
            keys::SERVICE_DESC_UNINSTALL,
            keys::SERVICE_RESULT_SUCCESS,
            keys::SERVICE_RESULT_SKIPPED,
            keys::SERVICE_RESULT_CANCELLED,
            keys::SERVICE_RESULT_FAILED,
            keys::SERVICE_STATE_RUNNING,
            keys::SERVICE_STATE_NOT_INSTALLED,
            keys::SERVICE_STATE_START_PENDING,
            keys::SERVICE_STATE_PAUSE_PENDING,
            keys::SERVICE_STATE_PAUSED,
            keys::SERVICE_STATE_CONTINUE_PENDING,
            keys::SERVICE_STATE_STOPPED,
            keys::SERVICE_STATE_STOP_PENDING,
            keys::SERVICE_STATE_UNKNOWN,
            keys::RULES_PANEL_SUBTITLE,
            keys::RULES_UNSAVED,
            keys::RULES_EXPERT_MODE,
            keys::RULES_EXPERT_HIDES_NOTE,
            keys::RULES_EXPERT_INFOBAR,
            keys::RULES_STORED_IN,
            keys::RULES_CAUTION,
            keys::RULES_SAVE_FAILED,
            keys::RULES_RESET_FAILED,
            keys::RULES_BTN_SAVE,
            keys::RULES_BTN_RESET,
            keys::TOAST_RULES_SAVED,
            keys::TOAST_RULES_RESET,
            keys::RULES_COL_FAMILY,
            keys::RULES_COL_EXTENSIONS,
            keys::RULES_COL_WARNING,
            keys::RULES_COL_STRIP_ALL,
            keys::RULES_COL_PRESERVE_ICC,
            keys::RULES_COL_STRIP_EXIF,
            keys::RULES_COL_STRIP_XMP,
            keys::RULES_COL_STRIP_IPTC,
            keys::RULES_COL_STRIP_TIME,
            keys::RULES_FAMILY_JPEG,
            keys::RULES_FAMILY_RAW,
            keys::RULES_FAMILY_VIDEO,
            keys::RULES_FAMILY_PDF,
            keys::RULES_FAMILY_EPS,
            keys::TRAY_OPEN_WINDOW,
            keys::TRAY_EXIT,
            keys::TRAY_TOOLTIP,
        ];
        let mut missing: Vec<&str> = ALL
            .iter()
            .filter(|key| !en.contains_key(**key))
            .copied()
            .collect();
        missing.sort_unstable();
        missing.dedup();
        assert!(missing.is_empty(), "en 缺键: {missing:?}");
    }
}
