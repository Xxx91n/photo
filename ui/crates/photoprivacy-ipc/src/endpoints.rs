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

// Design(#49): IPC 端点名是协议主缝的一部分——这里与 `WorkerIpcEndpointNames` 逐字一致，不改协议；
// 旧 C# UI 全程硬编码 Windows 管道名，新客户端（#54）改按 OS 解析，修的是客户端缺陷而不是协议。
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
}
