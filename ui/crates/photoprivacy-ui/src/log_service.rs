use crate::worker_connection::WorkerLink;
use chrono::Utc;
use photoprivacy_logs::{AuditConfig, AuditFeed, AuditRecord, ClearWatermark, parse_line};
use std::path::PathBuf;
use std::time::Duration;
use tokio::sync::mpsc;

const TAIL_POLL_INTERVAL: Duration = Duration::from_secs(1);
const BACKFILL_RETRY_INTERVAL: Duration = Duration::from_secs(5);

/// 日志批次的代际。清空视图时 UI 先自增代际，早于清空发出的批次在 UI 侧丢弃。
pub struct LogBatch {
    pub generation: u64,
    pub records: Vec<AuditRecord>,
}

enum LogCommand {
    Clear { generation: u64 },
    Backfill(Vec<String>),
}

/// 日志页对后台服务的控制面（目前只有清空）。
#[derive(Clone)]
pub struct LogService {
    commands: mpsc::UnboundedSender<LogCommand>,
}

impl LogService {
    pub fn clear(&self, generation: u64) {
        let _ = self.commands.send(LogCommand::Clear { generation });
    }

    /// 测试用：不接后台协调者的空控制面（发送静默失败即可，UI 单测不验证日志落盘）。
    #[cfg(test)]
    pub(crate) fn detached() -> Self {
        let (commands, _command_rx) = mpsc::unbounded_channel();
        Self { commands }
    }
}

/// 启动日志尾部与 backfill 两条后台通道。
///
/// 协调者独占 `AuditFeed`，因此 tail 与 IPC 的交付天然串行：无论谁先到，原始行只会进 UI 一次。
/// `watermark` 是持久化清空水位（config.json 同目录）：启动时喂给 backfill 过滤，
/// 用户清空时更新落盘。
pub fn start(
    runtime: &tokio::runtime::Handle,
    config: AuditConfig,
    link: WorkerLink,
    batches: mpsc::UnboundedSender<LogBatch>,
    watermark: ClearWatermark,
) -> LogService {
    let (commands, command_rx) = mpsc::unbounded_channel();
    let service = LogService {
        commands: commands.clone(),
    };

    runtime.spawn(run_coordinator(
        config.log_directory,
        watermark,
        command_rx,
        batches.clone(),
    ));
    runtime.spawn(run_backfill(link, commands));
    service
}

async fn run_coordinator(
    directory: PathBuf,
    watermark: ClearWatermark,
    mut commands: mpsc::UnboundedReceiver<LogCommand>,
    batches: mpsc::UnboundedSender<LogBatch>,
) {
    let mut feed = AuditFeed::new(directory);
    feed.start();
    // Design(#137): 重启后的 backfill 由落盘清空水位过滤——用户清空后重启，历史行不回灌；
    // 水位缺失或损坏按无水位处理（fail-open），首启语义不变。
    feed.set_backfill_watermark(watermark.load());

    // Design(#49): 本地 tail 只走 1s 轮询（对齐旧 UI LoopAsync 的兜底节拍），不引入平台 FSW 依赖；
    // FSW 盲区由 IPC backfill 补齐（#57）。
    let mut ticker = tokio::time::interval(TAIL_POLL_INTERVAL);
    ticker.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
    let mut generation = 0;

    loop {
        tokio::select! {
            _ = ticker.tick() => publish(feed.poll_tail(), generation, &batches),
            command = commands.recv() => {
                match command {
                    Some(LogCommand::Clear { generation: next }) => {
                        generation = next;
                        // Design(#137): 清空 = 写入新水位（绝对 UTC 时刻，落盘 + 内存过滤）
                        // + tail 推到文件末尾（既有 clear_view 语义）；落盘失败按 fail-open
                        // 继续——本会话过滤仍生效，最坏重启回灌一次，与损坏水位同一兜底。
                        let cleared_at = Utc::now();
                        let _ = watermark.save(cleared_at);
                        feed.set_backfill_watermark(Some(cleared_at));
                        feed.clear_view();
                    }
                    Some(LogCommand::Backfill(lines)) => {
                        publish(feed.accept_backfill(&lines), generation, &batches);
                    }
                    None => break,
                }
            }
        }
    }
}

/// 首次 backfill 失败按旧 UI 的 5s 间隔静默重试；成功一次后停止（尾部由本地 tail 接管）。
async fn run_backfill(link: WorkerLink, commands: mpsc::UnboundedSender<LogCommand>) {
    let WorkerLink {
        client,
        mut endpoint,
    } = link;

    let endpoint = loop {
        if let Some(endpoint) = (*endpoint.borrow_and_update()).clone() {
            break endpoint;
        }
        if endpoint.changed().await.is_err() {
            return;
        }
    };

    loop {
        match client.recent_logs(&endpoint).await {
            // 旧 UI 的实际行为：空数组也继续重试，直到拿到历史行。这样即使本地 tail 读不到文件，
            // Worker 侧后写入的审计仍能经 backfill 补上。
            Ok(lines) if lines.is_empty() => tokio::time::sleep(BACKFILL_RETRY_INTERVAL).await,
            Ok(lines) => {
                let _ = commands.send(LogCommand::Backfill(lines));
                return;
            }
            Err(_) => tokio::time::sleep(BACKFILL_RETRY_INTERVAL).await,
        }
    }
}

fn publish(lines: Vec<String>, generation: u64, batches: &mpsc::UnboundedSender<LogBatch>) {
    let records = lines
        .iter()
        .filter_map(|line| parse_line(line))
        .collect::<Vec<_>>();
    if !records.is_empty() {
        let _ = batches.send(LogBatch {
            generation,
            records,
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{DateTime, Duration};
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TempDir(PathBuf);

    impl TempDir {
        fn new(name: &str) -> Self {
            let nanos = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos();
            let dir = std::env::temp_dir().join(format!(
                "photoprivacy-log-service-{name}-{}-{nanos}",
                std::process::id()
            ));
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn utc(text: &str) -> DateTime<Utc> {
        DateTime::parse_from_rfc3339(text)
            .expect("rfc3339 timestamp")
            .with_timezone(&Utc)
    }

    fn line(event: &str, timestamp: &str) -> String {
        format!(r#"{{"event_type":"{event}","timestamp_utc":"{timestamp}"}}"#)
    }

    fn drain(batches: &mut mpsc::UnboundedReceiver<LogBatch>) -> Vec<LogBatch> {
        let mut drained = Vec::new();
        while let Ok(batch) = batches.try_recv() {
            drained.push(batch);
        }
        drained
    }

    #[tokio::test]
    async fn clear_persists_watermark_and_blocks_preclear_backfill() {
        // 防：清空只清了内存视图、水位没落盘或没接进 backfill 过滤，重启/补拉后历史回灌（#137）。
        let temp = TempDir::new("clear");
        let (commands, command_rx) = mpsc::unbounded_channel();
        let (batches_tx, mut batches_rx) = mpsc::unbounded_channel();
        let coordinator = tokio::spawn(run_coordinator(
            temp.0.join("audit"),
            ClearWatermark::from_directory(&temp.0),
            command_rx,
            batches_tx,
        ));

        commands.send(LogCommand::Clear { generation: 1 }).unwrap();
        // 清空时刻由协调者取当前时钟：清空前的行必须被挡下，清空后的行正常交付。
        let before = line("pre_clear", &(Utc::now() - Duration::hours(1)).to_rfc3339());
        let after = line(
            "post_clear",
            &(Utc::now() + Duration::hours(1)).to_rfc3339(),
        );
        commands
            .send(LogCommand::Backfill(vec![before, after]))
            .unwrap();
        drop(commands);
        coordinator.await.unwrap();

        let batches = drain(&mut batches_rx);
        assert_eq!(batches.len(), 1);
        assert_eq!(batches[0].generation, 1);
        assert_eq!(batches[0].records.len(), 1);
        assert_eq!(batches[0].records[0].event_type, "post_clear");

        // 水位已落盘：重启后的会话能读回。
        let reloaded = ClearWatermark::from_directory(&temp.0);
        assert!(reloaded.load().is_some());
    }

    #[tokio::test]
    async fn startup_watermark_blocks_history_backfill_after_restart() {
        // 防：上次会话清空落盘的水位在重启后没接进 backfill 过滤，Logs 页回灌历史（#102 AC2）。
        let temp = TempDir::new("restart");
        ClearWatermark::from_directory(&temp.0)
            .save(utc("2026-11-01T02:03:04Z"))
            .unwrap();

        let (commands, command_rx) = mpsc::unbounded_channel();
        let (batches_tx, mut batches_rx) = mpsc::unbounded_channel();
        let coordinator = tokio::spawn(run_coordinator(
            temp.0.join("audit"),
            ClearWatermark::from_directory(&temp.0),
            command_rx,
            batches_tx,
        ));

        // 本会话从未清空：过滤完全来自启动时读回的落盘水位。
        let history = line("history", "2026-11-01T02:03:03Z");
        let fresh = line("fresh", "2026-11-01T02:03:05Z");
        commands
            .send(LogCommand::Backfill(vec![history, fresh]))
            .unwrap();
        drop(commands);
        coordinator.await.unwrap();

        let batches = drain(&mut batches_rx);
        assert_eq!(batches.len(), 1);
        assert_eq!(batches[0].generation, 0);
        assert_eq!(batches[0].records.len(), 1);
        assert_eq!(batches[0].records[0].event_type, "fresh");
    }
}
