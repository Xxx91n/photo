use crate::client::IpcClient;
use crate::endpoints::{Endpoint, WorkerMode};
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

// Design(#54): 心跳退避与旧 UI 的 ConnectionStateService 逐步一致（1s→60s）。
// 状态机：Connecting → Connected / Reconnecting；Disconnected 只用于端点无法解析（任务永不启动）。
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
// Design(#54): 心跳探针用 GetStatus 而非旧 UI 的 Ping——每跳顺带刷新协议版本、模式与 ExifTool 版本，
// 代价是「Ping 通但 GetStatus 失败」会被判为不可用；旧 UI 不用这些字段所以只发 Ping。
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
                        snapshot.state = ConnectionState::Connected;
                        snapshot.protocol_version = protocol_version;
                        snapshot.exif_tool_version = Some(status.exif_tool_version);
                        snapshot.status_mode = Some(status.mode);
                        snapshot.round_trips += 1;
                        snapshot.last_round_trip = Some(elapsed);
                        snapshot.last_error = None;
                    });
                    tokio::time::sleep(Duration::from_secs(1)).await;
                }
                Err(err) => {
                    backoff = next_backoff(backoff);
                    tx.send_modify(|snapshot| {
                        snapshot.state = ConnectionState::Reconnecting;
                        snapshot.last_error = Some(err.to_string());
                    });
                    tokio::time::sleep(backoff).await;
                }
            }
        }
    })
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
}
