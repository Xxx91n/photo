use photoprivacy_logs::{
    AuditLevel, AuditRecord, EventTone, LevelFilter, LogBuffer, MAX_LOG_ENTRIES, parse_line,
};
use std::fs::File;
use std::io::{BufRead, BufReader};
use std::path::Path;

#[test]
fn test_large_volume_log_buffer_truncation() {
    let mut buffer = LogBuffer::new();

    // 灌入 600 条记录（超过 500 条上限）
    let count = 600;
    let records: Vec<AuditRecord> = (0..count)
        .map(|i| {
            let line = format!(
                r#"{{"timestamp_utc":"2026-11-03T00:00:00Z","level":"Information","event_type":"file_cleaned","message":"Item {i}","source_path":"/fake/{i}.jpg","wipe_result":"Cleaned_Modified"}}"#
            );
            parse_line(&line).expect("valid audit jsonl")
        })
        .collect();

    buffer.push_batch(records);

    // 断言容量被硬截断为 MAX_LOG_ENTRIES (500)
    assert_eq!(
        buffer.len(),
        MAX_LOG_ENTRIES,
        "LogBuffer 超过上限时必须截断为 MAX_LOG_ENTRIES 条"
    );

    // 断言过滤索引与可见项受控
    let visible = buffer.visible_indices(LevelFilter::All);
    assert_eq!(visible.len(), MAX_LOG_ENTRIES);
}

#[test]
fn test_load_audit_file_and_buffer_truncation() {
    // 若在虚拟环境生成了测试文件，可验证从文件流式读取并压入 LogBuffer
    let path = std::env::var("AUDIT_TEST_FILE").unwrap_or_default();
    if path.is_empty() || !Path::new(&path).exists() {
        return;
    }

    let file = File::open(&path).expect("open audit file");
    let reader = BufReader::new(file);

    let mut buffer = LogBuffer::new();
    let mut total_lines = 0;

    let records: Vec<AuditRecord> = reader
        .lines()
        .map_while(Result::ok)
        .filter_map(|l| {
            total_lines += 1;
            parse_line(&l)
        })
        .collect();

    buffer.push_batch(records);

    if total_lines >= MAX_LOG_ENTRIES {
        assert_eq!(buffer.len(), MAX_LOG_ENTRIES);
    } else {
        assert_eq!(buffer.len(), total_lines);
    }
}
