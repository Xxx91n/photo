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

// Design(#54): 新 UI 客户端按 OS 解析端点；旧 C# UI 全程硬编码 Windows 管道名，Linux/macOS 实际连不上
// （换栈调研 §2.6 的第一条既有缺陷）。端点常量与 `WorkerIpcEndpointNames` 逐字一致，不改协议。
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
