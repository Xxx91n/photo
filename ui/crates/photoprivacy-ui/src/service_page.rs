use photoprivacy_service::{
    ServiceCommandResult, ServiceCommandStatus, ServiceRuntimeState, platform_manager,
};
use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, watch};

/// 旧 UI `WindowPollingHostedService` 的服务态轮询节奏：3s。
pub const SERVICE_POLL_INTERVAL: Duration = Duration::from_secs(3);

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ServiceFeedback {
    pub status: ServiceCommandStatus,
    pub message: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ServiceSnapshot {
    pub state: ServiceRuntimeState,
    pub busy: bool,
    pub feedback: Option<ServiceFeedback>,
}

impl Default for ServiceSnapshot {
    fn default() -> Self {
        Self {
            state: ServiceRuntimeState::Unknown,
            busy: false,
            feedback: None,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ServiceCommand {
    Refresh,
    Install,
    Start,
    Stop,
    Uninstall,
}

/// 启动服务页的后台循环：每 3s 探测一次；操作与刷新经同一通道串行执行，
/// 避免两个提权进程同时操作同一个服务。
// Design(#49): 服务探测走平台辅助进程（sc/systemctl/launchctl），不扩 IPC 协议。
pub fn start(
    runtime: &tokio::runtime::Handle,
    worker: Option<PathBuf>,
    config: Option<PathBuf>,
) -> (
    watch::Receiver<ServiceSnapshot>,
    mpsc::UnboundedSender<ServiceCommand>,
) {
    let manager = Arc::new(platform_manager(worker, config));
    let (snapshot_tx, snapshot_rx) = watch::channel(ServiceSnapshot::default());
    let (command_tx, mut command_rx) = mpsc::unbounded_channel();

    runtime.spawn(async move {
        loop {
            let probe_manager = Arc::clone(&manager);
            let state = tokio::task::spawn_blocking(move || probe_manager.state())
                .await
                .unwrap_or(ServiceRuntimeState::Unknown);
            snapshot_tx.send_modify(|snapshot| snapshot.state = state);

            let command = tokio::select! {
                _ = tokio::time::sleep(SERVICE_POLL_INTERVAL) => continue,
                command = command_rx.recv() => match command {
                    Some(command) => command,
                    None => break,
                },
            };

            if command == ServiceCommand::Refresh {
                continue;
            }

            snapshot_tx.send_modify(|snapshot| {
                snapshot.busy = true;
                snapshot.feedback = None;
            });

            let action_manager = Arc::clone(&manager);
            let result = tokio::task::spawn_blocking(move || match command {
                ServiceCommand::Install => action_manager.install(),
                ServiceCommand::Start => action_manager.start(),
                ServiceCommand::Stop => action_manager.stop(),
                ServiceCommand::Uninstall => action_manager.uninstall(),
                ServiceCommand::Refresh => unreachable!("Refresh 已在上面处理"),
            })
            .await
            .unwrap_or_else(|_| {
                ServiceCommandResult::failed("Service operation task failed", None)
            });

            let probe_manager = Arc::clone(&manager);
            let state = tokio::task::spawn_blocking(move || probe_manager.state())
                .await
                .unwrap_or(ServiceRuntimeState::Unknown);

            snapshot_tx.send_modify(|snapshot| {
                snapshot.busy = false;
                snapshot.state = state;
                snapshot.feedback = Some(ServiceFeedback {
                    status: result.status,
                    message: result.message,
                });
            });
        }
    });

    (snapshot_rx, command_tx)
}
