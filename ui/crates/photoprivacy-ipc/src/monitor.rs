use crate::client::{IpcClient, IpcError};
use crate::endpoints::{Endpoint, WorkerMode};
use crate::protocol::Status;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::watch;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ConnectionState {
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
}

impl ConnectionState {
    pub fn label(self) -> &'static str {
        match self {
            ConnectionState::Connecting => "Connecting",
            ConnectionState::Connected => "Connected",
            ConnectionState::Reconnecting => "Reconnecting",
            ConnectionState::Disconnected => "Disconnected",
        }
    }
}

#[derive(Clone, Debug)]
pub struct ConnectionSnapshot {
    pub state: ConnectionState,
    pub endpoint: Option<String>,
    pub worker_mode: Option<WorkerMode>,
    pub protocol_version: Option<i32>,
    pub exif_tool_version: Option<String>,
    pub status_mode: Option<String>,
    pub round_trips: u64,
    pub last_round_trip: Option<Duration>,
    pub last_error: Option<String>,
}

impl ConnectionSnapshot {
    pub fn connecting() -> Self {
        Self {
            state: ConnectionState::Connecting,
            endpoint: None,
            worker_mode: None,
            protocol_version: None,
            exif_tool_version: None,
            status_mode: None,
            round_trips: 0,
            last_round_trip: None,
            last_error: None,
        }
    }
}

const BACKOFF_STEPS: [u64; 7] = [1, 2, 4, 8, 16, 32, 60];

// 心跳退避与旧 UI 的 ConnectionStateService 逐步一致（1s→60s）；状态机 Connecting → Connected /
// Reconnecting，Disconnected 只用于端点无法解析（任务永不启动）。
// Design(#49): 退避序列与旧 UI 对齐并封顶 60s。
pub fn next_backoff(current: Duration) -> Duration {
    let seconds = current.as_secs();
    let next = BACKOFF_STEPS
        .iter()
        .copied()
        .find(|step| *step > seconds)
        .unwrap_or(60);
    Duration::from_secs(next)
}

/// 心跳循环：成功每 1s 一次，失败按退避步进；状态经 `watch` 广播给 UI。
///
/// 探针用 GetStatus 而非旧 UI 的 Ping：每跳顺带刷新协议版本、模式与 ExifTool 版本；代价是
/// 「Ping 通但 GetStatus 失败」会被判为不可用（旧 UI 不用这些字段，所以只发 Ping）。
// Design(#49): 心跳探针固定为 GetStatus，版本可见性优先于 Ping 的纯活性语义。
pub fn spawn_monitor(
    client: Arc<IpcClient>,
    endpoint: Endpoint,
    tx: watch::Sender<ConnectionSnapshot>,
) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        let mut backoff = Duration::from_secs(1);
        loop {
            let started = Instant::now();
            let result = client.status(&endpoint).await;
            let elapsed = started.elapsed();

            match result {
                Ok((status, protocol_version)) => {
                    backoff = Duration::from_secs(1);
                    tx.send_modify(|snapshot| {
                        apply_success(snapshot, &status, protocol_version, elapsed)
                    });
                    tokio::time::sleep(Duration::from_secs(1)).await;
                }
                Err(err) => {
                    backoff = next_backoff(backoff);
                    tx.send_modify(|snapshot| apply_failure(snapshot, &err));
                    tokio::time::sleep(backoff).await;
                }
            }
        }
    })
}

fn apply_success(
    snapshot: &mut ConnectionSnapshot,
    status: &Status,
    protocol_version: Option<i32>,
    elapsed: Duration,
) {
    snapshot.state = ConnectionState::Connected;
    snapshot.protocol_version = protocol_version;
    snapshot.exif_tool_version = Some(status.exif_tool_version.clone());
    snapshot.status_mode = Some(status.mode.clone());
    snapshot.round_trips += 1;
    snapshot.last_round_trip = Some(elapsed);
    snapshot.last_error = None;
}

// 失败时清掉连接期字段：它们只代表上一次成功往返，留在界面上会被读成 Worker 的当前状态。
// `last_round_trip` 保留——界面文案本身就是「上次往返」的历史语义。
fn apply_failure(snapshot: &mut ConnectionSnapshot, err: &IpcError) {
    snapshot.state = ConnectionState::Reconnecting;
    snapshot.protocol_version = None;
    snapshot.exif_tool_version = None;
    snapshot.status_mode = None;
    snapshot.last_error = Some(err.to_string());
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn backoff_steps_match_connection_state_service() {
        // 防：退避步进漂移（旧 UI 1→2→4→8→16→32→60，封顶 60），心跳过密或状态久不刷新（#54）。
        let mut delay = Duration::from_secs(1);
        let mut seen = Vec::new();
        for _ in 0..8 {
            delay = next_backoff(delay);
            seen.push(delay.as_secs());
        }
        assert_eq!(seen, vec![2, 4, 8, 16, 32, 60, 60, 60]);
    }

    #[test]
    fn snapshot_starts_connecting_without_endpoint() {
        // 防：首帧就报 Connected/Disconnected，界面在协议往返前谎报连接状态（#54）。
        let snapshot = ConnectionSnapshot::connecting();
        assert_eq!(snapshot.state, ConnectionState::Connecting);
        assert!(snapshot.endpoint.is_none());
        assert_eq!(snapshot.round_trips, 0);
    }

    #[test]
    fn failure_clears_connection_scoped_details() {
        // 防：Worker 掉线后界面仍显示上一次的协议版本/ExifTool/模式，把陈旧值当成当前状态（#54）。
        let mut snapshot = ConnectionSnapshot::connecting();
        apply_success(
            &mut snapshot,
            &Status {
                exif_tool_version: "13.20".into(),
                mode: "background".into(),
                ..Status::default()
            },
            Some(1),
            Duration::from_millis(3),
        );
        assert_eq!(snapshot.state, ConnectionState::Connected);

        apply_failure(&mut snapshot, &IpcError::Timeout);
        assert_eq!(snapshot.state, ConnectionState::Reconnecting);
        assert!(snapshot.protocol_version.is_none());
        assert!(snapshot.exif_tool_version.is_none());
        assert!(snapshot.status_mode.is_none());
        assert_eq!(snapshot.last_round_trip, Some(Duration::from_millis(3)));
        assert!(snapshot.last_error.is_some());
    }
}
