// 协议字段与语义沿用既有 IPC 契约：方法名、请求 {method,id,v}、响应 {ok,data,logs,message,id,v}
// 都不动；可选字段为空时省略（服务端按可空字段解析），新 UI 只是同一协议的另一个客户端实现。
use serde::{Deserialize, Serialize};
use tokio::io::{AsyncRead, AsyncReadExt};

pub const PROTOCOL_VERSION: i32 = 1;

/// 与 `WorkerIpcClient.MaxResponseBytes` 一致：超过上限的响应按「无响应」丢弃。
pub const MAX_RESPONSE_BYTES: usize = 64 * 1024;

pub mod methods {
    pub const PING: &str = "Ping";
    pub const GET_STATUS: &str = "GetStatus";
    pub const GET_EXIF_TOOL_VERSION: &str = "GetExifToolVersion";
    pub const GET_RECENT_LOGS: &str = "GetRecentLogs";
}

// Design(#49): IPC JSON 行协议是新旧 UI 的唯一主缝——方法名与字段语义冻结，新能力只以 method 名扩展。
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
        // 防：照抄「按行读」直觉等换行——Worker 响应没有结尾换行，等下去会读到读超时（#54）。
        let payload = br#"{"ok":true,"v":1}"#;
        assert_eq!(
            read_all(payload, MAX_RESPONSE_BYTES).await.as_deref(),
            Some(r#"{"ok":true,"v":1}"#)
        );
    }

    #[tokio::test]
    async fn newline_terminates_line_and_drops_carriage_returns() {
        // 防：把 CR 留在 JSON 末尾导致解析失败，或读完第一条响应继续吞后续字节（#54）。
        let payload = b"{\"ok\":true}\r\n{\"ok\":false}";
        assert_eq!(
            read_all(payload, MAX_RESPONSE_BYTES).await.as_deref(),
            Some(r#"{"ok":true}"#)
        );
    }

    #[tokio::test]
    async fn empty_stream_is_an_empty_line() {
        // 防：空响应被当作错误抛出，调用方把「Worker 已关闭连接」误判为协议异常（#54）。
        assert_eq!(read_all(b"", MAX_RESPONSE_BYTES).await.as_deref(), Some(""));
    }

    #[tokio::test]
    async fn oversize_line_is_rejected() {
        // 防：超长响应被整段读入内存，或被截断后当作合法 JSON 解析（#54）。
        let payload = vec![b'x'; 32];
        assert_eq!(read_all(&payload, 16).await, None);
    }

    #[test]
    fn request_serializes_dotnet_compatible_fields() {
        // 防：请求字段名/大小写漂移，Worker 反序列化后 method 为空而回 invalid request（#54）。
        let json = serde_json::to_string(&Request::new(methods::GET_STATUS, Some("7"))).unwrap();
        assert_eq!(json, r#"{"method":"GetStatus","id":"7","v":1}"#);
        let json = serde_json::to_string(&Request::new(methods::PING, None)).unwrap();
        assert_eq!(json, r#"{"method":"Ping","v":1}"#);
    }

    #[test]
    fn response_parses_real_worker_payload() {
        // 防：字段改名后 Response 静默取默认值（如 mode 变空串），UI 显示「已连接但无版本信息」（#54）。
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
    fn response_parses_recent_log_lines() {
        // 防：GetRecentLogs 的 logs.lines 字段名漂移后被静默读成空数组，日志页 backfill 永远无内容（#57）。
        let line = r#"{"ok":true,"logs":{"lines":["{\"event_type\":\"file_detected\"}","{\"event_type\":\"file_skipped\"}"]},"v":1}"#;
        let response: Response = serde_json::from_str(line).unwrap();
        assert!(response.ok);
        let logs = response.logs.expect("logs payload");
        assert_eq!(logs.lines.len(), 2);
        assert_eq!(logs.lines[0], r#"{"event_type":"file_detected"}"#);
    }

    #[test]
    fn response_accepts_empty_recent_logs() {
        // 防：Worker 可达但今日还没有审计行时把空数组当协议错误，backfill 无法按契约进入等待（#57）。
        let response: Response =
            serde_json::from_str(r#"{"ok":true,"logs":{"lines":[]},"v":1}"#).unwrap();
        assert!(response.logs.is_some_and(|logs| logs.lines.is_empty()));
    }

    #[test]
    fn response_tolerates_unknown_and_missing_fields() {
        // 防：Worker 新增字段或省略可空字段时客户端解析失败，把正常响应当成协议错误（#54）。
        let response: Response =
            serde_json::from_str(r#"{"ok":false,"message":"unknown method","future":42}"#).unwrap();
        assert!(!response.ok);
        assert_eq!(response.message.as_deref(), Some("unknown method"));
        assert!(response.data.is_none());
    }
}
