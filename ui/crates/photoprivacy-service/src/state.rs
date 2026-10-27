/// 与旧 UI `ServiceRuntimeState` 逐项对应；探测结果只落在这个枚举上，界面不做字符串判断。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ServiceRuntimeState {
    NotInstalled,
    Running,
    StartPending,
    PausePending,
    Paused,
    ContinuePending,
    Stopped,
    StopPending,
    Unknown,
}

impl ServiceRuntimeState {
    /// 与旧 UI `ServiceExists` 语义一致：`Unknown` 也算已安装（探测失败时不把安装按钮放出来）。
    pub fn is_installed(self) -> bool {
        !matches!(self, Self::NotInstalled)
    }

    /// 与旧 UI `ServiceUiPolicy.IsServiceRunningLike` 一致：pending/paused 都视为“运行侧”。
    pub fn is_running_like(self) -> bool {
        matches!(
            self,
            Self::Running
                | Self::StartPending
                | Self::ContinuePending
                | Self::PausePending
                | Self::Paused
        )
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::NotInstalled => "Not installed",
            Self::Running => "Running",
            Self::StartPending => "Starting…",
            Self::PausePending => "Pausing…",
            Self::Paused => "Paused",
            Self::ContinuePending => "Resuming…",
            Self::Stopped => "Stopped",
            Self::StopPending => "Stopping…",
            Self::Unknown => "Unknown",
        }
    }
}

/// 与旧 UI `ServiceButtonState` 同构；刷新按钮不在此列，始终可用。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ServiceButtonState {
    pub install: bool,
    pub uninstall: bool,
    pub start: bool,
    pub stop: bool,
}

/// 与旧 UI `ServiceUiPolicy.BuildButtonState` 的四分支逐项一致。
pub fn button_state(state: ServiceRuntimeState) -> ServiceButtonState {
    if !state.is_installed() {
        return ServiceButtonState {
            install: true,
            uninstall: false,
            start: false,
            stop: false,
        };
    }

    if state.is_running_like() {
        return ServiceButtonState {
            install: false,
            uninstall: false,
            start: false,
            stop: true,
        };
    }

    ServiceButtonState {
        install: false,
        uninstall: true,
        start: true,
        stop: false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn not_installed_only_enables_install() {
        // 防：未安装态放出启停/卸载按钮，点击后只能得到 1060 之类的失败（#58，旧 ServiceUiPolicyTests）。
        let state = button_state(ServiceRuntimeState::NotInstalled);
        assert_eq!(
            state,
            ServiceButtonState {
                install: true,
                uninstall: false,
                start: false,
                stop: false,
            }
        );
    }

    #[test]
    fn installed_stopped_enables_start_and_uninstall() {
        // 防：服务已安装但未运行时把安装按钮重新放出来，重复 create 得到 1073（#58）。
        let state = button_state(ServiceRuntimeState::Stopped);
        assert!(state.start);
        assert!(state.uninstall);
        assert!(!state.install);
        assert!(!state.stop);
    }

    #[test]
    fn running_side_only_enables_stop() {
        // 防：running/pending/paused 被当成“未运行”，运行中仍可点启动（#58）。
        for state in [
            ServiceRuntimeState::Running,
            ServiceRuntimeState::StartPending,
            ServiceRuntimeState::ContinuePending,
            ServiceRuntimeState::PausePending,
            ServiceRuntimeState::Paused,
        ] {
            let buttons = button_state(state);
            assert!(buttons.stop, "{state:?} 应允许停止");
            assert!(!buttons.start, "{state:?} 不应允许启动");
            assert!(!buttons.uninstall, "{state:?} 不应允许卸载");
            assert!(!buttons.install, "{state:?} 不应允许安装");
        }
    }

    #[test]
    fn unknown_counts_as_installed() {
        // 防：探测失败（Unknown）被当成未安装，界面放出 Install 并在已安装服务上触发 1073（#58）。
        assert!(ServiceRuntimeState::Unknown.is_installed());
        assert!(!button_state(ServiceRuntimeState::Unknown).install);
    }

    #[test]
    fn stop_pending_is_not_running_like() {
        // 防：StopPending 被归到运行侧后停止按钮仍可用，用户可重复 stop（#58，对齐旧 UI 枚举分组）。
        assert!(!ServiceRuntimeState::StopPending.is_running_like());
        assert!(button_state(ServiceRuntimeState::StopPending).start);
    }
}
