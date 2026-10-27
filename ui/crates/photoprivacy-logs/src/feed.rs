use chrono::{DateTime, FixedOffset, Local, NaiveDate};
use serde_json::Value;
use std::cmp::Ordering;
use std::collections::{HashSet, VecDeque};
use std::fs::{self, File};
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};

/// 原始行去重记忆上限：覆盖 backfill 50 行 + FSW 窗口增量，防止长会话内存无界增长。
pub const DELIVERED_MEMORY_CAP: usize = 512;

/// 已交付原始行的 LRU 记忆。tail 与 backfill 共用，保证同一行只出现一次。
pub struct DeliveredLines {
    seen: HashSet<String>,
    order: VecDeque<String>,
    cap: usize,
}

impl DeliveredLines {
    pub fn new() -> Self {
        Self::with_cap(DELIVERED_MEMORY_CAP)
    }

    fn with_cap(cap: usize) -> Self {
        Self {
            seen: HashSet::with_capacity(cap),
            order: VecDeque::with_capacity(cap),
            cap,
        }
    }

    /// 返回 true 表示这行是首次交付；false 表示已由另一通道送过。
    pub fn claim(&mut self, line: &str) -> bool {
        if !self.seen.insert(line.to_string()) {
            return false;
        }
        self.order.push_back(line.to_string());
        while self.order.len() > self.cap {
            if let Some(oldest) = self.order.pop_front() {
                self.seen.remove(&oldest);
            }
        }
        true
    }
}

impl Default for DeliveredLines {
    fn default() -> Self {
        Self::new()
    }
}

/// 审计尾部双通道协调者：本地 tail 负责新增行，IPC backfill 补齐 FSW 盲区。
///
/// 启动时把水位推到当前文件末尾（不回放历史），历史只由 backfill 提供；之后任何一通道交付过的
/// 原始行都会被另一通道跳过。清空视图只把水位推到末尾并保留去重记忆。
pub struct AuditFeed {
    directory: PathBuf,
    current_path: PathBuf,
    position: u64,
    delivered: DeliveredLines,
    stall_length: u64,
    stall_partial: Vec<u8>,
    suppress_next_backfill: bool,
}

impl AuditFeed {
    pub fn new(directory: impl Into<PathBuf>) -> Self {
        let directory = directory.into();
        let current_path = audit_path(&directory, Local::now().date_naive());
        Self {
            directory,
            current_path,
            position: 0,
            delivered: DeliveredLines::new(),
            stall_length: 0,
            stall_partial: Vec::new(),
            suppress_next_backfill: false,
        }
    }

    /// 建立启动水位并确保日志目录存在；必须在任何读取/backfill 之前调用。
    pub fn start(&mut self) {
        let _ = fs::create_dir_all(&self.directory);
        self.current_path = audit_path(&self.directory, Local::now().date_naive());
        self.position = file_length(&self.current_path);
        self.reset_stall();
    }

    /// 读取水位之后新增的完整行；未换行的尾段留到下一次轮询。
    pub fn poll_tail(&mut self) -> Vec<String> {
        self.poll_tail_on(Local::now().date_naive())
    }

    /// 测试与跨日逻辑使用：以指定日期定位审计文件。
    pub fn poll_tail_on(&mut self, day: NaiveDate) -> Vec<String> {
        self.rotate_if_needed(day);

        let path = self.current_path.clone();
        let mut file = match File::open(&path) {
            Ok(file) => file,
            Err(_) => return Vec::new(),
        };
        let length = file.metadata().map(|meta| meta.len()).unwrap_or(0);

        if length < self.position {
            // 文件被截断/替换：从头上重新读，去重记忆仍会挡住已交付内容。
            self.position = 0;
            self.reset_stall();
        }
        if length == self.position {
            self.reset_stall();
            return Vec::new();
        }
        if file.seek(SeekFrom::Start(self.position)).is_err() {
            return Vec::new();
        }

        let mut chunk = Vec::new();
        if file.read_to_end(&mut chunk).is_err() || chunk.is_empty() {
            return Vec::new();
        }

        let actual_length = self.position + chunk.len() as u64;
        self.process_chunk(&chunk, actual_length)
    }

    /// 接受 Worker 的最近 N 行：稳定按时间升序、去重，再与 tail 已交付行合并。
    ///
    /// 清空视图之后到达的第一次 backfill 只登记去重记忆、不交付——否则用户刚清空的历史会立刻回流。
    /// 清空后新追加的行仍由 tail 从新水位读取，不会丢。
    pub fn accept_backfill(&mut self, lines: &[String]) -> Vec<String> {
        let mut accepted = Vec::new();
        for line in merge_backfill_lines(lines) {
            if self.delivered.claim(&line) {
                accepted.push(line);
            }
        }

        if self.suppress_next_backfill {
            self.suppress_next_backfill = false;
            return Vec::new();
        }
        accepted
    }

    /// 用户清空日志视图：丢掉水位之后的未读内容，但保留去重记忆。
    pub fn clear_view(&mut self) {
        self.position = file_length(&self.current_path);
        self.reset_stall();
        self.suppress_next_backfill = true;
    }

    pub fn current_path(&self) -> &Path {
        &self.current_path
    }

    fn process_chunk(&mut self, chunk: &[u8], actual_length: u64) -> Vec<String> {
        let mut lines = Vec::new();
        let complete_end = chunk
            .iter()
            .rposition(|byte| *byte == b'\n')
            .map_or(0, |index| index + 1);

        if complete_end > 0 {
            let text = String::from_utf8_lossy(&chunk[..complete_end]);
            for raw in text.split('\n') {
                let raw = raw.strip_suffix('\r').unwrap_or(raw);
                if raw.is_empty() {
                    continue;
                }
                if self.delivered.claim(raw) {
                    lines.push(raw.to_string());
                }
            }
            self.position += complete_end as u64;
        }

        let partial = &chunk[complete_end..];
        if partial.is_empty() {
            self.reset_stall();
            return lines;
        }

        if self.stall_length == actual_length && self.stall_partial.as_slice() == partial {
            // 文件长度与未换行尾段都没变：写入方已停止追加，这段就是完整内容。
            // 截断/坏行由上层解析丢弃，但先登记去重，避免每轮重复尝试。
            let raw = String::from_utf8_lossy(partial);
            let raw = raw.as_ref().strip_suffix('\r').unwrap_or(raw.as_ref());
            if !raw.is_empty() && self.delivered.claim(raw) {
                lines.push(raw.to_string());
            }
            self.position = actual_length;
            self.reset_stall();
        } else {
            self.stall_length = actual_length;
            self.stall_partial = partial.to_vec();
        }

        lines
    }

    fn rotate_if_needed(&mut self, day: NaiveDate) {
        let expected = audit_path(&self.directory, day);
        if expected == self.current_path {
            return;
        }
        self.current_path = expected;
        self.position = 0;
        self.reset_stall();
    }

    fn reset_stall(&mut self) {
        self.stall_length = 0;
        self.stall_partial.clear();
    }
}

/// 旧 UI `AuditTailService.BuildAuditPath` 的等价实现（本地日期）。
pub fn audit_path(directory: &Path, day: NaiveDate) -> PathBuf {
    directory.join(format!("audit-{}.jsonl", day.format("%Y-%m-%d")))
}

fn file_length(path: &Path) -> u64 {
    fs::metadata(path).map(|meta| meta.len()).unwrap_or(0)
}

fn timestamp(line: &str) -> Option<DateTime<FixedOffset>> {
    let value: Value = serde_json::from_str(line).ok()?;
    let text = value.get("timestamp_utc")?.as_str()?;
    DateTime::parse_from_rfc3339(text).ok()
}

/// 合并 backfill 原始行：去空行、按原始字符串去重、按 timestamp_utc 稳定升序；
/// 无时间戳的行保持输入顺序排在最后（旧 UI `MergeBackfillLines` 语义）。
pub fn merge_backfill_lines(lines: &[String]) -> Vec<String> {
    let mut seen = HashSet::new();
    let mut unique = Vec::new();

    for (index, raw) in lines.iter().enumerate() {
        if raw.trim().is_empty() || !seen.insert(raw.clone()) {
            continue;
        }
        unique.push((raw.clone(), timestamp(raw), index));
    }

    unique.sort_by(|left, right| {
        let time_order = match (left.1, right.1) {
            (Some(left), Some(right)) => left.cmp(&right),
            (Some(_), None) => Ordering::Less,
            (None, Some(_)) => Ordering::Greater,
            (None, None) => Ordering::Equal,
        };
        time_order.then(left.2.cmp(&right.2))
    });

    unique.into_iter().map(|(line, _, _)| line).collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs::OpenOptions;
    use std::io::Write;

    fn append(path: &Path, text: &str) {
        let mut file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
            .expect("open audit file");
        file.write_all(text.as_bytes()).expect("append audit line");
    }

    fn line(event: &str, timestamp: &str) -> String {
        format!(r#"{{"event_type":"{event}","timestamp_utc":"{timestamp}"}}"#)
    }

    #[test]
    fn start_skips_history_and_reads_only_appended_lines() {
        // 防：启动时回放整个审计文件，日志页刚打开就灌入历史；或水位建错导致新行永远读不到（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        append(&path, &format!("{}\n", line("old", "2026-10-27T00:00:00Z")));

        let mut feed = AuditFeed::new(temp.path());
        feed.start();
        assert!(feed.poll_tail_on(day).is_empty());

        append(&path, &format!("{}\n", line("new", "2026-10-27T00:00:01Z")));
        let lines = feed.poll_tail_on(day);
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("\"new\""));
    }

    #[test]
    fn partial_line_is_held_until_the_writer_stalls() {
        // 防：写入方半行未换行时被当成完整行交付，截断的 JSON 丢失后半段（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        append(&path, r#"{"event_type":"part"#);
        assert!(feed.poll_tail_on(day).is_empty());

        // 长度与尾段都没变化 = 写入方已停止，交付这段完整内容。
        let stalled = feed.poll_tail_on(day);
        assert_eq!(stalled, vec![r#"{"event_type":"part"#.to_string()]);
        assert!(feed.poll_tail_on(day).is_empty());
    }

    #[test]
    fn complete_lines_are_delivered_before_a_trailing_partial() {
        // 防：文件以「完整行 + 半行」结尾时把半行也交付，或把完整行一起压住（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        append(
            &path,
            &format!(
                "{}\n{}",
                line("first", "2026-10-27T00:00:00Z"),
                r#"{"event_type":"sec"#
            ),
        );
        let lines = feed.poll_tail_on(day);
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("\"first\""));

        append(&path, "ond\"}\n");
        let lines = feed.poll_tail_on(day);
        assert_eq!(lines, vec![r#"{"event_type":"second"}"#.to_string()]);
    }

    #[test]
    fn crlf_and_blank_lines_are_normalized() {
        // 防：Windows 写入方留下 \r 让 JSON 解析失败，或空行被当成记录（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        append(&path, "\r\n\r\n");
        assert!(feed.poll_tail_on(day).is_empty());

        append(&path, "{}\r\n");
        assert_eq!(feed.poll_tail_on(day), vec!["{}".to_string()]);
    }

    #[test]
    fn backfill_merges_sorts_and_dedupes() {
        // 防：backfill 原始顺序直接入列，日志时间线倒挂；重复行显示两次（#57）。
        let early = line("early", "2026-10-27T00:00:01Z");
        let late = line("late", "2026-10-27T00:00:03Z");
        let middle = line("middle", "2026-10-27T00:00:02Z");
        let no_time = r#"{"event_type":"no_time"}"#;
        let lines = vec![
            late.clone(),
            "  ".to_string(),
            early.clone(),
            late.clone(),
            no_time.to_string(),
            middle.clone(),
        ];

        let merged = merge_backfill_lines(&lines);
        assert_eq!(merged, vec![early, middle, late, no_time.to_string()]);
    }

    #[test]
    fn backfill_skips_lines_already_delivered_by_the_tail() {
        // 防：同一事件同时经 tail 与 IPC 到达，日志页出现重复条目（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        let raw = line("same", "2026-10-27T00:00:00Z");
        append(&path, &format!("{raw}\n"));
        assert_eq!(feed.poll_tail_on(day), vec![raw.clone()]);

        assert!(feed.accept_backfill(&[raw]).is_empty());
    }

    #[test]
    fn clear_view_advances_watermark_and_keeps_dedupe_memory() {
        // 防：清空后 backfill 把已清历史重新送回；或水位没动导致清空前的行再次被 tail 读出（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        let raw = line("history", "2026-10-27T00:00:00Z");
        append(&path, &format!("{raw}\n"));
        assert_eq!(feed.poll_tail_on(day).len(), 1);

        feed.clear_view();
        assert!(feed.accept_backfill(&[raw]).is_empty());

        append(
            &path,
            &format!("{}\n", line("after", "2026-10-27T00:00:01Z")),
        );
        let lines = feed.poll_tail_on(day);
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("\"after\""));
    }

    #[test]
    fn backfill_after_clear_is_suppressed_but_still_claimed() {
        // 防：用户还没等到首次 backfill 就清空视图时，50 行历史立刻回流；同时 tail 不能重复交付它们（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        let history = line("history", "2026-10-27T00:00:00Z");
        append(&path, &format!("{history}\n"));
        feed.clear_view();

        assert!(
            feed.accept_backfill(std::slice::from_ref(&history))
                .is_empty()
        );
        // 水位在清空时已推到文件末尾，因此 tail 也不会重读历史。
        assert!(feed.poll_tail_on(day).is_empty());
    }

    #[test]
    fn day_rotation_reads_the_new_file_from_the_start() {
        // 防：跨日后仍盯着昨天的文件，新一天的审计事件全部丢失（#57）。
        let temp = tempfile::tempdir().expect("temp dir");
        let first_day = NaiveDate::from_ymd_opt(2026, 10, 26).expect("date");
        let second_day = NaiveDate::from_ymd_opt(2026, 10, 27).expect("date");
        let first = audit_path(temp.path(), first_day);
        let second = audit_path(temp.path(), second_day);
        append(
            &first,
            &format!("{}\n", line("day1", "2026-10-26T23:59:59Z")),
        );

        let mut feed = AuditFeed::new(temp.path());
        assert_eq!(
            feed.poll_tail_on(first_day),
            vec![line("day1", "2026-10-26T23:59:59Z")]
        );

        append(
            &second,
            &format!("{}\n", line("day2", "2026-10-27T00:00:01Z")),
        );
        assert_eq!(
            feed.poll_tail_on(second_day),
            vec![line("day2", "2026-10-27T00:00:01Z")]
        );
    }

    #[test]
    fn delivered_memory_is_bounded() {
        // 防：长会话下去重集合无上限增长（旧 UI 上限 512）（#57）。
        let mut delivered = DeliveredLines::with_cap(4);
        for index in 0..10 {
            assert!(delivered.claim(&index.to_string()));
        }
        assert!(!delivered.claim("9"));
        assert!(delivered.claim("0"));
    }
}
