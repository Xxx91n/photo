use chrono::{DateTime, FixedOffset, Local, NaiveDate, Utc};
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
/// 原始行都会被另一通道跳过。清空视图把 tail 水位推到文件末尾并保留去重记忆；重启后的 backfill
/// 另由持久化清空水位过滤——调用方经 `set_backfill_watermark` 喂入，`AuditFeed` 只比较不落盘。
pub struct AuditFeed {
    directory: PathBuf,
    current_path: PathBuf,
    position: u64,
    delivered: DeliveredLines,
    /// backfill 过滤水位：行内 `timestamp_utc` 必须严格大于它才交付；`None` = 不过滤。
    backfill_watermark: Option<DateTime<Utc>>,
    /// 上次轮询时文件的总长度，用于识别未换行尾段是否仍在增长。
    stall_length: u64,
    /// 上次轮询时水位之后的未换行字节；长度与内容都不再变化时一次性交付。
    stall_partial: Vec<u8>,
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
            backfill_watermark: None,
            stall_length: 0,
            stall_partial: Vec::new(),
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

    /// 设置 backfill 过滤水位；`None` = 不过滤（首启无水位语义不变）。
    /// 值由调用方决定：启动时来自持久化清空水位，清空时来自清空动作的时刻。
    // Design(#102): backfill 只交付 timestamp_utc 严格大于清空水位的行——比较基准是
    // 行内绝对 UTC 时刻，与文件名日期、本地墙钟日期、分卷后缀全部无关。
    pub fn set_backfill_watermark(&mut self, watermark: Option<DateTime<Utc>>) {
        self.backfill_watermark = watermark;
    }

    /// 接受 Worker 的最近 N 行：稳定按时间升序、去重，再与 tail 已交付行合并。
    ///
    /// 有清空水位时只交付严格晚于水位的行：清空前的历史行（含清空时还未显示过的）
    /// 重启后不回灌、不补交；无水位时行为不变。清空视图推进 tail 水位并保留去重记忆。
    pub fn accept_backfill(&mut self, lines: &[String]) -> Vec<String> {
        let mut accepted = Vec::new();
        for line in merge_backfill_lines(lines) {
            if !self.is_after_backfill_watermark(&line) {
                continue;
            }
            if self.delivered.claim(&line) {
                accepted.push(line);
            }
        }
        accepted
    }

    /// 水位存在时，行必须带可解析的 `timestamp_utc` 且严格晚于水位；无时间戳的行
    /// 无法自证晚于清空，按挡下处理（fail-closed）。无水位时全部放行。
    fn is_after_backfill_watermark(&self, line: &str) -> bool {
        match self.backfill_watermark {
            None => true,
            Some(watermark) => {
                timestamp(line).is_some_and(|time| time.with_timezone(&Utc) > watermark)
            }
        }
    }

    /// 用户清空日志视图：丢掉水位之后的未读内容，但保留去重记忆。
    pub fn clear_view(&mut self) {
        self.position = file_length(&self.current_path);
        self.reset_stall();
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
    use crate::ClearWatermark;
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

    fn utc(text: &str) -> DateTime<Utc> {
        DateTime::parse_from_rfc3339(text)
            .expect("rfc3339 timestamp")
            .with_timezone(&Utc)
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
    fn clear_with_watermark_blocks_preclear_backfill_even_never_shown_lines() {
        // 防：清空后 backfill 把清空前的历史行（含从未显示过的）重新送回视图（#102）。
        // 顺序镜像协调者：清空 = 更新过滤水位 + clear_view。#57 的「未显示行补交」由
        // #137 显式退役——水位存在即按时间过滤，同进程内同样生效。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        let history = line("history", "2026-10-27T00:00:00Z");
        append(&path, &format!("{history}\n"));
        feed.set_backfill_watermark(Some(utc("2026-10-27T12:00:00Z")));
        feed.clear_view();

        assert!(
            feed.accept_backfill(std::slice::from_ref(&history))
                .is_empty()
        );
        // 清空后新写入的行照常经 tail 追加显示。
        append(
            &path,
            &format!("{}\n", line("after", "2026-10-27T12:00:01Z")),
        );
        let lines = feed.poll_tail_on(day);
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("\"after\""));
    }

    #[test]
    fn restart_backfill_filters_history_by_persisted_watermark() {
        // 防：清空后重启 UI，backfill 把已清历史行灌回 Logs 页，清空状态丢失（#102）。
        let temp = tempfile::tempdir().expect("temp dir");
        let mut feed = AuditFeed::new(temp.path());
        feed.start();
        // 模拟重启：水位来自上次会话落盘的清空时刻，本会话从未调用 clear_view。
        feed.set_backfill_watermark(Some(utc("2026-11-01T02:03:04Z")));

        let before = line("before", "2026-11-01T02:03:03Z");
        let at_watermark = line("boundary", "2026-11-01T02:03:04Z");
        let after = line("after", "2026-11-01T02:03:05Z");
        let no_time = r#"{"event_type":"no_time"}"#;
        let delivered =
            feed.accept_backfill(&[before, at_watermark, after.clone(), no_time.to_string()]);
        // 严格大于：与水位相等的行也挡；无时间戳的行无法自证晚于清空，一并挡下。
        assert_eq!(delivered, vec![after]);
    }

    #[test]
    fn without_watermark_backfill_keeps_full_history_delivery() {
        // 防：无水位（首启/从未清空）时误开过滤，把 backfill 补历史的整个功能打断。
        let temp = tempfile::tempdir().expect("temp dir");
        let mut feed = AuditFeed::new(temp.path());
        feed.start();

        let history = line("history", "2020-01-01T00:00:00Z");
        let no_time = r#"{"event_type":"no_time"}"#;
        assert_eq!(
            feed.accept_backfill(&[history.clone(), no_time.to_string()]),
            vec![history, no_time.to_string()]
        );
    }

    #[test]
    fn watermark_filters_by_absolute_time_across_days() {
        // 防：跨日重启后按文件名日期或本地墙钟日期过滤，错删水位后的旧行、漏显新行（#129 交互）。
        let temp = tempfile::tempdir().expect("temp dir");
        let second_day = NaiveDate::from_ymd_opt(2026, 10, 27).expect("date");
        let mut feed = AuditFeed::new(temp.path());
        feed.start();
        feed.set_backfill_watermark(Some(utc("2026-10-26T23:00:00Z")));

        let before = line("before", "2026-10-26T22:59:59Z");
        let late_day1 = line("late_day1", "2026-10-26T23:30:00Z");
        let day2 = line("day2", "2026-10-27T00:00:30Z");
        let delivered = feed.accept_backfill(&[before, late_day1.clone(), day2.clone()]);
        // 水位只看行内绝对时刻：昨日深夜（水位后）不错删，跨日新行不漏显。
        assert_eq!(delivered, vec![late_day1, day2]);

        // 跨日后 tail 换新文件照常追加；tail 由文件位置守门，不受水位影响。
        let path = audit_path(temp.path(), second_day);
        append(
            &path,
            &format!("{}\n", line("tail_day2", "2026-10-27T00:01:00Z")),
        );
        assert_eq!(feed.poll_tail_on(second_day).len(), 1);
    }

    #[test]
    fn watermark_still_applies_after_audit_file_is_archived() {
        // 防：水位隐式绑定具体文件（路径/偏移），归档改名后过滤失效（#102 B′；E5 原型形态）。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();
        feed.set_backfill_watermark(Some(utc("2026-11-01T02:03:04Z")));

        // 归档前活动文件已有两行并消费到末尾（建立真实 tail 位置），随后改名留档、
        // 同名重建继续写：重建文件更短，tail 走「截断/替换」路径从头上重读。
        append(
            &path,
            &format!("{}\n", line("archived_1", "2026-11-01T02:03:02Z")),
        );
        append(
            &path,
            &format!("{}\n", line("archived_2", "2026-11-01T02:03:03Z")),
        );
        assert_eq!(feed.poll_tail_on(day).len(), 2);
        fs::rename(&path, temp.path().join("audit-cleared-archive.jsonl")).expect("archive rename");
        append(
            &path,
            &format!("{}\n", line("post_archive", "2026-11-01T02:03:06Z")),
        );

        let old = line("old", "2026-11-01T02:03:03Z");
        let post = line("post_backfill", "2026-11-01T02:03:06Z");
        // backfill 混入归档前的旧行：水位只看行内时刻、不指向文件，旧行仍被挡住。
        assert_eq!(feed.accept_backfill(&[old, post.clone()]), vec![post]);

        // tail 在重建的活动文件上照常读出新行；归档名不进 tail，归档内容不回流。
        assert_eq!(
            feed.poll_tail_on(day),
            vec![line("post_archive", "2026-11-01T02:03:06Z")]
        );
    }

    #[test]
    fn e4_probe_clear_then_restart_keeps_history_blocked_by_persisted_watermark() {
        // 防（#102 E4 探针复现）：#29 实测的回灌形态——点击清空只清了视图，重启后旧
        // 审计日志整批回来。镜像 E4 全链：同进程清空 = 落盘水位（ClearWatermark.save）
        // + tail 推到末尾；重启 = 新 `AuditFeed` 从磁盘读回水位；Worker backfill 带回
        // 「末 50 行」窗口（已清历史 + 清空后新行），水位只放行晚于清空时刻的行。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let watermark_store = ClearWatermark::from_directory(temp.path().join("config"));

        // 第一会话：两条历史行经 tail 显示后，用户点击清空。
        let mut session = AuditFeed::new(temp.path());
        session.start();
        let first = line("first", "2026-11-01T02:03:01Z");
        let second = line("second", "2026-11-01T02:03:02Z");
        append(&path, &format!("{first}\n{second}\n"));
        assert_eq!(session.poll_tail_on(day).len(), 2);

        let cleared_at = utc("2026-11-01T02:03:04Z");
        watermark_store.save(cleared_at).expect("save watermark");
        session.set_backfill_watermark(Some(cleared_at));
        session.clear_view();

        // 清空后产生一条新行（#102 AC3：新日志照常追加落盘）。
        let fresh = line("fresh", "2026-11-01T02:03:05Z");
        append(&path, &format!("{fresh}\n"));

        // 重启（第二会话）：本会话从未清空，过滤完全来自磁盘读回的水位。
        let mut restarted = AuditFeed::new(temp.path());
        restarted.start();
        restarted.set_backfill_watermark(watermark_store.load());
        // Worker 末 50 行窗口带回两条已清历史 + 一条清空后新行：历史不回灌（AC2），
        // 新行照常显示（AC3）。
        let delivered = restarted.accept_backfill(&[first, second, fresh.clone()]);
        assert_eq!(delivered, vec![fresh]);
        // tail 从文件末尾起步不回放历史；重启后再写入的行照常经 tail 追加。
        assert!(restarted.poll_tail_on(day).is_empty());
        let after_restart = line("after_restart", "2026-11-01T02:03:06Z");
        append(&path, &format!("{after_restart}\n"));
        assert_eq!(restarted.poll_tail_on(day), vec![after_restart]);
    }

    #[test]
    fn fork_window_geometry_filters_by_inline_utc_not_file_name_date() {
        // 防（#102 × #129 分叉时窗）：本地日期领先 UTC 的时窗内，UTC「昨日」时刻的行
        // 落在本地「今日」名的文件里。水位若比文件名日期或本地墙钟日期，此时窗几何会
        // 整批错删或漏显；只能比行内 timestamp_utc 绝对时刻（#102 原型报告 §3 场景 4）。
        // 构造是模拟而非复现：文件名日期取运行机当日、行内时刻固定为「昨日」，两者
        // 组成的几何随时区变化——但被测不变量（比较只看行内时刻）在任何时区下恒成立。
        let temp = tempfile::tempdir().expect("temp dir");
        let day = Local::now().date_naive();
        let path = audit_path(temp.path(), day);
        let mut feed = AuditFeed::new(temp.path());
        feed.start();
        feed.set_backfill_watermark(Some(utc("2026-10-31T19:00:00Z")));

        // 模拟时窗几何：文件名是本地「今日」，backfill 行内 UTC 时刻全是「昨日」。
        let before = line("pre_clear", "2026-10-31T18:59:59Z");
        let after = line("post_clear", "2026-10-31T19:00:01Z");
        // 清空前的行挡下；「昨日」时刻但晚于水位的行不错删——文件名日期不参与比较。
        assert_eq!(feed.accept_backfill(&[before, after.clone()]), vec![after]);

        // tail 由文件位置守门、水位不参与：时窗内新写入的行照常读出，不漏显。
        let tail_line = line("tail_in_window", "2026-10-31T19:00:02Z");
        append(&path, &format!("{tail_line}\n"));
        assert_eq!(feed.poll_tail_on(day), vec![tail_line]);
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
