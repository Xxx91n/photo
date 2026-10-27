use crate::state::ServiceRuntimeState;

#[cfg(any(windows, test))]
const SC_STATE_WORDS: [&str; 7] = [
    "START_PENDING",
    "STOP_PENDING",
    "CONTINUE_PENDING",
    "PAUSE_PENDING",
    "RUNNING",
    "PAUSED",
    "STOPPED",
];

/// `sc.exe query <name>`：0 = 存在；1060 = 不存在；其余错误不谎报为未安装。
#[cfg(any(windows, test))]
pub fn parse_sc_query(exit_code: i32, stdout: &str) -> ServiceRuntimeState {
    match exit_code {
        0 => sc_state(stdout).unwrap_or(ServiceRuntimeState::Unknown),
        1060 => ServiceRuntimeState::NotInstalled,
        _ => ServiceRuntimeState::Unknown,
    }
}

#[cfg(any(windows, test))]
fn sc_state(stdout: &str) -> Option<ServiceRuntimeState> {
    for line in stdout.lines() {
        let upper = line.to_ascii_uppercase();
        let looks_like_state =
            upper.contains("STATE") || SC_STATE_WORDS.iter().any(|word| upper.contains(word));
        if !looks_like_state {
            continue;
        }

        // 数值优先：`STATE : 4 RUNNING` 的 4 在所有语言版本里都不变，状态词可能会被本地化。
        if let Some((_, value)) = line.split_once(':')
            && let Some(state) = state_from_numeric(value)
        {
            return Some(state);
        }

        for word in SC_STATE_WORDS {
            if upper.contains(word) {
                return Some(match word {
                    "START_PENDING" => ServiceRuntimeState::StartPending,
                    "STOP_PENDING" => ServiceRuntimeState::StopPending,
                    "CONTINUE_PENDING" => ServiceRuntimeState::ContinuePending,
                    "PAUSE_PENDING" => ServiceRuntimeState::PausePending,
                    "RUNNING" => ServiceRuntimeState::Running,
                    "PAUSED" => ServiceRuntimeState::Paused,
                    "STOPPED" => ServiceRuntimeState::Stopped,
                    _ => ServiceRuntimeState::Unknown,
                });
            }
        }
    }

    // 标签也可能被本地化（例如中文版 sc.exe 不打印英文 STATE）。此时退化为扫描
    // 「非 TYPE、冒号后为单个数字 1–7」的行；0/0x0/10/16 等其它字段不会命中。
    for line in stdout.lines() {
        let upper = line.to_ascii_uppercase();
        if upper.contains("TYPE") {
            continue;
        }
        if let Some((_, value)) = line.split_once(':')
            && !value.contains("0x")
            && !value.contains('(')
            && value
                .split_whitespace()
                .next()
                .is_some_and(|token| token.len() == 1)
            && let Some(state) = state_from_numeric(value)
        {
            return Some(state);
        }
    }

    None
}

#[cfg(any(windows, test))]
fn state_from_numeric(value: &str) -> Option<ServiceRuntimeState> {
    let code = value.split_whitespace().next()?.parse::<u8>().ok()?;
    Some(match code {
        1 => ServiceRuntimeState::Stopped,
        2 => ServiceRuntimeState::StartPending,
        3 => ServiceRuntimeState::StopPending,
        4 => ServiceRuntimeState::Running,
        5 => ServiceRuntimeState::ContinuePending,
        6 => ServiceRuntimeState::PausePending,
        7 => ServiceRuntimeState::Paused,
        _ => return None,
    })
}

/// `systemctl is-active` 的文档化退出码：0 active、3 inactive、4 unit 不存在。
#[cfg(any(target_os = "linux", test))]
pub fn parse_systemctl_active(exit_code: i32, stdout: &str) -> ServiceRuntimeState {
    match exit_code {
        0 => ServiceRuntimeState::Running,
        3 => ServiceRuntimeState::Stopped,
        4 => ServiceRuntimeState::NotInstalled,
        _ => match stdout.trim() {
            "active" => ServiceRuntimeState::Running,
            "inactive" | "failed" => ServiceRuntimeState::Stopped,
            "unknown" => ServiceRuntimeState::NotInstalled,
            _ => ServiceRuntimeState::Unknown,
        },
    }
}

/// `launchctl list <label>`：非 0 退出 = 未安装；有非零 PID = 运行中；否则已安装未运行。
#[cfg(any(target_os = "macos", test))]
pub fn parse_launchctl_list(exit_code: i32, stdout: &str) -> ServiceRuntimeState {
    if exit_code != 0 {
        return ServiceRuntimeState::NotInstalled;
    }

    for line in stdout.lines() {
        let trimmed = line.trim();

        if let Some((key, value)) = trimmed.split_once('=').or_else(|| trimmed.split_once(':'))
            && key.trim().trim_matches('"').eq_ignore_ascii_case("PID")
        {
            return state_from_pid(value);
        }

        // `launchctl list`（不带 label）的表格行：PID  Status  Label。
        let mut fields = trimmed.split_whitespace();
        if let (Some(pid), Some(_status), Some(_label)) =
            (fields.next(), fields.next(), fields.next())
            && let Ok(pid) = pid.parse::<i64>()
        {
            return if pid > 0 {
                ServiceRuntimeState::Running
            } else {
                ServiceRuntimeState::Stopped
            };
        }
    }

    ServiceRuntimeState::Stopped
}

#[cfg(any(target_os = "macos", test))]
fn state_from_pid(value: &str) -> ServiceRuntimeState {
    let raw = value
        .trim()
        .trim_end_matches(';')
        .trim_matches('"')
        .split_whitespace()
        .next()
        .unwrap_or_default();
    match raw.parse::<i64>() {
        Ok(pid) if pid > 0 => ServiceRuntimeState::Running,
        _ => ServiceRuntimeState::Stopped,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sc_query_maps_numeric_state_even_when_words_are_localized() {
        // 防：只认英文状态词；本地化 Windows 的 sc 输出把 RUNNING 翻译后状态行永久 Unknown（#58）。
        let output = "SERVICE_NAME: PhotoPrivacyCleaner\n        STATE              : 4  \u{8fd0}\u{884c}\u{4e2d}\n";
        assert_eq!(parse_sc_query(0, output), ServiceRuntimeState::Running);
    }

    #[test]
    fn sc_query_maps_state_even_when_labels_are_localized() {
        // 防：中文/其它语言版 sc.exe 把 STATE 标签也翻译后，服务态永久 Unknown（#58）。
        let output = "SERVICE_NAME: PhotoPrivacyCleaner\n        \u{72b6}\u{6001}              : 4  \u{8fd0}\u{884c}\u{4e2d}\n        \u{7c7b}\u{578b}              : 16  WIN32_OWN_PROCESS\n";
        assert_eq!(parse_sc_query(0, output), ServiceRuntimeState::Running);
    }

    #[test]
    fn sc_query_does_not_treat_exit_codes_as_states() {
        // 防：本地化标签回退把 `WIN32_EXIT_CODE : 4 (0x4)` 当成 STATE 4，把已停止服务显示为运行中（#58）。
        let output = "SERVICE_NAME: PhotoPrivacyCleaner\n        WIN32_EXIT_CODE    : 4  (0x4)\n        SERVICE_EXIT_CODE  : 4  (0x4)\n";
        assert_eq!(parse_sc_query(0, output), ServiceRuntimeState::Unknown);
    }

    #[test]
    fn sc_query_maps_all_seven_numeric_states() {
        // 防：pending/paused 被折叠成 Running/Stopped，按钮可用性与真实服务态不符（#58）。
        let cases = [
            (1, ServiceRuntimeState::Stopped),
            (2, ServiceRuntimeState::StartPending),
            (3, ServiceRuntimeState::StopPending),
            (4, ServiceRuntimeState::Running),
            (5, ServiceRuntimeState::ContinuePending),
            (6, ServiceRuntimeState::PausePending),
            (7, ServiceRuntimeState::Paused),
        ];
        for (code, expected) in cases {
            let output = format!("        STATE              : {code}  SOMETHING\n");
            assert_eq!(parse_sc_query(0, &output), expected);
        }
    }

    #[test]
    fn sc_query_missing_service_is_not_installed() {
        // 防：sc 返回 1060 时被当成 Unknown，安装按钮永远不出现（#58）。
        assert_eq!(
            parse_sc_query(1060, "The specified service does not exist"),
            ServiceRuntimeState::NotInstalled
        );
    }

    #[test]
    fn sc_query_real_output_parses_state_line() {
        // 防：真实 sc.exe 的多行缩进输出解析失败（#58）。
        let output = "SERVICE_NAME: Dnscache \r\n        TYPE               : 10  WIN32_OWN_PROCESS  \r\n        STATE              : 4  RUNNING \r\n                                (NOT_STOPPABLE, NOT_PAUSABLE, IGNORES_SHUTDOWN)\r\n        WIN32_EXIT_CODE    : 0  (0x0)\r\n";
        assert_eq!(parse_sc_query(0, output), ServiceRuntimeState::Running);
    }

    #[test]
    fn sc_query_access_denied_is_unknown_not_uninstalled() {
        // 防：探测权限不足时谎报未安装，界面放出 Install 并在已安装服务上失败（#58）。
        assert_eq!(
            parse_sc_query(5, "Access is denied."),
            ServiceRuntimeState::Unknown
        );
    }

    #[test]
    fn systemctl_active_exit_codes_match_old_probe() {
        // 防：systemctl 的 0/3/4 语义漂移，Linux 服务态判断错位（#58，旧 SystemdStateProbe）。
        assert_eq!(
            parse_systemctl_active(0, "active"),
            ServiceRuntimeState::Running
        );
        assert_eq!(
            parse_systemctl_active(3, "inactive"),
            ServiceRuntimeState::Stopped
        );
        assert_eq!(
            parse_systemctl_active(4, "unknown"),
            ServiceRuntimeState::NotInstalled
        );
        assert_eq!(
            parse_systemctl_active(1, "weird"),
            ServiceRuntimeState::Unknown
        );
    }

    #[test]
    fn launchctl_dictionary_pid_drives_running_state() {
        // 防：只按旧 UI 的 `StartsWith("PID")` 解析，launchctl 字典输出带引号/缩进时永远 Stopped（#58）。
        let running = r#"{
    "Label" = "com.photoprivacy.cleaner";
    "LastExitStatus" = 0;
    "PID" = 4321;
}"#;
        assert_eq!(
            parse_launchctl_list(0, running),
            ServiceRuntimeState::Running
        );

        let stopped = r#"{
    "Label" = "com.photoprivacy.cleaner";
    "LastExitStatus" = 0;
}"#;
        assert_eq!(
            parse_launchctl_list(0, stopped),
            ServiceRuntimeState::Stopped
        );
    }

    #[test]
    fn launchctl_missing_label_is_not_installed() {
        // 防：launchctl 非 0 退出（label 不存在）仍显示“已安装未运行”（#58）。
        assert_eq!(
            parse_launchctl_list(113, "Could not find service"),
            ServiceRuntimeState::NotInstalled
        );
    }

    #[test]
    fn launchctl_tabular_output_is_supported() {
        // 防：`launchctl list` 表格形态（PID Status Label）解析失败（#58）。
        assert_eq!(
            parse_launchctl_list(0, "PID\tStatus\tLabel\n4321\t0\tcom.photoprivacy.cleaner\n"),
            ServiceRuntimeState::Running
        );
        assert_eq!(
            parse_launchctl_list(0, "PID\tStatus\tLabel\n-\t0\tcom.photoprivacy.cleaner\n"),
            ServiceRuntimeState::Stopped
        );
    }
}
