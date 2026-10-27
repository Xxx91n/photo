//! 审计日志（Audit Log）双通道：本地文件 tail + IPC `GetRecentLogs` backfill。
//!
//! 旧 Avalonia UI 的 `AuditTailService` 是行为事实源：启动时把本地文件水位推到末尾（不回放历史），
//! 之后只交付新增行；FSW 盲区由 Worker 的最近 50 行 backfill 补齐。两条通道按原始 JSONL 行去重，
//! 去重记忆有上限，清空视图不删除记忆（防止已清空历史经 backfill 回流）。

mod buffer;
mod config;
mod feed;
mod model;

pub use buffer::{LogBuffer, MAX_LOG_ENTRIES};
pub use config::{AuditConfig, resolve_audit_config};
pub use feed::{AuditFeed, DELIVERED_MEMORY_CAP, DeliveredLines, merge_backfill_lines};
pub use model::{
    AuditLevel, AuditRecord, EventTone, LevelFilter, display_event, event_tone, parse_line,
};
