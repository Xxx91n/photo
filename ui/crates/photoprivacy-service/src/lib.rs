//! 三平台服务管理：状态探测、安装/启停/卸载与提权分型。
//!
//! 行为基准是冻结旧 UI 的 `ServiceManager` / `ServiceStateProbe` / 三个提权执行器：
//! Windows 走 `sc.exe` + runas，Linux 走 `pkexec`，macOS 走 `osascript`；
//! “四态反馈”（Success / Skipped / ElevationCancelled / Failed）与旧 UI 语义一致。
//!
//! 本 crate 不依赖 IPC，也不扩协议：服务安装/启停必须在没有 Worker 时也能工作，
//! 所以提权动作由 UI 进程拉起平台辅助进程完成。
//!
//! 取舍判据见 #49（UI 侧服务层重写）与 GLOSSARY 的 Service State Probe / Elevation Verb。

mod command;
mod manager;
mod parse;
mod process;
mod state;

// `test` 分支让 Linux/macOS 的命令构造与解析在任意宿主上都能被单元测试覆盖；
// 非测试构建仍只编译当前目标的实现。
#[cfg(any(unix, test))]
mod unix;
#[cfg(windows)]
mod windows;

pub use command::{ServiceCommandResult, ServiceCommandStatus};
pub use manager::{ServiceManager, ServiceOps, ServiceProbe, platform_manager};
pub use process::{CommandOutput, ProcessRunner, SystemProcessRunner};
pub use state::{ServiceButtonState, ServiceRuntimeState, button_state};
