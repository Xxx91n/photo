//! 后台管道服务端进程身份核验（#7 残余①：客户端认证服务端）。
//!
//! 威胁模型：后台 Worker 由 UI 拉起，发现通道（拉起参数、同用户可写位置、管道枚举）同用户
//! 均可伪造，熵化无法闭环。本模块在连接建立后、任何业务请求之前核验对端进程身份：
//!
//! 1. 取服务端 PID（Windows `GetNamedPipeServerProcessId`；Unix 经 tokio
//!    `UnixStream::peer_cred`，Linux 走 `SO_PEERCRED`，macOS 走 `LOCAL_PEEREPID`）；
//! 2. 首选匹配 UI 本次拉起的 Worker 子进程 PID（最强绑定，Worker 崩溃重启后由 UI 重新
//!    拉起即自然更新）；
//! 3. 回退核验进程镜像路径（UI 重启但后台 Worker 仍在、UI 不知道 PID 的场景）；
//! 4. 两条都不匹配 → 该连接作废。
//!
//! 上限（如实登记）：同用户进程在 Windows 上可注入 UI、读 UI 内存——同用户本就是无隔离级差
//! 的信任域，本加固不宣称防同用户恶意软件。PID 快照核验存在固有的 PID 复用窗口（拿 PID 后
//! 到 OpenProcess 之前），镜像是 speed bump 而非不可伪造凭据；签名核验（WinVerifyTrust）
//! 未入首版。
// Design(#7): 后台管道客户端在首笔业务请求前核验服务端进程身份（PID 优先、镜像路径回退），
// 核验失败即作废连接；服务管道不叠加（熵 + 拆位 + HKLM 通告已闭环）。

use crate::endpoints::Endpoint;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

/// 平台侧读到的服务端进程标识。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ServerPeer {
    /// 取到服务端 PID。
    Known(u32),
    /// 平台/内核不提供对端 PID（能力缺口，按调用方策略处置）。
    Unavailable(String),
    /// 平台提供该能力但查询失败（可疑：同样条件下同用户进程不应查询失败）。
    QueryFailed(String),
}

/// 进程镜像路径查询结果。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ImageProbeError {
    /// 进程已退出（核验瞬间的竞争，按瞬时连接失败重试）。
    Gone,
    /// 平台未实现进程镜像查询（macOS 回退路径按 fail-open + warning 处置）。
    Unsupported(String),
    /// 查询本身失败（权限/系统调用错误）。
    QueryFailed(String),
}

/// 可注入的进程镜像查询器：生产实现走平台原生调用，测试注入伪造结果。
pub trait ProcessImageProbe: Send + Sync {
    fn image_path(&self, pid: u32) -> Result<PathBuf, ImageProbeError>;
}

/// 核验结论。`Transient` 与 `Unverifiable` 的区别是处置方向：前者重试，后者按平台策略。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum VerifyOutcome {
    /// 对端即期望的 Worker。
    Trusted,
    /// 对端不是期望的 Worker（PID/镜像不符，或查询失败）——立即作废，不重试。
    Rejected(String),
    /// 核验期间对端进程消失——按瞬时连接失败在连接预算内重试，不升级为授权失败。
    Transient(String),
    /// 无法核验（平台能力缺口/无期望基准）——由 [UnverifiablePolicy] 决定放行或拒绝。
    Unverifiable(String),
}

/// 可注入的服务端身份核验器（#7 AC5）：测试用简单实现覆盖判定链，生产实现见
/// [ProcessIdentityVerifier]。
pub trait ServerIdentityVerifier: Send + Sync {
    fn verify(&self, peer: &ServerPeer) -> VerifyOutcome;
}

/// 无法核验时的平台策略（#7 D2）。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum UnverifiablePolicy {
    /// 可用性优先：放行并记 warning（与 #70 SID 缺失回退、#7 通告失败回退同构）。
    FailOpen,
    /// 安全优先：拒绝该连接。
    FailClosed,
}

/// 核验作用域：只对该端点生效（后台管道），其余端点（服务管道，D3）零行为变化。
pub struct ServerGuard {
    endpoint: Endpoint,
    verifier: Arc<dyn ServerIdentityVerifier>,
    unverifiable: UnverifiablePolicy,
    /// fail-open 告警只打一次（#7 AC2 噪声）：心跳与 1s 版本轮询共用同一 guard，
    /// macOS 无期望 PID 时每拍都会 Unverifiable——按连接重复告警会把 stderr 刷爆。
    warned_unverifiable: AtomicBool,
}

impl ServerGuard {
    pub fn new(
        endpoint: Endpoint,
        verifier: Arc<dyn ServerIdentityVerifier>,
        unverifiable: UnverifiablePolicy,
    ) -> Self {
        Self {
            endpoint,
            verifier,
            unverifiable,
            warned_unverifiable: AtomicBool::new(false),
        }
    }

    pub fn applies_to(&self, endpoint: &Endpoint) -> bool {
        &self.endpoint == endpoint
    }

    /// 返回 true 当且仅当这是本 guard 第一次「无法核验」——调用方据此只告警一次。
    pub fn claim_unverifiable_warning(&self) -> bool {
        !self.warned_unverifiable.swap(true, Ordering::Relaxed)
    }

    pub fn verify(&self, peer: &ServerPeer) -> VerifyOutcome {
        self.verifier.verify(peer)
    }

    pub fn unverifiable_policy(&self) -> UnverifiablePolicy {
        self.unverifiable
    }
}

/// 生产核验器：期望 PID 优先，其次镜像路径。
///
/// 期望 PID 由 UI 在拉起 Worker 后登记（`std::process::Child::id()`），0 表示未知——
/// UI 重启但后台 Worker 仍在时走镜像回退。
pub struct ProcessIdentityVerifier {
    expected_pid: AtomicU32,
    expected_image: Option<PathBuf>,
    probe: Arc<dyn ProcessImageProbe>,
}

impl ProcessIdentityVerifier {
    /// 生产构造：镜像回退走平台原生查询。
    pub fn new(expected_image: Option<PathBuf>) -> Self {
        Self::with_probe(expected_image, Arc::new(SystemImageProbe))
    }

    /// 测试构造：注入镜像查询器（ubuntu CI 上覆盖全部判定分支）。
    pub fn with_probe(expected_image: Option<PathBuf>, probe: Arc<dyn ProcessImageProbe>) -> Self {
        Self {
            expected_pid: AtomicU32::new(0),
            expected_image,
            probe,
        }
    }

    /// 登记 UI 本次拉起的 Worker PID（Worker 重启后由新一轮拉起覆盖）。
    pub fn expect_pid(&self, pid: u32) {
        self.expected_pid.store(pid, Ordering::SeqCst);
    }

    pub fn expected_pid(&self) -> Option<u32> {
        match self.expected_pid.load(Ordering::SeqCst) {
            0 => None,
            pid => Some(pid),
        }
    }
}

impl ServerIdentityVerifier for ProcessIdentityVerifier {
    fn verify(&self, peer: &ServerPeer) -> VerifyOutcome {
        let peer_pid = match peer {
            ServerPeer::Known(pid) => *pid,
            ServerPeer::Unavailable(reason) => {
                return VerifyOutcome::Unverifiable(format!("平台不提供对端 PID: {reason}"));
            }
            ServerPeer::QueryFailed(reason) => {
                return VerifyOutcome::Rejected(format!("服务端 PID 查询失败: {reason}"));
            }
        };

        let expected_pid = self.expected_pid();
        if expected_pid == Some(peer_pid) {
            return VerifyOutcome::Trusted;
        }

        let Some(expected_image) = self.expected_image.as_deref() else {
            return match expected_pid {
                Some(expected) => VerifyOutcome::Rejected(format!(
                    "服务端 PID {peer_pid} 与 UI 拉起的 Worker PID {expected} 不符"
                )),
                None => VerifyOutcome::Unverifiable(
                    "既无期望 PID 也无期望镜像路径，无法核验对端".to_string(),
                ),
            };
        };

        match self.probe.image_path(peer_pid) {
            Ok(actual) if image_paths_equal(&actual, expected_image) => VerifyOutcome::Trusted,
            Ok(actual) => VerifyOutcome::Rejected(format!(
                "服务端 PID {peer_pid} 的镜像 {} 不是期望的 {}",
                actual.display(),
                expected_image.display()
            )),
            Err(ImageProbeError::Gone) => {
                VerifyOutcome::Transient(format!("核验期间服务端进程 {peer_pid} 已退出"))
            }
            Err(ImageProbeError::Unsupported(reason)) => {
                // 已知期望 PID 却对不上：这不是「核验不了」，而是对端不是我们拉起的 Worker
                // （macOS 上抢建场景正是这一支）。能力缺口只在无 PID 基准时才交给平台策略。
                if expected_pid.is_some() {
                    VerifyOutcome::Rejected(format!(
                        "服务端 PID {peer_pid} 与期望 PID 不符，且镜像不可核验: {reason}"
                    ))
                } else {
                    VerifyOutcome::Unverifiable(format!(
                        "服务端进程 {peer_pid} 镜像不可核验: {reason}"
                    ))
                }
            }
            Err(ImageProbeError::QueryFailed(reason)) => {
                VerifyOutcome::Rejected(format!("服务端进程 {peer_pid} 镜像查询失败: {reason}"))
            }
        }
    }
}

/// 镜像路径比较：Windows 去 `\\?\` 前缀并按大小写不敏感比较；Unix 先尽量解析软链再逐字比较。
fn image_paths_equal(actual: &Path, expected: &Path) -> bool {
    #[cfg(windows)]
    {
        normalize_windows_image_path(actual)
            .eq_ignore_ascii_case(&normalize_windows_image_path(expected))
    }
    #[cfg(not(windows))]
    {
        let actual = std::fs::canonicalize(actual).unwrap_or_else(|_| actual.to_path_buf());
        let expected = std::fs::canonicalize(expected).unwrap_or_else(|_| expected.to_path_buf());
        actual == expected
    }
}

#[cfg(windows)]
fn normalize_windows_image_path(path: &Path) -> String {
    path.to_string_lossy()
        .trim_start_matches(r"\\?\")
        .to_string()
}

/// 生产镜像查询器（平台原生）。
struct SystemImageProbe;

impl ProcessImageProbe for SystemImageProbe {
    #[cfg(windows)]
    fn image_path(&self, pid: u32) -> Result<PathBuf, ImageProbeError> {
        windows_image_path(pid)
    }

    #[cfg(target_os = "linux")]
    fn image_path(&self, pid: u32) -> Result<PathBuf, ImageProbeError> {
        match std::fs::read_link(format!("/proc/{pid}/exe")) {
            Ok(path) => Ok(path),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Err(ImageProbeError::Gone),
            Err(err) => Err(ImageProbeError::QueryFailed(err.to_string())),
        }
    }

    #[cfg(all(unix, not(target_os = "linux")))]
    fn image_path(&self, _pid: u32) -> Result<PathBuf, ImageProbeError> {
        // macOS 可经 `proc_pidpath` 取镜像，但需 libproc FFI；首版把该回退按 D2 的
        // fail-open + warning 处置（PID 匹配仍可用，见 ipc-contract 已知残余）。
        Err(ImageProbeError::Unsupported(
            "该平台首版未实现进程镜像查询".to_string(),
        ))
    }

    #[cfg(not(any(windows, unix)))]
    fn image_path(&self, _pid: u32) -> Result<PathBuf, ImageProbeError> {
        Err(ImageProbeError::Unsupported(
            "该平台无进程镜像查询能力".to_string(),
        ))
    }
}

#[cfg(windows)]
fn windows_image_path(pid: u32) -> Result<PathBuf, ImageProbeError> {
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Threading::{
        OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
        QueryFullProcessImageNameW,
    };

    const ERROR_INVALID_PARAMETER: i32 = 87;
    const MAX_IMAGE_PATH_CHARS: usize = 1024;

    // SAFETY: OpenProcess 只取句柄（PROCESS_QUERY_LIMITED_INFORMATION 不含读内存）；
    // 失败返回空句柄，成功返回的句柄在下方查询后关闭一次。
    let handle = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
    if handle.is_null() {
        let err = std::io::Error::last_os_error();
        return Err(match err.raw_os_error() {
            // ERROR_INVALID_PARAMETER：PID 已不存在——核验瞬间的进程退出竞争。
            Some(ERROR_INVALID_PARAMETER) => ImageProbeError::Gone,
            _ => ImageProbeError::QueryFailed(err.to_string()),
        });
    }

    let mut buffer = vec![0u16; MAX_IMAGE_PATH_CHARS];
    let mut size = buffer.len() as u32;
    // SAFETY: buffer 是 size 个 u16 的有效可写缓冲；handle 由 OpenProcess 成功返回。
    let ok = unsafe {
        QueryFullProcessImageNameW(handle, PROCESS_NAME_WIN32, buffer.as_mut_ptr(), &mut size)
    };
    let query_error = if ok == 0 {
        Some(std::io::Error::last_os_error())
    } else {
        None
    };
    // SAFETY: handle 由 OpenProcess 成功返回，且仅在此处关闭；与 tokio 句柄无关。
    unsafe {
        CloseHandle(handle);
    }

    if let Some(err) = query_error {
        return Err(ImageProbeError::QueryFailed(err.to_string()));
    }
    buffer.truncate(size as usize);
    Ok(PathBuf::from(String::from_utf16_lossy(&buffer)))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    struct FakeProbe {
        images: HashMap<u32, Result<PathBuf, ImageProbeError>>,
    }

    impl FakeProbe {
        fn with(entries: &[(u32, Result<&str, ImageProbeError>)]) -> Self {
            Self {
                images: entries
                    .iter()
                    .map(|(pid, result)| {
                        (
                            *pid,
                            match result {
                                Ok(path) => Ok(PathBuf::from(*path)),
                                Err(err) => Err(err.clone()),
                            },
                        )
                    })
                    .collect(),
            }
        }
    }

    impl ProcessImageProbe for FakeProbe {
        fn image_path(&self, pid: u32) -> Result<PathBuf, ImageProbeError> {
            self.images
                .get(&pid)
                .cloned()
                .unwrap_or(Err(ImageProbeError::Gone))
        }
    }

    fn make_verifier(
        expected_pid: Option<u32>,
        expected_image: Option<&str>,
        probe: FakeProbe,
    ) -> ProcessIdentityVerifier {
        let verifier =
            ProcessIdentityVerifier::with_probe(expected_image.map(PathBuf::from), Arc::new(probe));
        if let Some(pid) = expected_pid {
            verifier.expect_pid(pid);
        }
        verifier
    }

    #[test]
    fn matching_spawned_pid_is_trusted_without_image_probe() {
        // 防（#7 AC1）：UI 拉起的 Worker PID 匹配仍去查镜像——多一次 OpenProcess，且进程
        // 恰在查询时退出的竞争会把合法连接误判为不可核验。首选判定必须是 PID 直配。
        let verifier = make_verifier(
            Some(4242),
            Some("C:/app/PhotoPrivacyWorker.exe"),
            FakeProbe::with(&[]),
        );
        assert_eq!(
            verifier.verify(&ServerPeer::Known(4242)),
            VerifyOutcome::Trusted
        );
    }

    #[test]
    fn pid_mismatch_with_foreign_image_is_rejected() {
        // 防（#7 残余①回潮）：同用户抢先建的管道被当成 Worker——PID 不符且镜像不是
        // PhotoPrivacyWorker.exe 时必须作废连接，而不是放行后把状态页交给冒充者。
        let verifier = make_verifier(
            Some(4242),
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[(777, Ok("/tmp/evil-server"))]),
        );
        match verifier.verify(&ServerPeer::Known(777)) {
            VerifyOutcome::Rejected(reason) => {
                assert!(reason.contains("777"), "reason: {reason}");
                assert!(reason.contains("evil-server"), "reason: {reason}");
            }
            other => panic!("镜像不符必须 Rejected，得到 {other:?}"),
        }
    }

    #[test]
    fn pid_mismatch_falls_back_to_matching_image() {
        // 防（#7）：UI 重启但后台 Worker 仍在（期望 PID 未知/陈旧）时把合法 Worker 拒掉——
        // 回退镜像路径核验必须能放行同一可执行文件的进程。
        let image = "/opt/pp/PhotoPrivacyWorker";
        let verifier = make_verifier(None, Some(image), FakeProbe::with(&[(9001, Ok(image))]));
        assert_eq!(
            verifier.verify(&ServerPeer::Known(9001)),
            VerifyOutcome::Trusted
        );

        // 陈旧期望 PID + 镜像相符：Worker 崩溃重启换了 PID，仍应放行。
        let verifier = make_verifier(
            Some(4242),
            Some(image),
            FakeProbe::with(&[(9001, Ok(image))]),
        );
        assert_eq!(
            verifier.verify(&ServerPeer::Known(9001)),
            VerifyOutcome::Trusted
        );
    }

    #[test]
    fn process_gone_during_verification_is_transient() {
        // 防（#7 AC5）：核验瞬间进程退出被升级成授权失败——连接预算内的瞬时空窗应重试，
        // 否则 Worker 正常重启会被当成「不可信服务端」。
        let verifier = make_verifier(
            Some(4242),
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[(777, Err(ImageProbeError::Gone))]),
        );
        match verifier.verify(&ServerPeer::Known(777)) {
            VerifyOutcome::Transient(_) => {}
            other => panic!("进程消失必须 Transient，得到 {other:?}"),
        }
    }

    #[test]
    fn unverifiable_image_with_known_pid_mismatch_is_rejected() {
        // 防（#7 AC2 macOS 自毁链）：平台无镜像回退时，已知期望 PID 却对不上的连接仍按能力
        // 缺口 fail-open 放行——抢建者只要让 PID 不符即可绕过整条核验。
        let verifier = make_verifier(
            Some(4242),
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[(777, Err(ImageProbeError::Unsupported("mac".into())))]),
        );
        assert!(matches!(
            verifier.verify(&ServerPeer::Known(777)),
            VerifyOutcome::Rejected(_)
        ));

        // 无期望 PID（UI 重启、Worker 仍在）时才是真正的能力缺口 → 交给平台策略。
        let verifier = make_verifier(
            None,
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[(777, Err(ImageProbeError::Unsupported("mac".into())))]),
        );
        assert!(matches!(
            verifier.verify(&ServerPeer::Known(777)),
            VerifyOutcome::Unverifiable(_)
        ));
    }

    #[test]
    fn peer_capability_gap_and_missing_expectation_are_unverifiable() {
        // 防（#7 D2）：平台取不到 PID（或既无期望 PID 也无镜像）被当成 Rejected 或 Trusted——
        // 前者让 macOS/能力缺口完全不可用，后者直接静默放行冒充者。两者都必须走策略层。
        let verifier = make_verifier(Some(4242), None, FakeProbe::with(&[]));
        assert!(matches!(
            verifier.verify(&ServerPeer::Unavailable("no pid".into())),
            VerifyOutcome::Unverifiable(_)
        ));
        let bare = make_verifier(None, None, FakeProbe::with(&[]));
        assert!(matches!(
            bare.verify(&ServerPeer::Known(777)),
            VerifyOutcome::Unverifiable(_)
        ));
    }

    #[test]
    fn peer_query_and_image_query_failures_are_rejected() {
        // 防（#7 AC2）：能力存在但查询失败（权限/系统调用错误）被 fail-open 放行——
        // 同用户进程不应查询失败，失败本身即可疑信号，按 Chromium 同姿态 fail-closed。
        let verifier = make_verifier(
            Some(4242),
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[]),
        );
        assert!(matches!(
            verifier.verify(&ServerPeer::QueryFailed("boom".into())),
            VerifyOutcome::Rejected(_)
        ));

        let verifier = make_verifier(
            Some(4242),
            Some("/opt/pp/PhotoPrivacyWorker"),
            FakeProbe::with(&[(777, Err(ImageProbeError::QueryFailed("denied".into())))]),
        );
        assert!(matches!(
            verifier.verify(&ServerPeer::Known(777)),
            VerifyOutcome::Rejected(_)
        ));
    }

    #[test]
    fn guard_only_applies_to_its_own_endpoint() {
        // 防（#7 D3）：核验器被误作用到服务管道——服务端点是 SYSTEM 进程、与 UI 拉起的
        // Worker PID 必然不符，会把合法的服务模式连接整体拒掉。
        let guard = ServerGuard::new(
            Endpoint::Pipe("PhotoPrivacyCleaner.Background".into()),
            Arc::new(make_verifier(None, None, FakeProbe::with(&[]))),
            UnverifiablePolicy::FailOpen,
        );
        assert!(guard.applies_to(&Endpoint::Pipe("PhotoPrivacyCleaner.Background".into())));
        assert!(!guard.applies_to(&Endpoint::Pipe("PhotoPrivacyCleaner.Service.abc".into())));
        assert!(!guard.applies_to(&Endpoint::UnixSocket(
            "/run/photoprivacy/worker.sock".into()
        )));
    }

    #[test]
    fn unverifiable_warning_is_claimed_once_per_guard() {
        // 防（#7 AC2 噪声回潮）：fail-open 告警按连接重打——macOS 无期望 PID 时心跳与 1s
        // 版本轮询共用同一 guard，每拍一条 stderr 会把日志刷爆。
        let guard = ServerGuard::new(
            Endpoint::Pipe("PhotoPrivacyCleaner.Background".into()),
            Arc::new(make_verifier(None, None, FakeProbe::with(&[]))),
            UnverifiablePolicy::FailOpen,
        );
        assert!(guard.claim_unverifiable_warning());
        assert!(!guard.claim_unverifiable_warning());
        assert!(!guard.claim_unverifiable_warning());
    }

    #[test]
    #[cfg(target_os = "linux")]
    fn system_probe_reads_own_image_from_proc() {
        // 防（#7 AC2 Linux）：/proc/<pid>/exe 的实现被写错（拼错路径/把读取失败当空路径），
        // Linux 上镜像回退整体失效——用自身进程作真实探针，ubuntu CI 真跑。
        let path = SystemImageProbe
            .image_path(std::process::id())
            .expect("自身进程镜像必须可读");
        assert!(
            path.is_absolute(),
            "镜像路径应为绝对路径: {}",
            path.display()
        );
    }

    #[test]
    #[cfg(target_os = "linux")]
    fn system_probe_maps_missing_process_to_gone() {
        // 防（#7 AC5 Linux）：不存在的进程被归为 QueryFailed（直接作废连接）而不是 Gone
        // （按瞬时失败重试）——Worker 重启窗口会被误判为不可信服务端。
        assert_eq!(
            SystemImageProbe.image_path(u32::MAX),
            Err(ImageProbeError::Gone)
        );
    }
}
