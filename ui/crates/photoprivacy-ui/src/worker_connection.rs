use photoprivacy_ipc::{
    ConnectFailureCause, ConnectionSnapshot, ConnectionState, Endpoint, HostPlatform, IpcClient,
    IpcError, ProcessIdentityVerifier, ServerGuard, UnverifiablePolicy, WorkerMode, current_home,
    current_platform, current_service_endpoint, is_service_access_denied, protocol::methods,
    resolve_endpoint, service_pipe_endpoint_with_advertised, spawn_monitor,
    spawn_show_window_server, watch_service_reachability, watch_version,
};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use tokio::sync::{mpsc, watch};

#[derive(Clone, Debug, Default)]
pub struct ConnectOptions {
    pub config_path: Option<PathBuf>,
    pub worker_path: Option<PathBuf>,
}

impl ConnectOptions {
    /// 与旧 UI 相同的布局假设：`config/config.json` 与 Worker 可执行文件都在 UI 同目录。
    pub fn discover() -> Self {
        Self {
            config_path: default_config_path(),
            worker_path: default_worker_path(),
        }
    }

    pub fn with_overrides(
        mut self,
        config_path: Option<PathBuf>,
        worker_path: Option<PathBuf>,
    ) -> Self {
        if config_path.is_some() {
            self.config_path = config_path;
        }
        if worker_path.is_some() {
            self.worker_path = worker_path;
        }
        self
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum UiEvent {
    ShowWindow,
}

/// 日志 backfill 需要的 Worker 访问面：共享客户端 + 连接编排解析出的端点。
///
/// 端点可能在心跳之前就广播（连接探测成功后立刻可见），backfill 自行重试到 Worker 可达。
pub struct WorkerLink {
    pub client: Arc<IpcClient>,
    pub endpoint: watch::Receiver<Option<Endpoint>>,
}

/// 启动 IPC 侧的全部后台任务：唤起管道服务、连接编排、心跳监控。
pub fn start(
    runtime: &tokio::runtime::Handle,
    options: ConnectOptions,
    snapshots: watch::Sender<ConnectionSnapshot>,
    events: mpsc::UnboundedSender<UiEvent>,
) -> WorkerLink {
    let events_for_server = events.clone();
    runtime.spawn(async move {
        spawn_show_window_server(move || {
            let _ = events_for_server.send(UiEvent::ShowWindow);
        });
    });

    // 服务态 3s 拍：与连接编排并行起跑（旧 UI 的服务模式轮询同样独立于心跳）。
    // 短连接预算：服务未安装时不值得每拍耗满 700ms 的重试预算。
    // #7：服务端点每拍动态解析（通告名优先、静态名兜底）——服务重启换随机名后探测不悬死。
    if current_service_endpoint(current_platform(), current_home().as_deref()).is_some() {
        let probe_platform = current_platform();
        let probe_home = current_home();
        let probe = Arc::new(IpcClient::with_connect_timeout(Duration::from_millis(250)));
        runtime.spawn(watch_service_reachability(
            move || {
                let probe = probe.clone();
                let endpoint = probe_service_endpoint(probe_platform, probe_home.as_deref());
                async move { probe.is_alive(&endpoint).await }
            },
            snapshots.clone(),
        ));
    }

    // #7 残余①：后台管道核验服务端进程身份。期望 PID 在拉起 Worker 后登记；镜像路径作
    // 「UI 重启但后台 Worker 仍在」的回退基准。作用域只覆盖后台端点（服务管道不叠加，D3）。
    let (guard, identity) = background_server_guard(&options);
    let client = Arc::new(with_optional_guard(IpcClient::new(), guard.as_ref()));
    let (endpoint_tx, endpoint_rx) = watch::channel(None);
    let link = WorkerLink {
        client: client.clone(),
        endpoint: endpoint_rx,
    };

    runtime.spawn(async move {
        match connect_worker(&client, &options, &identity).await {
            Some((endpoint, mode, failure)) => {
                let _ = endpoint_tx.send(Some(endpoint.clone()));
                snapshots.send_modify(|snapshot| {
                    snapshot.state = ConnectionState::Connecting;
                    snapshot.endpoint = Some(endpoint.display());
                    snapshot.worker_mode = Some(mode);
                    // #169：编排可能落到从未连上的端点（服务被拒→后台缺失→拉起未就绪）；
                    // 失败分类随快照广播，心跳连上时由 apply_success 清空。
                    snapshot.connect_failure = failure;
                });
                // 端点解析器（#7）：后台 = 静态契约名（恒等）；服务 = 动态解析（通告名优先、静态兜底），
                // 解析结果推送 endpoint watch（backfill 每 5s 重试时重读，服务重启换名后自愈）。
                // 心跳与版本轮询共用：服务重启换名后两者都不悬死在旧名。
                let resolve = endpoint_resolver(
                    mode,
                    current_platform(),
                    current_home(),
                    endpoint.clone(),
                    endpoint_tx.clone(),
                );
                spawn_monitor(client, resolve.clone(), snapshots.clone());
                // 独立 1s 版本轮询：单独客户端避免与心跳的单飞锁互相拖慢；心跳退避不影响版本节拍。
                // #7：版本轮询同样核验后台服务端身份（服务端点由 guard 作用域过滤，零行为变化）。
                let version_client =
                    Arc::new(with_optional_guard(IpcClient::new(), guard.as_ref()));
                let version_resolve = resolve;
                tokio::spawn(watch_version(
                    move || {
                        let client = version_client.clone();
                        let endpoint = version_resolve();
                        async move { client.exif_tool_version(&endpoint).await.ok() }
                    },
                    snapshots,
                ));
            }
            None => {
                snapshots.send_modify(|snapshot| {
                    snapshot.state = ConnectionState::Disconnected;
                    snapshot.last_error =
                        Some("无法解析 Worker 端点（缺少用户主目录）".to_string());
                });
            }
        }
    });

    link
}

/// 服务探测端点（#7）：每拍重解析——Windows 通告名优先、静态名兜底；解析不到的平台形态
/// （mac 无 HOME，仅理论可达：探测任务仅在初始解析成功时起跑）退静态服务名（经
/// service_pipe_endpoint_with_advertised 的单一出处派生，消除字面量重复——审查返修 n-R2），
/// 探测必然失败等价「服务不可达」，不产生误导性成功。
fn probe_service_endpoint(platform: HostPlatform, home: Option<&Path>) -> Endpoint {
    current_service_endpoint(platform, home)
        .unwrap_or_else(|| service_pipe_endpoint_with_advertised(None))
}

/// 按模式构造端点解析器（#7）：后台 = 静态契约名（恒等返回）；服务 = 每次调用重读通告名
/// （随机化后服务重启会换名），解析不到时退连接时的端点。服务模式解析结果推送 `endpoint_tx`
/// （backfill 持 receiver 每 5s 重试时重读自愈）；同值不写——避免 1s 版本节拍制造稳定的
/// watch 唤醒（与 watch_service_reachability 的同值抑制同口径）。
fn endpoint_resolver(
    mode: WorkerMode,
    platform: HostPlatform,
    home: Option<PathBuf>,
    connected: Endpoint,
    endpoint_tx: watch::Sender<Option<Endpoint>>,
) -> impl Fn() -> Endpoint + Clone {
    move || match mode {
        WorkerMode::Background => connected.clone(),
        WorkerMode::Service => {
            let resolved = current_service_endpoint(platform, home.as_deref())
                .unwrap_or_else(|| connected.clone());
            endpoint_tx.send_if_modified(|current| {
                if current.as_ref() == Some(&resolved) {
                    false
                } else {
                    *current = Some(resolved.clone());
                    true
                }
            });
            resolved
        }
    }
}

/// 连接顺序与旧 UI `WorkerProcessManager.ConnectOrLaunchAsync` 对齐：服务端点优先，其次后台端点，
/// 都不可用时启动后台 Worker 并轮询 5s。服务「已安装未运行」时本版会落到后台 Worker，而不是像旧 UI
/// 停在服务壳；服务态本身由服务页独立探测。
// Design(#82): 连接编排维持「服务端点优先、不可用回落后台 Worker」；同 config 双 Worker 并存的审计争用由 #162 单写者准入门兜底，编排预防归 #58。
// Design(#82): 编排顺序与轮询预算不变，只把每一步的真实失败原因（服务端点 DACL 拒、后台拉起结果）带出给呈现层分类（#169 落地后关票，决策归档）。
async fn connect_worker(
    client: &IpcClient,
    options: &ConnectOptions,
    identity: &ProcessIdentityVerifier,
) -> Option<(Endpoint, WorkerMode, Option<ConnectFailureCause>)> {
    let platform = current_platform();
    let home = current_home();
    // #7：服务端点动态解析（通告名优先、静态名兜底）——随机化后的服务端点只有通告名可发现。
    let service = current_service_endpoint(platform, home.as_deref());
    let background = resolve_endpoint(platform, WorkerMode::Background, home.as_deref());

    // #169：探测不再把 IpcError 吞成 bool——服务端点被 DACL 拒（raw 5）本身即「端点存在且当前
    // 账户不在授权集合」的证明，是回落链第一环的真实失败原因；端点缺失/超时不携带该信号，照旧回落。
    let mut service_access_denied = false;
    // #7 残余①：后台端点连上但对端不是期望 Worker 的原因（核验失败），供失败分类带出。
    let mut identity_failure: Option<String> = None;
    for (mode, endpoint) in [
        (WorkerMode::Service, service),
        (WorkerMode::Background, background.clone()),
    ] {
        let Some(endpoint) = endpoint else {
            continue;
        };
        match client.request(&endpoint, methods::PING).await {
            Ok(_) => return Some((endpoint, mode, None)),
            Err(err) => {
                if mode == WorkerMode::Service && is_service_access_denied(&err) {
                    service_access_denied = true;
                }
                if mode == WorkerMode::Background {
                    if let IpcError::ServerIdentity(reason) = &err {
                        identity_failure = Some(reason.clone());
                    }
                }
            }
        }
    }

    // #169：拉起结果仅作失败分类的观察，不改变「拉起 + 轮询 5s」的既有编排。
    let mut launch = None;
    if let (Some(endpoint), Some(worker)) = (
        background.as_ref(),
        options.worker_path.as_ref().filter(|path| path.exists()),
    ) {
        launch = Some(
            match launch_background_worker(worker, options.config_path.as_deref()) {
                Ok((exit_slot, pid)) => {
                    // #7 残余①：记下本次拉起的 Worker PID——后续每次连接的核验首选比对它。
                    identity.expect_pid(pid);
                    LaunchObservation {
                        spawn_error: None,
                        exit_slot: Some(exit_slot),
                    }
                }
                Err(err) => {
                    eprintln!("[photoprivacy-ui] 启动后台 Worker 失败: {err}");
                    LaunchObservation {
                        spawn_error: Some(err),
                        exit_slot: None,
                    }
                }
            },
        );

        let deadline = Instant::now() + Duration::from_secs(5);
        while Instant::now() < deadline {
            // 不走 is_alive：它把错误吞成 bool，核验失败的原因会丢（AC2 要求原因可观测）。
            match client.request(endpoint, methods::PING).await {
                Ok(_) => return Some((endpoint.clone(), WorkerMode::Background, None)),
                Err(IpcError::ServerIdentity(reason)) => identity_failure = Some(reason),
                Err(_) => {}
            }
            tokio::time::sleep(Duration::from_millis(200)).await;
        }
    }

    // #7 残余①：后台端点连上了但对端不是期望的 Worker——比「未就绪/未安装」更准确：
    // Worker 可能根本没参与，接管端点的是同用户抢先建的管道。归入既有三态的启动失败，
    // 并在编排层记 warning（AC2：失败原因可观测；分类函数保持纯函数）。
    if let Some(reason) = identity_failure.as_ref() {
        eprintln!("[photoprivacy-ui] 后台 IPC 服务端身份核验失败: {reason}");
    }
    let failure = classify_connect_failure(service_access_denied, identity_failure, launch);
    background.map(|endpoint| (endpoint, WorkerMode::Background, Some(failure)))
}

/// 拉起后台 Worker 的观察结果（#169 分类用）：spawn 错误，或回收线程回填的退出状态槽。
struct LaunchObservation {
    spawn_error: Option<std::io::Error>,
    exit_slot: Option<Arc<Mutex<Option<std::process::ExitStatus>>>>,
}

/// 连接全部失败后按「最近一次真实失败原因」三态归类（#169）：服务端点权限拒绝优先——它是
/// 未授权用户唯一正确的行动线索（找安装者加授权）；其次区分「拉起了后台 Worker 但未就绪」
/// （含 #163 单写者准入门让位退出）与「未找到可拉起的 Worker 程序」。
fn classify_connect_failure(
    service_access_denied: bool,
    identity_failure: Option<String>,
    launch: Option<LaunchObservation>,
) -> ConnectFailureCause {
    if service_access_denied {
        return ConnectFailureCause::ServiceAccessDenied;
    }
    // #7 残余①：后台端点连上了但对端不是期望的 Worker（同用户抢先建管道）——归入既有
    // 三态的启动失败，细节带上核验原因（warning 由编排层在归类前记录）。
    if let Some(reason) = identity_failure {
        return ConnectFailureCause::WorkerStartupFailed(format!("untrusted IPC server: {reason}"));
    }
    let Some(launch) = launch else {
        return ConnectFailureCause::WorkerNotInstalled;
    };
    if let Some(err) = launch.spawn_error.as_ref() {
        return ConnectFailureCause::WorkerStartupFailed(format!("spawn: {err}"));
    }
    let exit_status = launch
        .exit_slot
        .and_then(|slot| slot.lock().ok().and_then(|guard| *guard));
    let detail = match exit_status {
        Some(status) => match status.code() {
            // #163 单写者让位退出码是 1，但退出码 1 并非让位专属，只如实上报码值，
            // 「让位」作为可能性留在呈现层文案的条件句里。
            Some(code) => format!("worker exited with code {code}"),
            None => "worker terminated by signal".to_string(),
        },
        None => "worker not ready within 5s".to_string(),
    };
    ConnectFailureCause::WorkerStartupFailed(detail)
}

/// 拉起后台 Worker；返回退出状态共享槽与本次拉起的 PID（#7 残余①：核验首选比对它），
/// 回收线程在 Worker 退出时回填退出状态（#169 观察用）。
fn launch_background_worker(
    worker: &Path,
    config: Option<&Path>,
) -> std::io::Result<(Arc<Mutex<Option<std::process::ExitStatus>>>, u32)> {
    let mut child = background_worker_command(worker, config).spawn()?;
    let pid = child.id();
    // 旧 UI 不持有 Worker 句柄；这里补一个回收线程，避免 Unix 上 Worker 先退出时留下僵尸。
    // #169：wait 结果回传共享槽，连接编排在 5s 轮询结束后读取退出状态
    // （如 #163 单写者准入门让位的秒退），供失败分类附上真实退出码。
    let exit_slot = Arc::new(Mutex::new(None));
    let slot_for_reaper = exit_slot.clone();
    std::thread::spawn(move || {
        if let Ok(status) = child.wait() {
            *slot_for_reaper.lock().unwrap() = Some(status);
        }
    });
    Ok((exit_slot, pid))
}

/// 后台 Worker 可执行文件名（与 `background_worker_command` 的钉死口径同源）。
fn worker_binary_name() -> &'static str {
    if cfg!(windows) {
        "PhotoPrivacyWorker.exe"
    } else {
        "PhotoPrivacyWorker"
    }
}

/// 实际会被拉起的 Worker 镜像路径：override 只取目录、文件名钉死。
fn background_worker_image(worker: &Path) -> PathBuf {
    worker
        .parent()
        .map(|dir| dir.join(worker_binary_name()))
        .unwrap_or_else(|| PathBuf::from(worker_binary_name()))
}

/// 服务端身份核验的镜像回退基准（#7 残余①）：与拉起路径同源；找不到可拉起的 Worker 时为 None
/// （此时核验只剩 PID 匹配，取不到基准按 fail-open + warning 处置）。
fn expected_worker_image(options: &ConnectOptions) -> Option<PathBuf> {
    let worker = options.worker_path.clone().or_else(default_worker_path)?;
    Some(background_worker_image(&worker))
}

/// 核验不了时的平台策略（#7 D2 / AC2）：macOS 首版无镜像回退（`LOCAL_PEEREPID` 只给 PID），
/// 且该平台攻击者本可直接抢建 socket 阻断可用性，fail-closed 无净增益 → fail-open + warning；
/// Windows/Linux 按 AC2 fail-closed（核验不了即拒该连接）。
// Design(#7): 核验不了时的平台矩阵——Windows/Linux 按 AC2 fail-closed（核验不了即拒连），macOS 按 D2 fail-open + warning；服务管道不叠加（D3）。
fn unverifiable_policy(platform: HostPlatform) -> UnverifiablePolicy {
    match platform {
        HostPlatform::MacOs => UnverifiablePolicy::FailOpen,
        HostPlatform::Windows | HostPlatform::Linux => UnverifiablePolicy::FailClosed,
    }
}

/// 构造后台服务端身份核验作用域（#7 残余①）：端点解析不到时（mac 无 HOME，仅理论可达）
/// 返回 None——与连接编排同样拿不到端点，不额外产生行为。
fn background_server_guard(
    options: &ConnectOptions,
) -> (Option<Arc<ServerGuard>>, Arc<ProcessIdentityVerifier>) {
    let platform = current_platform();
    let verifier = Arc::new(ProcessIdentityVerifier::new(expected_worker_image(options)));
    let guard = resolve_endpoint(platform, WorkerMode::Background, current_home().as_deref()).map(
        |endpoint| {
            Arc::new(ServerGuard::new(
                endpoint,
                verifier.clone(),
                unverifiable_policy(platform),
            ))
        },
    );
    (guard, verifier)
}

fn with_optional_guard(client: IpcClient, guard: Option<&Arc<ServerGuard>>) -> IpcClient {
    match guard {
        Some(guard) => client.with_server_guard(guard.clone()),
        None => client,
    }
}

/// Builds the command that launches the background worker process.
///
/// Only the directory portion of `worker` is honoured: the executable name is
/// always pinned to the canonical worker binary (`PhotoPrivacyWorker.exe` on
/// Windows, `PhotoPrivacyWorker` elsewhere), so a caller-supplied path cannot
/// substitute an arbitrary program. When `worker` has no parent directory the
/// bare binary name is used and resolved by the OS executable search.
///
/// The worker is started with `--mode background`, plus `--config <path>` when
/// `config` is given. On Windows the process is created without a console
/// window. The command is spawned directly, never through a shell.
///
/// Note: this pins the filename only; it does not verify that the resolved
/// directory is trusted.
fn background_worker_command(worker: &Path, config: Option<&Path>) -> std::process::Command {
    let safe_worker = background_worker_image(worker);
    #[allow(unused_mut)]
    let mut command = std::process::Command::new(&safe_worker);
    command.arg("--mode").arg("background");
    if let Some(config) = config {
        command.arg("--config").arg(config);
    }
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    command
}

fn exe_dir() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?;
    exe.parent().map(Path::to_path_buf)
}

fn default_config_path() -> Option<PathBuf> {
    // 与旧 UI 一致：默认路径始终传给 Worker（存在与否由 Worker 自己校验），不做存在性筛选。
    Some(exe_dir()?.join("config").join("config.json"))
}

fn default_worker_path() -> Option<PathBuf> {
    let path = exe_dir()?.join(worker_binary_name());
    path.exists().then_some(path)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 跨平台构造指定退出码的 `ExitStatus`（Windows raw 即码值；Unix wait status 在高 8 位）。
    fn exit_status_with_code(code: i32) -> std::process::ExitStatus {
        #[cfg(windows)]
        {
            use std::os::windows::process::ExitStatusExt;
            std::process::ExitStatus::from_raw(code as u32)
        }
        #[cfg(unix)]
        {
            use std::os::unix::process::ExitStatusExt;
            // Unix 的 from_raw 收 i32 wait status（Windows 才是 u32）；退出码在高位 8 比特。
            std::process::ExitStatus::from_raw(code << 8)
        }
    }

    #[test]
    fn connect_failure_classification_covers_three_states() {
        // 防：三态归错——未授权用户被报成「未安装」、裸机缺 Worker 被报成「未授权」（#169 AC）。
        // 授权拒绝优先：未授权场景下后台 Worker 同时让位退出（#163），行动指引仍应指向加授权。
        assert_eq!(
            classify_connect_failure(true, None, None),
            ConnectFailureCause::ServiceAccessDenied
        );
        assert_eq!(
            classify_connect_failure(false, None, None),
            ConnectFailureCause::WorkerNotInstalled
        );

        // spawn 失败：保留 Worker 缺失语义并附启动错误。
        let launch = LaunchObservation {
            spawn_error: Some(std::io::Error::from_raw_os_error(5)),
            exit_slot: None,
        };
        match classify_connect_failure(false, None, Some(launch)) {
            ConnectFailureCause::WorkerStartupFailed(detail) => {
                assert!(detail.starts_with("spawn: "), "detail: {detail}");
            }
            other => panic!("spawn 失败应为 WorkerStartupFailed，得到 {other:?}"),
        }
    }

    #[test]
    fn exited_worker_reports_its_code_for_single_writer_yield() {
        // 防：#163 单写者让位（秒退、退出码 1）被 5s 轮询超时口径吞掉，用户只看到笼统的未就绪（#169）。
        let exit_slot = Arc::new(Mutex::new(Some(exit_status_with_code(1))));
        let launch = LaunchObservation {
            spawn_error: None,
            exit_slot: Some(exit_slot),
        };
        match classify_connect_failure(false, None, Some(launch)) {
            ConnectFailureCause::WorkerStartupFailed(detail) => {
                assert!(detail.contains("exited with code 1"), "detail: {detail}");
            }
            other => panic!("已退出应为 WorkerStartupFailed，得到 {other:?}"),
        }
    }

    #[test]
    fn untrusted_ipc_server_is_classified_as_startup_failure() {
        // 防（#7 AC1）：后台端点连上但对端不是期望 Worker 时被吞成「未就绪/未安装」——
        // 用户看不到端点被同用户进程抢占这一真实原因。原因必须带进既有三态的分类细节。
        let reason = "服务端 PID 777 的镜像 /tmp/evil-server 不是期望的 /opt/pp/PhotoPrivacyWorker";
        match classify_connect_failure(false, Some(reason.to_string()), None) {
            ConnectFailureCause::WorkerStartupFailed(detail) => {
                assert!(
                    detail.starts_with("untrusted IPC server: "),
                    "detail: {detail}"
                );
                assert!(detail.contains("777"), "detail: {detail}");
            }
            other => panic!("核验失败应为 WorkerStartupFailed，得到 {other:?}"),
        }
    }

    #[test]
    fn expected_image_pins_worker_filename_to_the_spawn_path() {
        // 防（#7）：核验基准与实际拉起路径分叉——override 传任意文件名时，拉起仍是钉死的
        // PhotoPrivacyWorker，基准若跟着 override 走会让合法 Worker 恒被误拒。
        let options = ConnectOptions {
            config_path: None,
            worker_path: Some(PathBuf::from("/opt/pp/SomeOtherName")),
        };
        let expected = Path::new("/opt/pp").join(worker_binary_name());
        assert_eq!(expected_worker_image(&options), Some(expected));
    }

    #[test]
    fn background_guard_is_scoped_to_background_endpoint_with_platform_policy() {
        // 防（#7 D2/D3）：核验作用域扩到服务端点（把 SYSTEM 服务拒掉），或平台策略错位
        // （Windows/Linux 该 fail-closed 却放行；macOS 该 fail-open 却让后台模式恒不可用）。
        let (guard, verifier) = background_server_guard(&ConnectOptions::default());
        assert_eq!(verifier.expected_pid(), None, "拉起前不应有期望 PID");
        let Some(guard) = guard else {
            return;
        };
        let platform = current_platform();
        let home = current_home();
        let background = resolve_endpoint(platform, WorkerMode::Background, home.as_deref())
            .expect("后台端点应可解析");
        let service = resolve_endpoint(platform, WorkerMode::Service, home.as_deref())
            .expect("服务端点应可解析");
        assert!(guard.applies_to(&background));
        assert!(!guard.applies_to(&service));
        assert_eq!(
            guard.unverifiable_policy(),
            unverifiable_policy(platform),
            "作用域策略必须与平台矩阵一致"
        );
        let expected = if platform == HostPlatform::MacOs {
            UnverifiablePolicy::FailOpen
        } else {
            UnverifiablePolicy::FailClosed
        };
        assert_eq!(guard.unverifiable_policy(), expected);
    }

    #[test]
    fn still_running_worker_reports_the_readiness_timeout() {
        // 防：进程活着但没就绪时谎报退出码，或把「5s 未就绪」误归为未安装（#169）。
        let launch = LaunchObservation {
            spawn_error: None,
            exit_slot: Some(Arc::new(Mutex::new(None))),
        };
        assert_eq!(
            classify_connect_failure(false, None, Some(launch)),
            ConnectFailureCause::WorkerStartupFailed("worker not ready within 5s".to_string())
        );
    }

    #[test]
    fn worker_launch_matches_old_ui_arguments() {
        // 防：启动参数漂移导致 Worker 用错模式或忽略 UI 指定的配置（旧 UI 契约见 WorkerProcessManager，#54）。
        let config = Path::new("C:/tmp/config.json");
        let command = background_worker_command(Path::new("PhotoPrivacyWorker.exe"), Some(config));
        let args: Vec<_> = command
            .get_args()
            .map(|arg| arg.to_string_lossy().into_owned())
            .collect();
        assert_eq!(
            args,
            vec!["--mode", "background", "--config", "C:/tmp/config.json"]
        );
    }

    #[test]
    fn worker_launch_omits_config_when_absent() {
        // 防：无配置时仍传 `--config ""` 之类的空参数，让 Worker 解析到空路径（#54）。
        let command = background_worker_command(Path::new("PhotoPrivacyWorker"), None);
        let args: Vec<_> = command
            .get_args()
            .map(|arg| arg.to_string_lossy().into_owned())
            .collect();
        assert_eq!(args, vec!["--mode", "background"]);
    }

    #[test]
    fn background_resolver_is_identity_and_leaves_watch_untouched() {
        // 防（#7）：后台端点是静态契约名——解析器必须恒等返回，且不把后台端点误推进
        // 服务专用的 endpoint watch（backfill 重读通道只该反映服务模式的重解析）。
        // 服务臂依赖机器注册表（通告名），不做单测——纯逻辑归 photoprivacy-ipc 的
        // endpoints 测试，实机行为归 S4 复测（同仓 CI 平台限制先例）。
        let (tx, mut rx) = watch::channel(None);
        let connected = Endpoint::Pipe("PhotoPrivacyCleaner.Background".into());
        let resolve = endpoint_resolver(
            WorkerMode::Background,
            HostPlatform::Windows,
            None,
            connected,
            tx,
        );

        assert_eq!(
            resolve(),
            Endpoint::Pipe("PhotoPrivacyCleaner.Background".into())
        );
        assert_eq!(*rx.borrow_and_update(), None);
    }
}
