use crate::command::ServiceCommandResult;
use crate::state::ServiceRuntimeState;
use std::path::{Path, PathBuf};
use std::sync::Arc;

pub trait ServiceProbe: Send + Sync {
    fn state(&self) -> ServiceRuntimeState;
}

/// 平台操作面：Windows 的 sc.exe、Linux 的 pkexec、macOS 的 osascript 都实现这一组语义动作。
///
/// worker/config 是 UI 侧上下文：Windows 需要它们拼 `sc create/config`；Linux/macOS 的安装脚本
/// 与 unit 自带安装路径，所以允许为 `None`，由平台实现决定是否必需。
pub trait ServiceOps: Send + Sync {
    fn install(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult;
    fn start(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult;
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

    pub fn install(&self) -> ServiceCommandResult {
        self.ops
            .install(self.worker.as_deref(), self.config.as_deref())
    }

    pub fn start(&self) -> ServiceCommandResult {
        self.ops
            .start(self.worker.as_deref(), self.config.as_deref())
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
    const UNSUPPORTED: &str = "Service management is not supported on this platform";

    struct UnavailableProbe;
    impl ServiceProbe for UnavailableProbe {
        fn state(&self) -> ServiceRuntimeState {
            ServiceRuntimeState::Unknown
        }
    }

    struct UnavailableOps;
    impl ServiceOps for UnavailableOps {
        fn install(&self, _: Option<&Path>, _: Option<&Path>) -> ServiceCommandResult {
            ServiceCommandResult::skipped(UNSUPPORTED)
        }
        fn start(&self, _: Option<&Path>, _: Option<&Path>) -> ServiceCommandResult {
            ServiceCommandResult::skipped(UNSUPPORTED)
        }
        fn stop(&self) -> ServiceCommandResult {
            ServiceCommandResult::skipped(UNSUPPORTED)
        }
        fn uninstall(&self) -> ServiceCommandResult {
            ServiceCommandResult::skipped(UNSUPPORTED)
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

    fn display_opt(path: Option<&Path>) -> String {
        path.map(|path| path.display().to_string())
            .unwrap_or_default()
    }

    impl ServiceOps for RecordingOps {
        fn install(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
            self.calls.lock().unwrap().push(format!(
                "install {} {}",
                display_opt(worker),
                display_opt(config)
            ));
            ServiceCommandResult::success(Some(0), "ok")
        }

        fn start(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
            self.calls.lock().unwrap().push(format!(
                "start {} {}",
                display_opt(worker),
                display_opt(config)
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
    fn install_forwards_optional_paths() {
        // 防：门面把路径吞掉或顺序写反，Windows 侧拿不到 Worker/config 拼 sc create（#58）。
        let ops = Arc::new(RecordingOps::default());
        let worker = temp_worker();
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::NotInstalled)),
            ops.clone(),
            Some(worker.clone()),
            Some(PathBuf::from("config.json")),
        );

        assert_eq!(
            manager.install().status,
            crate::ServiceCommandStatus::Success
        );
        assert_eq!(
            ops.calls.lock().unwrap().as_slice(),
            [format!("install {} config.json", worker.display())].as_slice()
        );
        let _ = std::fs::remove_file(worker);
    }

    #[test]
    fn start_forwards_none_config() {
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
    fn stop_and_uninstall_delegate_without_paths() {
        // 防：卸载/停止被误加上 Worker 路径前置条件，Linux/macOS 无本地 Worker 时操作不可用（#58）。
        let ops = Arc::new(RecordingOps::default());
        let manager = ServiceManager::new(
            Arc::new(FixedProbe(ServiceRuntimeState::Running)),
            ops.clone(),
            None,
            None,
        );

        assert_eq!(manager.stop().status, crate::ServiceCommandStatus::Success);
        assert_eq!(
            manager.uninstall().status,
            crate::ServiceCommandStatus::Success
        );
        assert_eq!(
            ops.calls.lock().unwrap().as_slice(),
            ["stop".to_string(), "uninstall".to_string()].as_slice()
        );
    }
}
