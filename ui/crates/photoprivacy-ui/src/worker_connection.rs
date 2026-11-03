use photoprivacy_ipc::{
    ConnectionSnapshot, ConnectionState, Endpoint, HostPlatform, IpcClient, WorkerMode,
    current_home, current_platform, current_service_endpoint, resolve_endpoint,
    service_pipe_endpoint_with_advertised, spawn_monitor, spawn_show_window_server,
    watch_service_reachability, watch_version,
};
use std::path::{Path, PathBuf};
use std::sync::Arc;
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

    let client = Arc::new(IpcClient::new());
    let (endpoint_tx, endpoint_rx) = watch::channel(None);
    let link = WorkerLink {
        client: client.clone(),
        endpoint: endpoint_rx,
    };

    runtime.spawn(async move {
        match connect_worker(&client, &options).await {
            Some((endpoint, mode)) => {
                let _ = endpoint_tx.send(Some(endpoint.clone()));
                snapshots.send_modify(|snapshot| {
                    snapshot.state = ConnectionState::Connecting;
                    snapshot.endpoint = Some(endpoint.display());
                    snapshot.worker_mode = Some(mode);
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
                let version_client = Arc::new(IpcClient::new());
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
async fn connect_worker(
    client: &IpcClient,
    options: &ConnectOptions,
) -> Option<(Endpoint, WorkerMode)> {
    let platform = current_platform();
    let home = current_home();
    // #7：服务端点动态解析（通告名优先、静态名兜底）——随机化后的服务端点只有通告名可发现。
    let service = current_service_endpoint(platform, home.as_deref());
    let background = resolve_endpoint(platform, WorkerMode::Background, home.as_deref());

    for (mode, endpoint) in [
        (WorkerMode::Service, service),
        (WorkerMode::Background, background.clone()),
    ] {
        if let Some(endpoint) = endpoint
            && client.is_alive(&endpoint).await
        {
            return Some((endpoint, mode));
        }
    }

    if let (Some(endpoint), Some(worker)) = (
        background.as_ref(),
        options.worker_path.as_ref().filter(|path| path.exists()),
    ) {
        if let Err(err) = launch_background_worker(worker, options.config_path.as_deref()) {
            eprintln!("[photoprivacy-ui] 启动后台 Worker 失败: {err}");
        }

        let deadline = Instant::now() + Duration::from_secs(5);
        while Instant::now() < deadline {
            if client.is_alive(endpoint).await {
                return Some((endpoint.clone(), WorkerMode::Background));
            }
            tokio::time::sleep(Duration::from_millis(200)).await;
        }
    }

    background.map(|endpoint| (endpoint, WorkerMode::Background))
}

fn launch_background_worker(worker: &Path, config: Option<&Path>) -> std::io::Result<()> {
    let mut child = background_worker_command(worker, config).spawn()?;
    // 旧 UI 不持有 Worker 句柄；这里补一个回收线程，避免 Unix 上 Worker 先退出时留下僵尸。
    std::thread::spawn(move || {
        let _ = child.wait();
    });
    Ok(())
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
    let allowed_name = if cfg!(windows) {
        "PhotoPrivacyWorker.exe"
    } else {
        "PhotoPrivacyWorker"
    };
    let safe_worker = worker
        .parent()
        .map(|dir| dir.join(allowed_name))
        .unwrap_or_else(|| PathBuf::from(allowed_name));
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
    let name = if cfg!(windows) {
        "PhotoPrivacyWorker.exe"
    } else {
        "PhotoPrivacyWorker"
    };
    let path = exe_dir()?.join(name);
    path.exists().then_some(path)
}

#[cfg(test)]
mod tests {
    use super::*;

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
