//! `config.json` 的数据模型。
//!
//! 事实源是 .NET 侧的 `PhotoPrivacy.Core.Configuration.AppConfig` / `AppConfigJson`：
//! 字段名是 snake_case、顺序固定、每个节都能缺省。新 UI 必须与旧 UI 读写同一份文件，
//! 因此这里刻意复制旧侧默认值，而不是另立一套「更合理」的默认。
//!
//! 读侧规则（对齐 `AppConfigLoader`）：
//! - 缺节/缺字段回落 `AppConfig.Default`；
//! - 顶层遗留 `exiftool_path` 仅在 `exiftool.path` 为空时生效；
//! - `quarantine.directory` / `audit.log_directory` 为空时回落到 exe 同目录 `hot/` 下的默认子目录；
//! - `ui` 的 theme/locale/theme_id 为空时回落 system/zh-CN/catppuccin。
//!
//! 写侧规则（对齐 `AppConfigJson.ToIndentedJson`）：所有节都写出、顺序固定、两空格缩进。

use serde::{Deserialize, Deserializer, Serialize, Serializer};
use std::path::{Component, Path, PathBuf};

pub const DEFAULT_SCHEMA_VERSION: i32 = 1;

pub const ALLOWED_EXTENSIONS: &[&str] = &[
    // JPEG family
    ".jpg", ".jpeg", ".jpe", ".jps", ".jph", // TIFF / DNG
    ".tif", ".tiff", ".dng", // RAW
    ".cr2", ".cr3", ".arw", ".nef", ".orf", ".raf", ".rw2", ".pef", ".srw", ".sr2",
    // HEIF / AVIF
    ".heic", ".heif", ".hif", ".avif", // PNG
    ".png", ".apng", // Video (MOV/MP4)
    ".mov", ".mp4", ".m4v", ".qt", ".3gp", ".3g2", // PDF
    ".pdf", // EPS / PS / AI
    ".eps", ".ps", ".ai",
];

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AppConfig {
    #[serde(default = "default_schema_version")]
    pub schema_version: i32,
    #[serde(default)]
    pub exiftool: ExifToolConfig,
    #[serde(default)]
    pub watch: WatchConfig,
    #[serde(default)]
    pub rules: RulesConfig,
    #[serde(default)]
    pub retry: RetryConfig,
    #[serde(default)]
    pub backup: BackupConfig,
    #[serde(default)]
    pub quarantine: QuarantineConfig,
    #[serde(default)]
    pub audit: AuditConfig,
    #[serde(default)]
    pub ui: UiConfig,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            schema_version: DEFAULT_SCHEMA_VERSION,
            exiftool: ExifToolConfig::default(),
            watch: WatchConfig::default(),
            rules: RulesConfig::default(),
            retry: RetryConfig::default(),
            backup: BackupConfig::default(),
            quarantine: QuarantineConfig::default(),
            audit: AuditConfig::default(),
            ui: UiConfig::default(),
        }
    }
}

impl AppConfig {
    /// 解析磁盘上的 config.json，并套用旧侧读侧兜底。
    pub fn from_json(json: &str) -> Result<Self, serde_json::Error> {
        let value: serde_json::Value = serde_json::from_str(json)?;
        let legacy_exiftool_path = value
            .get("exiftool_path")
            .and_then(serde_json::Value::as_str)
            .map(str::to_string);
        let nested_exiftool_path_is_set = value
            .pointer("/exiftool/path")
            .and_then(serde_json::Value::as_str)
            .is_some_and(|path| !path.trim().is_empty());

        // 旧侧 JsonSerializerOptions.PropertyNameCaseInsensitive = true：键名大小写不敏感。
        let mut config: Self = serde_json::from_value(lowercase_object_keys(value))?;
        config.normalize(legacy_exiftool_path, nested_exiftool_path_is_set);
        Ok(config)
    }

    /// 与 `AppConfigJson.ToIndentedJson` 同形：两空格缩进、无结尾换行。
    pub fn to_json(&self) -> String {
        serde_json::to_string_pretty(self).expect("AppConfig 序列化不应失败")
    }

    fn normalize(&mut self, legacy_exiftool_path: Option<String>, nested_path_is_set: bool) {
        self.schema_version = self.schema_version.max(DEFAULT_SCHEMA_VERSION);

        if !nested_path_is_set
            && let Some(path) = legacy_exiftool_path
            && !path.trim().is_empty()
        {
            self.exiftool.path = path;
        }

        if self.backup.suffix.trim().is_empty() {
            self.backup.suffix = ".bak".to_string();
        }
        if self.quarantine.directory.trim().is_empty() {
            self.quarantine.directory = default_quarantine_directory();
        }
        if self.audit.log_directory.trim().is_empty() {
            self.audit.log_directory = default_audit_directory();
        }
        if self.audit.log_level.trim().is_empty() {
            self.audit.log_level = "info".to_string();
        }
        if self.ui.theme_variant.trim().is_empty() {
            self.ui.theme_variant = "system".to_string();
        }
        if self.ui.theme_id.trim().is_empty() {
            self.ui.theme_id = "catppuccin".to_string();
        }
        if self.ui.locale.trim().is_empty() {
            self.ui.locale = "zh-CN".to_string();
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ExifToolConfig {
    #[serde(
        default = "default_exiftool_path",
        deserialize_with = "null_as_empty_string"
    )]
    pub path: String,
    #[serde(default = "default_true")]
    pub enable_windows_long_path: bool,
    #[serde(default = "default_true")]
    pub enable_large_file_support: bool,
    #[serde(default)]
    pub dry_run: bool,
    #[serde(default, deserialize_with = "null_as_empty_vec")]
    pub extra_exiftool_args: Vec<String>,
    #[serde(default = "default_stay_open_pool_size")]
    pub stay_open_pool_size: i32,
    #[serde(default = "default_max_parallel_drain")]
    pub max_parallel_drain: i32,
}

impl Default for ExifToolConfig {
    fn default() -> Self {
        Self {
            path: default_exiftool_path(),
            enable_windows_long_path: true,
            enable_large_file_support: true,
            dry_run: false,
            extra_exiftool_args: Vec::new(),
            stay_open_pool_size: 1,
            max_parallel_drain: 1,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WatchConfig {
    #[serde(
        default = "default_hot_folder",
        deserialize_with = "null_as_empty_string"
    )]
    pub hot_folder: String,
    #[serde(default = "default_true")]
    pub include_subdirectories: bool,
    #[serde(default = "default_debounce_ms")]
    pub debounce_ms: i32,
    #[serde(default = "default_internal_buffer_size")]
    pub internal_buffer_size: i32,
    #[serde(default, deserialize_with = "null_as_empty_vec")]
    pub auto_excluded_directories: Vec<String>,
    #[serde(default)]
    pub polling_interval_seconds: i32,
}

impl Default for WatchConfig {
    fn default() -> Self {
        Self {
            hot_folder: default_hot_folder(),
            include_subdirectories: true,
            debounce_ms: 800,
            internal_buffer_size: 65536,
            auto_excluded_directories: Vec::new(),
            polling_interval_seconds: 0,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RulesConfig {
    // 缺字段用 AppConfig.Default；显式 null 与旧侧 `?? []` 一致，回落空列表。
    #[serde(
        default = "default_allowed_extensions",
        deserialize_with = "null_as_empty_vec"
    )]
    pub allowed_extensions: Vec<String>,
    #[serde(
        default = "default_excluded_patterns",
        deserialize_with = "null_as_empty_vec"
    )]
    pub excluded_patterns: Vec<String>,
    #[serde(
        default = "default_output_mode",
        deserialize_with = "null_as_empty_string"
    )]
    pub output_mode: String,
    #[serde(default, deserialize_with = "null_as_empty_string")]
    pub output_directory: String,
}

impl Default for RulesConfig {
    fn default() -> Self {
        Self {
            allowed_extensions: default_allowed_extensions(),
            excluded_patterns: default_excluded_patterns(),
            output_mode: default_output_mode(),
            output_directory: String::new(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RetryConfig {
    #[serde(default = "default_max_attempts")]
    pub max_attempts: i32,
    // 旧侧显式 null 走 `?? [1]`，与缺字段的 AppConfig.Default（[1,3,10]）不同。
    #[serde(
        default = "default_backoff_seconds",
        deserialize_with = "null_as_single_retry"
    )]
    pub backoff_seconds: Vec<i32>,
}

impl Default for RetryConfig {
    fn default() -> Self {
        Self {
            max_attempts: 3,
            backoff_seconds: vec![1, 3, 10],
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BackupConfig {
    #[serde(default = "default_true")]
    pub enabled: bool,
    #[serde(default, deserialize_with = "null_as_empty_string")]
    pub directory: String,
    #[serde(
        default = "default_backup_suffix",
        deserialize_with = "null_as_empty_string"
    )]
    pub suffix: String,
    #[serde(default = "default_backup_max_size_mb")]
    pub max_size_mb: i32,
    #[serde(default = "default_retain_days")]
    pub retain_days: i32,
}

impl Default for BackupConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            directory: String::new(),
            suffix: ".bak".to_string(),
            max_size_mb: 5000,
            retain_days: 30,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct QuarantineConfig {
    #[serde(default = "default_true")]
    pub enabled: bool,
    #[serde(
        default = "default_quarantine_directory",
        deserialize_with = "null_as_empty_string"
    )]
    pub directory: String,
}

impl Default for QuarantineConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            directory: default_quarantine_directory(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AuditConfig {
    #[serde(
        default = "default_audit_directory",
        deserialize_with = "null_as_empty_string"
    )]
    pub log_directory: String,
    #[serde(default = "default_retain_days")]
    pub retain_days: i32,
    #[serde(default)]
    pub diagnostic_mode: bool,
    #[serde(
        default = "default_log_level",
        deserialize_with = "null_as_empty_string"
    )]
    pub log_level: String,
}

impl Default for AuditConfig {
    fn default() -> Self {
        Self {
            log_directory: default_audit_directory(),
            retain_days: 30,
            diagnostic_mode: false,
            log_level: "info".to_string(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct UiConfig {
    #[serde(default)]
    pub hide_main_window_on_startup: bool,
    #[serde(default)]
    pub hide_tray_icon: bool,
    #[serde(
        default = "default_theme_variant",
        deserialize_with = "null_as_empty_string"
    )]
    pub theme_variant: String,
    #[serde(
        default = "default_theme_id",
        deserialize_with = "null_as_empty_string"
    )]
    pub theme_id: String,
    #[serde(default = "default_locale", deserialize_with = "null_as_empty_string")]
    pub locale: String,
    #[serde(
        default = "default_sidebar_width",
        serialize_with = "serialize_dotnet_number"
    )]
    pub sidebar_width: f64,
}

impl Default for UiConfig {
    fn default() -> Self {
        Self {
            hide_main_window_on_startup: false,
            hide_tray_icon: false,
            theme_variant: default_theme_variant(),
            theme_id: default_theme_id(),
            locale: default_locale(),
            sidebar_width: default_sidebar_width(),
        }
    }
}

/// 旧 UI 的 `WatchPathFilter.ResolveAutoExcludedSubdirectories`：审计/隔离/备份目录
/// 只要严格位于监控目录之下就列为「系统自动排除」，供配置页只读展示。
pub fn resolve_auto_excluded_subdirectories(config: &AppConfig) -> Vec<String> {
    let hot_folder = normalize_path(&config.watch.hot_folder);
    if hot_folder.is_empty() {
        return Vec::new();
    }

    let mut candidates: Vec<&str> = vec![&config.audit.log_directory, &config.quarantine.directory];
    if !config.backup.directory.trim().is_empty() {
        candidates.push(&config.backup.directory);
    }
    let default_backup = default_backup_directory_for(&config.watch.hot_folder);
    candidates.push(&default_backup);
    for directory in &config.watch.auto_excluded_directories {
        candidates.push(directory);
    }

    let mut excluded: Vec<String> = Vec::new();
    for candidate in candidates {
        let normalized = normalize_path(candidate);
        if normalized.is_empty() || !is_strict_subdirectory(&normalized, &hot_folder) {
            continue;
        }
        if !excluded
            .iter()
            .any(|existing| existing.to_lowercase() == normalized.to_lowercase())
        {
            excluded.push(normalized);
        }
    }

    excluded.sort_by_key(|path| path.to_lowercase());
    excluded
}

fn is_strict_subdirectory(candidate: &str, parent: &str) -> bool {
    let candidate = comparable_path(candidate);
    let parent = comparable_path(parent);
    if candidate == parent {
        return false;
    }
    let prefix = if parent.ends_with(std::path::MAIN_SEPARATOR) {
        parent
    } else {
        format!("{parent}{}", std::path::MAIN_SEPARATOR)
    };
    candidate.starts_with(&prefix)
}

fn comparable_path(path: &str) -> String {
    path.replace(['/', '\\'], std::path::MAIN_SEPARATOR_STR)
        .to_lowercase()
}

fn normalize_path(path: &str) -> String {
    if path.trim().is_empty() {
        return String::new();
    }
    let absolute = std::path::absolute(path).unwrap_or_else(|_| PathBuf::from(path));
    // 旧侧是 Path.GetFullPath，会消掉 `.`/`..`；std::path::absolute 不消，需要自己折叠。
    let mut collapsed = PathBuf::new();
    for component in absolute.components() {
        match component {
            Component::CurDir => {}
            // 根/前缀上的 pop 是空操作，`C:\..\x` 不会跑出盘符。
            Component::ParentDir => {
                collapsed.pop();
            }
            other => collapsed.push(other.as_os_str()),
        }
    }
    let mut text = collapsed.to_string_lossy().into_owned();
    // 根路径（`C:\` / `/`）保留尾分隔符，其余去掉；旧 UI 的 NormalizePath 同口径。
    let root_len = if text.len() >= 2 && text.as_bytes()[1] == b':' {
        3
    } else if text.starts_with('/') {
        1
    } else {
        0
    };
    while text.len() > root_len && (text.ends_with('\\') || text.ends_with('/')) {
        text.pop();
    }
    text
}

/// 与旧 UI 的 `ui.theme_variant` 取值域一致；`ThemeMode` 的字符串形态只从这里取。
pub const THEME_VARIANTS: [&str; 3] = ["system", "light", "dark"];

pub fn normalize_theme_variant(value: &str) -> &'static str {
    if value.eq_ignore_ascii_case(THEME_VARIANTS[1]) {
        THEME_VARIANTS[1]
    } else if value.eq_ignore_ascii_case(THEME_VARIANTS[2]) {
        THEME_VARIANTS[2]
    } else {
        THEME_VARIANTS[0]
    }
}

/// 旧 UI `ResolveExifToolPath`：当前路径真实存在就不动；否则探测常见安装位置与 PATH，
/// 返回可用的新路径。返回 `None` 表示保持原值（也是刷新提示的边界）。
pub fn detect_exiftool_path(current: &str) -> Option<String> {
    if !current.trim().is_empty() && Path::new(current).is_file() {
        return None;
    }
    let detected = resolve_default_exiftool_path();
    if detected != current && Path::new(&detected).is_file() {
        Some(detected)
    } else {
        None
    }
}

pub const LOCALES: &[(&str, &str)] = &[
    ("zh-CN", "中文"),
    ("en", "English"),
    ("ja", "日本語"),
    ("ko", "한국어"),
    ("de", "Deutsch"),
    ("fr", "Français"),
    ("es", "Español"),
    ("pt", "Português"),
    ("ru", "Русский"),
    ("ar", "العربية"),
];

pub const LOG_LEVELS: &[&str] = &["all", "info", "debug", "warn", "error"];

fn default_schema_version() -> i32 {
    DEFAULT_SCHEMA_VERSION
}

fn default_true() -> bool {
    true
}

fn default_exiftool_path() -> String {
    resolve_default_exiftool_path()
}

fn default_hot_folder() -> String {
    app_directory()
        .map(|dir| dir.join("hot"))
        .unwrap_or_else(|| PathBuf::from("hot"))
        .to_string_lossy()
        .into_owned()
}

fn default_quarantine_directory() -> String {
    default_hot_folder() + std::path::MAIN_SEPARATOR_STR + "_quarantine"
}

fn default_audit_directory() -> String {
    default_hot_folder() + std::path::MAIN_SEPARATOR_STR + "_audit"
}

pub(crate) fn default_backup_directory_for(hot_folder: &str) -> String {
    if hot_folder.trim().is_empty() {
        String::new()
    } else {
        // Path::join 与 C# Path.Combine 一样吞掉热目录的尾分隔符，不会写出 `hot\\bak`。
        Path::new(hot_folder)
            .join("bak")
            .to_string_lossy()
            .into_owned()
    }
}

fn default_allowed_extensions() -> Vec<String> {
    ALLOWED_EXTENSIONS
        .iter()
        .map(|ext| ext.to_string())
        .collect()
}

fn default_excluded_patterns() -> Vec<String> {
    vec!["~$*".to_string(), "*.tmp".to_string()]
}

fn default_output_mode() -> String {
    "same_as_source".to_string()
}

fn default_max_attempts() -> i32 {
    3
}

fn default_backoff_seconds() -> Vec<i32> {
    vec![1, 3, 10]
}

fn default_backup_suffix() -> String {
    ".bak".to_string()
}

fn default_backup_max_size_mb() -> i32 {
    5000
}

fn default_retain_days() -> i32 {
    30
}

fn default_log_level() -> String {
    "info".to_string()
}

fn default_theme_variant() -> String {
    "system".to_string()
}

fn default_theme_id() -> String {
    "catppuccin".to_string()
}

fn default_locale() -> String {
    "zh-CN".to_string()
}

fn default_sidebar_width() -> f64 {
    200.0
}

fn default_stay_open_pool_size() -> i32 {
    1
}

fn default_max_parallel_drain() -> i32 {
    1
}

fn default_debounce_ms() -> i32 {
    800
}

fn default_internal_buffer_size() -> i32 {
    65536
}

fn app_directory() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf))
}

fn resolve_default_exiftool_path() -> String {
    let mut candidates: Vec<PathBuf> = Vec::new();
    if let Some(dir) = app_directory() {
        candidates.push(dir.join("ExifTool").join(exiftool_file_name()));
        candidates.push(dir.join(exiftool_file_name()));
    }

    if cfg!(windows) {
        candidates.extend([
            PathBuf::from(r"C:\Program Files\ExifTool\exiftool.exe"),
            PathBuf::from(r"C:\Program Files (x86)\ExifTool\exiftool.exe"),
            PathBuf::from(r"C:\Windows\exiftool.exe"),
        ]);
    } else {
        candidates.extend([
            PathBuf::from("/usr/bin/exiftool"),
            PathBuf::from("/usr/local/bin/exiftool"),
            PathBuf::from("/opt/homebrew/bin/exiftool"),
        ]);
    }

    for candidate in &candidates {
        if candidate.is_file() {
            return candidate.to_string_lossy().into_owned();
        }
    }
    if let Some(found) = resolve_exiftool_from_path() {
        return found.to_string_lossy().into_owned();
    }
    if cfg!(windows) {
        r"C:\Program Files\ExifTool\exiftool.exe".to_string()
    } else {
        "exiftool".to_string()
    }
}

fn resolve_exiftool_from_path() -> Option<PathBuf> {
    let name = exiftool_file_name();
    std::env::var_os("PATH").and_then(|paths| {
        std::env::split_paths(&paths)
            .map(|dir| dir.join(name))
            .find(|candidate| candidate.is_file())
    })
}

fn exiftool_file_name() -> &'static str {
    if cfg!(windows) {
        "exiftool.exe"
    } else {
        "exiftool"
    }
}

/// `System.Text.Json` 的键名匹配大小写不敏感；serde 默认敏感，这里统一折成小写再反序列化。
/// 配置里没有自由格式 map，折叠键名不会碰到用户数据。
fn lowercase_object_keys(value: serde_json::Value) -> serde_json::Value {
    match value {
        serde_json::Value::Object(map) => serde_json::Value::Object(
            map.into_iter()
                .map(|(key, value)| (key.to_lowercase(), lowercase_object_keys(value)))
                .collect(),
        ),
        serde_json::Value::Array(items) => {
            serde_json::Value::Array(items.into_iter().map(lowercase_object_keys).collect())
        }
        other => other,
    }
}

fn null_as_empty_string<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: Deserializer<'de>,
{
    Ok(Option::<String>::deserialize(deserializer)?.unwrap_or_default())
}

fn null_as_empty_vec<'de, D, T>(deserializer: D) -> Result<Vec<T>, D::Error>
where
    D: Deserializer<'de>,
    T: Deserialize<'de>,
{
    Ok(Option::<Vec<T>>::deserialize(deserializer)?.unwrap_or_default())
}

fn null_as_single_retry<'de, D>(deserializer: D) -> Result<Vec<i32>, D::Error>
where
    D: Deserializer<'de>,
{
    Ok(Option::<Vec<i32>>::deserialize(deserializer)?.unwrap_or_else(|| vec![1]))
}

/// System.Text.Json 对整数值 double 写出 `200` 而不是 `200.0`；保持同形，避免同一份
/// 配置在两侧来回写时出现无意义的 diff。
fn serialize_dotnet_number<S>(value: &f64, serializer: S) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    if value.is_finite() && value.fract() == 0.0 && value.abs() < 9_007_199_254_740_992.0 {
        serializer.serialize_i64(*value as i64)
    } else {
        serializer.serialize_f64(*value)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const SAMPLE: &str = include_str!("../../../../config/config.sample.json");

    #[test]
    fn sample_config_round_trips_without_losing_fields() {
        // 防：新 UI 写回时丢掉旧 UI 生成的节/字段（rules/retry/backup 等不由本页面编辑），
        // 让同一份 config.json 在两侧互读时漂移（#55）。
        let parsed = AppConfig::from_json(SAMPLE).expect("sample 必须可解析");
        let written = parsed.to_json();
        let reparsed = AppConfig::from_json(&written).expect("写出的 JSON 必须可再解析");
        assert_eq!(parsed, reparsed);

        let value: serde_json::Value = serde_json::from_str(&written).unwrap();
        for section in [
            "schema_version",
            "exiftool",
            "watch",
            "rules",
            "retry",
            "backup",
            "quarantine",
            "audit",
            "ui",
        ] {
            assert!(value.get(section).is_some(), "写回缺少节：{section}");
        }
        assert_eq!(
            value["rules"]["allowed_extensions"]
                .as_array()
                .unwrap()
                .len(),
            ALLOWED_EXTENSIONS.len()
        );
        assert_eq!(
            value["retry"]["backoff_seconds"],
            serde_json::json!([1, 3, 10])
        );
        assert_eq!(value["backup"]["suffix"], ".bak");
    }

    #[test]
    fn serialized_shape_matches_dotnet_writer() {
        // 防：字段名改成 camelCase 或写成浮点 200.0，旧 UI 读回后 sidebar_width 变化、
        // 或字段大小写不匹配被静默忽略（#55）。C# 侧字段名见 AppConfigJson.cs。
        let config = AppConfig::from_json(SAMPLE).unwrap();
        let json = config.to_json();
        assert!(json.contains("\"schema_version\": 1"));
        assert!(json.contains("\"enable_windows_long_path\": true"));
        assert!(json.contains("\"auto_excluded_directories\": []"));
        assert!(json.contains("\"sidebar_width\": 200"), "{json}");
        assert!(!json.contains("\"schemaVersion\""));
    }

    #[test]
    fn missing_sections_use_dotnet_defaults() {
        // 防：config.json 只写了 ui 节时，其余节被序列化成空对象/零值，
        // 旧 UI 读回后热目录、备份、重试等配置被清空（#55）。
        let config = AppConfig::from_json(r#"{"ui":{"theme_variant":"dark"}}"#).unwrap();
        assert_eq!(config.schema_version, 1);
        assert!(config.exiftool.enable_windows_long_path);
        assert_eq!(config.watch.debounce_ms, 800);
        assert_eq!(config.watch.internal_buffer_size, 65536);
        assert_eq!(
            config.rules.allowed_extensions.len(),
            ALLOWED_EXTENSIONS.len()
        );
        assert_eq!(config.retry.backoff_seconds, vec![1, 3, 10]);
        assert!(config.backup.enabled);
        assert!(config.quarantine.enabled);
        assert_eq!(config.audit.retain_days, 30);
        assert_eq!(config.audit.log_level, "info");
        assert_eq!(config.ui.theme_variant, "dark");
        assert_eq!(config.ui.locale, "zh-CN");
        assert_eq!(config.ui.theme_id, "catppuccin");
        assert_eq!(config.ui.sidebar_width, 200.0);
    }

    #[test]
    fn blank_paths_fall_back_to_app_local_defaults() {
        // 防：空白 quarantine/audit 目录被写成空串，Worker 的 Path.Combine("", name)
        // 把隔离/审计文件落到进程 CWD（AppConfigLoader 已专门兜底，见 #55）。
        let config = AppConfig::from_json(
            r#"{"quarantine":{"directory":""},"audit":{"log_directory":"  "}}"#,
        )
        .unwrap();
        assert!(config.quarantine.directory.ends_with("_quarantine"));
        assert!(config.audit.log_directory.ends_with("_audit"));
        assert_eq!(config.backup.suffix, ".bak");
    }

    #[test]
    fn legacy_top_level_exiftool_path_is_honored_only_when_nested_is_blank() {
        // 防：迁移前的顶层 exiftool_path 被忽略，用户 ExifTool 路径在换 UI 后丢失（#55）。
        let legacy = AppConfig::from_json(
            r#"{"exiftool_path":"D:\\tools\\exiftool.exe","exiftool":{"path":""}}"#,
        )
        .unwrap();
        assert_eq!(legacy.exiftool.path, r"D:\tools\exiftool.exe");

        let nested = AppConfig::from_json(
            r#"{"exiftool_path":"D:\\old.exe","exiftool":{"path":"D:\\new.exe"}}"#,
        )
        .unwrap();
        assert_eq!(nested.exiftool.path, r"D:\new.exe");

        // 整个 exiftool 节缺省时，默认路径不能盖掉遗留的顶层值（C# ResolveExifToolPath 同口径）。
        let only_legacy =
            AppConfig::from_json(r#"{"exiftool_path":"D:\\tools\\exiftool.exe"}"#).unwrap();
        assert_eq!(only_legacy.exiftool.path, r"D:\tools\exiftool.exe");
    }

    #[test]
    fn null_values_fall_back_like_the_dotnet_loader() {
        // 防：旧 UI 读得下的 null（数组/字符串）把新 UI 卡成「config.json error」并拒绝保存（#55）。
        let config = AppConfig::from_json(
            r#"{
                "exiftool": {"path": null, "extra_exiftool_args": null},
                "watch": {"hot_folder": null, "auto_excluded_directories": null},
                "rules": {"allowed_extensions": null, "excluded_patterns": null,
                          "output_mode": null, "output_directory": null},
                "retry": {"backoff_seconds": null},
                "backup": {"directory": null, "suffix": null},
                "quarantine": {"directory": null},
                "audit": {"log_directory": null, "log_level": null},
                "ui": {"theme_variant": null, "theme_id": null, "locale": null}
            }"#,
        )
        .expect("显式 null 必须可解析");

        assert_eq!(config.exiftool.path, "");
        assert!(config.exiftool.extra_exiftool_args.is_empty());
        assert_eq!(config.watch.hot_folder, "");
        assert!(config.watch.auto_excluded_directories.is_empty());
        // 旧侧 `?? []`：显式 null 不等同于缺字段的 AppConfig.Default。
        assert!(config.rules.allowed_extensions.is_empty());
        assert!(config.rules.excluded_patterns.is_empty());
        // 旧侧 Retry 显式 null 走 `?? [1]`。
        assert_eq!(config.retry.backoff_seconds, vec![1]);
        assert_eq!(config.backup.suffix, ".bak");
        assert_eq!(config.audit.log_level, "info");
        assert_eq!(config.ui.theme_variant, "system");
        assert_eq!(config.ui.locale, "zh-CN");
    }

    #[test]
    fn property_names_are_case_insensitive_like_dotnet() {
        // 防：旧侧 PropertyNameCaseInsensitive=true 读得下的键，新 UI 静默落默认值再覆盖原文件（#55）。
        let config = AppConfig::from_json(
            r#"{"Schema_Version": 2, "Watch": {"Hot_Folder": "D:\\photos"},
                "UI": {"Theme_Variant": "dark", "Locale": "en"}}"#,
        )
        .unwrap();
        assert_eq!(config.schema_version, 2);
        assert_eq!(config.watch.hot_folder, r"D:\photos");
        assert_eq!(config.ui.theme_variant, "dark");
        assert_eq!(config.ui.locale, "en");
    }

    #[test]
    fn dot_segments_are_collapsed_before_subdirectory_checks() {
        // 防：`<hot>/../photos-other` 被前缀比较误当成子目录，配置页列出错误排除项（#55）。
        let base = std::env::temp_dir().join("photoprivacy-dot-segments-test");
        let hot = base.join("photos");
        let mut config = AppConfig::default();
        config.watch.hot_folder = hot.display().to_string();
        config.watch.auto_excluded_directories = vec![format!(
            "{}{}..{}photos-other",
            hot.display(),
            std::path::MAIN_SEPARATOR,
            std::path::MAIN_SEPARATOR
        )];

        let excluded = resolve_auto_excluded_subdirectories(&config);
        assert!(
            !excluded.iter().any(|path| path.ends_with("photos-other")),
            "`..` 必须折叠后再判断，兄弟目录不能入排除列表：{excluded:?}"
        );
    }

    #[test]
    fn auto_excluded_directories_match_old_ui_rules() {
        // 防：系统自动排除列表把监控目录本身或兄弟目录列进去，用户在配置页看到错误信息（#55）。
        let base = std::env::temp_dir().join("photoprivacy-auto-excluded-test");
        let hot = base.join("photos");
        let mut config = AppConfig::default();
        config.watch.hot_folder = hot.display().to_string();
        config.quarantine.directory = hot.join("_quarantine").display().to_string();
        config.audit.log_directory = hot.join("_audit").display().to_string();
        config.backup.directory = hot.join("bak").display().to_string();
        config.watch.auto_excluded_directories = vec![
            hot.join("custom").display().to_string(),
            base.join("photos-other").display().to_string(),
        ];

        let excluded = resolve_auto_excluded_subdirectories(&config);
        assert_eq!(
            excluded.len(),
            4,
            "备份+审计+隔离+自定义各一条：{excluded:?}"
        );
        assert!(excluded.iter().any(|path| path.ends_with("bak")));
        assert!(excluded.iter().any(|path| path.ends_with("custom")));
        assert!(excluded.iter().any(|path| path.ends_with("_audit")));
        assert!(excluded.iter().any(|path| path.ends_with("_quarantine")));
        assert!(
            !excluded.iter().any(|path| path.ends_with("photos-other")),
            "兄弟目录不能被当成热目录的子目录：{excluded:?}"
        );
        assert!(
            !excluded.iter().any(|path| path == &config.watch.hot_folder),
            "热目录本身不能进排除列表：{excluded:?}"
        );
    }
}
