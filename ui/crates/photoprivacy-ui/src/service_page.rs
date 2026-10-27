use photoprivacy_service::{
    ServiceCommandResult, ServiceCommandStatus, ServiceRuntimeState, button_state, platform_manager,
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
    /// 失败命令的平台退出码；UI 用 `service.exitcode.*` 键翻译成当前语言（#60）。
    pub exit_code: Option<i32>,
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

            // UI 的 busy 快照每 200ms 才同步；双击可能在窗口内把同一变更命令入队两次。
            // 执行前按当前探测态重新校验，第二次命令在状态已变化时直接跳过。
            let probe_manager = Arc::clone(&manager);
            let current = tokio::task::spawn_blocking(move || probe_manager.state())
                .await
                .unwrap_or(ServiceRuntimeState::Unknown);
            if !command_allowed(command, current) {
                snapshot_tx.send_modify(|snapshot| snapshot.state = current);
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
                    exit_code: result.exit_code,
                });
            });
        }
    });

    (snapshot_rx, command_tx)
}

fn command_allowed(command: ServiceCommand, state: ServiceRuntimeState) -> bool {
    let buttons = button_state(state);
    match command {
        ServiceCommand::Install => buttons.install,
        ServiceCommand::Start => buttons.start,
        ServiceCommand::Stop => buttons.stop,
        ServiceCommand::Uninstall => buttons.uninstall,
        ServiceCommand::Refresh => true,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mutating_commands_are_gated_by_the_current_state() {
        // 防：双击在同一 busy 快照窗口入队两次，第二次仍触发提权/重复操作（#58 CodeRabbit）。
        assert!(command_allowed(
            ServiceCommand::Install,
            ServiceRuntimeState::NotInstalled
        ));
        assert!(!command_allowed(
            ServiceCommand::Install,
            ServiceRuntimeState::Stopped
        ));
        assert!(!command_allowed(
            ServiceCommand::Stop,
            ServiceRuntimeState::Stopped
        ));
        assert!(command_allowed(
            ServiceCommand::Stop,
            ServiceRuntimeState::Running
        ));
        assert!(!command_allowed(
            ServiceCommand::Uninstall,
            ServiceRuntimeState::Running
        ));
        assert!(command_allowed(
            ServiceCommand::Uninstall,
            ServiceRuntimeState::Stopped
        ));
        assert!(!command_allowed(
            ServiceCommand::Start,
            ServiceRuntimeState::Unknown
        ));
        assert!(!command_allowed(
            ServiceCommand::Uninstall,
            ServiceRuntimeState::Unknown
        ));
    }

    #[test]
    fn refresh_is_always_allowed() {
        // 防：刷新被状态门挡住，用户无法强制重探（#58）。
        for state in [
            ServiceRuntimeState::NotInstalled,
            ServiceRuntimeState::Running,
            ServiceRuntimeState::Stopped,
            ServiceRuntimeState::Unknown,
        ] {
            assert!(command_allowed(ServiceCommand::Refresh, state));
        }
    }
}
