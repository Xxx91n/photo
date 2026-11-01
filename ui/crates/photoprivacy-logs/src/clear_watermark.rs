//! 「清空水位」持久化：用户上次清空日志视图的绝对 UTC 时刻。
//!
//! 水位文件是 config.json 同目录的独立小文件（`logs-clear-watermark.json`），不进
//! config.json；写入走 `<文件>.write.tmp` → rename 的原子覆盖，与 `photoprivacy-ui`
//! 的 config_file.rs 同一写盘顺序。水位损坏不做 `.bak` 备份链——兜底是 fail-open：
//! 按无水位处理，最坏回灌一次，下次清空重写。

use chrono::{DateTime, SecondsFormat, Utc};
use serde_json::{Value, json};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

// Design(#136): 水位落 config.json 同目录的独立小文件，不进 config.json（config/rules JSON 契约冻结）。
pub const CLEAR_WATERMARK_FILENAME: &str = "logs-clear-watermark.json";

/// 「清空水位」存储：用户上次清空日志视图的绝对 UTC 时刻。
// Design(#136): 水位值是绝对 UTC 时刻，不存本地墙钟日期、文件名日期或字节偏移。
pub struct ClearWatermark {
    path: PathBuf,
}

impl ClearWatermark {
    /// config.json 路径 → 同目录水位文件；与 config.json 的落盘目录保持同源。
    pub fn from_config_path(config_path: &Path) -> Self {
        let directory = config_path.parent().unwrap_or_else(|| Path::new(""));
        Self::from_directory(directory)
    }

    /// 以显式目录构造水位存储；测试与无 config.json 场景使用。
    pub fn from_directory(directory: &Path) -> Self {
        Self {
            path: directory.join(CLEAR_WATERMARK_FILENAME),
        }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    /// 读取水位：文件缺失或损坏（非 JSON、缺键、类型不符、时间不可解析）返回 `None`，
    /// 其余读取失败同样按无水位处理——首启语义不变，绝不阻塞启动。
    ///
    /// 不创建、不改写水位文件；损坏文件的恢复动作是下次清空时的重写。
    // Design(#136): 文件缺失/损坏一律按无水位处理（fail-open），下次清空重写，load 绝不阻塞启动。
    pub fn load(&self) -> Option<DateTime<Utc>> {
        let text = fs::read_to_string(&self.path).ok()?;
        let root: Value = serde_json::from_str(&text).ok()?;
        let timestamp = root.get("cleared_at_utc")?.as_str()?;
        DateTime::parse_from_rfc3339(timestamp)
            .ok()
            .map(|time| time.with_timezone(&Utc))
    }

    /// 写入水位（用户清空视图时的唯一写入口）：临时文件 + rename 原子覆盖，
    /// 同一次进程内连续清空只保留最新一次。
    // Design(#136): 水位只由用户清空视图写入，原子覆盖（临时文件+rename），连续清空只保留最新。
    pub fn save(&self, cleared_at: DateTime<Utc>) -> io::Result<()> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }
        let body =
            json!({ "cleared_at_utc": cleared_at.to_rfc3339_opts(SecondsFormat::AutoSi, true) });
        let write_tmp = with_suffix(&self.path, "write.tmp");
        fs::write(&write_tmp, body.to_string())?;
        fs::rename(&write_tmp, &self.path)?;
        Ok(())
    }
}

/// `logs-clear-watermark.json` -> `logs-clear-watermark.json.write.tmp`
/// （字符串追加，不替换扩展名，与 config_file.rs 的临时文件命名一致）。
fn with_suffix(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| CLEAR_WATERMARK_FILENAME.to_string());
    name.push('.');
    name.push_str(suffix);
    path.with_file_name(name)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn utc(text: &str) -> DateTime<Utc> {
        DateTime::parse_from_rfc3339(text)
            .expect("rfc3339 timestamp")
            .with_timezone(&Utc)
    }

    #[test]
    fn missing_file_loads_no_watermark_and_creates_no_file() {
        // 防：首启把「没有水位」当错误阻塞启动，或顺手建出空水位文件（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        assert_eq!(watermark.load(), None);
        assert!(!watermark.path().exists());
    }

    #[test]
    fn save_then_fresh_load_round_trips_the_absolute_utc_instant() {
        // 防：水位落盘后读回值漂移，重启后用错时刻过滤（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        let cleared_at = utc("2026-11-01T02:03:04.123456789Z");

        watermark.save(cleared_at).expect("save watermark");
        // 模拟重启：新建实例从磁盘读回。
        let reloaded = ClearWatermark::from_directory(temp.path());
        assert_eq!(reloaded.load(), Some(cleared_at));
    }

    #[test]
    fn stored_value_is_absolute_utc_in_rfc3339() {
        // 防：水位值存成本地墙钟或相对秒数，跨日与 #129 分叉时窗下错删或漏显（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        watermark
            .save(utc("2026-11-01T02:03:04Z"))
            .expect("save watermark");

        let text = fs::read_to_string(watermark.path()).expect("read watermark file");
        assert_eq!(text, r#"{"cleared_at_utc":"2026-11-01T02:03:04Z"}"#);
    }

    #[test]
    fn save_is_atomic_and_leaves_no_temp_file_behind() {
        // 防：写入中途崩溃留下半份水位文件被当成有效时刻，或临时文件长期残留（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        let cleared_at = utc("2026-11-01T02:03:04Z");

        watermark.save(cleared_at).expect("save watermark");
        assert!(
            !temp
                .path()
                .join("logs-clear-watermark.json.write.tmp")
                .exists()
        );
        assert_eq!(
            ClearWatermark::from_directory(temp.path()).load(),
            Some(cleared_at)
        );
    }

    #[test]
    fn save_creates_missing_parent_directory() {
        // 防：config 目录尚未建立时清空动作直接失败，水位没落盘（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let config_dir = temp.path().join("config");
        let watermark = ClearWatermark::from_directory(&config_dir);

        watermark
            .save(utc("2026-11-01T02:03:04Z"))
            .expect("save watermark");
        assert!(watermark.path().is_file());
    }

    #[test]
    fn corrupt_file_loads_no_watermark_and_survives_until_next_save() {
        // 防：损坏水位被当成有效时刻错删新日志；或 load 顺手删改文件，把 fail-open 变成破坏（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        fs::write(watermark.path(), "{not json").expect("write corrupt file");

        assert_eq!(watermark.load(), None);
        assert_eq!(
            fs::read_to_string(watermark.path()).as_deref(),
            Ok("{not json")
        );

        let cleared_at = utc("2026-11-01T02:03:04Z");
        watermark.save(cleared_at).expect("save watermark");
        assert_eq!(watermark.load(), Some(cleared_at));
    }

    #[test]
    fn malformed_payloads_all_load_as_no_watermark() {
        // 防：缺键/类型不符/空文件/非 UTF-8 的水位文件被解析出错误时刻（#136）。
        let payloads: Vec<Vec<u8>> = vec![
            b"{}".to_vec(),
            br#"{"cleared_at_utc":1761996000}"#.to_vec(),
            br#"{"cleared_at_utc":"not-a-timestamp"}"#.to_vec(),
            br#"["1761996000"]"#.to_vec(),
            Vec::new(),
            vec![0xFF, 0xFE, 0x00, 0x62],
        ];
        for payload in payloads {
            let temp = tempfile::tempdir().expect("temp dir");
            let watermark = ClearWatermark::from_directory(temp.path());
            fs::write(watermark.path(), &payload).expect("write payload");
            assert_eq!(
                watermark.load(),
                None,
                "payload {:?} 应按无水位处理",
                String::from_utf8_lossy(&payload)
            );
        }
    }

    #[test]
    fn repeated_saves_keep_only_the_latest_watermark() {
        // 防：连续清空后水位停在旧值，重启后把已清历史重新放出来（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        let first = utc("2026-11-01T02:03:04Z");
        let second = utc("2026-11-01T02:03:05Z");

        watermark.save(first).expect("save first watermark");
        watermark.save(second).expect("save second watermark");
        assert_eq!(watermark.load(), Some(second));
    }

    #[test]
    fn config_path_derives_same_directory_watermark_file() {
        // 防：水位文件漂进别的目录，config.json 迁移后水位语义断裂（#136）。
        let watermark = ClearWatermark::from_config_path(Path::new("app/config/config.json"));
        assert_eq!(
            watermark.path(),
            Path::new("app/config/logs-clear-watermark.json")
        );
    }
}
