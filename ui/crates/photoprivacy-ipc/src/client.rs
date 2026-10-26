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
            IpcError::Timeout => write!(f, "IPC 读写超时"),
            IpcError::Connect(err) => write!(f, "连接 Worker 失败: {err}"),
            IpcError::Io(err) => write!(f, "IPC 传输错误: {err}"),
            IpcError::Oversize => write!(f, "响应超过 {MAX_RESPONSE_BYTES} 字节上限"),
            IpcError::Protocol(err) => write!(f, "响应 JSON 解析失败: {err}"),
            IpcError::Rejected(message) => write!(f, "Worker 拒绝请求: {message}"),
        }
    }
}

impl std::error::Error for IpcError {}

// Design(#54): 单飞——Worker 服务端同一时刻只预建一个管道实例，并发连接会撞 ERROR_PIPE_BUSY
// 甚至极短的「无实例」窗口；因此所有请求共用一把异步锁，并在连接阶段重试 busy/not-found。
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
        matches!(
            self.request(endpoint, methods::PING).await,
            Ok(Response { ok: true, .. })
        )
    }

    pub async fn request(&self, endpoint: &Endpoint, method: &str) -> Result<Response, IpcError> {
        let _gate = self.gate.lock().await;
        let id = format!("ui-{}", self.next_id.fetch_add(1, Ordering::Relaxed));

        let mut stream = self.connect(endpoint).await?;
        let mut payload = serde_json::to_vec(&Request::new(method, Some(id.as_str())))
            .expect("请求序列化不应失败");
        payload.push(b'\n');

        let line = timeout(self.request_timeout, async {
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
            match connect_once(endpoint).await {
                Ok(stream) => return Ok(stream),
                Err(err) => {
                    let retryable = is_retryable(&err);
                    if !retryable || Instant::now() >= deadline {
                        return Err(IpcError::Connect(err));
                    }
                    tokio::time::sleep(CONNECT_RETRY_INTERVAL).await;
                }
            }
        }
    }
}

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
        // ERROR_PIPE_BUSY = 231（服务端唯一实例被占用）、ERROR_FILE_NOT_FOUND = 2（无实例窗口）。
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
    fn pipe_busy_and_missing_endpoint_are_retryable() {
        #[cfg(windows)]
        {
            assert!(is_retryable(&io::Error::from_raw_os_error(231)));
            assert!(is_retryable(&io::Error::from_raw_os_error(2)));
            assert!(!is_retryable(&io::Error::from_raw_os_error(5)));
        }
        assert!(is_retryable(&io::Error::from(
            io::ErrorKind::ConnectionRefused
        )));
        assert!(!is_retryable(&io::Error::from(
            io::ErrorKind::PermissionDenied
        )));
    }

    #[test]
    fn timeout_errors_are_displayed_with_the_contract_value() {
        let text = IpcError::Oversize.to_string();
        assert!(text.contains("65536"));
    }
}
