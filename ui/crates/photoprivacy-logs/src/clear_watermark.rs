//! 「清空水位」持久化：用户上次清空日志视图的绝对 UTC 时刻。
//!
//! 水位文件是 config.json 同目录的独立小文件（`logs-clear-watermark.json`），不进
//! config.json；写入走「同目录临时文件 → rename」的原子覆盖，与 `photoprivacy-ui`
//! 的 config_file.rs 同一契约，但临时文件每次调用独立命名，多个调用互不踩踏。
//! 水位损坏不做 `.bak` 备份链——兜底是 fail-open：按无水位处理，最坏回灌一次，
//! 下次清空重写。

use chrono::{DateTime, SecondsFormat, Utc};
use serde_json::{Value, json};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

/// 进程内 save 调用序号：保证每次写入的临时文件名互不相同。
static SAVE_SEQUENCE: AtomicU64 = AtomicU64::new(0);

// Design(#209): 水位落 config.json 同目录的独立小文件，不进 config.json（config/rules JSON 契约冻结）。
pub const CLEAR_WATERMARK_FILENAME: &str = "logs-clear-watermark.json";

/// 「清空水位」存储：用户上次清空日志视图的绝对 UTC 时刻。
// Design(#209): 水位值是绝对 UTC 时刻，不存本地墙钟日期、文件名日期或字节偏移。
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
    // Design(#209): 文件缺失/损坏一律按无水位处理（fail-open），下次清空重写，load 绝不阻塞启动。
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
    ///
    /// 临时文件按调用独立命名，并发的 save 不会共用固定 `.write.tmp` 互相踩踏
    /// （一方把另一方的值 rename 落盘、或对方因临时文件消失而失败）；调用的串行化
    /// 仍由调用方负责（UI 单实例、单一清空路径，接线归 #137）。崩溃可能残留一个
    /// 临时文件（体积极小、不影响读取），不做清扫——清扫会在并发下删掉别的调用
    /// 正在写的临时文件。
    // Design(#209): 水位只由用户清空视图写入，原子覆盖（临时文件+rename），连续清空只保留最新。
    pub fn save(&self, cleared_at: DateTime<Utc>) -> io::Result<()> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }
        let body =
            json!({ "cleared_at_utc": cleared_at.to_rfc3339_opts(SecondsFormat::AutoSi, true) });
        let write_tmp = self.unique_write_tmp();
        fs::write(&write_tmp, body.to_string())?;
        fs::rename(&write_tmp, &self.path)?;
        Ok(())
    }

    /// `logs-clear-watermark.json` → `logs-clear-watermark.json.write.<pid>.<序号>.tmp`：
    /// 同目录、每次调用唯一，rename 后即消失；进程内靠序号、跨进程靠 pid 区分。
    fn unique_write_tmp(&self) -> PathBuf {
        let sequence = SAVE_SEQUENCE.fetch_add(1, Ordering::Relaxed);
        with_suffix(
            &self.path,
            &format!("write.{}.{}.tmp", std::process::id(), sequence),
        )
    }
}

/// `文件名` -> `文件名.<suffix>`（字符串追加，不替换扩展名，与 config_file.rs 同一手法）。
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
        // 防：写入中途崩溃留下半份水位文件被当成有效时刻；并发 save 共用固定临时
        // 文件互相踩踏（CodeRabbit #140 线程）——临时文件按调用独立命名，落盘后
        // 目录里不残留任何 .tmp（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        let cleared_at = utc("2026-11-01T02:03:04Z");

        watermark.save(cleared_at).expect("save watermark");
        watermark.save(cleared_at).expect("save watermark again");
        let leftovers: Vec<String> = std::fs::read_dir(temp.path())
            .expect("read dir")
            .map(|entry| {
                entry
                    .expect("dir entry")
                    .file_name()
                    .to_string_lossy()
                    .into_owned()
            })
            .filter(|name| name.ends_with(".tmp"))
            .collect();
        assert_eq!(leftovers, Vec::<String>::new());
        assert_eq!(
            ClearWatermark::from_directory(temp.path()).load(),
            Some(cleared_at)
        );
    }

    #[test]
    fn staged_save_is_invisible_until_rename_replaces_the_target() {
        // 防：原子替换测试只查「无 .tmp 残留」——原地写也能通过（CodeRabbit #140 Minor）；
        // 契约是 rename 前写入不可见、旧水位保持可读，rename 一步替换到新值（#136）。
        let temp = tempfile::tempdir().expect("temp dir");
        let watermark = ClearWatermark::from_directory(temp.path());
        let first = utc("2026-11-01T02:03:04Z");
        let second = utc("2026-11-01T02:03:05Z");
        watermark.save(first).expect("save first watermark");

        // 模拟 save 在 rename 前中断：临时文件已写好，替换未发生。
        let staged = watermark.unique_write_tmp();
        let staged_body =
            json!({ "cleared_at_utc": second.to_rfc3339_opts(SecondsFormat::AutoSi, true) })
                .to_string();
        fs::write(&staged, staged_body).expect("write staged body");

        // 写入阶段不产生可观测变化：旧水位照常可读，目标文件未被触碰。
        assert_eq!(watermark.load(), Some(first));

        // rename = 原子替换：旧值一步到新值，临时文件消失。
        fs::rename(&staged, watermark.path()).expect("replace watermark");
        assert_eq!(watermark.load(), Some(second));
        assert!(!staged.exists());
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
        let after_load = fs::read_to_string(watermark.path()).expect("read corrupt file");
        assert_eq!(after_load, "{not json");

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
