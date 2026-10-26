use photoprivacy_ipc::{
    ConnectionSnapshot, ConnectionState, Endpoint, IpcClient, WorkerMode, current_home,
    current_platform, resolve_endpoint, spawn_monitor, spawn_show_window_server,
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

/// 启动 IPC 侧的全部后台任务：唤起管道服务、连接编排、心跳监控。
pub fn start(
    runtime: &tokio::runtime::Handle,
    options: ConnectOptions,
    snapshots: watch::Sender<ConnectionSnapshot>,
    events: mpsc::UnboundedSender<UiEvent>,
) {
    let events_for_server = events.clone();
    runtime.spawn(async move {
        spawn_show_window_server(move || {
            let _ = events_for_server.send(UiEvent::ShowWindow);
        });
    });

    runtime.spawn(async move {
        let client = Arc::new(IpcClient::new());
        match connect_worker(&client, &options).await {
            Some((endpoint, mode)) => {
                snapshots.send_modify(|snapshot| {
                    snapshot.state = ConnectionState::Connecting;
                    snapshot.endpoint = Some(endpoint.display());
                    snapshot.worker_mode = Some(mode);
                });
                spawn_monitor(client, endpoint, snapshots);
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
}

/// 连接顺序与旧 UI `WorkerProcessManager.ConnectOrLaunchAsync` 对齐：服务端点优先，其次后台端点，
/// 都不可用时启动后台 Worker 并轮询 5s。
// Design(#58): 服务「已安装未运行」的探测属于服务管理票，本轮不做；因此不会像旧 UI 那样停在服务壳，
// 而是落到后台 Worker——这是本竖切的已知差异，等 #58 补齐探测后收敛。
async fn connect_worker(
    client: &IpcClient,
    options: &ConnectOptions,
) -> Option<(Endpoint, WorkerMode)> {
    let platform = current_platform();
    let home = current_home();
    let service = resolve_endpoint(platform, WorkerMode::Service, home.as_deref());
    let background = resolve_endpoint(platform, WorkerMode::Background, home.as_deref());

    if let Some(endpoint) = service.as_ref()
        && client.is_alive(endpoint).await
    {
        return Some((endpoint.clone(), WorkerMode::Service));
    }

    if let Some(endpoint) = background.as_ref()
        && client.is_alive(endpoint).await
    {
        return Some((endpoint.clone(), WorkerMode::Background));
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

fn background_worker_command(worker: &Path, config: Option<&Path>) -> std::process::Command {
    #[allow(unused_mut)]
    let mut command = std::process::Command::new(worker);
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
}
