use crate::model::{AuditRecord, LevelFilter};
use std::collections::VecDeque;

/// 与旧 UI `MainWindowViewModel` 的日志列表上限一致：超出后从最旧端丢弃。
pub const MAX_LOG_ENTRIES: usize = 500;

/// 日志页的展示缓冲：新条目在最前，容量有上限。
#[derive(Debug, Default)]
pub struct LogBuffer {
    entries: VecDeque<AuditRecord>,
}

impl LogBuffer {
    pub fn new() -> Self {
        Self::default()
    }

    /// 批量追加。批次须按时间升序（尾读与 backfill 合并都保证这一点），
    /// 逐条插入队首后最新条目落在索引 0，与旧 UI 的新鲜度顺序一致。
    pub fn push_batch(&mut self, batch: impl IntoIterator<Item = AuditRecord>) {
        for entry in batch {
            self.entries.push_front(entry);
        }
        while self.entries.len() > MAX_LOG_ENTRIES {
            self.entries.pop_back();
        }
    }

    pub fn clear(&mut self) {
        self.entries.clear();
    }

    pub fn len(&self) -> usize {
        self.entries.len()
    }

    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    pub fn get(&self, index: usize) -> Option<&AuditRecord> {
        self.entries.get(index)
    }

    pub fn iter(&self) -> impl Iterator<Item = &AuditRecord> {
        self.entries.iter()
    }

    /// 当前级别过滤下可见条目在缓冲中的索引。渲染层按此索引走虚拟列表。
    pub fn visible_indices(&self, filter: LevelFilter) -> Vec<usize> {
        self.entries
            .iter()
            .enumerate()
            .filter(|(_, entry)| filter.allows(entry.level))
            .map(|(index, _)| index)
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::EventTone;
    use crate::model::AuditLevel;

    fn record(level: AuditLevel, message: &str) -> AuditRecord {
        AuditRecord {
            time_text: "00:00:00".to_string(),
            level,
            event_type: "file_detected".to_string(),
            display_event: "File detected".to_string(),
            source_path_masked: String::new(),
            message: message.to_string(),
            tone: EventTone::Info,
        }
    }

    #[test]
    fn newest_batch_entry_lands_at_the_front() {
        // 防：升序批次被原序塞进列表，日志页最新事件排到最旧位置（#57）。
        let mut buffer = LogBuffer::new();
        buffer.push_batch(vec![
            record(AuditLevel::Info, "old"),
            record(AuditLevel::Info, "new"),
        ]);
        assert_eq!(buffer.get(0).unwrap().message, "new");
        assert_eq!(buffer.get(1).unwrap().message, "old");
    }

    #[test]
    fn cap_keeps_only_the_newest_entries() {
        // 防：长时间运行后日志列表无上限增长，UI 内存与滚动开销持续上升（#57）。
        let mut buffer = LogBuffer::new();
        let batch = (0..MAX_LOG_ENTRIES + 25)
            .map(|index| record(AuditLevel::Info, &index.to_string()))
            .collect::<Vec<_>>();
        buffer.push_batch(batch);
        assert_eq!(buffer.len(), MAX_LOG_ENTRIES);
        assert_eq!(buffer.get(0).unwrap().message, "524");
        assert_eq!(buffer.get(MAX_LOG_ENTRIES - 1).unwrap().message, "25");
    }

    #[test]
    fn visible_indices_follow_the_level_filter() {
        // 防：级别过滤只影响新条目或索引错位，切档后列表内容与徽章不一致（#57）。
        let mut buffer = LogBuffer::new();
        buffer.push_batch(vec![
            record(AuditLevel::Debug, "debug"),
            record(AuditLevel::Info, "info"),
            record(AuditLevel::Warn, "warn"),
            record(AuditLevel::Error, "error"),
        ]);
        // 批次升序插入队首后，索引 0 是 error，3 是 debug。
        assert_eq!(buffer.visible_indices(LevelFilter::All), vec![0, 1, 2, 3]);
        assert_eq!(buffer.visible_indices(LevelFilter::Debug), vec![0, 1, 2, 3]);
        assert_eq!(buffer.visible_indices(LevelFilter::Info), vec![0, 1, 2]);
        assert_eq!(buffer.visible_indices(LevelFilter::Warn), vec![0, 1]);
        assert_eq!(buffer.visible_indices(LevelFilter::Error), vec![0]);
        assert!(buffer.visible_indices(LevelFilter::All) != vec![3, 2, 1, 0]);
    }

    #[test]
    fn clear_drops_every_entry() {
        // 防：清空只清可见索引、缓冲保留旧条目，切换过滤后清掉的历史重新出现（#57）。
        let mut buffer = LogBuffer::new();
        buffer.push_batch(vec![record(AuditLevel::Info, "kept?")]);
        buffer.clear();
        assert!(buffer.is_empty());
        assert!(buffer.visible_indices(LevelFilter::All).is_empty());
    }
}
