use std::io::Read;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

/// 子进程结果。`spawn_error` / `timed_out` 与正常退出分开，调用方才能把“没跑起来”
/// 和“跑了但失败”区分成不同反馈。
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CommandOutput {
    pub exit_code: Option<i32>,
    pub stdout: String,
    pub stderr: String,
    pub timed_out: bool,
    pub spawn_error: Option<String>,
}

impl CommandOutput {
    pub fn spawn_error(error: impl Into<String>) -> Self {
        Self {
            spawn_error: Some(error.into()),
            ..Self::default()
        }
    }
}

pub trait ProcessRunner: Send + Sync {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> CommandOutput;
}

/// 真实子进程执行：stderr/stdout 用独立线程读，避免输出超过管道缓冲时把子进程顶死
/// （旧 UI `ScCommandExecutor` 踩过的同类问题）；超时后 kill 并回收。
#[derive(Clone, Copy, Debug, Default)]
pub struct SystemProcessRunner;

impl ProcessRunner for SystemProcessRunner {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> CommandOutput {
        let mut command = Command::new(program);
        command
            .args(args)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x0800_0000;
            command.creation_flags(CREATE_NO_WINDOW);
        }

        let mut child = match command.spawn() {
            Ok(child) => child,
            Err(error) => return CommandOutput::spawn_error(error.to_string()),
        };

        let stdout = child.stdout.take().expect("stdout 已设为 piped");
        let stderr = child.stderr.take().expect("stderr 已设为 piped");
        let stdout_reader = std::thread::spawn(move || read_all(stdout));
        let stderr_reader = std::thread::spawn(move || read_all(stderr));

        let deadline = Instant::now() + timeout;
        let mut timed_out = false;
        let exit_status = loop {
            match child.try_wait() {
                Ok(Some(status)) => break Some(status),
                Ok(None) => {
                    if Instant::now() >= deadline {
                        timed_out = true;
                        let _ = child.kill();
                        let _ = child.wait();
                        break None;
                    }
                    std::thread::sleep(Duration::from_millis(20));
                }
                Err(error) => {
                    let _ = child.kill();
                    let _ = child.wait();
                    return CommandOutput {
                        exit_code: None,
                        stdout: stdout_reader.join().unwrap_or_default(),
                        stderr: stderr_reader.join().unwrap_or_default(),
                        timed_out: false,
                        spawn_error: Some(error.to_string()),
                    };
                }
            }
        };

        CommandOutput {
            exit_code: exit_status.and_then(|status| status.code()),
            stdout: stdout_reader.join().unwrap_or_default(),
            stderr: stderr_reader.join().unwrap_or_default(),
            timed_out,
            spawn_error: None,
        }
    }
}

fn read_all(mut reader: impl Read) -> String {
    let mut bytes = Vec::new();
    let _ = reader.read_to_end(&mut bytes);
    String::from_utf8_lossy(&bytes).into_owned()
}
