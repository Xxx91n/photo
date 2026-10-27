use crate::command::{
    ServiceCommandResult, ServiceCommandStatus, result_from_output, translate_exit_code,
};
use crate::manager::{ServiceOps, ServiceProbe};
use crate::parse::parse_sc_query;
use crate::process::{CommandOutput, ProcessRunner};
use crate::state::ServiceRuntimeState;
use std::ffi::c_void;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;
use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_CANCELLED, GetLastError, HANDLE, SetLastError, WAIT_OBJECT_0, WAIT_TIMEOUT,
};
use windows_sys::Win32::Security::{
    GetTokenInformation, TOKEN_ELEVATION, TOKEN_QUERY, TokenElevation,
};
use windows_sys::Win32::System::SystemInformation::GetSystemDirectoryW;
use windows_sys::Win32::System::Threading::{
    GetCurrentProcess, GetExitCodeProcess, OpenProcessToken, TerminateProcess, WaitForSingleObject,
};
use windows_sys::Win32::UI::Shell::{SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW};
use windows_sys::Win32::UI::WindowsAndMessaging::SW_HIDE;

pub const SERVICE_NAME: &str = "PhotoPrivacyCleaner";
const PROBE_TIMEOUT: Duration = Duration::from_secs(3);
const COMMAND_TIMEOUT: Duration = Duration::from_secs(60);

/// `ShellExecuteExW` 与 `CreateProcess` 都会在当前目录搜索裸程序名；发布目录可写时，
/// 用户批准 UAC 后可能执行同目录的恶意 `sc.exe`（CWE-427）。这里用系统 API 取绝对路径。
fn sc_exe_path() -> Option<PathBuf> {
    let mut buffer = [0u16; 260];
    let len = unsafe { GetSystemDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return None;
    }
    let directory = String::from_utf16_lossy(&buffer[..len as usize]);
    Some(PathBuf::from(directory).join("sc.exe"))
}

pub struct WindowsProbe {
    runner: Arc<dyn ProcessRunner>,
}

impl WindowsProbe {
    pub fn new(runner: Arc<dyn ProcessRunner>) -> Self {
        Self { runner }
    }
}

impl ServiceProbe for WindowsProbe {
    fn state(&self) -> ServiceRuntimeState {
        let Some(sc_exe) = sc_exe_path() else {
            return ServiceRuntimeState::Unknown;
        };
        let output = self.runner.run(
            &sc_exe.to_string_lossy(),
            &["query".to_string(), SERVICE_NAME.to_string()],
            PROBE_TIMEOUT,
        );
        if output.spawn_error.is_some() || output.timed_out {
            return ServiceRuntimeState::Unknown;
        }
        parse_sc_query(output.exit_code.unwrap_or(-1), &output.stdout)
    }
}

pub trait AdminChecker: Send + Sync {
    fn is_admin(&self) -> bool;
}

#[derive(Clone, Copy, Debug, Default)]
pub struct RealAdminChecker;

impl AdminChecker for RealAdminChecker {
    fn is_admin(&self) -> bool {
        token_is_elevated()
    }
}

/// 提权执行结果：与普通子进程分开，取消 UAC 不能被折叠成普通失败。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ElevatedOutcome {
    Exited(i32),
    Cancelled,
    Failed(String),
}

pub trait ElevatedRunner: Send + Sync {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> ElevatedOutcome;
}

/// `ProcessStartInfo { Verb = "runas", UseShellExecute = true }` 的等价物：ShellExecuteExW。
#[derive(Clone, Copy, Debug, Default)]
pub struct ShellExecuteRunner;

impl ElevatedRunner for ShellExecuteRunner {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> ElevatedOutcome {
        let verb = wide("runas");
        let file = wide(program);
        let parameters = wide(&join_windows_args(args));

        let mut info: SHELLEXECUTEINFOW = unsafe { std::mem::zeroed() };
        info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        info.fMask = SEE_MASK_NOCLOSEPROCESS;
        info.lpVerb = verb.as_ptr();
        info.lpFile = file.as_ptr();
        info.lpParameters = parameters.as_ptr();
        info.nShow = SW_HIDE;

        unsafe {
            SetLastError(0);
            if ShellExecuteExW(&mut info) == 0 {
                let error = GetLastError();
                return if error == ERROR_CANCELLED {
                    ElevatedOutcome::Cancelled
                } else {
                    ElevatedOutcome::Failed(format!(
                        "failed to start elevated {program} (Win32 error {error})"
                    ))
                };
            }

            if info.hProcess.is_null() {
                return ElevatedOutcome::Failed(
                    "elevated process did not return a process handle".to_string(),
                );
            }

            let timeout_ms = timeout.as_millis().min(u128::from(u32::MAX)) as u32;
            let wait = WaitForSingleObject(info.hProcess, timeout_ms);
            if wait == WAIT_TIMEOUT {
                TerminateProcess(info.hProcess, 1);
                let _ = WaitForSingleObject(info.hProcess, 5_000);
            }

            let mut exit_code = 0u32;
            let read_exit = GetExitCodeProcess(info.hProcess, &mut exit_code) != 0;
            CloseHandle(info.hProcess);

            if wait == WAIT_TIMEOUT {
                return ElevatedOutcome::Failed(format!(
                    "elevated {program} did not exit within {timeout_ms} ms"
                ));
            }
            if wait != WAIT_OBJECT_0 || !read_exit {
                return ElevatedOutcome::Failed(format!(
                    "elevated {program} could not be waited on (WaitForSingleObject = {wait})"
                ));
            }

            ElevatedOutcome::Exited(exit_code as i32)
        }
    }
}

pub struct WindowsOps {
    runner: Arc<dyn ProcessRunner>,
    elevated: Arc<dyn ElevatedRunner>,
    admin: Arc<dyn AdminChecker>,
    probe: Arc<dyn ServiceProbe>,
}

impl WindowsOps {
    pub fn new(
        runner: Arc<dyn ProcessRunner>,
        elevated: Arc<dyn ElevatedRunner>,
        admin: Arc<dyn AdminChecker>,
        probe: Arc<dyn ServiceProbe>,
    ) -> Self {
        Self {
            runner,
            elevated,
            admin,
            probe,
        }
    }

    fn run_sc(&self, args: Vec<String>, require_admin: bool) -> ServiceCommandResult {
        let Some(sc_exe) = sc_exe_path() else {
            return ServiceCommandResult::failed(
                "Unable to resolve System32\\sc.exe for the service command",
                None,
            );
        };
        let sc_exe = sc_exe.to_string_lossy();

        if require_admin && !self.admin.is_admin() {
            return match self.elevated.run(&sc_exe, &args, COMMAND_TIMEOUT) {
                ElevatedOutcome::Exited(0) => {
                    ServiceCommandResult::success(Some(0), "Operation succeeded")
                }
                ElevatedOutcome::Exited(code) => {
                    ServiceCommandResult::failed(translate_exit_code(Some(code)), Some(code))
                }
                ElevatedOutcome::Cancelled => {
                    ServiceCommandResult::elevation_cancelled("Elevation was cancelled")
                }
                ElevatedOutcome::Failed(message) => ServiceCommandResult::failed(message, None),
            };
        }

        command_result(self.runner.run(&sc_exe, &args, COMMAND_TIMEOUT))
    }

    fn ensure_removed_before_install(&self) -> ServiceCommandResult {
        let state = self.probe.state();
        if !state.is_installed() {
            return ServiceCommandResult::success(None, "Service is not installed");
        }

        if state.is_running_like() {
            let stop = self.run_sc(sc_stop(), true);
            if stop.status == ServiceCommandStatus::ElevationCancelled {
                return stop;
            }
            if stop.status == ServiceCommandStatus::Failed && stop.exit_code != Some(1062) {
                return stop.with_friendly_message();
            }
        }

        let delete = self.run_sc(sc_delete(), true);
        if delete.status == ServiceCommandStatus::ElevationCancelled {
            return delete;
        }
        if delete.status == ServiceCommandStatus::Failed
            && !matches!(delete.exit_code, Some(1060) | Some(1072))
        {
            return delete.with_friendly_message();
        }

        ServiceCommandResult::success(None, "Service was removed")
    }
}

impl ServiceOps for WindowsOps {
    fn install(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
        let (Some(worker), Some(config)) = (worker, config) else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker and config.json must be next to the UI to install the service",
                None,
            );
        };
        if !worker.is_file() {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must exist next to the UI to install the service",
                None,
            );
        }
        if !is_worker_executable(worker) {
            return ServiceCommandResult::failed(
                "The service must be installed from PhotoPrivacyWorker.exe",
                None,
            );
        }

        let args = match build_install_arguments(worker, config) {
            Ok(args) => args,
            Err(message) => return ServiceCommandResult::failed(message, None),
        };

        let precheck = self.ensure_removed_before_install();
        if precheck.status != ServiceCommandStatus::Success {
            return precheck;
        }

        let mut create = self.run_sc(args.clone(), true);
        if create.exit_code == Some(1073) {
            // 旧 UI 的竞态兜底：清理窗口里服务又被安装回来时，再删一次再 create。
            let retry = self.ensure_removed_before_install();
            if retry.status != ServiceCommandStatus::Success {
                return retry;
            }
            create = self.run_sc(args, true);
        }

        create.with_friendly_message()
    }

    fn start(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
        let Some(worker) = worker else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must be next to the UI to start the service",
                None,
            );
        };
        if !worker.is_file() {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must exist next to the UI to start the service",
                None,
            );
        }
        if !is_worker_executable(worker) {
            return ServiceCommandResult::failed(
                "The service must target PhotoPrivacyWorker.exe",
                None,
            );
        }

        if !self.probe.state().is_installed() {
            return ServiceCommandResult::failed(translate_exit_code(Some(1060)), Some(1060));
        }

        if let Some(config) = config.filter(|path| !path.as_os_str().is_empty()) {
            let args = match build_reconfig_arguments(worker, config) {
                Ok(args) => args,
                Err(message) => return ServiceCommandResult::failed(message, None),
            };
            let reconfigured = self.run_sc(args, true);
            match reconfigured.status {
                ServiceCommandStatus::Failed => return reconfigured.with_friendly_message(),
                ServiceCommandStatus::ElevationCancelled => return reconfigured,
                _ => {}
            }
        }

        self.run_sc(sc_start(), true).with_friendly_message()
    }

    fn stop(&self) -> ServiceCommandResult {
        self.run_sc(sc_stop(), true).with_friendly_message()
    }

    fn uninstall(&self) -> ServiceCommandResult {
        if self.probe.state().is_running_like() {
            let stop = self.run_sc(sc_stop(), true);
            if stop.status == ServiceCommandStatus::ElevationCancelled {
                return stop;
            }
            if stop.status == ServiceCommandStatus::Failed && stop.exit_code != Some(1062) {
                return stop.with_friendly_message();
            }
        }

        self.run_sc(sc_delete(), true).with_friendly_message()
    }
}

fn command_result(output: CommandOutput) -> ServiceCommandResult {
    result_from_output(
        "sc.exe",
        output,
        &translate_exit_code(Some(0)),
        "Elevation was cancelled",
        |_| false,
        |output| translate_exit_code(output.exit_code),
    )
}

fn sc_stop() -> Vec<String> {
    vec!["stop".to_string(), SERVICE_NAME.to_string()]
}

fn sc_start() -> Vec<String> {
    vec!["start".to_string(), SERVICE_NAME.to_string()]
}

fn sc_delete() -> Vec<String> {
    vec!["delete".to_string(), SERVICE_NAME.to_string()]
}

pub(crate) fn build_install_arguments(worker: &Path, config: &Path) -> Result<Vec<String>, String> {
    validate_path(worker)?;
    validate_path(config)?;
    Ok(vec![
        "create".to_string(),
        SERVICE_NAME.to_string(),
        "binPath=".to_string(),
        bin_path(worker, config),
        "start=".to_string(),
        "auto".to_string(),
    ])
}

pub(crate) fn build_reconfig_arguments(
    worker: &Path,
    config: &Path,
) -> Result<Vec<String>, String> {
    validate_path(worker)?;
    validate_path(config)?;
    Ok(vec![
        "config".to_string(),
        SERVICE_NAME.to_string(),
        "binPath=".to_string(),
        bin_path(worker, config),
        "start=".to_string(),
        "auto".to_string(),
    ])
}

fn bin_path(worker: &Path, config: &Path) -> String {
    format!(
        "\"{}\" --mode service --config \"{}\"",
        worker.display(),
        config.display()
    )
}

fn validate_path(path: &Path) -> Result<(), String> {
    let raw = path.to_string_lossy();
    if raw.trim().is_empty() {
        return Err("Service path must not be empty".to_string());
    }
    if raw
        .chars()
        .any(|ch| matches!(ch, '"' | '`' | '$' | '|' | '>' | '<' | '&' | '\0'))
    {
        return Err(format!(
            "Service path contains characters that cannot be passed to sc.exe: {raw}"
        ));
    }
    if !path.is_absolute() {
        return Err(format!("Service path must be absolute: {raw}"));
    }
    Ok(())
}

fn is_worker_executable(worker: &Path) -> bool {
    worker
        .file_name()
        .map(|name| {
            name.to_string_lossy()
                .eq_ignore_ascii_case("PhotoPrivacyWorker.exe")
        })
        .unwrap_or(false)
}

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(std::iter::once(0)).collect()
}

fn join_windows_args(args: &[String]) -> String {
    args.iter()
        .map(|arg| quote_windows_arg(arg))
        .collect::<Vec<_>>()
        .join(" ")
}

/// MSVC 运行库的 argv 引用规则：反斜杠只在引号前转义，结尾反斜杠加倍。
fn quote_windows_arg(arg: &str) -> String {
    if !arg.is_empty()
        && !arg
            .chars()
            .any(|ch| matches!(ch, ' ' | '\t' | '"' | '\n' | '\u{b}'))
    {
        return arg.to_string();
    }

    let mut quoted = String::from("\"");
    let mut backslashes = 0usize;
    for ch in arg.chars() {
        match ch {
            '\\' => backslashes += 1,
            '"' => {
                quoted.push_str(&"\\".repeat(backslashes * 2 + 1));
                quoted.push('"');
                backslashes = 0;
            }
            _ => {
                quoted.push_str(&"\\".repeat(backslashes));
                backslashes = 0;
                quoted.push(ch);
            }
        }
    }
    quoted.push_str(&"\\".repeat(backslashes * 2));
    quoted.push('"');
    quoted
}

fn token_is_elevated() -> bool {
    unsafe {
        let mut token: HANDLE = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return false;
        }

        let mut elevation = TOKEN_ELEVATION { TokenIsElevated: 0 };
        let mut returned = 0u32;
        let ok = GetTokenInformation(
            token,
            TokenElevation,
            &mut elevation as *mut _ as *mut c_void,
            std::mem::size_of::<TOKEN_ELEVATION>() as u32,
            &mut returned,
        );
        CloseHandle(token);
        ok != 0 && elevation.TokenIsElevated != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;
    use std::sync::Mutex;

    #[derive(Default)]
    struct QueueRunner {
        outputs: Mutex<VecDeque<CommandOutput>>,
        calls: Mutex<Vec<Vec<String>>>,
        programs: Mutex<Vec<String>>,
    }

    impl QueueRunner {
        fn new(outputs: Vec<CommandOutput>) -> Self {
            Self {
                outputs: Mutex::new(outputs.into()),
                calls: Mutex::new(Vec::new()),
                programs: Mutex::new(Vec::new()),
            }
        }

        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.lock().unwrap().clone()
        }

        fn programs(&self) -> Vec<String> {
            self.programs.lock().unwrap().clone()
        }
    }

    impl ProcessRunner for QueueRunner {
        fn run(&self, program: &str, args: &[String], _: Duration) -> CommandOutput {
            self.programs.lock().unwrap().push(program.to_string());
            self.calls.lock().unwrap().push(args.to_vec());
            self.outputs
                .lock()
                .unwrap()
                .pop_front()
                .unwrap_or_else(|| CommandOutput::spawn_error("no scripted output"))
        }
    }

    struct FixedProbe(ServiceRuntimeState);

    impl ServiceProbe for FixedProbe {
        fn state(&self) -> ServiceRuntimeState {
            self.0
        }
    }

    struct FixedAdmin(bool);

    impl AdminChecker for FixedAdmin {
        fn is_admin(&self) -> bool {
            self.0
        }
    }

    #[derive(Default)]
    struct RecordingElevated {
        calls: Mutex<Vec<Vec<String>>>,
        programs: Mutex<Vec<String>>,
        outcome: Mutex<Option<ElevatedOutcome>>,
    }

    impl RecordingElevated {
        fn new(outcome: ElevatedOutcome) -> Self {
            Self {
                calls: Mutex::new(Vec::new()),
                programs: Mutex::new(Vec::new()),
                outcome: Mutex::new(Some(outcome)),
            }
        }

        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.lock().unwrap().clone()
        }

        fn programs(&self) -> Vec<String> {
            self.programs.lock().unwrap().clone()
        }
    }

    impl ElevatedRunner for RecordingElevated {
        fn run(&self, program: &str, args: &[String], _: Duration) -> ElevatedOutcome {
            self.programs.lock().unwrap().push(program.to_string());
            self.calls.lock().unwrap().push(args.to_vec());
            self.outcome
                .lock()
                .unwrap()
                .clone()
                .unwrap_or_else(|| ElevatedOutcome::Failed("no scripted outcome".to_string()))
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

    fn temp_worker() -> std::path::PathBuf {
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let seq = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let dir =
            std::env::temp_dir().join(format!("photoprivacy-windows-{}-{seq}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("PhotoPrivacyWorker.exe");
        std::fs::write(&path, b"").unwrap();
        path
    }

    fn cleanup_worker(worker: &Path) {
        let _ = std::fs::remove_file(worker);
        if let Some(dir) = worker.parent() {
            let _ = std::fs::remove_dir(dir);
        }
    }

    fn ops(
        state: ServiceRuntimeState,
        outputs: Vec<CommandOutput>,
        admin: bool,
        elevated: RecordingElevated,
    ) -> (WindowsOps, Arc<QueueRunner>, Arc<RecordingElevated>) {
        let runner = Arc::new(QueueRunner::new(outputs));
        let elevated = Arc::new(elevated);
        let ops = WindowsOps::new(
            runner.clone(),
            elevated.clone(),
            Arc::new(FixedAdmin(admin)),
            Arc::new(FixedProbe(state)),
        );
        (ops, runner, elevated)
    }

    #[test]
    fn install_arguments_match_old_sc_command() {
        // 防：binPath 的引号层级漂移，Worker 收到被截断的 --config 参数（#58，旧 ServiceManagerTests）。
        let args = build_install_arguments(
            Path::new(r"C:\Program Files\Photo Privacy\PhotoPrivacyWorker.exe"),
            Path::new(r"D:\cfg path\config.json"),
        )
        .unwrap();

        assert_eq!(
            args,
            vec![
                "create",
                "PhotoPrivacyCleaner",
                "binPath=",
                "\"C:\\Program Files\\Photo Privacy\\PhotoPrivacyWorker.exe\" --mode service --config \"D:\\cfg path\\config.json\"",
                "start=",
                "auto",
            ]
        );
    }

    #[test]
    fn install_rejects_unsafe_or_relative_paths() {
        // 防：路径里的引号/管道符被拼进 sc 命令行，形成额外参数（#58，旧 BuildReconfigArguments 校验）。
        assert!(
            build_install_arguments(
                Path::new(r"C:\app\PhotoPrivacyWorker.exe"),
                Path::new(r#"C:\cfg\"x"\config.json"#)
            )
            .is_err()
        );
        assert!(
            build_install_arguments(
                Path::new(r"relative\PhotoPrivacyWorker.exe"),
                Path::new(r"C:\cfg\config.json")
            )
            .is_err()
        );
    }

    #[test]
    fn worker_executable_check_is_case_insensitive() {
        // 防：大小写差异让合法 Worker 被拒绝，或任意 exe 被安装成服务（#58）。
        assert!(is_worker_executable(Path::new(
            r"C:\app\PhotoPrivacyWorker.exe"
        )));
        assert!(is_worker_executable(Path::new(
            r"C:\app\photoprivacyworker.EXE"
        )));
        assert!(!is_worker_executable(Path::new(r"C:\app\PhotoPrivacy.exe")));
    }

    #[test]
    fn sc_exe_resolves_to_absolute_system32_path() {
        // 防：裸名 sc.exe 在当前目录可写时被同名二进制劫持（CWE-427，#58 CodeRabbit）。
        let path = sc_exe_path().expect("System32 should resolve");
        assert!(path.is_absolute());
        assert!(
            path.to_string_lossy()
                .to_ascii_lowercase()
                .ends_with("\\system32\\sc.exe")
        );
    }

    #[test]
    fn probe_and_ops_use_absolute_sc_path() {
        // 防：探测或普通/提权操作回退到裸名 sc.exe，重新引入当前目录劫持面（#58）。
        let runner = Arc::new(QueueRunner::new(vec![CommandOutput {
            exit_code: Some(0),
            stdout: "        STATE              : 4  RUNNING\n".to_string(),
            ..CommandOutput::default()
        }]));
        let probe = WindowsProbe::new(runner.clone());
        assert_eq!(probe.state(), ServiceRuntimeState::Running);
        assert!(
            runner.programs()[0]
                .to_ascii_lowercase()
                .ends_with("\\system32\\sc.exe")
        );

        let (ops_elevated, runner, elevated) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
        );
        let _ = ops_elevated.uninstall();
        assert!(runner.programs().is_empty(), "非管理员时卸装应走提权路径");
        assert!(
            elevated
                .programs()
                .iter()
                .all(|program| program.to_ascii_lowercase().ends_with("\\system32\\sc.exe"))
        );

        let (ops_direct, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );
        let _ = ops_direct.uninstall();
        assert!(
            runner
                .programs()
                .iter()
                .all(|program| program.to_ascii_lowercase().ends_with("\\system32\\sc.exe"))
        );
    }

    #[test]
    fn install_stops_and_deletes_existing_service_before_create() {
        // 防：服务已运行时直接 create 得到 1073，安装按钮永久失败（#58，旧 Auto_Retry 测试）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        let calls = runner.calls();
        assert_eq!(calls.len(), 3);
        assert_eq!(calls[0], ["stop", "PhotoPrivacyCleaner"]);
        assert_eq!(calls[1], ["delete", "PhotoPrivacyCleaner"]);
        assert_eq!(calls[2][0], "create");
        cleanup_worker(&worker);
    }

    #[test]
    fn install_requires_worker_and_config_options() {
        // 防：缺少 Worker/config 上下文时仍拉起提权进程，用户只看到平台错误（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::NotInstalled,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        assert_eq!(ops.install(None, None).status, ServiceCommandStatus::Failed);
        assert_eq!(
            ops.install(
                Some(Path::new(r"C:\definitely-missing\PhotoPrivacyWorker.exe")),
                Some(Path::new(r"C:\cfg\config.json"))
            )
            .status,
            ServiceCommandStatus::Failed
        );
        assert!(runner.calls().is_empty());
    }

    #[test]
    fn install_retries_after_service_already_exists() {
        // 防：清理与 create 之间服务被重新装回时不再重试，安装停在 1073（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok(), code(1073), ok(), ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(runner.calls().len(), 6);
        cleanup_worker(&worker);
    }

    #[test]
    fn install_does_not_reprompt_after_cancelled_precheck() {
        // 防：用户取消预清理的 UAC 后再次弹 create 提权框，四态反馈丢失（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Running,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
        );
        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");

        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert_eq!(elevated.calls().len(), 1);
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn start_fails_fast_when_service_is_not_installed() {
        // 防：未安装时仍跑 sc config/start，把 1060 包成含混的失败（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::NotInstalled,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let result = ops.start(Some(worker.as_path()), None);

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(result.exit_code, Some(1060));
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn start_requires_worker_option() {
        // 防：没有 Worker 上下文时仍尝试 sc config/start，启动结果无法解释（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        assert_eq!(ops.start(None, None).status, ServiceCommandStatus::Failed);
        assert!(runner.calls().is_empty());
    }

    #[test]
    fn start_reconfigures_then_starts() {
        // 防：启动前不写回最新 Worker/config 路径，服务仍用旧配置（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Stopped,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.start(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        let calls = runner.calls();
        assert_eq!(calls.len(), 2);
        assert_eq!(calls[0][0], "config");
        assert_eq!(calls[1], ["start", "PhotoPrivacyCleaner"]);
        cleanup_worker(&worker);
    }

    #[test]
    fn uninstall_stops_running_service_before_delete() {
        // 防：运行中直接 delete 得到 1072/1055，卸载失败（#58，旧 Uninstall_Should_Stop 测试）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(
            runner.calls(),
            vec![
                vec!["stop".to_string(), "PhotoPrivacyCleaner".to_string()],
                vec!["delete".to_string(), "PhotoPrivacyCleaner".to_string()],
            ]
        );
    }

    #[test]
    fn elevation_cancelled_is_not_reported_as_failure() {
        // 防：用户取消 UAC 被归为 Failed，四态反馈退化成三态（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
        );

        let result = ops.stop();

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert!(runner.calls().is_empty());
        assert_eq!(
            elevated.calls(),
            vec![vec!["stop".to_string(), "PhotoPrivacyCleaner".to_string()]]
        );
    }

    #[test]
    fn elevated_sc_uses_runas_when_not_admin() {
        // 防：非管理员直接跑 sc.exe，权限不足（5）而不是弹提权提示（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
        );

        let worker = temp_worker();
        let result = ops.start(Some(worker.as_path()), None);

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert!(runner.calls().is_empty());
        assert_eq!(elevated.calls().len(), 1);
        assert_eq!(
            elevated.calls()[0],
            vec!["start".to_string(), "PhotoPrivacyCleaner".to_string()]
        );
        cleanup_worker(&worker);
    }

    #[test]
    fn quote_windows_arg_matches_msvc_rules() {
        // 防：提权路径把所有参数拼成一条命令行时引号/反斜杠转义错误，sc.exe 收到错误 binPath（#58）。
        assert_eq!(quote_windows_arg("simple"), "simple");
        assert_eq!(quote_windows_arg(""), "\"\"");
        assert_eq!(quote_windows_arg("a b"), "\"a b\"");
        assert_eq!(quote_windows_arg("a\"b"), "\"a\\\"b\"");
        assert_eq!(
            quote_windows_arg(r"C:\path with space\"),
            "\"C:\\path with space\\\\\""
        );
    }
}
