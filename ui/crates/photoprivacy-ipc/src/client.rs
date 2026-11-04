use crate::endpoints::Endpoint;
use crate::protocol::{self, MAX_RESPONSE_BYTES, Request, Response, Status, methods};
use crate::verify::{ServerGuard, ServerPeer, UnverifiablePolicy, VerifyOutcome};
use std::fmt;
use std::io;
use std::sync::Arc;
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

// ------------------------------------------------------------------
// #7 / CWE-732：IPC 客户端请求掩码（与服务端拆位后的授权用户掩码同值，双端同构）
// ------------------------------------------------------------------

/// FILE_GENERIC_READ（winnt.h）：READ_DATA|READ_ATTRIBUTES|READ_EA|READ_CONTROL|SYNCHRONIZE。
const FILE_GENERIC_READ: u32 = 0x0012_0089;
/// FILE_GENERIC_WRITE（winnt.h）：WRITE_DATA|APPEND_DATA|WRITE_ATTRIBUTES|WRITE_EA|READ_CONTROL|SYNCHRONIZE。
/// APPEND_DATA(0x4) 与 FILE_CREATE_PIPE_INSTANCE 同值——微软 Named Pipe 文档登记的位碰撞缺陷源。
const FILE_GENERIC_WRITE: u32 = 0x0012_0116;
/// FILE_CREATE_PIPE_INSTANCE：仅服务端建例需要；客户端请求掩码必须剥除（#7），否则对拆位后的
/// 服务端 DACL 连接被 AccessCheck 整体拒绝（UI 完全连不上服务 Worker）。
const FILE_CREATE_PIPE_INSTANCE: u32 = 0x0000_0004;
/// IPC 客户端请求掩码（#7）：FILE_GENERIC_READ|WRITE 剥除 0x4 后的 0x0012019B，与 C# 侧
/// `WindowsServicePipeAcl.ServiceClientAccessMask` 同值（位级行为断言见本文件测试）。
/// pub：双端契约锚（C# 侧同为 public const），也使 Linux 编译面（无 windows 打开路径）不判死码。
pub const IPC_CLIENT_DESIRED_ACCESS: u32 =
    FILE_GENERIC_READ | (FILE_GENERIC_WRITE & !FILE_CREATE_PIPE_INSTANCE);

// 打开 flags 的本地常量（winnt.h/winbase.h 值；不 import windows_sys 是为了在非 Windows 编译面
// 也能用测试钉住组合——审查返修 M1：漏 SECURITY_SQOS_PRESENT 会让 Identification 级别失效）。
// 叶常量可达性口径（审查返修 M-R1 + CI 返修）：flags 叶常量经 pub 的 IPC_CLIENT_OPEN_FLAGS
// 初始化式可达（非 Windows lib 面不判死码）；OPEN_EXISTING 是 dwCreationDisposition 实参
// 而非 flag，仅 connect_once（cfg(windows)）使用，故单独 cfg——非 Windows 面不编译，
// Windows 面经调用点可达。
/// FILE_FLAG_OVERLAPPED（winbase.h）：tokio IOCP 注册所需。
const FILE_FLAG_OVERLAPPED: u32 = 0x4000_0000;
/// OPEN_EXISTING（winbase.h）：管道客户端打开语义（不存在即失败，不创建）；仅 windows 打开路径使用（CI 返修：非 Windows 面零引用会撞 dead_code）。
#[cfg(windows)]
const OPEN_EXISTING: u32 = 3;
/// SECURITY_SQOS_PRESENT（winnt.h）：SQOS 位生效开关——微软 CreateFileW 文档的前置条件，
/// 不带它则冒充级别不在文档化行为内（tokio 1.54.1 ClientOptions::new 默认值含此位，源码核实）。
const SECURITY_SQOS_PRESENT: u32 = 0x0010_0000;
/// SECURITY_IDENTIFICATION（winnt.h）：冒充级别=仅识别——服务端不可对客户端 ImpersonateNamedPipeClient 冒充。
const SECURITY_IDENTIFICATION: u32 = 0x0001_0000;
/// IPC 客户端打开 flags（#7）：SQOS 三件套，与 tokio ClientOptions::new 默认 flags 同构（1.54.1 源码核实）。
/// pub：与 IPC_CLIENT_DESIRED_ACCESS 同理——非 Windows 编译面（无 windows 打开路径）不判死码，
/// 三个 flags 叶常量经本常量可达。
pub const IPC_CLIENT_OPEN_FLAGS: u32 =
    SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION | FILE_FLAG_OVERLAPPED;

/// ReloadConfig 会停 watcher → 重建 bridge → 重启 watcher；旧 UI 的 3s 读超时覆盖不了，
/// 会把「正在重载」误判成 Worker 掉线。契约要求 ≥6s，这里取 10s 留余量。
// Design(#49): ReloadConfig 的读超时独立于普通请求（10s），避免热重载被 3s 超时误杀。
pub const RELOAD_CONFIG_TIMEOUT: Duration = Duration::from_secs(10);

#[derive(Debug)]
pub enum IpcError {
    Timeout,
    Connect(io::Error),
    Io(io::Error),
    Oversize,
    Protocol(serde_json::Error),
    Rejected(String),
    /// 服务端进程身份核验失败（#7 残余①）：连接已建立但对端不是期望的 Worker。
    /// 与 `Connect` 分开携带原因，供编排层归入既有的 Worker 启动失败分类。
    ServerIdentity(String),
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
            IpcError::ServerIdentity(reason) => {
                write!(f, "IPC 服务端身份核验失败: {reason}")
            }
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
    /// 服务端身份核验作用域（#7 残余①）：仅后台端点的连接被核验，服务端点不叠加（D3）。
    server_guard: Option<Arc<ServerGuard>>,
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
            server_guard: None,
        }
    }

    /// 短连接预算客户端：服务端点 3s 可达性拍用它探测；服务未安装时不必每拍耗满 700ms 重试预算。
    /// 只缩短连接预算，读仍走 3s `REQUEST_TIMEOUT`。
    pub fn with_connect_timeout(connect_timeout: Duration) -> Self {
        Self {
            connect_timeout,
            ..Self::new()
        }
    }

    /// 挂上服务端身份核验器（#7 残余①）：guard 只对其作用域端点生效，核验发生在连接建立后、
    /// 首笔业务请求之前，占用同一份连接预算。
    pub fn with_server_guard(mut self, guard: Arc<ServerGuard>) -> Self {
        self.server_guard = Some(guard);
        self
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

    /// 读取 ExifTool 版本；`GetExifToolVersion` 的 data 与 GetStatus 同形（完整 Status）。
    pub async fn exif_tool_version(&self, endpoint: &Endpoint) -> Result<String, IpcError> {
        let response = self
            .request(endpoint, methods::GET_EXIF_TOOL_VERSION)
            .await?;
        let status = response.data.ok_or_else(|| {
            IpcError::Rejected(
                response
                    .message
                    .unwrap_or_else(|| "GetExifToolVersion 响应缺少 data".to_string()),
            )
        })?;
        Ok(status.exif_tool_version)
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

    /// 拉取 Worker 侧今日审计尾部（旧 UI 的 IPC Log Pull / backfill 通道）。
    ///
    /// `Ok` 表示 Worker 可达；空数组是合法结果（今日还没有审计行），由调用方决定是否继续等待。
    pub async fn recent_logs(&self, endpoint: &Endpoint) -> Result<Vec<String>, IpcError> {
        let response = self.request(endpoint, methods::GET_RECENT_LOGS).await?;
        Ok(response.logs.map(|logs| logs.lines).unwrap_or_default())
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
        let mut transient: Option<String> = None;
        loop {
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                return Err(identity_exhausted(endpoint, transient));
            }
            // 每次尝试都套上剩余预算：Unix 非阻塞 connect 可能等到可写（EINPROGRESS），
            // 不设上限会让一次探测挂到 OS 级连接超时，远超 700ms 的连接预算。
            // #7：服务端身份核验并入同一次尝试——核验失败不属于「连接没建立」，
            // 但同受连接预算约束（AC4 超时语义不变）。
            match timeout(
                remaining,
                attempt_connect(endpoint, self.server_guard.as_deref()),
            )
            .await
            {
                Ok(ConnectAttempt::Connected(stream)) => return Ok(stream),
                Ok(ConnectAttempt::TransientIdentity(reason)) => {
                    // 核验瞬间对端进程消失：按瞬时连接失败在预算内重试，不升级为授权失败（AC5）。
                    transient = Some(reason);
                }
                Ok(ConnectAttempt::RejectedIdentity(reason)) => {
                    return Err(IpcError::ServerIdentity(reason));
                }
                Ok(ConnectAttempt::Failed(err)) => {
                    // 最近一次失败发生在连接层：此前记录的瞬时核验原因不再代表当前状态，
                    // 预算耗尽时不得把它误报成「身份核验未完成」。
                    transient = None;
                    let retryable = is_retryable(&err);
                    if !retryable || Instant::now() >= deadline {
                        return Err(IpcError::Connect(err));
                    }
                }
                Err(_elapsed) => return Err(identity_exhausted(endpoint, transient)),
            }
            tokio::time::sleep(CONNECT_RETRY_INTERVAL).await;
        }
    }
}

/// 连接预算耗尽：本轮出现过瞬时核验失败（进程已退出）时，按连接失败上报并记 warning
/// （AC2 的原因可观测 + AC5 不升级为授权失败）；否则保持原有的超时语义（AC4）。
fn identity_exhausted(endpoint: &Endpoint, transient: Option<String>) -> IpcError {
    match transient {
        Some(reason) => {
            eprintln!(
                "[photoprivacy-ipc] 服务端身份核验未完成（{}）：{reason}",
                endpoint.display()
            );
            IpcError::Connect(io::Error::new(io::ErrorKind::TimedOut, reason))
        }
        None => IpcError::Timeout,
    }
}

/// 连接尝试的结果：把「连不上」与「连上了但不是期望的 Worker」分开，
/// 后者不得被 busy/not-found 的重试语义吞掉。
enum ConnectAttempt {
    Connected(Stream),
    TransientIdentity(String),
    RejectedIdentity(String),
    Failed(io::Error),
}

/// 建立连接并在需要时核验服务端身份（#7 残余①）：核验先于任何业务数据写入。
async fn attempt_connect(endpoint: &Endpoint, guard: Option<&ServerGuard>) -> ConnectAttempt {
    let stream = match connect_once(endpoint).await {
        Ok(stream) => stream,
        Err(err) => return ConnectAttempt::Failed(err),
    };

    let Some(guard) = guard.filter(|guard| guard.applies_to(endpoint)) else {
        return ConnectAttempt::Connected(stream);
    };

    match guard.verify(&probe_server_peer(&stream)) {
        VerifyOutcome::Trusted => ConnectAttempt::Connected(stream),
        VerifyOutcome::Rejected(reason) => ConnectAttempt::RejectedIdentity(reason),
        VerifyOutcome::Transient(reason) => ConnectAttempt::TransientIdentity(reason),
        VerifyOutcome::Unverifiable(reason) => match guard.unverifiable_policy() {
            // #7 D2：能力缺口（如 macOS 未实现镜像回退）放行但留可观测 warning。
            UnverifiablePolicy::FailOpen => {
                eprintln!(
                    "[photoprivacy-ipc] 服务端身份无法核验（{}），按 fail-open 放行: {reason}",
                    endpoint.display()
                );
                ConnectAttempt::Connected(stream)
            }
            UnverifiablePolicy::FailClosed => {
                ConnectAttempt::RejectedIdentity(format!("无法核验服务端身份: {reason}"))
            }
        },
    }
}

/// 取服务端进程 PID（#7 残余①）：Windows 经管道客户端句柄查服务端 PID；
/// Unix 走内核在 connect 时绑定的对端凭据（Linux `SO_PEERCRED`、macOS `LOCAL_PEEREPID`）。
#[cfg(windows)]
fn probe_server_peer(stream: &Stream) -> ServerPeer {
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::System::Pipes::GetNamedPipeServerProcessId;

    let mut pid: u32 = 0;
    // SAFETY: stream 在本调用期间存活，管道句柄有效；pid 是本栈上的有效可写出参。
    let ok = unsafe { GetNamedPipeServerProcessId(stream.as_raw_handle(), &mut pid) };
    if ok != 0 {
        ServerPeer::Known(pid)
    } else {
        ServerPeer::QueryFailed(std::io::Error::last_os_error().to_string())
    }
}

#[cfg(unix)]
fn probe_server_peer(stream: &Stream) -> ServerPeer {
    match stream.peer_cred() {
        Ok(cred) => match cred.pid() {
            Some(pid) if pid > 0 => ServerPeer::Known(pid as u32),
            Some(_) => ServerPeer::Unavailable("对端 PID 非正".to_string()),
            None => ServerPeer::Unavailable("对端凭据不含 PID".to_string()),
        },
        Err(err) => ServerPeer::QueryFailed(err.to_string()),
    }
}

// 旧 UI 的客户端带 `PipeOptions.CurrentUserOnly`，连接后校验管道属主 == 当前用户。该选项在服务模式下
// 自相矛盾（服务 Worker 以 LocalSystem 运行，属主是 SYSTEM，用户 UI 必然被拒）——换栈调研 §2.6 已把
// 「Windows 服务模式管道认证」登记为既有缺陷，属主边界改由服务端 DACL 承担，客户端不复制该校验。
// Design(#49): IPC 认证以 OS 身份为边界（服务端 DACL），客户端不做属主校验。
//
// #7 残余①（后台管道同用户抢先建面）由本文件 + verify.rs 闭环：连接建立后、首笔业务请求之前
// 核验服务端进程身份（UI 拉起的 Worker PID 优先、进程镜像路径回退），失败即作废该连接；
// 服务管道不叠加（命名熵 + 拆位 DACL + HKLM 通告已闭环，D3）。
//
// #7 起客户端改以细粒度掩码打开（IPC_CLIENT_DESIRED_ACCESS）：tokio 1.54 的 ClientOptions 无
// desired_access 重载（#7 票内评论所述 API 不存在，docs.rs 核实），故自写原生 CreateFileW 再经
// NamedPipeClient::from_raw_handle 交还 tokio IOCP 注册；busy/not-found 重试仍由上层 connect 的
// 预算循环承担（错误码不变）。打开 flags 与 tokio ClientOptions::new 默认值同构（1.54.1 源码核实）：
// share=0、OPEN_EXISTING、SQOS 三件套 SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION|
// FILE_FLAG_OVERLAPPED——SECURITY_SQOS_PRESENT 是微软 CreateFileW 文档的前置条件，不带它则
// Identification 级别不在文档化行为内（.NET 侧 TokenImpersonationLevel.Identification 内部同样生成
// PRESENT 位）。与 C# 侧对齐：防伪服务端（预蹲者）对客户端发起 ImpersonateNamedPipeClient 冒充；
// 产品服务端从不冒充客户端，收紧不改变合法链路。
// Design(#7): 客户端请求掩码剥除 FILE_CREATE_PIPE_INSTANCE（0x4），SQOS 三件套固定 Identification 冒充级别。
#[cfg(windows)]
async fn connect_once(endpoint: &Endpoint) -> io::Result<Stream> {
    use windows_sys::Win32::Foundation::INVALID_HANDLE_VALUE;
    use windows_sys::Win32::Storage::FileSystem::CreateFileW;

    let Endpoint::Pipe(name) = endpoint else {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "Windows 只支持命名管道端点",
        ));
    };

    let path: Vec<u16> = format!(r"\\.\pipe\{name}")
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();

    // SAFETY: path 是 NUL 结尾的有效 UTF-16 缓冲；其余指针实参均为 null（无安全属性/模板，
    // 与 tokio open() 同参形态）。句柄所有权随后转移给 from_raw_handle，失败路径由其内部处置。
    let handle = unsafe {
        CreateFileW(
            path.as_ptr(),
            IPC_CLIENT_DESIRED_ACCESS,
            0,
            std::ptr::null_mut(),
            OPEN_EXISTING,
            IPC_CLIENT_OPEN_FLAGS,
            std::ptr::null_mut(),
        )
    };
    if handle == INVALID_HANDLE_VALUE {
        return Err(io::Error::last_os_error());
    }

    // SAFETY: handle 是刚由 CreateFileW 成功返回的、未被共享的所有权句柄（from_raw_handle
    // 接管关闭与 IOCP 注册；运行时缺失/IO 未启用时返回 Err 并负责句柄处置）。
    unsafe { Stream::from_raw_handle(handle) }
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

/// 连接错误是否为「服务端点存在但被管道 DACL 拒绝」：授权拒绝与端点缺失是两种截然不同的
/// 处境（#169），前者用户该找安装者加授权，后者该检查 Worker 安装。只认连接阶段错误——
/// 读写阶段的 `IpcError::Io` 与超时都不携带端点存在性信息。
pub fn is_service_access_denied(err: &IpcError) -> bool {
    let IpcError::Connect(io_err) = err else {
        return false;
    };
    #[cfg(windows)]
    {
        // ERROR_ACCESS_DENIED = 5。DACL 拒绝发生在 open 管道对象时，该错误本身即端点存在的证明；
        // io::ErrorKind::PermissionDenied 是 std 对 raw 5 的映射，两判并置防映射表漂移。
        io_err.raw_os_error() == Some(5) || io_err.kind() == io::ErrorKind::PermissionDenied
    }
    #[cfg(not(windows))]
    {
        io_err.kind() == io::ErrorKind::PermissionDenied
    }
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

    #[test]
    fn access_denied_is_distinguished_from_endpoint_missing() {
        // 防：把 DACL 拒绝（raw 5）与端点缺失（raw 2）混为一谈，未授权用户只看到 os error 2（#169）。
        #[cfg(windows)]
        {
            let denied = IpcError::Connect(io::Error::from_raw_os_error(5));
            assert!(is_service_access_denied(&denied));
            let missing = IpcError::Connect(io::Error::from_raw_os_error(2));
            assert!(!is_service_access_denied(&missing));
            let busy = IpcError::Connect(io::Error::from_raw_os_error(231));
            assert!(!is_service_access_denied(&busy));
        }
        #[cfg(unix)]
        {
            let denied = IpcError::Connect(io::Error::from(io::ErrorKind::PermissionDenied));
            assert!(is_service_access_denied(&denied));
            let missing = IpcError::Connect(io::Error::from(io::ErrorKind::NotFound));
            assert!(!is_service_access_denied(&missing));
        }
        // 超时与读写错误不携带端点存在性信息，一律不判授权拒绝。
        assert!(!is_service_access_denied(&IpcError::Timeout));
        assert!(!is_service_access_denied(&IpcError::Rejected(
            "no".to_string()
        )));
    }

    #[test]
    fn client_desired_access_drops_create_pipe_instance_bit() {
        // 防（#7/CWE-732 回潮）：客户端掩码带回 0x4 —— 对拆位后的服务端 DACL 连接被整体拒绝
        // （UI 完全连不上服务 Worker）；位级值必须与 C# 侧 WindowsServicePipeAcl.ServiceClientAccessMask
        // 同构（FILE_GENERIC_READ|WRITE 去位 = 0x0012019B）。
        assert_eq!(0, IPC_CLIENT_DESIRED_ACCESS & FILE_CREATE_PIPE_INSTANCE);
        assert_eq!(
            FILE_GENERIC_READ | (FILE_GENERIC_WRITE & !FILE_CREATE_PIPE_INSTANCE),
            IPC_CLIENT_DESIRED_ACCESS
        );
        assert_eq!(0x0012_019B, IPC_CLIENT_DESIRED_ACCESS);
    }

    #[test]
    fn client_open_flags_pin_sqos_identification_and_overlapped() {
        // 防（#7 审查返修 M1 回潮）：漏 SECURITY_SQOS_PRESENT —— Identification 级别不在微软
        // CreateFileW 文档化行为内，伪服务端（预蹲者）仍可冒充客户端；漏 FILE_FLAG_OVERLAPPED
        // ——tokio IOCP 注册失败。组合必须与 tokio ClientOptions::new 默认 flags 同构。
        assert_eq!(
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION | FILE_FLAG_OVERLAPPED,
            IPC_CLIENT_OPEN_FLAGS
        );
        assert_eq!(0x0010_0000, SECURITY_SQOS_PRESENT);
        assert_eq!(0x0001_0000, SECURITY_IDENTIFICATION);
        assert_eq!(0x4000_0000, FILE_FLAG_OVERLAPPED);
    }

    #[cfg(unix)]
    #[tokio::test]
    async fn transient_identity_failures_retry_without_becoming_authorization_failure() {
        // 防（#7 AC5）：核验瞬间对端进程消失被当成授权失败——必须在连接预算内重试，
        // 预算耗尽也只报连接失败（调用方归入「未就绪」），不能报「不可信服务端」。
        use crate::verify::ServerIdentityVerifier;

        struct AlwaysTransient;

        impl ServerIdentityVerifier for AlwaysTransient {
            fn verify(&self, _peer: &ServerPeer) -> VerifyOutcome {
                VerifyOutcome::Transient("对端进程已退出".to_string())
            }
        }

        let dir = std::env::temp_dir().join(format!("pp-t7-transient-{}", std::process::id()));
        std::fs::create_dir_all(&dir).expect("临时目录");
        let path = dir.join("worker.sock");
        let listener = tokio::net::UnixListener::bind(&path).expect("绑定测试 socket");
        let accepts = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let accepts_for_server = accepts.clone();
        let accept = tokio::spawn(async move {
            // 每次连上即断开：模拟「核验瞬间进程消失」的竞争窗口，并按次数证明发生了重试。
            while let Ok((stream, _)) = listener.accept().await {
                accepts_for_server.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                drop(stream);
            }
        });

        let endpoint = Endpoint::UnixSocket(path.clone());
        let guard = Arc::new(ServerGuard::new(
            endpoint.clone(),
            Arc::new(AlwaysTransient),
            UnverifiablePolicy::FailClosed,
        ));
        let client = IpcClient::new().with_server_guard(guard);

        let error = client
            .connect(&endpoint)
            .await
            .expect_err("核验恒为瞬时失败时应报连接失败");
        accept.abort();
        let _ = std::fs::remove_dir_all(&dir);

        assert!(
            !matches!(error, IpcError::ServerIdentity(_)),
            "瞬时核验失败不得升级为授权失败: {error:?}"
        );
        let attempts = accepts.load(std::sync::atomic::Ordering::SeqCst);
        assert!(
            attempts >= 2,
            "应在连接预算内重试，实际接受 {attempts} 次连接"
        );
    }
}
