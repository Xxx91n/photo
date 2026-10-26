// Design(#54): 协议字段与语义逐字沿用既有 IPC 契约——方法名、请求 {method,id,v}、响应
// {ok,data,logs,message,id,v} 都不动；新 UI 只是同一协议的另一个客户端实现。
use serde::{Deserialize, Serialize};
use tokio::io::{AsyncRead, AsyncReadExt};

pub const PROTOCOL_VERSION: i32 = 1;

/// 与 `WorkerIpcClient.MaxResponseBytes` 一致：超过上限的响应按「无响应」丢弃。
pub const MAX_RESPONSE_BYTES: usize = 64 * 1024;

pub mod methods {
    pub const PING: &str = "Ping";
    pub const GET_STATUS: &str = "GetStatus";
}

#[derive(Debug, Clone, Serialize)]
pub struct Request<'a> {
    pub method: &'a str,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub v: Option<i32>,
}

impl<'a> Request<'a> {
    pub fn new(method: &'a str, id: Option<&'a str>) -> Self {
        Self {
            method,
            id,
            v: Some(PROTOCOL_VERSION),
        }
    }
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct Response {
    #[serde(default)]
    pub ok: bool,
    #[serde(default)]
    pub data: Option<Status>,
    #[serde(default)]
    pub logs: Option<Logs>,
    #[serde(default)]
    pub message: Option<String>,
    #[serde(default)]
    pub id: Option<String>,
    #[serde(default)]
    pub v: Option<i32>,
}

#[derive(Debug, Clone, Default, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct Status {
    #[serde(default)]
    pub is_paused: bool,
    #[serde(default)]
    pub exif_tool_version: String,
    #[serde(default)]
    pub watch_directory: String,
    #[serde(default)]
    pub mode: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct Logs {
    #[serde(default)]
    pub lines: Vec<String>,
}

/// 读取一条响应。
///
/// 语义与 `IpcLineReader.ReadBoundedLineAsync` 对齐：LF 结束一行、行内 CR 全部丢弃、
/// 长度达到 `max_bytes` 返回 `None`（超长丢弃）、EOF 也结束一行（Worker 响应没有结尾换行，
/// 写完即关闭连接）。
pub async fn read_bounded_line<R>(
    reader: &mut R,
    max_bytes: usize,
) -> std::io::Result<Option<String>>
where
    R: AsyncRead + Unpin,
{
    let mut accumulated: Vec<u8> = Vec::new();
    let mut chunk = vec![0u8; 4096];

    loop {
        let read = reader.read(&mut chunk).await?;
        if read == 0 {
            break;
        }

        for &byte in &chunk[..read] {
            if byte == b'\n' {
                return Ok(Some(decode(&accumulated)));
            }
            if byte != b'\r' {
                accumulated.push(byte);
                if accumulated.len() >= max_bytes {
                    return Ok(None);
                }
            }
        }
    }

    Ok(Some(decode(&accumulated)))
}

fn decode(bytes: &[u8]) -> String {
    String::from_utf8_lossy(bytes).into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    async fn read_all(payload: &[u8], max_bytes: usize) -> Option<String> {
        let mut reader = payload;
        read_bounded_line(&mut reader, max_bytes).await.unwrap()
    }

    #[tokio::test]
    async fn eof_terminates_line_without_trailing_newline() {
        // Worker 服务端写完响应就关闭连接，不发换行——客户端必须读到 EOF 才停。
        let payload = br#"{"ok":true,"v":1}"#;
        assert_eq!(
            read_all(payload, MAX_RESPONSE_BYTES).await.as_deref(),
            Some(r#"{"ok":true,"v":1}"#)
        );
    }

    #[tokio::test]
    async fn newline_terminates_line_and_drops_carriage_returns() {
        let payload = b"{\"ok\":true}\r\n{\"ok\":false}";
        assert_eq!(
            read_all(payload, MAX_RESPONSE_BYTES).await.as_deref(),
            Some(r#"{"ok":true}"#)
        );
    }

    #[tokio::test]
    async fn empty_stream_is_an_empty_line() {
        assert_eq!(read_all(b"", MAX_RESPONSE_BYTES).await.as_deref(), Some(""));
    }

    #[tokio::test]
    async fn oversize_line_is_rejected() {
        let payload = vec![b'x'; 32];
        assert_eq!(read_all(&payload, 16).await, None);
    }

    #[test]
    fn request_serializes_dotnet_compatible_fields() {
        let json = serde_json::to_string(&Request::new(methods::GET_STATUS, Some("7"))).unwrap();
        assert_eq!(json, r#"{"method":"GetStatus","id":"7","v":1}"#);
        let json = serde_json::to_string(&Request::new(methods::PING, None)).unwrap();
        assert_eq!(json, r#"{"method":"Ping","v":1}"#);
    }

    #[test]
    fn response_parses_real_worker_payload() {
        let line = r#"{"ok":true,"data":{"isPaused":false,"exifToolVersion":"13.20","watchDirectory":"C:\\photos","mode":"background"},"logs":null,"message":null,"id":null,"v":1}"#;
        let response: Response = serde_json::from_str(line).unwrap();
        assert!(response.ok);
        assert_eq!(response.v, Some(1));
        let status = response.data.unwrap();
        assert_eq!(status.mode, "background");
        assert_eq!(status.exif_tool_version, "13.20");
        assert_eq!(status.watch_directory, "C:\\photos");
        assert!(!status.is_paused);
    }

    #[test]
    fn response_tolerates_unknown_and_missing_fields() {
        let response: Response =
            serde_json::from_str(r#"{"ok":false,"message":"unknown method","future":42}"#).unwrap();
        assert!(!response.ok);
        assert_eq!(response.message.as_deref(), Some("unknown method"));
        assert!(response.data.is_none());
    }
}
