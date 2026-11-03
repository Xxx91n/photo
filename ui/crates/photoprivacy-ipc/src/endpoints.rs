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

// IPC 端点名是协议主缝的一部分：与 `WorkerIpcEndpointNames` 逐字一致，不改协议；旧 C# UI 全程硬编码
// Windows 管道名（Linux/macOS 实际连不上），新客户端（#54）改按 OS 解析，修的是客户端缺陷而非协议。
// Design(#49): 端点名冻结为既有契约，禁止为解析方便改名。
pub fn resolve_endpoint(
    platform: HostPlatform,
    mode: WorkerMode,
    home: Option<&Path>,
) -> Option<Endpoint> {
    match platform {
        HostPlatform::Windows => Some(Endpoint::Pipe(
            match mode {
                WorkerMode::Background => "PhotoPrivacyCleaner.Background",
                WorkerMode::Service => "PhotoPrivacyCleaner.Service",
            }
            .to_string(),
        )),
        HostPlatform::Linux => Some(Endpoint::UnixSocket(PathBuf::from(match mode {
            WorkerMode::Background => "/run/photoprivacy/worker-background.sock",
            WorkerMode::Service => "/run/photoprivacy/worker.sock",
        }))),
        HostPlatform::MacOs => {
            let home = home?;
            let dir = home.join("Library/Application Support/photoprivacy");
            Some(Endpoint::UnixSocket(dir.join(match mode {
                WorkerMode::Background => "worker-background.sock",
                WorkerMode::Service => "worker.sock",
            })))
        }
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

/// 服务管道通告名解析（#7）：有效通告名优先；缺失/非法回退静态名（与 `resolve_endpoint` 的
/// Windows Service 名一致——旧版服务兼容、值损坏防注入）。只在 Windows 语义下调用。
// Design(#7): 服务端点按通告名优先解析、缺失回退静态名；随机名防预蹲，HKLM 管理员可写防指针伪造。
pub fn service_pipe_endpoint_with_advertised(advertised: Option<&str>) -> Endpoint {
    match advertised.filter(|name| is_advertised_pipe_name(name)) {
        Some(name) => Endpoint::Pipe(name.to_string()),
        // 与 resolve_endpoint(Windows, Service) 同名（既有测试 windows_pipe_names_match_dotnet_contract 钉住）。
        None => Endpoint::Pipe("PhotoPrivacyCleaner.Service".to_string()),
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
    fn linux_socket_paths_match_dotnet_contract() {
        // 防：Unix socket 路径与 systemd RuntimeDirectory 下的 Worker 不一致（旧 UI 的既有缺陷，#54）。
        assert_eq!(
            resolve_endpoint(HostPlatform::Linux, WorkerMode::Background, None),
            Some(Endpoint::UnixSocket(
                "/run/photoprivacy/worker-background.sock".into()
            ))
        );
        assert_eq!(
            resolve_endpoint(HostPlatform::Linux, WorkerMode::Service, None),
            Some(Endpoint::UnixSocket("/run/photoprivacy/worker.sock".into()))
        );
    }

    #[test]
    fn macos_socket_paths_live_under_user_home() {
        // 防：macOS 端点落到错误的目录，或缺少 HOME 时静默拼出相对路径（#54）。
        let home = Path::new("/Users/example");
        assert_eq!(
            resolve_endpoint(HostPlatform::MacOs, WorkerMode::Background, Some(home)),
            Some(Endpoint::UnixSocket(
                "/Users/example/Library/Application Support/photoprivacy/worker-background.sock"
                    .into()
            ))
        );
        assert_eq!(
            resolve_endpoint(HostPlatform::MacOs, WorkerMode::Service, None),
            None
        );
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
        assert_eq!(current_service_endpoint(HostPlatform::MacOs, None), None);
    }
}
