use chrono::{DateTime, FixedOffset, Local};
use serde_json::Value;

/// 审计事件级别，排序即过滤强度（Debug < Info < Warn < Error）。
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum AuditLevel {
    Debug,
    Info,
    Warn,
    Error,
}

impl AuditLevel {
    /// 未知/缺失级别按 INFO 处理，与旧 UI `ShouldInclude` 的兜底一致。
    pub fn parse(text: &str) -> Self {
        match text.to_ascii_lowercase().as_str() {
            "debug" => Self::Debug,
            "warn" => Self::Warn,
            "error" => Self::Error,
            _ => Self::Info,
        }
    }
}

/// 日志页级别过滤。`All` 与 `Debug` 都显示 DEBUG：旧 UI 两者阈值同为 0。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LevelFilter {
    All,
    Debug,
    Info,
    Warn,
    Error,
}

impl LevelFilter {
    pub fn parse(text: &str) -> Self {
        match text.to_ascii_lowercase().as_str() {
            "all" => Self::All,
            "debug" => Self::Debug,
            "warn" => Self::Warn,
            "error" => Self::Error,
            _ => Self::Info,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::All => "All",
            Self::Debug => "Debug",
            Self::Info => "Info",
            Self::Warn => "Warn",
            Self::Error => "Error",
        }
    }

    pub fn all() -> [Self; 5] {
        [Self::All, Self::Debug, Self::Info, Self::Warn, Self::Error]
    }

    pub fn allows(self, level: AuditLevel) -> bool {
        match self {
            Self::All | Self::Debug => true,
            Self::Info => level >= AuditLevel::Info,
            Self::Warn => level >= AuditLevel::Warn,
            Self::Error => level >= AuditLevel::Error,
        }
    }
}

/// 事件徽章的语义色分组；具体色值由 UI 主题 token 决定。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EventTone {
    Success,
    Neutral,
    Muted,
    Info,
    Warning,
    Danger,
}

/// 一行审计 JSONL 的展示投影。解析失败或空白行不产生记录。
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AuditRecord {
    /// 本地时区展示时间（HH:MM:SS）。
    pub time_text: String,
    /// 原始 `timestamp_utc`；缺失或不可解析时记为解析时刻，供跨批次排序使用。
    pub timestamp_utc: DateTime<FixedOffset>,
    pub level: AuditLevel,
    pub event_type: String,
    pub display_event: String,
    pub source_path_masked: String,
    pub message: String,
    pub tone: EventTone,
}

/// 旧 UI `AuditTailService.ParseAuditLine` 的等价投影：容错解析，坏行直接丢弃。
pub fn parse_line(line: &str) -> Option<AuditRecord> {
    let line = line.trim();
    if line.is_empty() {
        return None;
    }

    let root: Value = serde_json::from_str(line).ok()?;
    if !root.is_object() {
        return None;
    }

    let event_type = root
        .get("event_type")
        .and_then(Value::as_str)
        .unwrap_or("unknown");
    let level = AuditLevel::parse(root.get("level").and_then(Value::as_str).unwrap_or("INFO"));
    let timestamp_utc = root
        .get("timestamp_utc")
        .and_then(Value::as_str)
        .and_then(|text| DateTime::parse_from_rfc3339(text).ok())
        .unwrap_or_else(|| Local::now().fixed_offset());
    let time_text = timestamp_utc
        .with_timezone(&Local)
        .format("%H:%M:%S")
        .to_string();

    Some(AuditRecord {
        time_text,
        timestamp_utc,
        level,
        event_type: event_type.to_string(),
        display_event: display_event(event_type),
        source_path_masked: root
            .get("source_path_masked")
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string(),
        message: root
            .get("message")
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string(),
        tone: event_tone(event_type),
    })
}

/// 已知事件复用 en.json 的 `audit.*` 文案；未知事件保留原始 event_type（#60 接入 i18n 后替换）。
pub fn display_event(event_type: &str) -> String {
    match event_type {
        "exiftool_started" => "ExifTool started",
        "service_started" => "Service started",
        "file_processing_succeeded" => "Cleaned",
        "file_skipped" => "Skipped",
        "file_detected" => "File detected",
        "instance_conflict" => "Duplicate launch rejected",
        "file_processing_failed" => "Cleaning failed",
        other => other,
    }
    .to_string()
}

pub fn event_tone(event_type: &str) -> EventTone {
    match event_type {
        "exiftool_started" | "service_started" => EventTone::Success,
        "file_processing_succeeded" => EventTone::Neutral,
        "file_skipped" => EventTone::Muted,
        "file_detected" => EventTone::Info,
        "instance_conflict" => EventTone::Warning,
        "file_processing_failed" => EventTone::Danger,
        _ => EventTone::Neutral,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_line_matches_old_ui_projection() {
        // 防：字段名/大小写漂移后时间、路径、文案全空，日志页只剩级别徽章（#57）。
        let line = r#"{"event_type":"file_processing_succeeded","level":"WARN","timestamp_utc":"2026-10-27T01:02:03.0000000Z","source_path_masked":"C:\\photos\\a.jpg","message":"cleaned"}"#;
        let record = parse_line(line).expect("valid audit line");
        assert_eq!(record.level, AuditLevel::Warn);
        assert_eq!(record.event_type, "file_processing_succeeded");
        assert_eq!(record.display_event, "Cleaned");
        assert_eq!(record.source_path_masked, r"C:\photos\a.jpg");
        assert_eq!(record.message, "cleaned");
        assert_eq!(record.tone, EventTone::Neutral);
        assert_eq!(record.time_text.len(), 8);
        assert_eq!(
            record.timestamp_utc,
            DateTime::parse_from_rfc3339("2026-10-27T01:02:03.0000000Z").expect("timestamp")
        );
    }

    #[test]
    fn malformed_and_non_object_lines_are_dropped() {
        // 防：坏行被当成空记录塞进列表，或非对象 JSON 让整页解析 panic（#57）。
        assert!(parse_line("").is_none());
        assert!(parse_line("   ").is_none());
        assert!(parse_line("not json").is_none());
        assert!(parse_line("[1,2,3]").is_none());
        assert!(parse_line("null").is_none());
    }

    #[test]
    fn unknown_fields_and_events_are_tolerated() {
        // 防：Worker 新增字段或未知事件类型时整行被丢弃；未知事件应保留原始 event_type（#57）。
        let record = parse_line(r#"{"event_type":"future_event","future":42}"#).expect("record");
        assert_eq!(record.event_type, "future_event");
        assert_eq!(record.display_event, "future_event");
        assert_eq!(record.level, AuditLevel::Info);
        assert!(record.source_path_masked.is_empty());
        assert!(record.message.is_empty());
        assert_eq!(record.tone, EventTone::Neutral);
    }

    #[test]
    fn level_filter_thresholds_match_old_ui() {
        // 防：ALL/DEBUG 被拆成不同阈值，或 warn/error 过滤反向，导致用户切档看不到应有事件（#57）。
        assert!(LevelFilter::All.allows(AuditLevel::Debug));
        assert!(LevelFilter::Debug.allows(AuditLevel::Debug));
        assert!(!LevelFilter::Info.allows(AuditLevel::Debug));
        assert!(LevelFilter::Info.allows(AuditLevel::Info));
        assert!(!LevelFilter::Warn.allows(AuditLevel::Info));
        assert!(LevelFilter::Warn.allows(AuditLevel::Warn));
        assert!(!LevelFilter::Error.allows(AuditLevel::Warn));
        assert!(LevelFilter::Error.allows(AuditLevel::Error));
    }

    #[test]
    fn event_tones_cover_known_events() {
        // 防：事件语义色回退成同一种颜色，状态（成功/失败/冲突）在列表里不可区分（#57）。
        assert_eq!(event_tone("exiftool_started"), EventTone::Success);
        assert_eq!(event_tone("service_started"), EventTone::Success);
        assert_eq!(event_tone("file_processing_succeeded"), EventTone::Neutral);
        assert_eq!(event_tone("file_skipped"), EventTone::Muted);
        assert_eq!(event_tone("file_detected"), EventTone::Info);
        assert_eq!(event_tone("instance_conflict"), EventTone::Warning);
        assert_eq!(event_tone("file_processing_failed"), EventTone::Danger);
        assert_eq!(event_tone("unknown"), EventTone::Neutral);
    }
}
