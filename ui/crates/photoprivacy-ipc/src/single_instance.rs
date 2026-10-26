use std::time::Duration;
use tokio::io::AsyncReadExt;
use tokio::task::JoinHandle;

/// 与旧 UI `UiSingleInstance.MutexName` 逐字一致：新旧 UI 互斥，任一已运行时第二个实例只唤起不重开。
pub const UI_INSTANCE_NAME: &str = r"Global\PhotoPrivacyUi_Instance";
/// 与旧 UI `UiSingleInstance.DefaultPipeName` 一致。
pub const SHOW_WINDOW_PIPE: &str = "PhotoPrivacyUi_ShowWindow";
pub const SHOW_WINDOW_MESSAGE: &str = "SHOW_WINDOW";

pub struct SingleInstanceGuard {
    _platform: platform::Guard,
    is_owner: bool,
}

impl SingleInstanceGuard {
    pub fn acquire(name: &str) -> Self {
        let (platform, is_owner) = platform::acquire(name);
        Self {
            _platform: platform,
            is_owner,
        }
    }

    pub fn is_owner(&self) -> bool {
        self.is_owner
    }
}

#[cfg(windows)]
mod platform {
    use std::ptr;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::Foundation::{
        ERROR_ALREADY_EXISTS, GetLastError, HANDLE, SetLastError,
    };
    use windows_sys::Win32::System::Threading::CreateMutexW;

    pub struct Guard {
        handle: HANDLE,
    }

    // 沿用旧 UI 的命名 Mutex 与名称，保证新旧入口互斥；CreateMutexW 返回 ERROR_ALREADY_EXISTS
    // 即视为「已有实例」，等价 .NET Mutex.createdNew == false。
    // Design(#54): 单实例守卫与旧 UI 共用同一命名对象，第二实例只唤起不重开。
    pub fn acquire(name: &str) -> (Guard, bool) {
        let wide: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
        unsafe {
            SetLastError(0);
            let handle = CreateMutexW(ptr::null(), 1, wide.as_ptr());
            if handle.is_null() {
                // 守卫本身失败不阻塞启动：宁可多开，也不要因权限/句柄耗尽让 UI 完全起不来。
                return (Guard { handle }, true);
            }
            let already_exists = GetLastError() == ERROR_ALREADY_EXISTS;
            if already_exists {
                // 非属主不保留句柄：句柄会让命名对象在属主退出后仍存活，把「对象存在」误判成「实例仍在」。
                CloseHandle(handle);
                return (
                    Guard {
                        handle: ptr::null_mut(),
                    },
                    false,
                );
            }
            (Guard { handle }, true)
        }
    }

    impl Drop for Guard {
        fn drop(&mut self) {
            if !self.handle.is_null() {
                unsafe {
                    CloseHandle(self.handle);
                }
            }
        }
    }
}

#[cfg(unix)]
mod platform {
    use std::fs::{File, OpenOptions};
    use std::os::fd::AsRawFd;
    use std::path::PathBuf;

    pub struct Guard {
        file: Option<File>,
        path: PathBuf,
        owner: bool,
    }

    // POSIX 锁文件路径与 `SingleInstanceGuardFactory` 的规范化规则一致（temp 目录 +
    // `photoprivacy-<sanitized name>.lock`），新旧入口争同一把 flock。
    // Design(#54): Unix 单实例锁路径与旧 UI 完全一致，避免迁移期双开。
    pub fn acquire(name: &str) -> (Guard, bool) {
        let path = lock_path(name);
        let file = match OpenOptions::new()
            .create(true)
            .read(true)
            .write(true)
            .open(&path)
        {
            Ok(file) => file,
            Err(_) => {
                return (
                    Guard {
                        file: None,
                        path,
                        owner: true,
                    },
                    true,
                );
            }
        };

        let locked = unsafe { libc::flock(file.as_raw_fd(), libc::LOCK_EX | libc::LOCK_NB) } == 0;
        (
            Guard {
                file: Some(file),
                path,
                owner: locked,
            },
            locked,
        )
    }

    impl Drop for Guard {
        fn drop(&mut self) {
            if !self.owner {
                return;
            }
            if let Some(file) = &self.file {
                unsafe {
                    libc::flock(file.as_raw_fd(), libc::LOCK_UN);
                }
            }
            let _ = std::fs::remove_file(&self.path);
        }
    }

    fn lock_path(name: &str) -> PathBuf {
        std::env::temp_dir().join(format!("photoprivacy-{}.lock", sanitize_for_path(name)))
    }

    fn sanitize_for_path(name: &str) -> String {
        name.replace(['/', '\\'], "-")
            .trim_start_matches('-')
            .to_string()
    }
}

/// 第二个实例的唤起路径：Windows 命名管道 / Unix 上 .NET 命名管道的 socket 映射路径。
fn show_window_path(name: &str) -> String {
    #[cfg(windows)]
    {
        format!(r"\\.\pipe\{name}")
    }
    #[cfg(unix)]
    {
        std::env::temp_dir()
            .join(format!("CoreFxPipe_{name}"))
            .display()
            .to_string()
    }
}

/// 通知已运行的实例显示窗口；500ms 内连不上即放弃（与旧 UI 的超时一致）。
pub async fn notify_existing_instance(timeout: Duration) -> bool {
    notify_existing_instance_named(SHOW_WINDOW_PIPE, timeout).await
}

pub async fn notify_existing_instance_named(name: &str, timeout: Duration) -> bool {
    tokio::time::timeout(timeout, send_show_window(name))
        .await
        .map(|result| result.is_ok())
        .unwrap_or(false)
}

async fn send_show_window(name: &str) -> std::io::Result<()> {
    use crate::client::is_retryable;
    use tokio::io::AsyncWriteExt;

    let path = show_window_path(name);
    // .NET 的 NamedPipeClientStream.Connect 会在 ERROR_PIPE_BUSY 上重试到超时；这里同样重试，
    // 避免恰好撞上属主重建管道实例的瞬间而漏掉唤起（上限由外层 500ms 超时兜住）。
    loop {
        #[cfg(windows)]
        let opened = {
            // 只写打开：旧 UI 的唤起管道是 PipeDirection.In（仅入站），请求读权会 ACCESS_DENIED，
            // 新 UI 作为第二实例将唤不起仍在运行的旧 UI（#54 迁移期兼容）。
            tokio::net::windows::named_pipe::ClientOptions::new()
                .read(false)
                .write(true)
                .open(&path)
        };
        #[cfg(unix)]
        let opened = tokio::net::UnixStream::connect(&path).await;

        match opened {
            Ok(mut stream) => {
                stream.write_all(SHOW_WINDOW_MESSAGE.as_bytes()).await?;
                stream.write_all(b"\n").await?;
                stream.flush().await?;
                return Ok(());
            }
            Err(err) if is_retryable(&err) => {
                tokio::time::sleep(Duration::from_millis(25)).await;
            }
            Err(err) => return Err(err),
        }
    }
}

/// 监听唤起请求。回调在 tokio 运行时线程上触发，UI 侧只做「把事件投递回主线程」。
pub fn spawn_show_window_server(on_request: impl Fn() + Send + Sync + 'static) -> JoinHandle<()> {
    spawn_show_window_server_named(SHOW_WINDOW_PIPE, on_request)
}

pub fn spawn_show_window_server_named(
    name: &str,
    on_request: impl Fn() + Send + Sync + 'static,
) -> JoinHandle<()> {
    let name = name.to_string();
    tokio::spawn(async move {
        loop {
            if serve_show_window_once(&name, &on_request).await.is_err() {
                // 端点被占用或客户端中途断开：退避后重建，不能让唤起功能静默失效。
                tokio::time::sleep(Duration::from_millis(100)).await;
            }
        }
    })
}

#[cfg(windows)]
async fn serve_show_window_once(
    name: &str,
    on_request: &(impl Fn() + Send + Sync),
) -> std::io::Result<()> {
    // tokio 未暴露 SECURITY_ATTRIBUTES，唤起管道使用进程默认 DACL（当前用户 + SYSTEM + 管理员），
    // 比旧 UI 的 PipeOptions.CurrentUserOnly（仅当前用户）宽松一档；同机其他普通用户仍连不上。
    // 双向访问是客户端可连的前提：旧 UI 客户端以 PipeDirection.Out（只写）打开。
    // Design(#54): 唤起管道维持进程默认 DACL，不做 CurrentUserOnly 级别的 SDDL 收紧。
    let mut server = tokio::net::windows::named_pipe::ServerOptions::new()
        .create(format!(r"\\.\pipe\{name}"))?;
    server.connect().await?;
    read_show_window_message(&mut server, on_request).await
}

#[cfg(unix)]
async fn serve_show_window_once(
    name: &str,
    on_request: &(impl Fn() + Send + Sync),
) -> std::io::Result<()> {
    let path = show_window_path(name);
    // 仅属主会进入这里建端点，残留 socket 直接清掉（与 Worker 侧 Stale Socket Cleanup 同口径）。
    let _ = std::fs::remove_file(&path);
    let listener = tokio::net::UnixListener::bind(&path)?;
    loop {
        let (mut stream, _) = listener.accept().await?;
        let _ = read_show_window_message(&mut stream, on_request).await;
    }
}

async fn read_show_window_message(
    stream: &mut (impl tokio::io::AsyncRead + Unpin),
    on_request: &(impl Fn() + Send + Sync),
) -> std::io::Result<()> {
    let mut buffer = [0u8; 256];
    let mut filled = 0;
    while filled < buffer.len() {
        let read = stream.read(&mut buffer[filled..]).await?;
        if read == 0 {
            break;
        }
        filled += read;
        if buffer[..filled].contains(&b'\n') {
            break;
        }
    }

    let text = String::from_utf8_lossy(&buffer[..filled]);
    if text.lines().next().map(str::trim) == Some(SHOW_WINDOW_MESSAGE) {
        on_request();
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unique_name(tag: &str) -> String {
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let full = format!("PhotoPrivacyTest_{tag}_{}_{nanos}", std::process::id());
        full
    }

    #[tokio::test]
    async fn second_guard_is_not_owner() {
        // 防：非属主保留句柄导致「旧实例已退出但新实例仍被判非属主」；或锁未随 Drop 释放（#54）。
        let name = unique_name("guard");
        let first = SingleInstanceGuard::acquire(&name);
        assert!(first.is_owner());

        let second = SingleInstanceGuard::acquire(&name);
        assert!(!second.is_owner());

        drop(first);
        let third = SingleInstanceGuard::acquire(&name);
        assert!(third.is_owner());
    }

    #[tokio::test]
    async fn show_window_notification_reaches_running_instance() {
        // 防：唤起协议（SHOW_WINDOW 行 + 500ms 超时）与旧 UI 不一致，第二实例唤起静默失效（#54）。
        let name = unique_name("notify");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel();
        let server = spawn_show_window_server_named(&name, move || {
            let _ = tx.send(());
        });

        let mut delivered = false;
        for _ in 0..40 {
            if notify_existing_instance_named(&name, Duration::from_millis(500)).await
                && rx.try_recv().is_ok()
            {
                delivered = true;
                break;
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
        }
        server.abort();

        assert!(delivered, "第二实例的 SHOW_WINDOW 应送达已运行实例");
    }

    #[tokio::test]
    async fn notification_without_listener_reports_failure() {
        // 防：无实例时 notify 挂死或误报成功，第二实例卡在退出前（#54）。
        let name = unique_name("absent");
        assert!(!notify_existing_instance_named(&name, Duration::from_millis(100)).await);
    }
}
