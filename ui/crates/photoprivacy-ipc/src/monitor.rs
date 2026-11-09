use crate::client::{IpcClient, IpcError, is_endpoint_missing};
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
/// 与三平台安装态枚举（NotInstalled/Stopped/Running）不是同一概念：
/// 旧 UI 读 SCM/launchd/systemd 的安装态，完整探测归 #58；本票先保证 3s 拍与消费点在位，
/// #58 接入安装态后再决定是本类型扩展还是另立类型。
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ServiceReachability {
    #[default]
    Unknown,
    Reachable,
    Unreachable,
}

/// 连接编排的最终失败分类：区分「被服务授权拒绝」与「Worker 缺失」，让 UI 对两种截然不同的
/// 处境给出各自的行动指引（找安装者加授权 vs 检查 Worker 安装），不再统一归到 os error 2。
///
/// 三态互斥，按最近一次真实失败原因归类：服务端点权限拒绝优先（它是回落链的第一环，也是
/// 未授权用户唯一正确的行动线索）；其次才是后台 Worker 启动未就绪（含单写者让位退出）
/// 与未找到可拉起的 Worker 程序。连接成功即清空（`apply_success`）；心跳失败不清——授权拒绝
/// 是连接阶段事实，心跳掉线不推翻它。
// 连接失败提示按三态分类呈现，不把授权拒绝混同为 Worker 缺失的 os error 2。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ConnectFailureCause {
    /// 服务端点存在但连接被服务管道 DACL 拒绝（未列入授权集合的账户，#1/#160）。
    ServiceAccessDenied,
    /// 服务端点不可用，UI 拉起的后台 Worker 未能就绪（spawn 失败 / 已退出 / 5s 未就绪，
    /// 含 #163 单写者准入门让位退出）；附带的字符串是自描述细节（如退出码）。
    WorkerStartupFailed(String),
    /// 端点均不可用且未找到可拉起的后台 Worker 可执行文件。
    WorkerNotInstalled,
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
    /// 连接编排的失败分类（#169）：`None` = 已连接或编排未给出结论；UI 呈现时优先于 `last_error`。
    pub connect_failure: Option<ConnectFailureCause>,
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
            connect_failure: None,
        }
    }
}

const BACKOFF_STEPS: [u64; 7] = [1, 2, 4, 8, 16, 32, 60];

// 心跳退避与旧 UI 的 ConnectionStateService 逐步一致（1s→60s）；状态机 Connecting → Connected /
// Reconnecting，Disconnected 只用于端点无法解析（任务永不启动）。
// 退避序列与旧 UI 对齐并封顶 60s。
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
/// 端点经 `resolve` 动态获取（#7）：服务管道随机化后服务重启会换名——端点不存在时把刚失败的
/// 端点交给解析器重解析，否则永远重试旧名；后台端点静态，重解析是零成本恒等。
/// 解析器返回下一次要试的端点（#7 残余⑤：通告名不存在时换静态名）。**只有端点不存在才换名**
/// ——忙/超时说明端点还在，换名只会打开预蹲面；授权拒绝更要保留原端点与真实原因（#169）。
/// 初始端点用连接编排已经连上的那个，心跳成功期不重解析（心跳零注册表读；服务模式下 1s 版本
/// 轮询与 3s 探测拍仍按拍重读，见 ipc-contract 的登记）。
/// 探针用 GetStatus 而非旧 UI 的 Ping：每跳顺带刷新协议版本、模式与 ExifTool 版本；代价是
/// 「Ping 通但 GetStatus 失败」会被判为不可用（旧 UI 不用这些字段，所以只发 Ping）。
// 心跳探针固定为 GetStatus，版本可见性优先于 Ping 的纯活性语义。
// 解析器按模式注入；失败时按刚失败的端点决定下一次尝试（#7 残余⑤）。
pub fn spawn_monitor<F>(
    client: Arc<IpcClient>,
    resolve: F,
    tx: watch::Sender<ConnectionSnapshot>,
) -> tokio::task::JoinHandle<()>
where
    F: Fn(Option<&Endpoint>) -> Endpoint + Send + 'static,
{
    tokio::spawn(async move {
        let mut backoff = HEARTBEAT_INTERVAL;
        let mut endpoint = resolve(None);
        loop {
            let started = Instant::now();
            let result = client.status(&endpoint).await;
            let elapsed = started.elapsed();

            match result {
                Ok((status, protocol_version)) => {
                    backoff = HEARTBEAT_INTERVAL;
                    tx.send_modify(|snapshot| {
                        apply_success(snapshot, &status, protocol_version, elapsed);
                        // #7 残余⑤：失败换名后连上的可能是静态名，界面要显示当前真正
                        // 连着的端点，而不是启动时那个陈旧通告名。
                        snapshot.endpoint = Some(endpoint.display());
                    });
                    tokio::time::sleep(HEARTBEAT_INTERVAL).await;
                }
                Err(err) => {
                    // #7 残余⑤：只有「端点不存在」才换名（通告名 → 静态名，或换新通告名）。
                    // 忙/超时说明端点还在，换名只会在健康服务慢一拍时打开预蹲面；授权拒绝
                    // 更要保留原端点与真实原因（#169）。
                    if is_endpoint_missing(&err) {
                        endpoint = resolve(Some(&endpoint));
                    }
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
/// 探针由调用方注入：生产用服务端点的连通性（短连接预算），安装态探测另经
/// `photoprivacy-service` 的 `ServiceProbe`（SCM / launchctl / systemd）取得；只换探针，
/// 不改节拍与快照字段。
// 服务态轮询 3s 节拍与快照字段固定、探针注入可换；安装态探测已在 photoprivacy-service 完成。
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
    // 连上即作废编排期的失败结论：Worker 已就绪，分类文案不再成立（#169）。
    snapshot.connect_failure = None;
}

// 失败时清掉连接期字段：它们只代表上一次成功往返，留在界面上会被读成 Worker 的当前状态。
// `last_round_trip` 保留——界面文案本身就是「上次往返」的历史语义。
/// 心跳失败后清掉连接期字段；保留历史往返、ExifTool 版本与服务可达性。
/// `connect_failure` 也保留：编排期的授权拒绝等结论不因心跳掉线被推翻，`last_error`
/// 才是心跳自身的错误面（#169）。
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

    /// 一个肯定不存在的端点（Windows 用管道名，Unix 用 socket 路径）：连接预算耗尽后
    /// 报 `Connect` 层的「找不到」，驱动 #7 残余⑤ 的换名分支。
    fn missing_endpoint(tag: &str) -> Endpoint {
        if cfg!(windows) {
            Endpoint::Pipe(format!("pp-t7-missing-{tag}"))
        } else {
            // 固定短目录：路径必须远低于 sun_path 上限，换机器/换 TMPDIR 也不能变成
            // InvalidInput（那会被判成非「缺失」，测试反而失败）。
            Endpoint::UnixSocket(std::path::PathBuf::from(format!("/tmp/pp-t7-{tag}.sock")))
        }
    }

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

    #[test]
    fn success_clears_connect_failure_but_heartbeat_failure_keeps_it() {
        // 防：连上后台 Worker 后界面仍显示编排期的失败分类（#169）——分类只代表「当前断开」的原因。
        let mut snapshot = ConnectionSnapshot::connecting();
        snapshot.connect_failure = Some(ConnectFailureCause::ServiceAccessDenied);

        apply_success(
            &mut snapshot,
            &Status::default(),
            Some(1),
            Duration::from_millis(3),
        );
        assert_eq!(snapshot.connect_failure, None);

        // 防：心跳掉线顺手清掉授权拒绝结论——os error 2 重新盖住「未被授权」。
        snapshot.connect_failure = Some(ConnectFailureCause::ServiceAccessDenied);
        apply_failure(&mut snapshot, &IpcError::Timeout);
        assert_eq!(
            snapshot.connect_failure,
            Some(ConnectFailureCause::ServiceAccessDenied)
        );
        assert!(snapshot.last_error.is_some());
    }

    #[test]
    fn connect_failure_variants_carry_their_payload() {
        // 防：三态判别式在快照克隆/比较中错位，UI 把启动失败细节显示成未授权文案（#169）。
        let variants = vec![
            ConnectFailureCause::ServiceAccessDenied,
            ConnectFailureCause::WorkerStartupFailed("exited with code 1".to_string()),
            ConnectFailureCause::WorkerNotInstalled,
        ];
        let cloned = variants.clone();
        assert_eq!(variants, cloned);
        assert_eq!(
            ConnectionSnapshot::connecting().connect_failure,
            None,
            "初始快照不应预置失败分类"
        );
    }

    #[tokio::test(start_paused = true)]
    async fn monitor_re_resolves_endpoint_after_failure() {
        // 防（#7）：服务管道随机化后服务重启会换名——心跳失败必须重解析端点，否则 UI 永远
        // 重试旧名（60s 退避下的表现是「服务回来了也连不上」直到 UI 重启）。
        use std::sync::atomic::{AtomicUsize, Ordering};

        let calls = Arc::new(AtomicUsize::new(0));
        let calls_for_resolve = calls.clone();
        let (tx, _rx) = watch::channel(ConnectionSnapshot::connecting());
        let client = Arc::new(IpcClient::new());
        let handle = spawn_monitor(
            client,
            move |_failed| {
                let n = calls_for_resolve.fetch_add(1, Ordering::SeqCst);
                // 不存在的端点：status() 必失败，驱动失败分支的重解析（Windows 是缺失的
                // 管道名、Unix 是不存在的 socket 路径，最终都报连接层的「找不到」）。
                missing_endpoint(&format!("resolve-{n}"))
            },
            tx,
        );

        // 跑过至少两轮「失败 → 退避」：初始解析 1 次 + 每次失败后重解析（paused 时间自动推进）。
        tokio::time::sleep(Duration::from_secs(10)).await;
        let resolved = calls.load(Ordering::SeqCst);
        assert!(
            resolved >= 3,
            "monitor must re-resolve endpoint on failure, got {resolved} resolve calls"
        );

        handle.abort();
    }

    #[tokio::test(start_paused = true)]
    async fn monitor_switches_to_fallback_after_stale_advertised_name_fails() {
        // 防（#7 残余③/⑤）：UI 启动读到陈旧通告名时，第一次失败后必须换静态名再试；
        // 否则每一拍都重试同一个陈旧名，服务明明在静态名上却永远连不上。
        use std::sync::Mutex;

        let stale = missing_endpoint("stale-advert");
        let fallback = missing_endpoint("static-fallback");
        let returned = Arc::new(Mutex::new(Vec::new()));
        let returned_for_resolve = returned.clone();
        let stale_for_resolve = stale.clone();
        let fallback_for_resolve = fallback.clone();
        let (tx, _rx) = watch::channel(ConnectionSnapshot::connecting());
        let client = Arc::new(IpcClient::new());
        let handle = spawn_monitor(
            client,
            move |failed| {
                let next = match failed {
                    None => stale_for_resolve.clone(),
                    Some(failed) if failed == &stale_for_resolve => fallback_for_resolve.clone(),
                    Some(_) => stale_for_resolve.clone(),
                };
                returned_for_resolve.lock().unwrap().push(next.clone());
                next
            },
            tx,
        );

        // 至少跑过一轮「失败 → 解析下一次端点」。
        tokio::time::sleep(Duration::from_secs(5)).await;
        let returned = returned.lock().unwrap().clone();
        assert_eq!(
            returned.first(),
            Some(&stale),
            "首拍必须用解析出的通告名（{returned:?}）"
        );
        assert_eq!(
            returned.get(1),
            Some(&fallback),
            "通告名一次失败后必须换静态名再试（{returned:?}）"
        );

        handle.abort();
    }
}
