use crate::endpoints::Endpoint;
use crate::protocol::{self, MAX_RESPONSE_BYTES, Request, Response, Status, methods};
use std::fmt;
use std::io;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;
use tokio::io::AsyncWriteExt;
use tokio::time::{Instant, timeout};

#[cfg(windows)]
type Stream = tokio::net::windows::named_pipe::NamedPipeClient;
#[cfg(unix)]
type Stream = tokio::net::UnixStream;

const CONNECT_TIMEOUT: Duration = Duration::from_millis(700);
const REQUEST_TIMEOUT: Duration = Duration::from_secs(3);
const CONNECT_RETRY_INTERVAL: Duration = Duration::from_millis(25);

/// ReloadConfig 会停 watcher → 重建 bridge → 重启 watcher；旧 UI 的 3s 读超时覆盖不了，
/// 会把「正在重载」误判成 Worker 掉线。契约要求 ≥6s，这里取 10s 留余量。
// Design(#55): ReloadConfig 的读超时独立于普通请求（10s），避免热重载被 3s 超时误杀。
pub const RELOAD_CONFIG_TIMEOUT: Duration = Duration::from_secs(10);

#[derive(Debug)]
pub enum IpcError {
    Timeout,
    Connect(io::Error),
    Io(io::Error),
    Oversize,
    Protocol(serde_json::Error),
    Rejected(String),
}

impl fmt::Display for IpcError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            IpcError::Timeout => write!(f, "IPC 连接/读写超时"),
            IpcError::Connect(err) => write!(f, "连接 Worker 失败: {err}"),
            IpcError::Io(err) => write!(f, "IPC 传输错误: {err}"),
            IpcError::Oversize => write!(f, "响应超过 {MAX_RESPONSE_BYTES} 字节上限"),
            IpcError::Protocol(err) => write!(f, "响应 JSON 解析失败: {err}"),
            IpcError::Rejected(message) => write!(f, "Worker 拒绝请求: {message}"),
        }
    }
}

impl std::error::Error for IpcError {}

// Worker 服务端同一时刻只预建一个管道实例，并发连接会撞 ERROR_PIPE_BUSY，属主重建实例时还有极短的
// 「无实例」窗口（ERROR_FILE_NOT_FOUND）。
// Design(#49): 所有 IPC 请求共用一把异步锁（单飞），连接阶段对 busy/not-found 重试到连接超时。
pub struct IpcClient {
    connect_timeout: Duration,
    request_timeout: Duration,
    gate: tokio::sync::Mutex<()>,
    next_id: AtomicU64,
}

impl Default for IpcClient {
    fn default() -> Self {
        Self::new()
    }
}

impl IpcClient {
    pub fn new() -> Self {
        Self {
            connect_timeout: CONNECT_TIMEOUT,
            request_timeout: REQUEST_TIMEOUT,
            gate: tokio::sync::Mutex::new(()),
            next_id: AtomicU64::new(1),
        }
    }

    pub async fn status(&self, endpoint: &Endpoint) -> Result<(Status, Option<i32>), IpcError> {
        let response = self.request(endpoint, methods::GET_STATUS).await?;
        let version = response.v;
        let status = response.data.ok_or_else(|| {
            IpcError::Rejected(
                response
                    .message
                    .unwrap_or_else(|| "GetStatus 响应缺少 data".to_string()),
            )
        })?;
        Ok((status, version))
    }

    pub async fn is_alive(&self, endpoint: &Endpoint) -> bool {
        self.request(endpoint, methods::PING).await.is_ok()
    }

    /// 让 Worker 重新读取当前 config.json。服务模式与后台（托盘）模式走同一端点/同一方法，
    /// 差异只由调用方连接的是哪个端点决定，重载链路本身不分支。
    pub async fn reload_config(&self, endpoint: &Endpoint) -> Result<Response, IpcError> {
        self.request_with_timeout(endpoint, methods::RELOAD_CONFIG, RELOAD_CONFIG_TIMEOUT)
            .await
    }

    pub async fn request(&self, endpoint: &Endpoint, method: &str) -> Result<Response, IpcError> {
        self.request_with_timeout(endpoint, method, self.request_timeout)
            .await
    }

    async fn request_with_timeout(
        &self,
        endpoint: &Endpoint,
        method: &str,
        request_timeout: Duration,
    ) -> Result<Response, IpcError> {
        let _gate = self.gate.lock().await;
        let id = format!("ui-{}", self.next_id.fetch_add(1, Ordering::Relaxed));

        let mut stream = self.connect(endpoint).await?;
        let mut payload = serde_json::to_vec(&Request::new(method, Some(id.as_str())))
            .expect("请求序列化不应失败");
        payload.push(b'\n');

        let line = timeout(request_timeout, async {
            stream.write_all(&payload).await?;
            stream.flush().await?;
            protocol::read_bounded_line(&mut stream, MAX_RESPONSE_BYTES).await
        })
        .await
        .map_err(|_| IpcError::Timeout)?
        .map_err(IpcError::Io)?;

        let line = match line {
            Some(line) if !line.is_empty() => line,
            Some(_) => {
                return Err(IpcError::Io(io::Error::new(
                    io::ErrorKind::UnexpectedEof,
                    "Worker 关闭连接但没有响应",
                )));
            }
            None => return Err(IpcError::Oversize),
        };

        let response: Response = serde_json::from_str(&line).map_err(IpcError::Protocol)?;
        if !response.ok {
            return Err(IpcError::Rejected(
                response.message.clone().unwrap_or_default(),
            ));
        }
        Ok(response)
    }

    async fn connect(&self, endpoint: &Endpoint) -> Result<Stream, IpcError> {
        let deadline = Instant::now() + self.connect_timeout;
        loop {
            // 每次尝试都套上剩余预算：Unix 非阻塞 connect 可能等到可写（EINPROGRESS），
            // 不设上限会让一次探测挂到 OS 级连接超时，远超 700ms 的连接预算。
            let remaining = deadline.saturating_duration_since(Instant::now());
            match timeout(remaining, connect_once(endpoint)).await {
                Ok(Ok(stream)) => return Ok(stream),
                Ok(Err(err)) => {
                    let retryable = is_retryable(&err);
                    if !retryable || Instant::now() >= deadline {
                        return Err(IpcError::Connect(err));
                    }
                }
                Err(_elapsed) => return Err(IpcError::Timeout),
            }
            tokio::time::sleep(CONNECT_RETRY_INTERVAL).await;
        }
    }
}

// 旧 UI 的客户端带 `PipeOptions.CurrentUserOnly`，连接后校验管道属主 == 当前用户。该选项在服务模式下
// 自相矛盾（服务 Worker 以 LocalSystem 运行，属主是 SYSTEM，用户 UI 必然被拒）——换栈调研 §2.6 已把
// 「Windows 服务模式管道认证」登记为既有缺陷，属主边界改由服务端 DACL 承担，客户端不复制该校验。
// Design(#49): IPC 认证以 OS 身份为边界（服务端 DACL），客户端不做属主校验。
#[cfg(windows)]
async fn connect_once(endpoint: &Endpoint) -> io::Result<Stream> {
    let Endpoint::Pipe(name) = endpoint else {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "Windows 只支持命名管道端点",
        ));
    };
    tokio::net::windows::named_pipe::ClientOptions::new().open(format!(r"\\.\pipe\{name}"))
}

#[cfg(unix)]
async fn connect_once(endpoint: &Endpoint) -> io::Result<Stream> {
    let Endpoint::UnixSocket(path) = endpoint else {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "Unix 只支持 socket 端点",
        ));
    };
    Stream::connect(path).await
}

pub(crate) fn is_retryable(err: &io::Error) -> bool {
    #[cfg(windows)]
    {
        // ERROR_PIPE_BUSY = 231（实例被占用）、ERROR_FILE_NOT_FOUND = 2（属主重建实例的空窗）。
        // 与 .NET NamedPipeClientStream.Connect 一致：两者都重试，直到调用方的超时兜底。
        if matches!(err.raw_os_error(), Some(231) | Some(2)) {
            return true;
        }
    }
    matches!(
        err.kind(),
        io::ErrorKind::NotFound
            | io::ErrorKind::ConnectionRefused
            | io::ErrorKind::ConnectionReset
            | io::ErrorKind::WouldBlock
            | io::ErrorKind::TimedOut
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn busy_and_missing_endpoint_are_retryable() {
        // 防：把属主重建实例时的 ERROR_FILE_NOT_FOUND 当永久失败，恰逢空窗的唤起/心跳被判离线（#54）。
        #[cfg(windows)]
        {
            assert!(is_retryable(&io::Error::from_raw_os_error(231)));
            assert!(is_retryable(&io::Error::from_raw_os_error(2)));
            assert!(!is_retryable(&io::Error::from_raw_os_error(5)));
        }
        #[cfg(unix)]
        {
            assert!(is_retryable(&io::Error::from(io::ErrorKind::NotFound)));
            assert!(is_retryable(&io::Error::from(
                io::ErrorKind::ConnectionRefused
            )));
            assert!(!is_retryable(&io::Error::from(
                io::ErrorKind::PermissionDenied
            )));
        }
    }

    #[test]
    fn reload_config_gets_a_longer_timeout_than_regular_requests() {
        // 防：配置热重载沿用 3s 读超时，Worker 重建 watcher 期间 UI 把成功重载误报成掉线（#55）。
        assert!(RELOAD_CONFIG_TIMEOUT >= Duration::from_secs(6));
        assert!(RELOAD_CONFIG_TIMEOUT > Duration::from_secs(3));
    }
}
