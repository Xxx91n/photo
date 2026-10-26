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

    pub fn with_endpoint(mut self, endpoint: &Endpoint, mode: WorkerMode) -> Self {
        self.endpoint = Some(endpoint.display());
        self.worker_mode = Some(mode);
        self
    }
}

const BACKOFF_STEPS: [u64; 7] = [1, 2, 4, 8, 16, 32, 60];

// Design(#54): 心跳退避与旧 UI 的 ConnectionStateService 逐步一致（1s→60s），
// 状态机同构：Connecting → Connected / Reconnecting，取消时才进 Disconnected。
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
pub fn spawn_monitor(
    client: Arc<IpcClient>,
    endpoint: Endpoint,
    mode: WorkerMode,
    tx: watch::Sender<ConnectionSnapshot>,
) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        tx.send_modify(|snapshot| snapshot.worker_mode = Some(mode));
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
        let snapshot = ConnectionSnapshot::connecting();
        assert_eq!(snapshot.state, ConnectionState::Connecting);
        assert!(snapshot.endpoint.is_none());
        assert_eq!(snapshot.round_trips, 0);
    }

    #[test]
    fn snapshot_records_resolved_endpoint() {
        let snapshot = ConnectionSnapshot::connecting().with_endpoint(
            &Endpoint::Pipe("PhotoPrivacyCleaner.Background".into()),
            WorkerMode::Background,
        );
        assert_eq!(
            snapshot.endpoint.as_deref(),
            Some("PhotoPrivacyCleaner.Background")
        );
        assert_eq!(snapshot.worker_mode, Some(WorkerMode::Background));
    }
}
