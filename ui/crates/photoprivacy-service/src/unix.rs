use crate::command::ServiceCommandResult;
use crate::manager::{ServiceOps, ServiceProbe};
use crate::process::{CommandOutput, ProcessRunner};
use crate::state::ServiceRuntimeState;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

const PROBE_TIMEOUT: Duration = Duration::from_secs(3);
const COMMAND_TIMEOUT: Duration = Duration::from_secs(60);

#[cfg(any(target_os = "linux", test))]
mod linux {
    use super::*;
    use crate::parse::parse_systemctl_active;

    pub const SYSTEMD_SERVICE: &str = "photoprivacy";
    pub const SYSTEMD_UNIT_PATH: &str = "/etc/systemd/system/photoprivacy.service";

    pub struct LinuxProbe {
        runner: Arc<dyn ProcessRunner>,
    }

    impl LinuxProbe {
        pub fn new(runner: Arc<dyn ProcessRunner>) -> Self {
            Self { runner }
        }
    }

    impl ServiceProbe for LinuxProbe {
        fn state(&self) -> ServiceRuntimeState {
            let output = self.runner.run(
                "systemctl",
                &["is-active".to_string(), SYSTEMD_SERVICE.to_string()],
                PROBE_TIMEOUT,
            );
            if output.spawn_error.is_some() || output.timed_out {
                return ServiceRuntimeState::Unknown;
            }
            parse_systemctl_active(output.exit_code.unwrap_or(-1), &output.stdout)
        }
    }

    pub struct LinuxOps {
        runner: Arc<dyn ProcessRunner>,
        script_path: PathBuf,
    }

    impl LinuxOps {
        pub fn new(runner: Arc<dyn ProcessRunner>, script_path: PathBuf) -> Self {
            Self {
                runner,
                script_path,
            }
        }

        fn run_pkexec(&self, args: Vec<String>, success_message: &str) -> ServiceCommandResult {
            let output = self.runner.run("pkexec", &args, COMMAND_TIMEOUT);
            elevated_result(output, success_message)
        }
    }

    impl ServiceOps for LinuxOps {
        fn install(&self, _: &Path, _: &Path) -> ServiceCommandResult {
            // 旧 UI 的 SystemdCommandExecutor 忽略调用方路径，统一走发布包内的安装脚本。
            if !self.script_path.is_file() {
                return ServiceCommandResult::failed(
                    format!("install script not found: {}", self.script_path.display()),
                    None,
                );
            }

            let args = vec![
                "sh".to_string(),
                self.script_path.to_string_lossy().into_owned(),
            ];
            self.run_pkexec(args, "systemd service installed")
        }

        fn start(&self, _: &Path, _: Option<&Path>) -> ServiceCommandResult {
            let args = vec![
                "systemctl".to_string(),
                "start".to_string(),
                SYSTEMD_SERVICE.to_string(),
            ];
            self.run_pkexec(args, "systemd service started")
        }

        fn stop(&self) -> ServiceCommandResult {
            let args = vec![
                "systemctl".to_string(),
                "stop".to_string(),
                SYSTEMD_SERVICE.to_string(),
            ];
            self.run_pkexec(args, "systemd service stopped")
        }

        fn uninstall(&self) -> ServiceCommandResult {
            let command = format!(
                "systemctl stop {SYSTEMD_SERVICE} 2>/dev/null; \
                 systemctl disable {SYSTEMD_SERVICE} 2>/dev/null; \
                 rm -f {SYSTEMD_UNIT_PATH}; \
                 systemctl daemon-reload"
            );
            let args = vec!["sh".to_string(), "-c".to_string(), command];
            self.run_pkexec(args, "systemd service uninstalled")
        }
    }

    fn elevated_result(output: CommandOutput, success_message: &str) -> ServiceCommandResult {
        if let Some(error) = output.spawn_error {
            return ServiceCommandResult::failed(format!("pkexec is not available: {error}"), None);
        }
        if output.timed_out {
            return ServiceCommandResult::failed("pkexec did not exit within 60 seconds", None);
        }

        match output.exit_code {
            Some(0) => ServiceCommandResult::success(Some(0), success_message),
            // pkexec 的 127 也可能是“程序不存在”；只有授权失败/取消才算提权取消。
            Some(127) if !output.stderr.to_ascii_lowercase().contains("not found") => {
                ServiceCommandResult::elevation_cancelled(
                    "Elevation was cancelled or not authorized",
                )
            }
            Some(126) => ServiceCommandResult::elevation_cancelled(
                "Elevation was cancelled or not authorized",
            ),
            Some(code) => ServiceCommandResult::failed(command_message(&output), Some(code)),
            None => ServiceCommandResult::failed(command_message(&output), None),
        }
    }

    fn command_message(output: &CommandOutput) -> String {
        let stderr = output.stderr.trim();
        if !stderr.is_empty() {
            return stderr.to_string();
        }
        let stdout = output.stdout.trim();
        if !stdout.is_empty() {
            return stdout.to_string();
        }
        format!("pkexec exited with code {}", output.exit_code.unwrap_or(-1))
    }
}

#[cfg(any(target_os = "macos", test))]
mod macos {
    use super::*;
    use crate::parse::parse_launchctl_list;

    pub const LAUNCHD_LABEL: &str = "com.photoprivacy.cleaner";
    pub const LAUNCHD_PLIST: &str = "/Library/LaunchDaemons/com.photoprivacy.cleaner.plist";

    pub struct MacProbe {
        runner: Arc<dyn ProcessRunner>,
    }

    impl MacProbe {
        pub fn new(runner: Arc<dyn ProcessRunner>) -> Self {
            Self { runner }
        }
    }

    impl ServiceProbe for MacProbe {
        fn state(&self) -> ServiceRuntimeState {
            let output = self.runner.run(
                "launchctl",
                &["list".to_string(), LAUNCHD_LABEL.to_string()],
                PROBE_TIMEOUT,
            );
            if output.spawn_error.is_some() || output.timed_out {
                return ServiceRuntimeState::Unknown;
            }
            parse_launchctl_list(output.exit_code.unwrap_or(-1), &output.stdout)
        }
    }

    pub struct MacOps {
        runner: Arc<dyn ProcessRunner>,
        script_path: PathBuf,
    }

    impl MacOps {
        pub fn new(runner: Arc<dyn ProcessRunner>, script_path: PathBuf) -> Self {
            Self {
                runner,
                script_path,
            }
        }

        fn run_admin_shell(&self, command: &str, success_message: &str) -> ServiceCommandResult {
            let script = admin_shell_script(command);
            let args = vec!["-e".to_string(), script];
            let output = self.runner.run("osascript", &args, COMMAND_TIMEOUT);
            macos_result(output, success_message)
        }
    }

    impl ServiceOps for MacOps {
        fn install(&self, _: &Path, _: &Path) -> ServiceCommandResult {
            if !self.script_path.is_file() {
                return ServiceCommandResult::failed(
                    format!("install script not found: {}", self.script_path.display()),
                    None,
                );
            }

            let command = format!("sh {}", shell_quote(&self.script_path.to_string_lossy()));
            self.run_admin_shell(&command, "launchd service installed")
        }

        fn start(&self, _: &Path, _: Option<&Path>) -> ServiceCommandResult {
            self.run_admin_shell(
                &format!("launchctl load -w {LAUNCHD_PLIST}"),
                "launchd service started",
            )
        }

        fn stop(&self) -> ServiceCommandResult {
            self.run_admin_shell(
                &format!("launchctl unload -w {LAUNCHD_PLIST}"),
                "launchd service stopped",
            )
        }

        fn uninstall(&self) -> ServiceCommandResult {
            self.run_admin_shell(
                &format!("launchctl unload -w {LAUNCHD_PLIST} 2>/dev/null; rm -f {LAUNCHD_PLIST}"),
                "launchd service uninstalled",
            )
        }
    }

    fn macos_result(output: CommandOutput, success_message: &str) -> ServiceCommandResult {
        if let Some(error) = output.spawn_error {
            return ServiceCommandResult::failed(
                format!("osascript is not available: {error}"),
                None,
            );
        }
        if output.timed_out {
            return ServiceCommandResult::failed("osascript did not exit within 60 seconds", None);
        }

        match output.exit_code {
            Some(0) => ServiceCommandResult::success(Some(0), success_message),
            Some(_) if is_cancelled(&output) => {
                ServiceCommandResult::elevation_cancelled("Elevation was cancelled")
            }
            Some(code) => ServiceCommandResult::failed(command_message(&output), Some(code)),
            None => ServiceCommandResult::failed(command_message(&output), None),
        }
    }

    fn is_cancelled(output: &CommandOutput) -> bool {
        let text = format!("{} {}", output.stdout, output.stderr).to_ascii_lowercase();
        text.contains("user canceled")
            || text.contains("user cancelled")
            || text.contains("(-128)")
            || text.contains("-128")
    }

    fn command_message(output: &CommandOutput) -> String {
        let stderr = output.stderr.trim();
        if !stderr.is_empty() {
            return stderr.to_string();
        }
        let stdout = output.stdout.trim();
        if !stdout.is_empty() {
            return stdout.to_string();
        }
        format!(
            "osascript exited with code {}",
            output.exit_code.unwrap_or(-1)
        )
    }

    pub(super) fn admin_shell_script(command: &str) -> String {
        format!(
            "do shell script \"{}\" with administrator privileges",
            applescript_escape(command)
        )
    }

    fn applescript_escape(value: &str) -> String {
        value.replace('\\', "\\\\").replace('"', "\\\"")
    }

    pub(super) fn shell_quote(value: &str) -> String {
        format!("'{}'", value.replace('\'', "'\\''"))
    }
}

#[cfg(target_os = "linux")]
pub use linux::{LinuxOps, LinuxProbe, SYSTEMD_SERVICE, SYSTEMD_UNIT_PATH};
#[cfg(target_os = "macos")]
pub use macos::{LAUNCHD_LABEL, LAUNCHD_PLIST, MacOps, MacProbe};

#[cfg(test)]
mod tests {
    use super::*;
    use crate::command::ServiceCommandStatus;
    use std::collections::VecDeque;
    use std::sync::Mutex;

    #[derive(Default)]
    struct QueueRunner {
        program: String,
        outputs: Mutex<VecDeque<CommandOutput>>,
        calls: Mutex<Vec<Vec<String>>>,
    }

    impl QueueRunner {
        fn new(program: &str, outputs: Vec<CommandOutput>) -> Self {
            Self {
                program: program.to_string(),
                outputs: Mutex::new(outputs.into()),
                calls: Mutex::new(Vec::new()),
            }
        }

        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.lock().unwrap().clone()
        }
    }

    impl ProcessRunner for QueueRunner {
        fn run(&self, program: &str, args: &[String], _: Duration) -> CommandOutput {
            assert_eq!(program, self.program, "unexpected helper program");
            self.calls.lock().unwrap().push(args.to_vec());
            self.outputs
                .lock()
                .unwrap()
                .pop_front()
                .unwrap_or_else(|| CommandOutput::spawn_error("no scripted output"))
        }
    }

    fn ok() -> CommandOutput {
        CommandOutput {
            exit_code: Some(0),
            ..CommandOutput::default()
        }
    }

    fn code(code: i32) -> CommandOutput {
        CommandOutput {
            exit_code: Some(code),
            ..CommandOutput::default()
        }
    }

    fn temp_script(tag: &str) -> PathBuf {
        // 测试线程名在 Windows 上含 `::`，不能直接当文件名；用进程内序号保证唯一。
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let seq = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let path = std::env::temp_dir().join(format!(
            "photoprivacy-{tag}-{}-{seq}.sh",
            std::process::id()
        ));
        std::fs::write(&path, "#!/bin/sh\nexit 0\n").unwrap();
        path
    }

    #[test]
    fn linux_probe_uses_systemctl_is_active() {
        // 防：Linux 探测命令/服务名漂移，服务态永远 Unknown（#58，旧 SystemdStateProbe）。
        let runner = Arc::new(QueueRunner::new("systemctl", vec![ok()]));
        let probe = linux::LinuxProbe::new(runner.clone());

        assert_eq!(probe.state(), ServiceRuntimeState::Running);
        assert_eq!(
            runner.calls(),
            vec![vec!["is-active".to_string(), "photoprivacy".to_string()]]
        );
    }

    #[test]
    fn macos_probe_uses_launchctl_list() {
        // 防：macOS 探测 label/命令漂移，服务态永远 Unknown（#58，旧 LaunchdStateProbe）。
        let runner = Arc::new(QueueRunner::new(
            "launchctl",
            vec![CommandOutput {
                exit_code: Some(0),
                stdout: "{\n  \"PID\" = 4321;\n}".to_string(),
                ..CommandOutput::default()
            }],
        ));
        let probe = macos::MacProbe::new(runner.clone());

        assert_eq!(probe.state(), ServiceRuntimeState::Running);
        assert_eq!(
            runner.calls(),
            vec![vec![
                "list".to_string(),
                "com.photoprivacy.cleaner".to_string()
            ]]
        );
    }

    #[test]
    fn linux_pkexec_missing_program_is_a_failure() {
        // 防：pkexec 的 127（程序不存在）被当成“用户取消”，给用户错误的操作提示（#58）。
        let runner = Arc::new(QueueRunner::new(
            "pkexec",
            vec![CommandOutput {
                exit_code: Some(127),
                stderr: "Error: program 'systemctl' not found".to_string(),
                ..CommandOutput::default()
            }],
        ));
        let ops = linux::LinuxOps::new(runner, PathBuf::from("/missing/script.sh"));

        let result = ops.stop();

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(result.exit_code, Some(127));
    }

    #[test]
    fn linux_install_runs_script_through_pkexec() {
        // 防：安装绕过提权直接执行脚本，普通用户永远装不上 systemd 服务（#58，旧 SystemdCommandExecutor）。
        let runner = Arc::new(QueueRunner::new("pkexec", vec![ok()]));
        let script = temp_script("install");
        let ops = linux::LinuxOps::new(runner.clone(), script.clone());

        let result = ops.install(Path::new("unused"), Path::new("unused"));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(
            runner.calls(),
            vec![vec!["sh".to_string(), script.display().to_string()]]
        );
        let _ = std::fs::remove_file(script);
    }

    #[test]
    fn linux_start_and_uninstall_use_systemctl_commands() {
        // 防：启停/卸载命令与服务名漂移，操作打不到 photoprivacy unit（#58）。
        let runner = Arc::new(QueueRunner::new("pkexec", vec![ok(), ok(), ok()]));
        let ops = linux::LinuxOps::new(runner.clone(), PathBuf::from("/missing/script.sh"));

        assert_eq!(
            ops.start(Path::new("w"), None).status,
            ServiceCommandStatus::Success
        );
        assert_eq!(ops.stop().status, ServiceCommandStatus::Success);
        assert_eq!(ops.uninstall().status, ServiceCommandStatus::Success);

        let calls = runner.calls();
        assert_eq!(
            calls[0],
            vec!["systemctl", "start", "photoprivacy"]
                .into_iter()
                .map(String::from)
                .collect::<Vec<_>>()
        );
        assert_eq!(
            calls[1],
            vec!["systemctl", "stop", "photoprivacy"]
                .into_iter()
                .map(String::from)
                .collect::<Vec<_>>()
        );
        assert_eq!(calls[2][0], "sh");
        assert_eq!(calls[2][1], "-c");
        assert!(calls[2][2].contains("systemctl stop photoprivacy"));
        assert!(calls[2][2].contains("rm -f /etc/systemd/system/photoprivacy.service"));
    }

    #[test]
    fn linux_cancelled_pkexec_stays_cancelled() {
        // 防：pkexec 的 126/127 被当成普通失败，四态反馈退化（#58）。
        let runner = Arc::new(QueueRunner::new("pkexec", vec![code(126)]));
        let ops = linux::LinuxOps::new(runner, PathBuf::from("/missing/script.sh"));

        let result = ops.stop();

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
    }

    #[test]
    fn linux_install_fails_when_script_is_missing() {
        // 防：脚本缺失时仍拉起 pkexec，用户看到的是 pkexec 的含混错误（#58）。
        let runner = Arc::new(QueueRunner::new("pkexec", Vec::new()));
        let ops = linux::LinuxOps::new(runner.clone(), PathBuf::from("/missing/script.sh"));

        let result = ops.install(Path::new("w"), Path::new("c"));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert!(result.message.contains("install script not found"));
        assert!(runner.calls().is_empty());
    }

    #[test]
    fn macos_install_uses_osascript_with_admin_privileges() {
        // 防：macOS 安装没走 osascript 提权，launchd plist 写入被拒（#58，旧 LaunchdCommandExecutor）。
        let runner = Arc::new(QueueRunner::new("osascript", vec![ok()]));
        let script = temp_script("install");
        let ops = macos::MacOps::new(runner.clone(), script.clone());

        let result = ops.install(Path::new("unused"), Path::new("unused"));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        let calls = runner.calls();
        assert_eq!(calls.len(), 1);
        assert_eq!(calls[0][0], "-e");
        assert!(calls[0][1].contains("with administrator privileges"));
        // AppleScript 字符串会把路径里的反斜杠再转义一层；按最终命令比对，避免宿主路径分隔符差异。
        let quoted_path = macos::shell_quote(&script.display().to_string()).replace('\\', "\\\\");
        assert!(calls[0][1].contains(&quoted_path));
        let _ = std::fs::remove_file(script);
    }

    #[test]
    fn macos_cancelled_authorization_is_detected() {
        // 防：用户取消 macOS 授权对话框被归为 Failed（#58）。
        let runner = Arc::new(QueueRunner::new(
            "osascript",
            vec![CommandOutput {
                exit_code: Some(1),
                stderr: "execution error: User canceled. (-128)".to_string(),
                ..CommandOutput::default()
            }],
        ));
        let ops = macos::MacOps::new(runner, PathBuf::from("/missing/script.sh"));

        assert_eq!(ops.stop().status, ServiceCommandStatus::ElevationCancelled);
    }

    #[test]
    fn macos_start_and_stop_use_launchctl_plist() {
        // 防：macOS 启停命令打错 label/plist，操作对运行中的 LaunchDaemon 无效（#58）。
        let runner = Arc::new(QueueRunner::new("osascript", vec![ok(), ok()]));
        let ops = macos::MacOps::new(runner.clone(), PathBuf::from("/missing/script.sh"));

        assert_eq!(
            ops.start(Path::new("w"), None).status,
            ServiceCommandStatus::Success
        );
        assert_eq!(ops.stop().status, ServiceCommandStatus::Success);

        let calls = runner.calls();
        assert!(
            calls[0][1].contains(
                "launchctl load -w /Library/LaunchDaemons/com.photoprivacy.cleaner.plist"
            )
        );
        assert!(
            calls[1][1].contains(
                "launchctl unload -w /Library/LaunchDaemons/com.photoprivacy.cleaner.plist"
            )
        );
    }

    #[test]
    fn applescript_escaping_keeps_paths_intact() {
        // 防：脚本路径里的引号/反斜杠破坏 AppleScript 字符串，安装脚本执行到错误命令（#58）。
        assert_eq!(
            macos::admin_shell_script(r#"sh "/tmp/a b.sh""#),
            "do shell script \"sh \\\"/tmp/a b.sh\\\"\" with administrator privileges"
        );
        assert_eq!(
            macos::shell_quote("/tmp/it's here.sh"),
            "'/tmp/it'\\''s here.sh'"
        );
    }
}
