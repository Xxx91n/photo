use crate::client::{IpcClient, IpcError};
use crate::endpoints::{Endpoint, WorkerMode};
use crate::protocol::Status;
use std::future::Future;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::watch;

/// 心跳节拍：成功时每 1s 一次 GetStatus，顺带刷新协议版本与 ExifTool 版本（版本 1s 节奏）。
pub const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(1);
/// 服务态节拍：与旧 UI `ServiceModeController.PollServiceModeTransitionAsync` 同为 3s。
pub const SERVICE_POLL_INTERVAL: Duration = Duration::from_secs(3);

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

/// 服务态：本版只区分「服务端点此刻能否连上」。
///
/// 旧 UI 读 SCM/launchd/systemd 的安装态（NotInstalled/Stopped/Running）；完整探测归 #58，
/// 本票先保证 3s 拍与消费点在位，#58 只替换 `watch_service_state` 的探针实现。
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ServiceState {
    #[default]
    Unknown,
    Reachable,
    Unreachable,
}

impl ServiceState {
    pub fn label(self) -> &'static str {
        match self {
            ServiceState::Unknown => "—",
            ServiceState::Reachable => "reachable",
            ServiceState::Unreachable => "unreachable",
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
    pub service_state: ServiceState,
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
            service_state: ServiceState::Unknown,
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
        let mut backoff = HEARTBEAT_INTERVAL;
        loop {
            let started = Instant::now();
            let result = client.status(&endpoint).await;
            let elapsed = started.elapsed();

            match result {
                Ok((status, protocol_version)) => {
                    backoff = HEARTBEAT_INTERVAL;
                    tx.send_modify(|snapshot| {
                        apply_success(snapshot, &status, protocol_version, elapsed)
                    });
                    tokio::time::sleep(HEARTBEAT_INTERVAL).await;
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

/// 服务态轮询：3s 一拍（旧 UI 的服务模式轮询同节奏），先等一拍再探，避免启动瞬间与连接编排抢管道。
///
/// 探针由调用方注入：生产用服务端点的连通性（短连接预算），#58 接入 SCM/launchd/systemd 安装态后
/// 只换探针，不改节拍与快照字段。
// Design(#58): 服务安装态探测未落地，本票用端点可达性代位；3s 节拍与消费点已固定。
pub async fn watch_service_state<F, Fut>(probe: F, tx: watch::Sender<ConnectionSnapshot>)
where
    F: Fn() -> Fut,
    Fut: Future<Output = bool>,
{
    loop {
        tokio::time::sleep(SERVICE_POLL_INTERVAL).await;
        let state = service_state(probe().await);
        // 状态没变就不触发 UI 重绘（3s 一拍不应制造稳定的 3s 重绘）。
        tx.send_if_modified(|snapshot| {
            if snapshot.service_state == state {
                false
            } else {
                snapshot.service_state = state;
                true
            }
        });
    }
}

fn service_state(reachable: bool) -> ServiceState {
    if reachable {
        ServiceState::Reachable
    } else {
        ServiceState::Unreachable
    }
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
    fn poll_rhythms_match_old_ui() {
        // 防：心跳 1s / 服务态 3s 的节奏漂移（旧 UI ConnectionStateService 1s 心跳、
        // ServiceModeController 3s 服务态轮询，#59）。
        assert_eq!(HEARTBEAT_INTERVAL, Duration::from_secs(1));
        assert_eq!(SERVICE_POLL_INTERVAL, Duration::from_secs(3));
    }

    #[test]
    fn service_state_maps_probe_result() {
        // 防：探测结果被写成 Unknown，界面分不清「还没探」与「探过不可达」（#59）。
        assert_eq!(service_state(true), ServiceState::Reachable);
        assert_eq!(service_state(false), ServiceState::Unreachable);
        assert_eq!(
            ConnectionSnapshot::connecting().service_state,
            ServiceState::Unknown
        );
    }

    #[test]
    fn heartbeat_updates_leave_service_state_alone() {
        // 防：心跳成功/失败顺手清掉服务态，3s 拍的结果被 1s 心跳覆盖（#59）。
        let mut snapshot = ConnectionSnapshot::connecting();
        snapshot.service_state = ServiceState::Reachable;

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
        assert_eq!(snapshot.service_state, ServiceState::Reachable);

        apply_failure(&mut snapshot, &IpcError::Timeout);
        assert_eq!(snapshot.service_state, ServiceState::Reachable);
    }

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
