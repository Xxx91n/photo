use crate::process::CommandOutput;

/// 与旧 UI `ServiceCommandStatus` 相同的四态：成功 / 跳过 / 提权取消 / 失败。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ServiceCommandStatus {
    Success,
    Skipped,
    ElevationCancelled,
    Failed,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ServiceCommandResult {
    pub status: ServiceCommandStatus,
    pub message: String,
    pub exit_code: Option<i32>,
}

impl ServiceCommandResult {
    pub fn success(exit_code: Option<i32>, message: impl Into<String>) -> Self {
        Self {
            status: ServiceCommandStatus::Success,
            message: message.into(),
            exit_code,
        }
    }

    pub fn skipped(message: impl Into<String>) -> Self {
        Self {
            status: ServiceCommandStatus::Skipped,
            message: message.into(),
            exit_code: None,
        }
    }

    pub fn elevation_cancelled(message: impl Into<String>) -> Self {
        Self {
            status: ServiceCommandStatus::ElevationCancelled,
            message: message.into(),
            exit_code: None,
        }
    }

    pub fn failed(message: impl Into<String>, exit_code: Option<i32>) -> Self {
        Self {
            status: ServiceCommandStatus::Failed,
            message: message.into(),
            exit_code,
        }
    }

    /// 与旧 UI `WithFriendlyMessage` 一致：Skipped / ElevationCancelled 保留原消息，
    /// 有 exit code 的失败替换成友好说明；没有 exit code 的失败（spawn/超时/提权）保留具体原因。
    pub fn with_friendly_message(mut self) -> Self {
        if matches!(
            self.status,
            ServiceCommandStatus::Skipped | ServiceCommandStatus::ElevationCancelled
        ) {
            return self;
        }

        if self.exit_code.is_some() {
            self.message = translate_exit_code(self.exit_code);
        }
        self
    }
}

/// 与旧 UI `ServiceManager.TranslateExitCode` 的 code 集合一致；文案待 #60 统一接入 locale。
pub fn translate_exit_code(code: Option<i32>) -> String {
    match code {
        Some(0) => "Operation succeeded".to_string(),
        Some(5) => "Access denied".to_string(),
        Some(1053) => "The service did not respond to the start request in time".to_string(),
        Some(1055) => "The service is marked for deletion".to_string(),
        Some(1056) => "The service is already running".to_string(),
        Some(1058) => "The service cannot accept control in its current state".to_string(),
        Some(1060) => "The service is not installed".to_string(),
        Some(1062) => "The service has not been started".to_string(),
        Some(1072) => "The service is marked for deletion".to_string(),
        Some(1073) => "The service already exists".to_string(),
        Some(code) => format!("Unknown error (code {code})"),
        None => "Unknown error".to_string(),
    }
}

/// 三平台共用的子进程结果映射：spawn 失败 / 超时 / 成功 / 取消 / 失败五路分流。
pub(crate) fn result_from_output(
    program: &str,
    output: CommandOutput,
    success_message: &str,
    cancel_message: &str,
    is_cancelled: impl Fn(&CommandOutput) -> bool,
    failure_message: impl Fn(&CommandOutput) -> String,
) -> ServiceCommandResult {
    if let Some(error) = output.spawn_error {
        return ServiceCommandResult::failed(format!("Failed to start {program}: {error}"), None);
    }
    if output.timed_out {
        return ServiceCommandResult::failed(
            format!("{program} did not exit within 60 seconds"),
            None,
        );
    }

    match output.exit_code {
        Some(0) => ServiceCommandResult::success(Some(0), success_message),
        Some(_) if is_cancelled(&output) => {
            ServiceCommandResult::elevation_cancelled(cancel_message)
        }
        Some(code) => ServiceCommandResult::failed(failure_message(&output), Some(code)),
        None => ServiceCommandResult::failed(failure_message(&output), None),
    }
}

#[cfg(any(unix, test))]
pub(crate) fn output_message(output: &CommandOutput, program: &str) -> String {
    let stderr = output.stderr.trim();
    if !stderr.is_empty() {
        return stderr.to_string();
    }
    let stdout = output.stdout.trim();
    if !stdout.is_empty() {
        return stdout.to_string();
    }
    format!(
        "{program} exited with code {}",
        output.exit_code.unwrap_or(-1)
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn friendly_message_matches_old_ui_exit_codes() {
        // 防：exit code 文案漂移，用户看到裸错误码而不是“服务不存在/已存在”（#58）。
        assert_eq!(translate_exit_code(Some(0)), "Operation succeeded");
        assert_eq!(translate_exit_code(Some(5)), "Access denied");
        assert_eq!(
            translate_exit_code(Some(1060)),
            "The service is not installed"
        );
        assert_eq!(
            translate_exit_code(Some(1073)),
            "The service already exists"
        );
        assert_eq!(translate_exit_code(Some(9999)), "Unknown error (code 9999)");
    }

    #[test]
    fn failures_without_exit_code_keep_their_specific_message() {
        // 防：spawn/超时/提权失败的具体原因被 "Unknown error" 覆盖（#58 CodeRabbit）。
        let result = ServiceCommandResult::failed("elevated sc.exe could not be waited on", None)
            .with_friendly_message();
        assert_eq!(result.message, "elevated sc.exe could not be waited on");
    }

    #[test]
    fn skipped_and_cancelled_keep_their_message() {
        // 防：提权取消被 friendly 文案覆盖成“未知错误”，四态在界面上退化成三态（#58）。
        let cancelled = ServiceCommandResult::elevation_cancelled("Elevation was cancelled")
            .with_friendly_message();
        assert_eq!(cancelled.status, ServiceCommandStatus::ElevationCancelled);
        assert_eq!(cancelled.message, "Elevation was cancelled");

        let skipped = ServiceCommandResult::skipped("unsupported").with_friendly_message();
        assert_eq!(skipped.status, ServiceCommandStatus::Skipped);
        assert_eq!(skipped.message, "unsupported");
    }
}
