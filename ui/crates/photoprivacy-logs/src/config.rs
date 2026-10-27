use crate::model::LevelFilter;
use serde_json::Value;
use std::path::{Path, PathBuf};

/// 日志页启动所需的配置切片：审计目录与初始级别过滤。
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AuditConfig {
    pub log_directory: PathBuf,
    pub log_level: LevelFilter,
}

/// 从同一份 `config.json` 读取 `audit.log_directory` / `audit.log_level`。
///
/// 与旧 UI 的解析口径一致：字段缺失、为空或文件不可读时回落到 `fallback_directory`
/// （旧 UI 为 `AppContext.BaseDirectory/_audit`），级别未知/缺失回落 INFO。
/// 目录按原值使用，不在这里展开相对路径——Worker 也用同一份值。
pub fn resolve_audit_config(config_path: Option<&Path>, fallback_directory: &Path) -> AuditConfig {
    let mut log_directory = None;
    let mut log_level = LevelFilter::Info;

    if let Some(config_path) = config_path
        && let Ok(text) = std::fs::read_to_string(config_path)
        && let Ok(root) = serde_json::from_str::<Value>(&text)
    {
        log_directory = root
            .pointer("/audit/log_directory")
            .and_then(Value::as_str)
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(PathBuf::from);
        log_level = root
            .pointer("/audit/log_level")
            .and_then(Value::as_str)
            .map(LevelFilter::parse)
            .unwrap_or(LevelFilter::Info);
    }

    AuditConfig {
        log_directory: log_directory.unwrap_or_else(|| fallback_directory.to_path_buf()),
        log_level,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn write_config(text: &str) -> tempfile::NamedTempFile {
        let mut file = tempfile::NamedTempFile::new().expect("temp config");
        file.write_all(text.as_bytes()).expect("write config");
        file
    }

    #[test]
    fn missing_config_falls_back_to_app_local_audit_directory() {
        // 防：配置缺失时日志页指向空目录，明明有日志却一直显示空态（#57）。
        let fallback = Path::new("C:/app/_audit");
        let config = resolve_audit_config(Some(Path::new("C:/app/config/missing.json")), fallback);
        assert_eq!(config.log_directory, fallback);
        assert_eq!(config.log_level, LevelFilter::Info);
    }

    #[test]
    fn empty_log_directory_falls_back_like_old_ui() {
        // 防：config.sample.json 的空字符串路径被当成有效目录，tail 永远读不到文件（#57）。
        let file = write_config(r#"{"audit":{"log_directory":"   ","log_level":"debug"}}"#);
        let config = resolve_audit_config(Some(file.path()), Path::new("C:/app/_audit"));
        assert_eq!(config.log_directory, Path::new("C:/app/_audit"));
        assert_eq!(config.log_level, LevelFilter::Debug);
    }

    #[test]
    fn configured_directory_and_level_are_used() {
        // 防：只读了目录却忽略用户配置的级别，或把路径字段名读错导致回落到默认目录（#57）。
        let file =
            write_config(r#"{"audit":{"log_directory":"D:\\hot\\_audit","log_level":"warn"}}"#);
        let config = resolve_audit_config(Some(file.path()), Path::new("C:/app/_audit"));
        assert_eq!(config.log_directory, Path::new(r"D:\hot\_audit"));
        assert_eq!(config.log_level, LevelFilter::Warn);
    }

    #[test]
    fn unknown_level_defaults_to_info() {
        // 防：未知级别被当成 ALL，默认把 DEBUG 噪声全量倒进日志页（#57）。
        let file = write_config(r#"{"audit":{"log_directory":"D:/logs","log_level":"trace"}}"#);
        let config = resolve_audit_config(Some(file.path()), Path::new("C:/app/_audit"));
        assert_eq!(config.log_level, LevelFilter::Info);
    }
}
