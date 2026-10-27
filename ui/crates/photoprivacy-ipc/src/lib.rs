//! PhotoPrivacy Worker 的 Rust IPC 客户端。
//!
//! 协议与旧 UI 的 `WorkerIpcClient` 完全同构：UTF-8 JSON 单行请求、响应无结尾换行（以连接关闭定界）、
//! 一连接一请求一响应、单飞（服务端同一时刻只预建一个管道实例）。契约出处：
//! `src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs`、`src/PhotoPrivacy.Ui/Services/WorkerIpcClient.cs`、
//! `src/PhotoPrivacy.Worker/WorkerIpcServerLoop.cs`。

pub mod client;
pub mod endpoints;
pub mod monitor;
pub mod protocol;
pub mod single_instance;

pub use client::{IpcClient, IpcError};
pub use endpoints::{
    Endpoint, HostPlatform, WorkerMode, current_home, current_platform, resolve_endpoint,
};
pub use monitor::{
    ConnectionSnapshot, ConnectionState, HEARTBEAT_INTERVAL, SERVICE_POLL_INTERVAL,
    ServiceReachability, VERSION_POLL_INTERVAL, next_backoff, spawn_monitor,
    watch_service_reachability, watch_version,
};
pub use protocol::{PROTOCOL_VERSION, Request, Response, Status};
pub use single_instance::{
    SHOW_WINDOW_MESSAGE, SHOW_WINDOW_PIPE, SingleInstanceGuard, UI_INSTANCE_NAME,
    notify_existing_instance, notify_existing_instance_named, spawn_show_window_server,
    spawn_show_window_server_named,
};
