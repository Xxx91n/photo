use crate::client::{IpcClient, IpcError};
use crate::endpoints::{Endpoint, WorkerMode};
use crate::protocol::Status;
use std::future::Future;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::watch;

/// 心跳节拍：成功时每 1s 一次 GetStatus，刷新协议版本与模式；ExifTool 版本由独立的
/// `VERSION_POLL_INTERVAL` 轮询维护，心跳失败退避不影响版本节拍。
pub const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(1);
/// 版本节拍：每 1s 一次 GetExifToolVersion，与心跳退避解耦（旧 UI 的独立版本轮询）。
pub const VERSION_POLL_INTERVAL: Duration = Duration::from_secs(1);
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

/// 服务端点可达性：本版只区分「服务端点此刻能否连上」。
///
/// 与 GLOSSARY 的 `Service State Probe`（三平台安装态枚举：NotInstalled/Stopped/Running）不是同一概念：
/// 旧 UI 读 SCM/launchd/systemd 的安装态，完整探测归 #58；本票先保证 3s 拍与消费点在位，
/// #58 接入安装态后再决定是本类型扩展还是另立类型。
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ServiceReachability {
    #[default]
    Unknown,
    Reachable,
    Unreachable,
}

#[derive(Clone, Debug)]
pub struct ConnectionSnapshot {
    pub state: ConnectionState,
    pub endpoint: Option<String>,
    pub worker_mode: Option<WorkerMode>,
    pub protocol_version: Option<i32>,
    pub exif_tool_version: Option<String>,
    pub watch_directory: Option<String>,
    pub status_mode: Option<String>,
    pub service_reachability: ServiceReachability,
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
            watch_directory: None,
            status_mode: None,
            service_reachability: ServiceReachability::Unknown,
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
pub async fn watch_service_reachability<F, Fut>(probe: F, tx: watch::Sender<ConnectionSnapshot>)
where
    F: Fn() -> Fut,
    Fut: Future<Output = bool>,
{
    loop {
        tokio::time::sleep(SERVICE_POLL_INTERVAL).await;
        let state = service_reachability(probe().await);
        // 状态没变就不触发 UI 重绘（3s 一拍不应制造稳定的 3s 重绘）。
        tx.send_if_modified(|snapshot| {
            if snapshot.service_reachability == state {
                false
            } else {
                snapshot.service_reachability = state;
                true
            }
        });
    }
}

/// 版本轮询：独立 1s 一拍（旧 UI `WindowPollingHostedService` 的版本轮询），先等一拍再探。
///
/// 与心跳退避解耦：心跳失败进入 60s 退避时，版本仍按 1s 刷新（不可读时写回 `None`）。
/// 探针由调用方注入，便于 paused-time 测试。
pub async fn watch_version<F, Fut>(probe: F, tx: watch::Sender<ConnectionSnapshot>)
where
    F: Fn() -> Fut,
    Fut: Future<Output = Option<String>>,
{
    loop {
        tokio::time::sleep(VERSION_POLL_INTERVAL).await;
        let version = probe().await;
        tx.send_if_modified(|snapshot| {
            if snapshot.exif_tool_version == version {
                false
            } else {
                snapshot.exif_tool_version = version;
                true
            }
        });
    }
}

/// 可达性探测结果 → 服务可达性枚举。
fn service_reachability(reachable: bool) -> ServiceReachability {
    if reachable {
        ServiceReachability::Reachable
    } else {
        ServiceReachability::Unreachable
    }
}

/// 心跳成功后写入连接字段；ExifTool 版本与服务可达性由各自的独立拍维护，不在这里覆盖。
fn apply_success(
    snapshot: &mut ConnectionSnapshot,
    status: &Status,
    protocol_version: Option<i32>,
    elapsed: Duration,
) {
    snapshot.state = ConnectionState::Connected;
    snapshot.protocol_version = protocol_version;
    snapshot.watch_directory = Some(status.watch_directory.clone());
    snapshot.status_mode = Some(status.mode.clone());
    snapshot.round_trips += 1;
    snapshot.last_round_trip = Some(elapsed);
    snapshot.last_error = None;
}

// 失败时清掉连接期字段：它们只代表上一次成功往返，留在界面上会被读成 Worker 的当前状态。
// `last_round_trip` 保留——界面文案本身就是「上次往返」的历史语义。
/// 心跳失败后清掉连接期字段；保留历史往返、ExifTool 版本与服务可达性。
fn apply_failure(snapshot: &mut ConnectionSnapshot, err: &IpcError) {
    snapshot.state = ConnectionState::Reconnecting;
    snapshot.protocol_version = None;
    snapshot.watch_directory = None;
    snapshot.status_mode = None;
    snapshot.last_error = Some(err.to_string());
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn poll_rhythms_match_old_ui() {
        // 防：心跳 1s / 版本 1s / 服务态 3s 的节奏漂移（旧 UI ConnectionStateService 心跳、
        // WindowPollingHostedService 版本轮询、ServiceModeController 服务态轮询，#59）。
        assert_eq!(HEARTBEAT_INTERVAL, Duration::from_secs(1));
        assert_eq!(VERSION_POLL_INTERVAL, Duration::from_secs(1));
        assert_eq!(SERVICE_POLL_INTERVAL, Duration::from_secs(3));
    }

    #[test]
    fn service_reachability_maps_probe_result() {
        // 防：探测结果被写成 Unknown，界面分不清「还没探」与「探过不可达」（#59）。
        assert_eq!(service_reachability(true), ServiceReachability::Reachable);
        assert_eq!(
            service_reachability(false),
            ServiceReachability::Unreachable
        );
        assert_eq!(
            ConnectionSnapshot::connecting().service_reachability,
            ServiceReachability::Unknown
        );
    }

    #[test]
    fn heartbeat_updates_leave_service_reachability_alone() {
        // 防：心跳成功/失败顺手清掉服务态，3s 拍的结果被 1s 心跳覆盖（#59）。
        let mut snapshot = ConnectionSnapshot::connecting();
        snapshot.service_reachability = ServiceReachability::Reachable;

        apply_success(
            &mut snapshot,
            &Status {
                exif_tool_version: "13.20".into(),
                mode: "background".into(),
                watch_directory: String::new(),
                ..Status::default()
            },
            Some(1),
            Duration::from_millis(3),
        );
        assert_eq!(
            snapshot.service_reachability,
            ServiceReachability::Reachable
        );
        assert_eq!(snapshot.watch_directory.as_deref(), Some(""));

        apply_failure(&mut snapshot, &IpcError::Timeout);
        assert_eq!(
            snapshot.service_reachability,
            ServiceReachability::Reachable
        );
        assert!(snapshot.watch_directory.is_none());
    }

    #[tokio::test(start_paused = true)]
    async fn service_watch_polls_on_three_second_beat() {
        // 防：服务态首拍立即探测或节拍漂移（旧 UI 先等 3s 再探，#59）。
        use std::sync::atomic::{AtomicUsize, Ordering};

        let calls = Arc::new(AtomicUsize::new(0));
        let calls_for_probe = calls.clone();
        let (tx, mut rx) = watch::channel(ConnectionSnapshot::connecting());
        let handle = tokio::spawn(watch_service_reachability(
            move || {
                calls_for_probe.fetch_add(1, Ordering::SeqCst);
                std::future::ready(true)
            },
            tx,
        ));

        tokio::task::yield_now().await;
        assert_eq!(calls.load(Ordering::SeqCst), 0, "首拍必须等满 3s");

        tokio::time::advance(SERVICE_POLL_INTERVAL).await;
        tokio::task::yield_now().await;
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        assert_eq!(
            rx.borrow_and_update().service_reachability,
            ServiceReachability::Reachable
        );

        tokio::time::advance(SERVICE_POLL_INTERVAL).await;
        tokio::task::yield_now().await;
        assert_eq!(calls.load(Ordering::SeqCst), 2);

        handle.abort();
    }

    #[tokio::test(start_paused = true)]
    async fn version_poll_keeps_one_second_beat() {
        // 防：版本刷新被绑在心跳上，心跳进入退避后版本停止刷新（#59 / CodeRabbit Linked Issues）。
        use std::sync::atomic::{AtomicUsize, Ordering};

        let calls = Arc::new(AtomicUsize::new(0));
        let calls_for_probe = calls.clone();
        let (tx, mut rx) = watch::channel(ConnectionSnapshot::connecting());
        let handle = tokio::spawn(watch_version(
            move || {
                calls_for_probe.fetch_add(1, Ordering::SeqCst);
                std::future::ready(Some("13.20".to_string()))
            },
            tx,
        ));

        tokio::task::yield_now().await;
        assert_eq!(calls.load(Ordering::SeqCst), 0);

        // 连续三个 1s 拍；心跳任务完全不在场，证明版本节拍与退避解耦。
        for expected in 1..=3 {
            tokio::time::advance(VERSION_POLL_INTERVAL).await;
            tokio::task::yield_now().await;
            assert_eq!(calls.load(Ordering::SeqCst), expected);
        }
        assert_eq!(
            rx.borrow_and_update().exif_tool_version.as_deref(),
            Some("13.20")
        );

        handle.abort();
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
        // 防：Worker 掉线后界面仍显示上一次的协议版本/模式，把陈旧值当成当前状态（#54）；
        // ExifTool 版本由独立 1s 轮询维护，不应被心跳失败连带清空（#59）。
        let mut snapshot = ConnectionSnapshot::connecting();
        snapshot.exif_tool_version = Some("13.20".into());
        apply_success(
            &mut snapshot,
            &Status {
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
        assert!(snapshot.status_mode.is_none());
        assert_eq!(snapshot.exif_tool_version.as_deref(), Some("13.20"));
        assert_eq!(snapshot.last_round_trip, Some(Duration::from_millis(3)));
        assert!(snapshot.last_error.is_some());
    }
}
