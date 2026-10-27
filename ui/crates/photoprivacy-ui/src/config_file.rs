//! config.json 的磁盘访问：加载、原子写、写前 `.bak` 备份、外部变更指纹。
//!
//! 写盘顺序逐字对齐旧 UI 的 `ConfigEditor.UpdateConfig`：
//! 1. 已有文件先复制成 `<config>.bak.tmp` 再改名 `<config>.bak`；
//! 2. 新内容写入 `<config>.write.tmp`；
//! 3. 原子改名覆盖 `<config>`。
//!
//! 两个临时文件名也保持旧侧同名，避免同一目录下出现两套残留。

use crate::config::AppConfig;
use std::collections::hash_map::DefaultHasher;
use std::fmt;
use std::fs;
use std::hash::{Hash, Hasher};
use std::io;
use std::path::{Path, PathBuf};

#[derive(Debug)]
pub enum ConfigFileError {
    Io(io::Error),
    Parse(serde_json::Error),
}

impl fmt::Display for ConfigFileError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ConfigFileError::Io(err) => write!(f, "config.json 读写失败: {err}"),
            ConfigFileError::Parse(err) => write!(f, "config.json 解析失败: {err}"),
        }
    }
}

impl std::error::Error for ConfigFileError {}

impl From<io::Error> for ConfigFileError {
    fn from(value: io::Error) -> Self {
        ConfigFileError::Io(value)
    }
}

impl From<serde_json::Error> for ConfigFileError {
    fn from(value: serde_json::Error) -> Self {
        ConfigFileError::Parse(value)
    }
}

/// 外部变更探测用：内容哈希比 mtime 可靠（Windows 文件时间戳在快速连续写入时可能相同）。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ConfigFingerprint(u64);

#[derive(Clone, Debug)]
pub struct ConfigFile {
    path: PathBuf,
}

impl ConfigFile {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into() }
    }

    /// 文件不存在时返回 `AppConfig::default()`（与旧 UI 的 `LoadConfigOrDefault` 一致）；
    /// 文件存在但解析失败时把错误交给调用方，绝不静默重置用户配置。
    pub fn load_or_default(&self) -> Result<AppConfig, ConfigFileError> {
        match fs::read_to_string(&self.path) {
            Ok(text) => Ok(AppConfig::from_json(&text)?),
            Err(err) if err.kind() == io::ErrorKind::NotFound => Ok(AppConfig::default()),
            Err(err) => Err(ConfigFileError::Io(err)),
        }
    }

    // Design(#55): 写盘顺序沿用旧 UI（.bak → .write.tmp → rename），新旧 UI 对同一文件保持同一原子写契约。
    pub fn save(&self, config: &AppConfig) -> Result<(), ConfigFileError> {
        let json = config.to_json();
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }

        if self.path.exists() {
            let backup = with_suffix(&self.path, "bak");
            let backup_tmp = with_suffix(&self.path, "bak.tmp");
            fs::copy(&self.path, &backup_tmp)?;
            fs::rename(&backup_tmp, &backup)?;
        }

        let write_tmp = with_suffix(&self.path, "write.tmp");
        fs::write(&write_tmp, json)?;
        fs::rename(&write_tmp, &self.path)?;
        Ok(())
    }

    pub fn fingerprint(&self) -> Option<ConfigFingerprint> {
        let bytes = fs::read(&self.path).ok()?;
        let mut hasher = DefaultHasher::new();
        bytes.hash(&mut hasher);
        Some(ConfigFingerprint(hasher.finish()))
    }
}

/// `config.json` -> `config.json.bak`（旧 UI 用字符串追加，不替换扩展名）。
fn with_suffix(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| "config.json".to_string());
    name.push('.');
    name.push_str(suffix);
    path.with_file_name(name)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TempDir(PathBuf);

    impl TempDir {
        fn new(name: &str) -> Self {
            let nanos = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos();
            let dir = std::env::temp_dir().join(format!(
                "photoprivacy-config-{}-{name}-{nanos}",
                std::process::id()
            ));
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }

        fn config_path(&self) -> PathBuf {
            self.0.join("config").join("config.json")
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn missing_file_loads_defaults() {
        // 防：首次启动时把「文件不存在」当错误弹窗，或反过来静默创建空配置（#55）。
        let dir = TempDir::new("missing");
        let config = ConfigFile::new(dir.config_path())
            .load_or_default()
            .unwrap();
        assert_eq!(config, AppConfig::default());
    }

    #[test]
    fn save_creates_parent_and_round_trips() {
        // 防：配置目录不存在时保存失败，用户改了设置却没有落盘（#55）。
        let dir = TempDir::new("save");
        let file = ConfigFile::new(dir.config_path());
        let mut config = AppConfig::default();
        config.ui.theme_variant = "dark".to_string();
        config.watch.hot_folder = "D:\\photos".to_string();

        file.save(&config).unwrap();
        assert!(dir.config_path().is_file());
        let loaded = file.load_or_default().unwrap();
        assert_eq!(loaded, config);
    }

    #[test]
    fn second_save_keeps_previous_config_in_bak() {
        // 防：原子写只保留新文件，用户手改坏配置后无法从 .bak 回滚（旧 UI 契约，#55）。
        let dir = TempDir::new("bak");
        let file = ConfigFile::new(dir.config_path());
        let mut first = AppConfig::default();
        first.audit.log_level = "warn".to_string();
        file.save(&first).unwrap();

        let mut second = first.clone();
        second.audit.log_level = "error".to_string();
        file.save(&second).unwrap();

        let backup = with_suffix(&dir.config_path(), "bak");
        let backup_text = fs::read_to_string(backup).unwrap();
        assert_eq!(
            AppConfig::from_json(&backup_text).unwrap().audit.log_level,
            "warn"
        );
        assert_eq!(file.load_or_default().unwrap().audit.log_level, "error");
        assert!(!with_suffix(&dir.config_path(), "write.tmp").exists());
    }

    #[test]
    fn fingerprint_tracks_content_not_only_mtime() {
        // 防：外部编辑器在同一时间戳粒度内改写配置，轮询 watcher 误判为无变化（#55）。
        let dir = TempDir::new("fingerprint");
        let file = ConfigFile::new(dir.config_path());
        let mut config = AppConfig::default();
        file.save(&config).unwrap();
        let before = file.fingerprint().unwrap();

        config.ui.hide_tray_icon = true;
        file.save(&config).unwrap();
        let after = file.fingerprint().unwrap();
        assert_ne!(before, after);
    }

    #[test]
    fn invalid_json_is_an_error_not_a_silent_reset() {
        // 防：用户手改坏 config.json 后，新 UI 用默认值覆盖掉原文件（旧 UI 保留旧配置，#55）。
        let dir = TempDir::new("invalid");
        let file = ConfigFile::new(dir.config_path());
        fs::create_dir_all(dir.config_path().parent().unwrap()).unwrap();
        fs::write(dir.config_path(), "{not json").unwrap();
        assert!(file.load_or_default().is_err());
        assert_eq!(fs::read_to_string(dir.config_path()).unwrap(), "{not json");
    }
}
