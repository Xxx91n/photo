use crate::worker_connection::WorkerLink;
use photoprivacy_logs::{AuditConfig, AuditFeed, AuditRecord, parse_line};
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
}

/// 启动日志尾部与 backfill 两条后台通道。
///
/// 协调者独占 `AuditFeed`，因此 tail 与 IPC 的交付天然串行：无论谁先到，原始行只会进 UI 一次。
pub fn start(
    runtime: &tokio::runtime::Handle,
    config: AuditConfig,
    link: WorkerLink,
    batches: mpsc::UnboundedSender<LogBatch>,
) -> LogService {
    let (commands, command_rx) = mpsc::unbounded_channel();
    let service = LogService {
        commands: commands.clone(),
    };

    runtime.spawn(run_coordinator(
        config.log_directory,
        command_rx,
        batches.clone(),
    ));
    runtime.spawn(run_backfill(link, commands));
    service
}

async fn run_coordinator(
    directory: PathBuf,
    mut commands: mpsc::UnboundedReceiver<LogCommand>,
    batches: mpsc::UnboundedSender<LogBatch>,
) {
    let mut feed = AuditFeed::new(directory);
    feed.start();

    // Design(#57): 本地 tail 只走 1s 轮询（对齐旧 UI LoopAsync 的兜底节拍），不引入平台 FSW 依赖；
    // FSW 盲区由 IPC backfill 补齐。
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
