use crate::command::ServiceCommandResult;
use crate::state::{ServiceButtonState, ServiceRuntimeState, button_state};
use std::path::{Path, PathBuf};
use std::sync::Arc;

pub trait ServiceProbe: Send + Sync {
    fn state(&self) -> ServiceRuntimeState;
}

/// 平台操作面：Windows 的 sc.exe、Linux 的 pkexec、macOS 的 osascript 都实现这一组语义动作。
pub trait ServiceOps: Send + Sync {
    fn install(&self, worker: &Path, config: &Path) -> ServiceCommandResult;
    fn start(&self, worker: &Path, config: Option<&Path>) -> ServiceCommandResult;
    fn stop(&self) -> ServiceCommandResult;
    fn uninstall(&self) -> ServiceCommandResult;
}

/// UI 服务页的共享门面：探测与操作分开注入，界面侧只需要 `state()` + 四个动作。
pub struct ServiceManager {
    probe: Arc<dyn ServiceProbe>,
    ops: Arc<dyn ServiceOps>,
    worker: Option<PathBuf>,
    config: Option<PathBuf>,
}

impl ServiceManager {
    pub fn new(
        probe: Arc<dyn ServiceProbe>,
        ops: Arc<dyn ServiceOps>,
        worker: Option<PathBuf>,
        config: Option<PathBuf>,
    ) -> Self {
        Self {
            probe,
            ops,
            worker,
            config,
        }
    }

    pub fn state(&self) -> ServiceRuntimeState {
        self.probe.state()
    }

    pub fn buttons(&self) -> ServiceButtonState {
        button_state(self.state())
    }

    pub fn install(&self) -> ServiceCommandResult {
        let (Some(worker), Some(config)) = (
            self.worker.as_deref().filter(|path| path.is_file()),
            self.config.as_deref(),
        ) else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker and config.json must be next to the UI to install the service",
                None,
            );
        };
        self.ops.install(worker, config)
    }

    pub fn start(&self) -> ServiceCommandResult {
        let Some(worker) = self.worker.as_deref().filter(|path| path.is_file()) else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must be next to the UI to start the service",
                None,
            );
        };
        self.ops.start(worker, self.config.as_deref())
    }

    pub fn stop(&self) -> ServiceCommandResult {
        self.ops.stop()
    }

    pub fn uninstall(&self) -> ServiceCommandResult {
        self.ops.uninstall()
    }
}

pub fn platform_manager(worker: Option<PathBuf>, config: Option<PathBuf>) -> ServiceManager {
    #[cfg(windows)]
    return platform_manager_windows(worker, config);

    #[cfg(target_os = "linux")]
    return platform_manager_linux(worker, config);

    #[cfg(target_os = "macos")]
    return platform_manager_macos(worker, config);

    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    return platform_manager_unavailable(worker, config);
}

#[cfg(windows)]
fn platform_manager_windows(worker: Option<PathBuf>, config: Option<PathBuf>) -> ServiceManager {
    use crate::process::SystemProcessRunner;
    use crate::windows::{RealAdminChecker, ShellExecuteRunner, WindowsOps, WindowsProbe};

    let runner = Arc::new(SystemProcessRunner);
    let probe = Arc::new(WindowsProbe::new(runner.clone()));
    let ops = Arc::new(WindowsOps::new(
        runner,
        Arc::new(ShellExecuteRunner),
        Arc::new(RealAdminChecker),
        probe.clone(),
    ));
    ServiceManager::new(probe, ops, worker, config)
}

#[cfg(target_os = "linux")]
fn platform_manager_linux(worker: Option<PathBuf>, config: Option<PathBuf>) -> ServiceManager {
    use crate::process::SystemProcessRunner;
    use crate::unix::{LinuxOps, LinuxProbe};

    let runner = Arc::new(SystemProcessRunner);
    let probe = Arc::new(LinuxProbe::new(runner.clone()));
    let ops = Arc::new(LinuxOps::new(
        runner,
        script_path("install-systemd-service.sh"),
    ));
    ServiceManager::new(probe, ops, worker, config)
}

#[cfg(target_os = "macos")]
fn platform_manager_macos(worker: Option<PathBuf>, config: Option<PathBuf>) -> ServiceManager {
    use crate::process::SystemProcessRunner;
    use crate::unix::{MacOps, MacProbe};

    let runner = Arc::new(SystemProcessRunner);
    let probe = Arc::new(MacProbe::new(runner.clone()));
    let ops = Arc::new(MacOps::new(
        runner,
        script_path("install-launchd-service.sh"),
    ));
    ServiceManager::new(probe, ops, worker, config)
}

#[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
fn platform_manager_unavailable(
    worker: Option<PathBuf>,
    config: Option<PathBuf>,
) -> ServiceManager {
    struct UnavailableProbe;
    impl ServiceProbe for UnavailableProbe {
        fn state(&self) -> ServiceRuntimeState {
            ServiceRuntimeState::Unknown
        }
    }

    struct UnavailableOps;
    impl ServiceOps for UnavailableOps {
        fn install(&self, _: &Path, _: &Path) -> ServiceCommandResult {
            ServiceCommandResult::skipped("Service management is not supported on this platform")
        }
        fn start(&self, _: &Path, _: Option<&Path>) -> ServiceCommandResult {
            ServiceCommandResult::skipped("Service management is not supported on this platform")
        }
        fn stop(&self) -> ServiceCommandResult {
            ServiceCommandResult::skipped("Service management is not supported on this platform")
        }
        fn uninstall(&self) -> ServiceCommandResult {
            ServiceCommandResult::skipped("Service management is not supported on this platform")
        }
    }

    ServiceManager::new(
        Arc::new(UnavailableProbe),
        Arc::new(UnavailableOps),
        worker,
        config,
    )
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn script_path(name: &str) -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.join("scripts").join(name)))
        .unwrap_or_else(|| PathBuf::from("scripts").join(name))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    #[derive(Default)]
    struct RecordingOps {
        calls: Mutex<Vec<String>>,
    }

    impl ServiceOps for RecordingOps {
        fn install(&self, worker: &Path, config: &Path) -> ServiceCommandResult {
            self.calls.lock().unwrap().push(format!(
                "install {} {}",
                worker.display(),
                config.display()
            ));
            ServiceCommandResult::success(Some(0), "ok")
        }

        fn start(&self, worker: &Path, config: Option<&Path>) -> ServiceCommandResult {
            self.calls.lock().unwrap().push(format!(
                "start {} {}",
                worker.display(),
                config.map(|p| p.display().to_string()).unwrap_or_default()
            ));
            ServiceCommandResult::success(Some(0), "ok")
        }

        fn stop(&self) -> ServiceCommandResult {
            self.calls.lock().unwrap().push("stop".to_string());
            ServiceCommandResult::success(Some(0), "ok")
        }

        fn uninstall(&self) -> ServiceCommandResult {
            self.calls.lock().unwrap().push("uninstall".to_string());
            ServiceCommandResult::success(Some(0), "ok")
        }
    }

    struct FixedProbe(ServiceRuntimeState);

    impl ServiceProbe for FixedProbe {
        fn state(&self) -> ServiceRuntimeState {
            self.0
        }
    }

    fn temp_worker() -> PathBuf {
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let seq = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let path =
            std::env::temp_dir().join(format!("photoprivacy-manager-{}-{seq}", std::process::id()));
        std::fs::write(&path, b"").unwrap();
        path
    }

    #[test]
    fn install_requires_worker_and_config() {
        // 防：缺失 Worker 时仍拉起提权进程，用户只看到一条平台错误而不是可操作的提示（#58）。
        let ops = Arc::new(RecordingOps::default());
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::NotInstalled)),
            ops.clone(),
            None,
            Some(PathBuf::from("config.json")),
        );

        let result = manager.install();

        assert_eq!(result.status, crate::ServiceCommandStatus::Failed);
        assert!(ops.calls.lock().unwrap().is_empty());
    }

    #[test]
    fn install_requires_an_existing_worker_file() {
        // 防：显式传入的 --worker 路径不存在时仍 create 服务，服务永远起不来且用户只看到平台错误（#58）。
        let ops = Arc::new(RecordingOps::default());
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::NotInstalled)),
            ops.clone(),
            Some(PathBuf::from("definitely-missing-PhotoPrivacyWorker")),
            Some(PathBuf::from("config.json")),
        );

        let result = manager.install();

        assert_eq!(result.status, crate::ServiceCommandStatus::Failed);
        assert!(ops.calls.lock().unwrap().is_empty());
    }

    #[test]
    fn start_skips_reconfig_when_config_is_absent() {
        // 防：没有 config 时仍把空路径拼进 sc config，Worker 启动后读到空配置（#58）。
        let ops = Arc::new(RecordingOps::default());
        let worker = temp_worker();
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::Stopped)),
            ops.clone(),
            Some(worker.clone()),
            None,
        );

        assert_eq!(manager.start().status, crate::ServiceCommandStatus::Success);
        assert_eq!(
            ops.calls.lock().unwrap().as_slice(),
            [format!("start {} ", worker.display())].as_slice()
        );
        let _ = std::fs::remove_file(worker);
    }

    #[test]
    fn buttons_follow_probe_state() {
        // 防：门面绕过状态策略直接给出按钮，页面状态与可用性脱节（#58）。
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::Running)),
            Arc::new(RecordingOps::default()),
            None,
            None,
        );

        assert!(manager.buttons().stop);
        assert!(!manager.buttons().start);
    }
}
