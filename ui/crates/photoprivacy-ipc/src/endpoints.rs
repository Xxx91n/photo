use std::path::{Path, PathBuf};

/// 端点形态：Windows 是命名管道名，Linux/macOS 是 socket 文件路径。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Endpoint {
    Pipe(String),
    UnixSocket(PathBuf),
}

impl Endpoint {
    pub fn display(&self) -> String {
        match self {
            Endpoint::Pipe(name) => name.clone(),
            Endpoint::UnixSocket(path) => path.display().to_string(),
        }
    }

    /// 是否同一个端点：Windows 管道名不区分大小写（内核与 CreateFileW 都按不区分处理），
    /// Unix socket 路径按精确比较。用于候选去重、失败端点比对与 watch 同值抑制。
    pub fn same_as(&self, other: &Endpoint) -> bool {
        match (self, other) {
            (Endpoint::Pipe(left), Endpoint::Pipe(right)) => left.eq_ignore_ascii_case(right),
            _ => self == other,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HostPlatform {
    Windows,
    Linux,
    MacOs,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WorkerMode {
    Background,
    Service,
}

impl WorkerMode {
    pub fn label(self) -> &'static str {
        match self {
            WorkerMode::Background => "background",
            WorkerMode::Service => "service",
        }
    }
}

pub fn current_platform() -> HostPlatform {
    if cfg!(windows) {
        HostPlatform::Windows
    } else if cfg!(target_os = "macos") {
        HostPlatform::MacOs
    } else {
        HostPlatform::Linux
    }
}

pub fn current_home() -> Option<PathBuf> {
    std::env::var_os("HOME").map(PathBuf::from)
}

/// macOS 服务模式 UDS 固定落点，与 `WorkerIpcEndpointNames.MacServiceSocketPath` 逐字一致。
/// 系统级单一事实源：服务端（LaunchDaemon，服务用户）与客户端（GUI 用户）解析到同一路径，
/// 权限靠安装脚本建的 0750 目录 + socket 0660 + 服务组边界（#167）。
pub const MACOS_SERVICE_SOCKET: &str = "/Library/Application Support/photoprivacy/worker.sock";

// ------------------------------------------------------------------
// #201 Linux 背景端点：迁往 $XDG_RUNTIME_DIR
// ------------------------------------------------------------------

// 旧契约 /run/photoprivacy/worker-background.sock 对普通用户是死路：该目录只由 systemd
// RuntimeDirectory 在服务模式创建（0750 服务用户），普通用户拉起的后台 Worker mkdir 吃
// EACCES，UI 只见 ENOENT——未安装服务时回落不到后台模式（#31 轮 2026-11-04 实机观察，
// 原记录已随 #254 移入 git 历史）。票内拍板走候选 1
// （$XDG_RUNTIME_DIR）；候选 2（tmpfiles.d 预建）需 root 安装落点，「未安装服务」形态下
// 无从落地，否决。服务端点 /run/photoprivacy/worker.sock 不动（#167/#180/#188）。

/// Linux 背景模式在运行时目录下的 socket 子目录与文件名：与 C# 侧
/// `WorkerIpcEndpointNames.LinuxBackgroundSocketDirectoryName / LinuxBackgroundSocketFileName`
/// 逐字一致（Rust 无法引用 C# 常量：C# 侧测试读本文件字面量
/// 核对，下方测试钉住解析形态）。
pub const LINUX_BACKGROUND_SOCKET_DIR: &str = "photoprivacy";
pub const LINUX_BACKGROUND_SOCKET_FILE: &str = "worker-background.sock";

/// `$XDG_RUNTIME_DIR` 原值（pam_systemd/logind 按用户会话建 `/run/user/<uid>`，0700 属主
/// 用户）：Linux 背景端点的唯一来源（#201）。不裁剪空白——路径语义不 trim，有效性判定
/// 交给 `linux_background_endpoint`（与 C# 侧同构）。
pub fn current_runtime_dir() -> Option<PathBuf> {
    std::env::var_os("XDG_RUNTIME_DIR").map(PathBuf::from)
}

/// Linux 背景端点纯函数解析（#201）：运行时目录缺失/空/非绝对根路径（`has_root`，Windows
/// CI 腿对 `/run/user/1000` 同判有据）→ None——背景模式不可用，与 macOS 缺 HOME 形态对称；
/// 不回落 `/run/photoprivacy`（缺陷本体），也不回落 `/tmp`（世界可写目录的可预测路径 =
/// CWE-377 预蹲面，且跨用户端点相撞）。与 C# 侧
/// `WorkerIpcEndpointNames.ResolveLinuxBackgroundSocket` 逐字节同构。
pub fn linux_background_endpoint(runtime_dir: Option<&Path>) -> Option<Endpoint> {
    let runtime_dir = runtime_dir.filter(|dir| dir.has_root())?;
    Some(Endpoint::UnixSocket(
        runtime_dir
            .join(LINUX_BACKGROUND_SOCKET_DIR)
            .join(LINUX_BACKGROUND_SOCKET_FILE),
    ))
}

/// 服务管道静态兜底名（与 C# `WorkerIpcEndpointNames.ServicePipe` 逐字一致）。随机名通告
/// 缺失/损坏，或通告名连不上时的兜底（#7；残余⑤ 见 `service_pipe_endpoint_after_failure`）。
pub const STATIC_SERVICE_PIPE_NAME: &str = "PhotoPrivacyCleaner.Service";

// IPC 端点名保持与 `WorkerIpcEndpointNames` 逐字一致，不改协议；旧 C# UI 全程硬编码
// Windows 管道名（Linux/macOS 实际连不上），新客户端改按 OS 解析，修的是客户端缺陷而非协议。
// 端点名保持既有约定，禁止为解析方便改名。
pub fn resolve_endpoint(
    platform: HostPlatform,
    mode: WorkerMode,
    home: Option<&Path>,
) -> Option<Endpoint> {
    match platform {
        HostPlatform::Windows => Some(Endpoint::Pipe(
            match mode {
                WorkerMode::Background => "PhotoPrivacyCleaner.Background",
                WorkerMode::Service => STATIC_SERVICE_PIPE_NAME,
            }
            .to_string(),
        )),
        HostPlatform::Linux => match mode {
            WorkerMode::Background => linux_background_endpoint(current_runtime_dir().as_deref()),
            WorkerMode::Service => {
                Some(Endpoint::UnixSocket("/run/photoprivacy/worker.sock".into()))
            }
        },
        HostPlatform::MacOs => match mode {
            // 后台模式：per-user，UI 拉起的 Worker 与 UI 同用户，随 HOME 走。
            WorkerMode::Background => Some(Endpoint::UnixSocket(
                home?
                    .join("Library/Application Support/photoprivacy")
                    .join("worker-background.sock"),
            )),
            // 服务模式：系统级固定落点，与调用者身份无关（#167）。LaunchDaemon 以服务用户
            // （NFSHomeDirectory=/opt/photoprivacy）运行，GUI 用户按自身 HOME 解析只会指向
            // 本机不存在的幽灵 socket；固定路径才与服务端 `MacServiceSocket` 同一事实源。
            WorkerMode::Service => Some(Endpoint::UnixSocket(PathBuf::from(MACOS_SERVICE_SOCKET))),
        },
    }
}

// ------------------------------------------------------------------
// #7 服务管道通告名（命名熵）：Windows 服务端点的动态发现
// ------------------------------------------------------------------

// 服务端写入端（C# Worker，NamedPipeIpcTransport）：每次监听生成 crypto-random 后缀，先建首实例
// 再把「基名.32 位 hex」写入 HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName（REG_SZ，键默认仅管理员/
// SYSTEM 可写——发现通道完整性 ≥ 管道客户端集合）。随机名防静态名预蹲，HKLM 权限防非提权伪造指针。
// 与 C# 侧出处对齐的常量（WorkerIpcEndpointNames.ServicePipe / WindowsServicePipeAcl）：Rust 无法
// 引用 C# 常量，靠下方测试钉住静态回退名，通告名形态见 is_advertised_pipe_name。

/// 服务端点候选（#7）：有合法通告名时先试通告名，再试静态名；没有或非法通告名时只试静态名。
/// 静态名不是第二个服务，而是通告值陈旧时的自愈路径（残余⑤：通告写入失败且残留清除也失败，
/// 服务已回绑静态名，UI 却还拿着旧随机名）。顺序固定、去重（通告值等于静态名时不重复）。
pub(crate) fn service_pipe_candidates(advertised: Option<&str>) -> Vec<Endpoint> {
    match advertised.filter(|name| is_advertised_pipe_name(name)) {
        // Windows 管道名不区分大小写：同一名字的另一种写法不算第二个候选。
        Some(name) if !name.eq_ignore_ascii_case(STATIC_SERVICE_PIPE_NAME) => vec![
            Endpoint::Pipe(name.to_string()),
            Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.to_string()),
        ],
        _ => vec![Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.to_string())],
    }
}

/// 服务管道通告名解析：有效通告名优先；缺失/非法回退静态名（与 `resolve_endpoint` 的
/// Windows Service 名一致——旧版服务兼容、值损坏防注入）。只在 Windows 语义下调用。
pub fn service_pipe_endpoint_with_advertised(advertised: Option<&str>) -> Endpoint {
    service_pipe_candidates(advertised)
        .into_iter()
        .next()
        .unwrap_or_else(|| Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.to_string()))
}

/// 连接失败后的下一次尝试（#7 残余⑤）：刚失败的是通告名就换静态名；失败的是静态名兜底时
/// 回到通告名（服务换了新随机名后仍能接上）。没有合法通告名时候选只有静态名，返回同一个值
/// ——没有别的可试。调用方只在「端点确实不存在」的失败上调用（服务还活着只是慢一拍时换名
/// 只会打开预蹲面）；Windows 管道名不区分大小写，比较按不区分大小写处理。
pub(crate) fn service_pipe_endpoint_after_failure(
    advertised: Option<&str>,
    failed: &Endpoint,
) -> Endpoint {
    let mut candidates = service_pipe_candidates(advertised).into_iter();
    let primary = candidates
        .next()
        .unwrap_or_else(|| Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.to_string()));
    if primary.same_as(failed) {
        candidates.next().unwrap_or(primary)
    } else {
        primary
    }
}

/// 通告名合法性：非空、≤ 200 字符（`\\.\pipe\` 全名上限 256，留前缀余量）、仅 ASCII
/// 字母数字与 `._-`（本仓生成形态为「基名.32 位 hex」；防注册表值被改出路径分隔/控制字符）。
fn is_advertised_pipe_name(name: &str) -> bool {
    !name.is_empty()
        && name.len() <= 200
        && name
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'.' || b == b'_' || b == b'-')
}

/// 解析当前服务端点（#7 起动态）：Windows 读通告名（缺失/非法回退静态名）；Linux/macOS
/// 端点静态，与 `resolve_endpoint(Service)` 一致。服务重启会换随机名——调用方必须按拍重解析
/// 或失败时重解析（见 monitor/worker_connection 的接线），不得缓存长于一个探测周期。
#[cfg(windows)]
pub fn current_service_endpoint(platform: HostPlatform, home: Option<&Path>) -> Option<Endpoint> {
    if platform == HostPlatform::Windows {
        Some(service_pipe_endpoint_with_advertised(
            read_advertised_service_pipe_name().as_deref(),
        ))
    } else {
        resolve_endpoint(platform, WorkerMode::Service, home)
    }
}

/// 非 Windows 宿主的等价实现：端点静态，直接复用 `resolve_endpoint(Service)`
/// （Unix 侧 SO_PEERCRED uid 边界，无通告名机制）。
#[cfg(not(windows))]
pub fn current_service_endpoint(platform: HostPlatform, home: Option<&Path>) -> Option<Endpoint> {
    resolve_endpoint(platform, WorkerMode::Service, home)
}

/// 当前服务端点候选（#7）：Windows 读一次通告名后给出「通告名 → 静态名」，调用方按序尝试；
/// Unix 端点静态，只有一项。Windows 管道名不区分大小写，重复写法只保留一项。
pub fn current_service_endpoints(platform: HostPlatform, home: Option<&Path>) -> Vec<Endpoint> {
    #[cfg(windows)]
    {
        if platform == HostPlatform::Windows {
            return service_pipe_candidates(read_advertised_service_pipe_name().as_deref());
        }
    }
    resolve_endpoint(platform, WorkerMode::Service, home)
        .into_iter()
        .collect()
}

/// 一次连接失败后的服务端点解析（#7 残余⑤）：Windows 重读通告名，走
/// `service_pipe_endpoint_after_failure`（通告名失效换静态名，静态名失效回通告名）；
/// Unix 端点静态，保持原值。
pub fn current_service_endpoint_after_failure(
    platform: HostPlatform,
    home: Option<&Path>,
    failed: &Endpoint,
) -> Endpoint {
    #[cfg(windows)]
    {
        if platform == HostPlatform::Windows {
            return service_pipe_endpoint_after_failure(
                read_advertised_service_pipe_name().as_deref(),
                failed,
            );
        }
    }
    resolve_endpoint(platform, WorkerMode::Service, home).unwrap_or_else(|| failed.clone())
}

/// 读服务管道通告名（#7）：`HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName`（REG_SZ）。
/// 键/值缺失、类型不符、超长或解码失败都返回 None（调用方回退静态名——fail-safe；
/// 通告值由 SYSTEM 写入，读侧只做防损坏/防注入校验，不承担信任决策）。
#[cfg(windows)]
fn read_advertised_service_pipe_name() -> Option<String> {
    use windows_sys::Win32::Foundation::ERROR_SUCCESS;
    use windows_sys::Win32::System::Registry::{
        HKEY, HKEY_LOCAL_MACHINE, KEY_READ, RegCloseKey, RegOpenKeyExW,
    };

    fn wide(text: &str) -> Vec<u16> {
        text.encode_utf16().chain(std::iter::once(0)).collect()
    }

    // 与 C# 侧 WindowsServicePipeAcl 的 RegistrySubKeyName / AdvertisedServicePipeValueName 对齐。
    let subkey = wide("SOFTWARE\\PhotoPrivacy");
    let value_name = wide("ServicePipeName");

    // SAFETY: hkey 仅在 RegOpenKeyExW 成功（返回 ERROR_SUCCESS）后被写出并使用；
    // 其余指针实参指向本函数栈上有效的 UTF-16 NUL 结尾缓冲或为 null 探针。
    unsafe {
        let mut hkey: HKEY = std::ptr::null_mut();
        if RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey.as_ptr(), 0, KEY_READ, &mut hkey)
            != ERROR_SUCCESS
        {
            return None;
        }

        let queried = query_registry_string(hkey, &value_name);
        RegCloseKey(hkey);
        queried
    }
}

/// `RegQueryValueExW` 的 REG_SZ 读取（#7）：先探尺寸再读数据；截断 NUL 与首尾空白后交由
/// 调用方的合法性校验。尺寸上限 1 KiB——远大于管道名上限，防异常值拉爆 UI 内存。
#[cfg(windows)]
fn query_registry_string(
    hkey: windows_sys::Win32::System::Registry::HKEY,
    name: &[u16],
) -> Option<String> {
    use windows_sys::Win32::Foundation::ERROR_SUCCESS;
    use windows_sys::Win32::System::Registry::{REG_SZ, RegQueryValueExW};

    // SAFETY: hkey 由调用方保证是 RegOpenKeyExW 成功返回、尚未关闭的句柄；name 为 NUL 结尾
    // UTF-16；null 数据指针 + 尺寸出参 = 尺寸探测（文档化行为），buffer 按 queried 尺寸分配。
    let mut value_type: u32 = 0;
    let mut size_bytes: u32 = 0;
    let probed = unsafe {
        RegQueryValueExW(
            hkey,
            name.as_ptr(),
            std::ptr::null(),
            &mut value_type,
            std::ptr::null_mut(),
            &mut size_bytes,
        )
    };
    if probed != ERROR_SUCCESS || value_type != REG_SZ || size_bytes == 0 || size_bytes > 1024 {
        return None;
    }

    let mut buffer = vec![0u8; size_bytes as usize];
    let read = unsafe {
        RegQueryValueExW(
            hkey,
            name.as_ptr(),
            std::ptr::null(),
            &mut value_type,
            buffer.as_mut_ptr(),
            &mut size_bytes,
        )
    };
    if read != ERROR_SUCCESS {
        return None;
    }

    // 奇数字节（异常形态）由 chunks_exact 丢弃尾字节；from_utf16_lossy 兜住坏代理对。
    let words: Vec<u16> = buffer
        .chunks_exact(2)
        .map(|pair| u16::from_le_bytes([pair[0], pair[1]]))
        .collect();
    let text = String::from_utf16_lossy(&words);
    let trimmed = text.trim_matches('\0').trim();
    if trimmed.is_empty() {
        None
    } else {
        Some(trimmed.to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windows_pipe_names_match_dotnet_contract() {
        // 防：Windows 管道名漂移后连不上已安装的 Worker（契约出处 WorkerIpcEndpointNames.cs，#54）。
        assert_eq!(
            resolve_endpoint(HostPlatform::Windows, WorkerMode::Background, None),
            Some(Endpoint::Pipe("PhotoPrivacyCleaner.Background".into()))
        );
        assert_eq!(
            resolve_endpoint(HostPlatform::Windows, WorkerMode::Service, None),
            Some(Endpoint::Pipe("PhotoPrivacyCleaner.Service".into()))
        );
    }

    #[test]
    fn linux_background_socket_follows_xdg_runtime_dir() {
        // 防：后台端点变回普通用户无法创建的 /run/photoprivacy；与 C# 行为测试对照。
        assert_eq!(
            linux_background_endpoint(Some(Path::new("/run/user/1000"))),
            Some(Endpoint::UnixSocket(
                "/run/user/1000/photoprivacy/worker-background.sock".into()
            ))
        );
    }

    #[test]
    fn linux_background_socket_unavailable_without_rooted_runtime_dir() {
        // 防（#201）：缺失运行时目录时静默回落或拼出相对路径，与 Worker 解析分裂。
        assert_eq!(linux_background_endpoint(None), None);
        for bad in ["", "   ", "run/user/1000", "relative", " /run/user/1000"] {
            assert_eq!(linux_background_endpoint(Some(Path::new(bad))), None);
        }
    }

    #[test]
    fn linux_service_socket_matches_dotnet_contract() {
        // 防（#201）：迁移背景端点误动 systemd 服务端点；服务模式不依赖 XDG_RUNTIME_DIR。
        assert_eq!(
            resolve_endpoint(HostPlatform::Linux, WorkerMode::Service, None),
            Some(Endpoint::UnixSocket("/run/photoprivacy/worker.sock".into()))
        );
    }

    #[test]
    fn macos_background_socket_lives_under_user_home() {
        // 防：macOS 后台端点落到错误的目录，或缺少 HOME 时静默拼出相对路径（#54）。
        let home = Path::new("/Users/example");
        assert_eq!(
            resolve_endpoint(HostPlatform::MacOs, WorkerMode::Background, Some(home)),
            Some(Endpoint::UnixSocket(
                "/Users/example/Library/Application Support/photoprivacy/worker-background.sock"
                    .into()
            ))
        );
        assert_eq!(
            resolve_endpoint(HostPlatform::MacOs, WorkerMode::Background, None),
            None
        );
    }

    #[test]
    fn macos_service_socket_is_identity_independent() {
        // 防（#167）：服务端点跟着调用者 HOME 走——LaunchDaemon 以服务用户（home=/opt/photoprivacy）
        // 落点，GUI 用户按自身 home 解析就指向不存在的幽灵 socket。服务端点必须是系统级固定路径，
        // 与 home 及其有无都无关（与 Linux /run/photoprivacy 同构）。
        let expected = Some(Endpoint::UnixSocket(PathBuf::from(
            "/Library/Application Support/photoprivacy/worker.sock",
        )));
        for home in [
            None,
            Some(Path::new("/Users/gui")),
            Some(Path::new("/opt/photoprivacy")),
            Some(Path::new("/var/root")),
        ] {
            assert_eq!(
                resolve_endpoint(HostPlatform::MacOs, WorkerMode::Service, home),
                expected
            );
        }
    }

    #[test]
    fn advertised_service_pipe_name_is_used_when_valid() {
        // 防（#7）：通告名存在却仍连静态名——随机化后的服务端点只有通告名可发现，
        // 静态名回退会全程踩空（UI 永远连不上服务 Worker）。
        assert_eq!(
            service_pipe_endpoint_with_advertised(Some("PhotoPrivacyCleaner.Service.0123ABCDEF")),
            Endpoint::Pipe("PhotoPrivacyCleaner.Service.0123ABCDEF".into())
        );
    }

    #[test]
    fn missing_advertisement_falls_back_to_static_service_pipe() {
        // 防（#7）：旧版服务（无通告值）/值未写 → 回退静态名而不是断路——升级窗口内 UI 仍可用。
        assert_eq!(
            service_pipe_endpoint_with_advertised(None),
            Endpoint::Pipe("PhotoPrivacyCleaner.Service".into())
        );
    }

    #[test]
    fn invalid_advertisement_falls_back_to_static_service_pipe() {
        // 防（#7）：注册表值被改出路径分隔/空白/控制字符/非 ASCII/超长（损坏或注入尝试）→
        // fail-safe 回退静态名，而不是把任意字符串当管道名拼进 \\.\pipe\ 路径。
        let too_long = "x".repeat(201);
        for bad in [
            "",
            " ",
            "with\\backslash",
            "with/slash",
            "with space",
            "with\nnewline",
            "含中文",
            too_long.as_str(),
        ] {
            assert_eq!(
                service_pipe_endpoint_with_advertised(Some(bad)),
                Endpoint::Pipe("PhotoPrivacyCleaner.Service".into()),
                "invalid advertised name must fall back: {bad:?}"
            );
        }
    }

    #[test]
    fn service_pipe_candidates_try_advertised_then_static() {
        // 防（#7 残余⑤）：通告名存在时只试通告名、不试静态名——通告写入失败且残留清除也失败
        // 时服务已回绑静态名，UI 会永远钉在旧随机名上。
        assert_eq!(
            service_pipe_candidates(Some("PhotoPrivacyCleaner.Service.0123ABCDEF")),
            vec![
                Endpoint::Pipe("PhotoPrivacyCleaner.Service.0123ABCDEF".into()),
                Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into()),
            ]
        );
        assert_eq!(
            service_pipe_candidates(None),
            vec![Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into())]
        );
        assert_eq!(
            service_pipe_candidates(Some("bad/name")),
            vec![Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into())]
        );
        // 通告值就是静态名：不重复同一个端点（重复只会白耗一次连接预算）。
        assert_eq!(
            service_pipe_candidates(Some(STATIC_SERVICE_PIPE_NAME)),
            vec![Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into())]
        );
        // Windows 管道名不区分大小写：换个写法仍是同一个名字，不能多出一个候选。
        assert_eq!(
            service_pipe_candidates(Some("photoprivacycleaner.service")),
            vec![Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into())]
        );
    }

    #[test]
    fn service_pipe_endpoint_after_failure_switches_names() {
        // 防（#7 残余⑤）：第一次失败后仍返回同一个通告名——UI 每一拍都撞同一堵墙，
        // 服务明明已回绑静态名却永远连不上。
        let stale = Endpoint::Pipe("PhotoPrivacyCleaner.Service.OLD".into());
        let static_endpoint = Endpoint::Pipe(STATIC_SERVICE_PIPE_NAME.into());
        assert_eq!(
            service_pipe_endpoint_after_failure(Some("PhotoPrivacyCleaner.Service.OLD"), &stale),
            static_endpoint
        );
        // 静态名也失败时回到通告名：服务重启换了新随机名后仍能接上，不在两个名字间死循环。
        assert_eq!(
            service_pipe_endpoint_after_failure(
                Some("PhotoPrivacyCleaner.Service.OLD"),
                &static_endpoint
            ),
            stale
        );
        // 没有合法通告名时只有静态名可试，返回原值（不是另造一个端点）。
        assert_eq!(
            service_pipe_endpoint_after_failure(None, &static_endpoint),
            static_endpoint
        );
        // ③：服务重启已把通告名换成 NEW，而 UI 手里还是 OLD——下一次解析必须选 NEW，
        // 不能还回旧值（否则服务回来了也连不上）。
        let new = Endpoint::Pipe("PhotoPrivacyCleaner.Service.NEW".into());
        assert_eq!(
            service_pipe_endpoint_after_failure(Some("PhotoPrivacyCleaner.Service.NEW"), &stale),
            new
        );
        // Windows 管道名不区分大小写：大小写写法差异仍是同一个失败端点，必须换静态名。
        assert_eq!(
            service_pipe_endpoint_after_failure(
                Some("PhotoPrivacyCleaner.Service.OLD"),
                &Endpoint::Pipe("photoprivacycleaner.service.old".into())
            ),
            static_endpoint
        );
    }

    #[test]
    #[cfg(not(windows))]
    fn current_service_endpoints_matches_static_resolution_off_windows() {
        // 防（#7）：Unix 侧被误接通告名候选（Unix 端点静态、SO_PEERCRED uid 边界，不适用）。
        for (platform, home) in [
            (HostPlatform::Linux, None),
            (HostPlatform::MacOs, Some(Path::new("/Users/example"))),
        ] {
            assert_eq!(
                current_service_endpoints(platform, home),
                resolve_endpoint(platform, WorkerMode::Service, home)
                    .into_iter()
                    .collect::<Vec<_>>()
            );
        }
    }

    #[test]
    #[cfg(not(windows))]
    fn current_service_endpoint_after_failure_keeps_static_socket_off_windows() {
        // 防（#7 残余⑤）把 Windows 的静态管道兜底误接到 Unix socket 上：Unix 端点只有一项，
        // 失败后必须还是同一个 socket（服务换随机名只存在于 Windows 管道）。
        let failed = Endpoint::UnixSocket("/run/photoprivacy/worker.sock".into());
        assert_eq!(
            current_service_endpoint_after_failure(HostPlatform::Linux, None, &failed),
            failed
        );
    }

    #[test]
    #[cfg(not(windows))]
    fn current_service_endpoint_matches_static_resolution_off_windows() {
        // 防（#7）：Unix 侧被误接通告名机制（Unix 端点静态、SO_PEERCRED uid 边界，不适用）。
        // 本测试在 Linux/macOS CI 真跑；Windows 宿主上通告名路径归实机 S4 覆盖。
        for (platform, home) in [
            (HostPlatform::Linux, None),
            (HostPlatform::MacOs, Some(Path::new("/Users/example"))),
        ] {
            assert_eq!(
                current_service_endpoint(platform, home),
                resolve_endpoint(platform, WorkerMode::Service, home)
            );
        }
        assert_eq!(
            current_service_endpoint(HostPlatform::MacOs, None),
            Some(Endpoint::UnixSocket(PathBuf::from(
                "/Library/Application Support/photoprivacy/worker.sock"
            )))
        );
    }
}
