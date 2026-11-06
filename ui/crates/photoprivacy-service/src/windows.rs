use crate::command::{
    ServiceCommandResult, ServiceCommandStatus, result_from_output, translate_exit_code,
};
use crate::manager::{ServiceOps, ServiceProbe};
use crate::parse::parse_sc_query;
use crate::process::{CommandOutput, ProcessRunner};
use crate::state::ServiceRuntimeState;
use std::ffi::c_void;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;
use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_CANCELLED, ERROR_FILE_NOT_FOUND, GetLastError, HANDLE, LocalFree,
    SetLastError, WAIT_OBJECT_0, WAIT_TIMEOUT,
};
use windows_sys::Win32::Security::Authorization::ConvertSidToStringSidW;
use windows_sys::Win32::Security::{
    GetTokenInformation, TOKEN_ELEVATION, TOKEN_QUERY, TOKEN_USER, TokenElevation, TokenUser,
};
use windows_sys::Win32::System::Registry::{
    HKEY, HKEY_LOCAL_MACHINE, KEY_QUERY_VALUE, KEY_SET_VALUE, REG_MULTI_SZ,
    REG_OPTION_NON_VOLATILE, REG_SZ, RegCloseKey, RegCreateKeyExW, RegDeleteValueW, RegOpenKeyExW,
    RegQueryValueExW, RegSetValueExW,
};
use windows_sys::Win32::System::SystemInformation::GetSystemDirectoryW;
use windows_sys::Win32::System::Threading::{
    GetCurrentProcess, GetExitCodeProcess, OpenProcessToken, TerminateProcess, WaitForSingleObject,
};
use windows_sys::Win32::UI::Shell::{SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW};
use windows_sys::Win32::UI::WindowsAndMessaging::SW_HIDE;

pub const SERVICE_NAME: &str = "PhotoPrivacyCleaner";
const PROBE_TIMEOUT: Duration = Duration::from_secs(3);
const COMMAND_TIMEOUT: Duration = Duration::from_secs(60);

/// `ShellExecuteExW` 与 `CreateProcess` 都会在当前目录搜索裸程序名；发布目录可写时，
/// 用户批准 UAC 后可能执行同目录的恶意 `sc.exe`（CWE-427）。这里用系统 API 取绝对路径。
fn sc_exe_path() -> Option<PathBuf> {
    let mut buffer = [0u16; 260];
    let len = unsafe { GetSystemDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return None;
    }
    let directory = String::from_utf16_lossy(&buffer[..len as usize]);
    Some(PathBuf::from(directory).join("sc.exe"))
}

pub struct WindowsProbe {
    runner: Arc<dyn ProcessRunner>,
}

impl WindowsProbe {
    pub fn new(runner: Arc<dyn ProcessRunner>) -> Self {
        Self { runner }
    }
}

impl ServiceProbe for WindowsProbe {
    fn state(&self) -> ServiceRuntimeState {
        let Some(sc_exe) = sc_exe_path() else {
            return ServiceRuntimeState::Unknown;
        };
        let output = self.runner.run(
            &sc_exe.to_string_lossy(),
            &["query".to_string(), SERVICE_NAME.to_string()],
            PROBE_TIMEOUT,
        );
        if output.spawn_error.is_some() || output.wait_error.is_some() || output.timed_out {
            return ServiceRuntimeState::Unknown;
        }
        parse_sc_query(output.exit_code.unwrap_or(-1), &output.stdout)
    }
}

pub trait AdminChecker: Send + Sync {
    fn is_admin(&self) -> bool;
}

#[derive(Clone, Copy, Debug, Default)]
pub struct RealAdminChecker;

impl AdminChecker for RealAdminChecker {
    fn is_admin(&self) -> bool {
        token_is_elevated()
    }
}

/// 提权执行结果：与普通子进程分开，取消 UAC 不能被折叠成普通失败。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ElevatedOutcome {
    Exited(i32),
    Cancelled,
    Failed(String),
}

pub trait ElevatedRunner: Send + Sync {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> ElevatedOutcome;
}

/// `ProcessStartInfo { Verb = "runas", UseShellExecute = true }` 的等价物：ShellExecuteExW。
#[derive(Clone, Copy, Debug, Default)]
pub struct ShellExecuteRunner;

impl ElevatedRunner for ShellExecuteRunner {
    fn run(&self, program: &str, args: &[String], timeout: Duration) -> ElevatedOutcome {
        let verb = wide("runas");
        let file = wide(program);
        let parameters = wide(&join_windows_args(args));

        let mut info: SHELLEXECUTEINFOW = unsafe { std::mem::zeroed() };
        info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        info.fMask = SEE_MASK_NOCLOSEPROCESS;
        info.lpVerb = verb.as_ptr();
        info.lpFile = file.as_ptr();
        info.lpParameters = parameters.as_ptr();
        info.nShow = SW_HIDE;

        unsafe {
            SetLastError(0);
            if ShellExecuteExW(&mut info) == 0 {
                let error = GetLastError();
                return if error == ERROR_CANCELLED {
                    ElevatedOutcome::Cancelled
                } else {
                    ElevatedOutcome::Failed(format!(
                        "failed to start elevated {program} (Win32 error {error})"
                    ))
                };
            }

            if info.hProcess.is_null() {
                return ElevatedOutcome::Failed(
                    "elevated process did not return a process handle".to_string(),
                );
            }

            let timeout_ms = timeout.as_millis().min(u128::from(u32::MAX)) as u32;
            let wait = WaitForSingleObject(info.hProcess, timeout_ms);
            if wait == WAIT_TIMEOUT {
                TerminateProcess(info.hProcess, 1);
                let _ = WaitForSingleObject(info.hProcess, 5_000);
            }

            let mut exit_code = 0u32;
            let read_exit = GetExitCodeProcess(info.hProcess, &mut exit_code) != 0;
            CloseHandle(info.hProcess);

            if wait == WAIT_TIMEOUT {
                return ElevatedOutcome::Failed(format!(
                    "elevated {program} did not exit within {timeout_ms} ms"
                ));
            }
            if wait != WAIT_OBJECT_0 || !read_exit {
                return ElevatedOutcome::Failed(format!(
                    "elevated {program} could not be waited on (WaitForSingleObject = {wait})"
                ));
            }

            ElevatedOutcome::Exited(exit_code as i32)
        }
    }
}

/// 与服务安装脚本同名的注册表落点（`scripts/install-service.ps1`）。
const ACL_REGISTRY_SUBKEY: &str = "SOFTWARE\\PhotoPrivacy";
const INSTALLER_SID_VALUE: &str = "ServicePipeInstallerSid";
const ALLOWED_SIDS_VALUE: &str = "ServicePipeAllowedSids";
const ADVERTISED_PIPE_VALUE: &str = "ServicePipeName";
/// 提权脚本用的 PowerShell 注册表路径（与 `ACL_REGISTRY_SUBKEY` 指向同一位置）。
const ACL_REGISTRY_KEY_PATH: &str = r"HKLM:\SOFTWARE\PhotoPrivacy";

/// UI 安装链的安装者 SID 登记面（#200 落地，决策见 #82 D12）。
///
/// Design(#82): 取「发起安装的 UI 进程令牌」的 `TokenUser`，不取提权后子进程令牌。
/// 依据：`run_sc` 的提权走 runas，OTS（输入不同管理员凭据）时提权进程属于另一账户，而那不是
/// 安装者本人的桌面会话；UI 进程本身就是发起安装的交互用户，同账户 UAC 提权不改变 SID，
/// 因此进程令牌 SID 即 #70 语义要求的安装者 SID，也就是 AC1 里「安装者本人的非提权桌面会话」。
/// SID 解析失败时安装直接中止（#205 ④ 分型），不落「只能提权访问」的半成品服务。
pub trait InstallerSidRegistry: Send + Sync {
    /// 解析安装者 SID（`S-1-5-21-...`），失败时给出可展示原因（#205 ④ 失败分型）。
    fn installer_sid(&self) -> Result<String, String>;
    /// 登记 InstallerSid 与 AllowedSids（单元素），与脚本不带 `-AuthorizedUsers` 时的格式一致，
    /// 顺带覆盖上一次安装遗留的陈旧集合值。
    fn register(&self, sid: &str) -> Result<(), String>;
    /// 清除 InstallerSid / AllowedSids / ServicePipeName 三项登记。
    fn clear(&self) -> Result<(), String>;
    /// 读既有登记作为安装失败时的恢复快照；实现不得为了读取而创建注册表键。
    fn snapshot(&self) -> Result<InstallerRegistration, String>;
    /// 安装失败收尾：把登记恢复成 `previous` 的样子。
    ///
    /// 只有登记项仍是本尝试写入的 `attempted_sid` 时才恢复——若并发安装者已经改成自己的
    /// SID，就保留对方的值，避免拿本次的旧快照覆盖别人的新登记。
    fn rollback(&self, previous: &InstallerRegistration, attempted_sid: &str)
    -> Result<(), String>;
}

/// 安装前的登记快照：`None` 表示该项本来不存在，恢复时应当删除而不是写成空串。
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct InstallerRegistration {
    /// 安装前的 `InstallerSid` 值。
    pub installer_sid: Option<String>,
    /// 安装前的 `AllowedSids` 值。
    pub allowed_sids: Option<Vec<String>>,
}

pub struct WindowsInstallerSidRegistry;

impl InstallerSidRegistry for WindowsInstallerSidRegistry {
    fn installer_sid(&self) -> Result<String, String> {
        current_process_user_sid()
    }

    fn register(&self, sid: &str) -> Result<(), String> {
        if !is_sid_string(sid) {
            return Err(format!("refusing to register a malformed SID ({sid})"));
        }

        // 两项值不是原子写入：中途失败会把既有安装者授权换成「新 SID + 旧集合」的混合值，
        // 下次服务启动可能把原用户挡在管道外，所以失败时按读到的旧值恢复，而不是留着半成品。
        let (previous_installer_sid, previous_allowed_sids) = read_acl_registration()?;

        let key = open_acl_key()?;
        let written = set_string_value(key, INSTALLER_SID_VALUE, sid)
            .and_then(|()| set_multi_string_value(key, ALLOWED_SIDS_VALUE, &[sid]));
        let rolled_back = if written.is_err() {
            restore_registration_values(
                key,
                previous_installer_sid.as_deref(),
                previous_allowed_sids.as_deref(),
            )
        } else {
            Ok(())
        };
        unsafe { RegCloseKey(key) };

        match (written, rolled_back) {
            (Ok(()), _) => Ok(()),
            (Err(status), Ok(())) => Err(describe_registry_error(status)),
            (Err(status), Err(restore_status)) => Err(format!(
                "{}; restoring the previous registration also failed ({})",
                describe_registry_error(status),
                describe_registry_error(restore_status)
            )),
        }
    }

    fn clear(&self) -> Result<(), String> {
        let key = open_acl_key()?;
        let result = [
            INSTALLER_SID_VALUE,
            ALLOWED_SIDS_VALUE,
            ADVERTISED_PIPE_VALUE,
        ]
        .iter()
        .try_for_each(|name| delete_value(key, name));
        unsafe { RegCloseKey(key) };
        result.map_err(describe_registry_error)
    }

    fn snapshot(&self) -> Result<InstallerRegistration, String> {
        let (installer_sid, allowed_sids) = read_acl_registration()?;
        Ok(InstallerRegistration {
            installer_sid,
            allowed_sids,
        })
    }

    fn rollback(
        &self,
        previous: &InstallerRegistration,
        attempted_sid: &str,
    ) -> Result<(), String> {
        // 键不存在说明本次登记已被移除，没有可恢复的内容，也不该为回滚新建空键。
        let Some(existing) = open_acl_key_read()? else {
            return Ok(());
        };
        let current = read_string_value(existing, INSTALLER_SID_VALUE);
        unsafe { RegCloseKey(existing) };
        if !matches!(current, Ok(Some(ref sid)) if sid == attempted_sid) {
            // 读不到或登记已不是本尝试写下的值：并发安装者改了它，保留对方的值。
            return current.map(|_| ()).map_err(describe_registry_error);
        }

        let key = open_acl_key()?;
        let result = restore_registration_values(
            key,
            previous.installer_sid.as_deref(),
            previous.allowed_sids.as_deref(),
        );
        unsafe { RegCloseKey(key) };
        result.map_err(describe_registry_error)
    }
}

/// `S-1-...` 形态校验：DACL 白名单值不接受任意字符串，避免把注入内容写进服务的授权来源。
fn is_sid_string(sid: &str) -> bool {
    sid.len() <= 184
        && sid.starts_with("S-1-")
        && sid[4..]
            .split('-')
            .all(|part| !part.is_empty() && part.bytes().all(|byte| byte.is_ascii_digit()))
}

fn current_process_user_sid() -> Result<String, String> {
    unsafe {
        let mut token: HANDLE = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return Err(format!("OpenProcessToken failed ({})", last_error_code()));
        }

        let mut size = 0u32;
        GetTokenInformation(token, TokenUser, std::ptr::null_mut(), 0, &mut size);
        if size == 0 {
            let code = last_error_code();
            CloseHandle(token);
            return Err(format!("TokenUser size probe failed ({code})"));
        }

        let mut buffer = vec![0u8; size as usize];
        let ok = GetTokenInformation(
            token,
            TokenUser,
            buffer.as_mut_ptr() as *mut c_void,
            size,
            &mut size,
        );
        CloseHandle(token);
        if ok == 0 {
            return Err(format!(
                "GetTokenInformation(TokenUser) failed ({})",
                last_error_code()
            ));
        }

        let user = std::ptr::read_unaligned(buffer.as_ptr() as *const TOKEN_USER);
        let mut raw: *mut u16 = std::ptr::null_mut();
        if ConvertSidToStringSidW(user.User.Sid, &mut raw) == 0 {
            return Err(format!(
                "ConvertSidToStringSidW failed ({})",
                last_error_code()
            ));
        }

        // SID 字符串上限 184 字符（is_sid_string 同口径），扫描加边界避免越界读。
        let mut len = 0usize;
        while len < 184 && *raw.add(len) != 0 {
            len += 1;
        }
        let sid = String::from_utf16_lossy(std::slice::from_raw_parts(raw, len));
        let _ = LocalFree(raw as *mut c_void);

        if !is_sid_string(&sid) {
            return Err(format!("resolved SID is malformed ({sid})"));
        }
        Ok(sid)
    }
}

fn last_error_code() -> u32 {
    unsafe { GetLastError() }
}

fn describe_registry_error(code: u32) -> String {
    match code {
        5 => "access denied (5)".to_string(),
        other => format!("Win32 error {other}"),
    }
}

fn system32_directory() -> Option<PathBuf> {
    let mut buffer = [0u16; 260];
    let len = unsafe { GetSystemDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return None;
    }
    Some(PathBuf::from(String::from_utf16_lossy(
        &buffer[..len as usize],
    )))
}

/// 提权登记走系统自带 Windows PowerShell（#200）：不依赖发布目录里的脚本，也不走 PATH 搜索。
fn powershell_exe_path() -> Option<PathBuf> {
    Some(
        system32_directory()?
            .join("WindowsPowerShell")
            .join("v1.0")
            .join("powershell.exe"),
    )
}

/// 与 `scripts/install-service.ps1` 同格式落登记：单值 InstallerSid + 单元素 AllowedSids，
/// 覆盖上一次安装遗留的陈旧集合值。SID 在写入前已过 `is_sid_string` 校验，不含引号等注入字符。
fn registration_script(sid: &str) -> String {
    format!(
        "$ErrorActionPreference = 'Stop'; \
         $key = '{ACL_REGISTRY_KEY_PATH}'; \
         if (-not (Test-Path -LiteralPath $key)) {{ New-Item -Path $key -Force | Out-Null }}; \
         New-ItemProperty -LiteralPath $key -Name '{INSTALLER_SID_VALUE}' \
             -Value '{sid}' -PropertyType String -Force | Out-Null; \
         New-ItemProperty -LiteralPath $key -Name '{ALLOWED_SIDS_VALUE}' \
             -Value @('{sid}') -PropertyType MultiString -Force | Out-Null"
    )
}

fn clear_script() -> String {
    format!(
        "$ErrorActionPreference = 'Stop'; \
         $key = '{ACL_REGISTRY_KEY_PATH}'; \
         if (Test-Path -LiteralPath $key) {{ \
             '{INSTALLER_SID_VALUE}','{ALLOWED_SIDS_VALUE}','{ADVERTISED_PIPE_VALUE}' | \
                 ForEach-Object {{ \
                     if ($null -ne (Get-ItemProperty -LiteralPath $key -Name $_ -ErrorAction SilentlyContinue)) {{ \
                         Remove-ItemProperty -LiteralPath $key -Name $_ -ErrorAction Stop \
                     }} \
                 }} \
         }}"
    )
}

fn open_acl_key() -> Result<HKEY, String> {
    let subkey = wide(ACL_REGISTRY_SUBKEY);
    let mut key: HKEY = std::ptr::null_mut();
    let mut disposition = 0u32;
    let status = unsafe {
        RegCreateKeyExW(
            HKEY_LOCAL_MACHINE,
            subkey.as_ptr(),
            0,
            std::ptr::null(),
            REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE,
            std::ptr::null(),
            &mut key,
            &mut disposition,
        )
    };
    if status != 0 {
        return Err(describe_registry_error(status));
    }
    Ok(key)
}

/// 只读打开登记键：键不存在返回 `None`，不用 `RegCreateKeyExW` 顺手建出空键。
fn open_acl_key_read() -> Result<Option<HKEY>, String> {
    let subkey = wide(ACL_REGISTRY_SUBKEY);
    let mut key: HKEY = std::ptr::null_mut();
    let status = unsafe {
        RegOpenKeyExW(
            HKEY_LOCAL_MACHINE,
            subkey.as_ptr(),
            0,
            KEY_QUERY_VALUE,
            &mut key,
        )
    };
    if status == ERROR_FILE_NOT_FOUND {
        return Ok(None);
    }
    if status != 0 {
        return Err(describe_registry_error(status));
    }
    Ok(Some(key))
}

fn read_raw_value(key: HKEY, name: &str) -> Result<Option<(u32, Vec<u8>)>, u32> {
    let name = wide(name);
    let mut kind = 0u32;
    let mut size = 0u32;
    let status = unsafe {
        RegQueryValueExW(
            key,
            name.as_ptr(),
            std::ptr::null_mut(),
            &mut kind,
            std::ptr::null_mut(),
            &mut size,
        )
    };
    // 值不存在是「没有旧值」，不是读取失败。
    if status == ERROR_FILE_NOT_FOUND {
        return Ok(None);
    }
    if status != 0 {
        return Err(status);
    }
    let mut buffer = vec![0u8; size as usize];
    let status = unsafe {
        RegQueryValueExW(
            key,
            name.as_ptr(),
            std::ptr::null_mut(),
            &mut kind,
            buffer.as_mut_ptr(),
            &mut size,
        )
    };
    if status != 0 {
        return Err(status);
    }
    buffer.truncate(size as usize);
    Ok(Some((kind, buffer)))
}

/// 读回既有 `REG_SZ`：值缺失或类型不符都算「没有可恢复的旧值」，只有真实读取失败才报错。
fn read_string_value(key: HKEY, name: &str) -> Result<Option<String>, u32> {
    let Some((kind, bytes)) = read_raw_value(key, name)? else {
        return Ok(None);
    };
    if kind != REG_SZ {
        return Ok(None);
    }
    let units: Vec<u16> = bytes
        .as_chunks::<2>()
        .0
        .iter()
        .map(|chunk| u16::from_le_bytes(*chunk))
        .collect();
    Ok(Some(
        String::from_utf16_lossy(&units)
            .trim_end_matches('\0')
            .to_string(),
    ))
}

/// 读回既有 `REG_MULTI_SZ`，语义同 `read_string_value`。
fn read_multi_string_value(key: HKEY, name: &str) -> Result<Option<Vec<String>>, u32> {
    let Some((kind, bytes)) = read_raw_value(key, name)? else {
        return Ok(None);
    };
    if kind != REG_MULTI_SZ {
        return Ok(None);
    }
    let mut values: Vec<String> = Vec::new();
    let mut current: Vec<u16> = Vec::new();
    for unit in bytes
        .as_chunks::<2>()
        .0
        .iter()
        .map(|chunk| u16::from_le_bytes(*chunk))
    {
        if unit == 0 {
            if current.is_empty() {
                break;
            }
            values.push(String::from_utf16_lossy(&current));
            current.clear();
        } else {
            current.push(unit);
        }
    }
    if !current.is_empty() {
        values.push(String::from_utf16_lossy(&current));
    }
    Ok(Some(values))
}

/// 按旧值恢复：有值写回，旧值为空才删除——不把既有安装者授权换成一半新值。
fn restore_registration_values(
    key: HKEY,
    installer_sid: Option<&str>,
    allowed_sids: Option<&[String]>,
) -> Result<(), u32> {
    match installer_sid {
        Some(sid) => set_string_value(key, INSTALLER_SID_VALUE, sid)?,
        None => delete_value(key, INSTALLER_SID_VALUE)?,
    }
    match allowed_sids {
        Some(values) => {
            let borrowed: Vec<&str> = values.iter().map(String::as_str).collect();
            set_multi_string_value(key, ALLOWED_SIDS_VALUE, &borrowed)?;
        }
        None => delete_value(key, ALLOWED_SIDS_VALUE)?,
    }
    Ok(())
}

/// 读既有登记的两项值，供登记失败时恢复：键不存在即「从未登记过」。
fn read_acl_registration() -> Result<(Option<String>, Option<Vec<String>>), String> {
    let Some(key) = open_acl_key_read()? else {
        return Ok((None, None));
    };
    let result = read_string_value(key, INSTALLER_SID_VALUE).and_then(|installer_sid| {
        read_multi_string_value(key, ALLOWED_SIDS_VALUE)
            .map(|allowed_sids| (installer_sid, allowed_sids))
    });
    unsafe { RegCloseKey(key) };
    result.map_err(describe_registry_error)
}

fn set_value_bytes(key: HKEY, name: &str, kind: u32, data: &[u8]) -> Result<(), u32> {
    let name = wide(name);
    let status = unsafe {
        RegSetValueExW(
            key,
            name.as_ptr(),
            0,
            kind,
            data.as_ptr(),
            data.len() as u32,
        )
    };
    if status == 0 { Ok(()) } else { Err(status) }
}

fn set_string_value(key: HKEY, name: &str, value: &str) -> Result<(), u32> {
    set_value_bytes(key, name, REG_SZ, &string_value_bytes(value))
}

/// `REG_SZ` 的字节数必须包含结尾 NUL，否则按 REG_SZ 读回的值没有终止符。
fn string_value_bytes(value: &str) -> Vec<u8> {
    let mut units: Vec<u16> = value.encode_utf16().collect();
    units.push(0);
    units.iter().flat_map(|unit| unit.to_le_bytes()).collect()
}

/// `REG_MULTI_SZ`：每个串以 NUL 结尾，整体再补一个 NUL，与 PowerShell 的 `-Type MultiString` 一致。
fn set_multi_string_value(key: HKEY, name: &str, values: &[&str]) -> Result<(), u32> {
    let mut units: Vec<u16> = Vec::new();
    for value in values {
        units.extend(value.encode_utf16());
        units.push(0);
    }
    units.push(0);

    let bytes: Vec<u8> = units.iter().flat_map(|unit| unit.to_le_bytes()).collect();
    set_value_bytes(key, name, REG_MULTI_SZ, &bytes)
}

fn delete_value(key: HKEY, name: &str) -> Result<(), u32> {
    let name = wide(name);
    let status = unsafe { RegDeleteValueW(key, name.as_ptr()) };
    // ERROR_FILE_NOT_FOUND(2)：值本来就不在，视为已清理。
    if status == 0 || status == 2 {
        Ok(())
    } else {
        Err(status)
    }
}

pub struct WindowsOps {
    runner: Arc<dyn ProcessRunner>,
    elevated: Arc<dyn ElevatedRunner>,
    admin: Arc<dyn AdminChecker>,
    probe: Arc<dyn ServiceProbe>,
    acl: Arc<dyn InstallerSidRegistry>,
}

impl WindowsOps {
    pub fn new(
        runner: Arc<dyn ProcessRunner>,
        elevated: Arc<dyn ElevatedRunner>,
        admin: Arc<dyn AdminChecker>,
        probe: Arc<dyn ServiceProbe>,
        acl: Arc<dyn InstallerSidRegistry>,
    ) -> Self {
        Self {
            runner,
            elevated,
            admin,
            probe,
            acl,
        }
    }

    /// 登记安装者 SID：UI 已提权就直写，否则走提权通道（HKLM 写需要管理员）。
    fn register_installer_sid(&self, sid: &str) -> Result<(), String> {
        if !is_sid_string(sid) {
            return Err(format!("refusing to register a malformed SID ({sid})"));
        }
        if self.admin.is_admin() {
            return self.acl.register(sid);
        }
        self.run_elevated_powershell(&registration_script(sid), "service pipe ACL registration")
    }

    fn clear_installer_sid(&self) -> Result<(), String> {
        if self.admin.is_admin() {
            return self.acl.clear();
        }
        self.run_elevated_powershell(&clear_script(), "service pipe ACL cleanup")
    }
    /// 提权执行一段 PowerShell：`sc.exe` 的 runas 只提升子进程，UI 自身仍非管理员，
    /// 因此 HKLM 里的登记必须同样经提权通道落盘（#200）。
    fn run_elevated_powershell(&self, script: &str, what: &str) -> Result<(), String> {
        let Some(powershell) = powershell_exe_path() else {
            return Err(format!(
                "Unable to resolve WindowsPowerShell\\v1.0\\powershell.exe for the {what}"
            ));
        };
        let program = powershell.to_string_lossy().to_string();
        let args = vec![
            "-NoProfile".to_string(),
            "-NonInteractive".to_string(),
            "-ExecutionPolicy".to_string(),
            "Bypass".to_string(),
            "-Command".to_string(),
            script.to_string(),
        ];

        match self.elevated.run(&program, &args, COMMAND_TIMEOUT) {
            ElevatedOutcome::Exited(0) => Ok(()),
            ElevatedOutcome::Exited(code) => Err(format!("{what} failed (exit {code})")),
            ElevatedOutcome::Cancelled => Err("elevation was cancelled".to_string()),
            ElevatedOutcome::Failed(message) => Err(message),
        }
    }

    fn run_sc(&self, args: Vec<String>, require_admin: bool) -> ServiceCommandResult {
        let Some(sc_exe) = sc_exe_path() else {
            return ServiceCommandResult::failed(
                "Unable to resolve System32\\sc.exe for the service command",
                None,
            );
        };
        let sc_exe = sc_exe.to_string_lossy();

        if require_admin && !self.admin.is_admin() {
            return match self.elevated.run(&sc_exe, &args, COMMAND_TIMEOUT) {
                ElevatedOutcome::Exited(0) => {
                    ServiceCommandResult::success(Some(0), "Operation succeeded")
                }
                ElevatedOutcome::Exited(code) => {
                    ServiceCommandResult::failed(translate_exit_code(Some(code)), Some(code))
                }
                ElevatedOutcome::Cancelled => {
                    ServiceCommandResult::elevation_cancelled("Elevation was cancelled")
                }
                ElevatedOutcome::Failed(message) => ServiceCommandResult::failed(message, None),
            };
        }

        command_result(self.runner.run(&sc_exe, &args, COMMAND_TIMEOUT))
    }

    fn ensure_removed_before_install(&self) -> ServiceCommandResult {
        let state = self.probe.state();
        if !state.is_installed() {
            return ServiceCommandResult::success(None, "Service is not installed");
        }

        if state.is_running_like() {
            let stop = self.run_sc(sc_stop(), true);
            if stop.status == ServiceCommandStatus::ElevationCancelled {
                return stop;
            }
            if stop.status == ServiceCommandStatus::Failed && stop.exit_code != Some(1062) {
                return stop.with_friendly_message();
            }
        }

        let delete = self.run_sc(sc_delete(), true);
        if delete.status == ServiceCommandStatus::ElevationCancelled {
            return delete;
        }
        if delete.status == ServiceCommandStatus::Failed
            && !matches!(delete.exit_code, Some(1060) | Some(1072))
        {
            return delete.with_friendly_message();
        }

        ServiceCommandResult::success(None, "Service was removed")
    }
}

impl ServiceOps for WindowsOps {
    fn install(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
        let (Some(worker), Some(config)) = (worker, config) else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker and config.json must be next to the UI to install the service",
                None,
            );
        };
        if !worker.is_file() {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must exist next to the UI to install the service",
                None,
            );
        }
        if !is_worker_executable(worker) {
            return ServiceCommandResult::failed(
                "The service must be installed from PhotoPrivacyWorker.exe",
                None,
            );
        }

        let args = match build_install_arguments(worker, config) {
            Ok(args) => args,
            Err(message) => return ServiceCommandResult::failed(message, None),
        };

        // #200 / #205 ④：SID 解析失败一律中止安装，不落一个「只能提权访问」的半成品服务。
        let installer_sid = match self.acl.installer_sid() {
            Ok(sid) => sid,
            Err(reason) => {
                return ServiceCommandResult::failed(
                    format!(
                        "Unable to resolve the installing user SID ({reason}); the service was not \
                         installed because the desktop session could never be granted pipe access"
                    ),
                    None,
                );
            }
        };

        // #200 返修：先清旧服务再落登记——停/删失败或用户取消时，HKLM 里的既有登记必须原样
        // 保留，否则服务重启后会拿新 SID 的 ACL 把原本被授权的用户挡在管道外。
        let precheck = self.ensure_removed_before_install();
        if precheck.status != ServiceCommandStatus::Success {
            return precheck;
        }

        // 快照必须在 register 之前读：失败退出时用它把登记恢复原状，
        // 而不是给 HKLM 留下指向不存在服务的授权值。读不到快照时退回「清掉本次登记」的旧语义。
        let previous_registration = self.acl.snapshot().unwrap_or_default();
        let rollback_registration = |status: ServiceCommandResult| -> ServiceCommandResult {
            match self.acl.rollback(&previous_registration, &installer_sid) {
                Ok(()) => status,
                Err(reason) => ServiceCommandResult {
                    message: format!(
                        "{} The service pipe ACL registration left by this attempt could not be \
                         rolled back ({reason}).",
                        status.message
                    ),
                    ..status
                },
            }
        };

        // #200 AC2：与 scripts/install-service.ps1 同格式落登记，写不进就不装——
        // 否则服务建好了却只有提权会话能连，用户看到「安装成功」却用不了。
        if let Err(reason) = self.register_installer_sid(&installer_sid) {
            // 用户取消 UAC 不是失败：与 sc.exe 通道一致报 ElevationCancelled，UI 才不会提示"安装失败"。
            let mut aborted = ServiceCommandResult::failed(
                format!(
                    "Unable to register the service pipe ACL for the installing user ({reason}); \
                     the service was not installed"
                ),
                None,
            );
            if reason.contains("elevation was cancelled") {
                aborted.status = ServiceCommandStatus::ElevationCancelled;
            }
            return aborted;
        }

        let mut create = self.run_sc(args.clone(), true);
        if create.exit_code == Some(1073) {
            // 旧 UI 的竞态兜底：清理窗口里服务又被安装回来时，再删一次再 create。
            let retry = self.ensure_removed_before_install();
            if retry.status != ServiceCommandStatus::Success {
                // 重试停/删失败或用户取消：服务没建成，本次写下的登记也必须撤回，
                // 否则被安装回来的服务会拿它当管道 ACL 来源。
                return rollback_registration(retry);
            }
            create = self.run_sc(args, true);
        }

        let created = create.with_friendly_message();
        if created.status == ServiceCommandStatus::Success {
            return created;
        }
        rollback_registration(created)
    }

    fn start(&self, worker: Option<&Path>, config: Option<&Path>) -> ServiceCommandResult {
        let Some(worker) = worker else {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must be next to the UI to start the service",
                None,
            );
        };
        if !worker.is_file() {
            return ServiceCommandResult::failed(
                "PhotoPrivacyWorker must exist next to the UI to start the service",
                None,
            );
        }
        if !is_worker_executable(worker) {
            return ServiceCommandResult::failed(
                "The service must target PhotoPrivacyWorker.exe",
                None,
            );
        }

        if !self.probe.state().is_installed() {
            return ServiceCommandResult::failed(translate_exit_code(Some(1060)), Some(1060));
        }

        if let Some(config) = config.filter(|path| !path.as_os_str().is_empty()) {
            let args = match build_reconfig_arguments(worker, config) {
                Ok(args) => args,
                Err(message) => return ServiceCommandResult::failed(message, None),
            };
            let reconfigured = self.run_sc(args, true);
            match reconfigured.status {
                ServiceCommandStatus::Failed => return reconfigured.with_friendly_message(),
                ServiceCommandStatus::ElevationCancelled => return reconfigured,
                _ => {}
            }
        }

        self.run_sc(sc_start(), true).with_friendly_message()
    }

    fn stop(&self) -> ServiceCommandResult {
        self.run_sc(sc_stop(), true).with_friendly_message()
    }

    fn uninstall(&self) -> ServiceCommandResult {
        if self.probe.state().is_running_like() {
            let stop = self.run_sc(sc_stop(), true);
            if stop.status == ServiceCommandStatus::ElevationCancelled {
                return stop;
            }
            if stop.status == ServiceCommandStatus::Failed && stop.exit_code != Some(1062) {
                return stop.with_friendly_message();
            }
        }

        let deleted = self.run_sc(sc_delete(), true).with_friendly_message();

        // #200：服务已不在时（1060）也要清掉遗留登记，否则陈旧 SID 会继续被当作管道授权来源；
        // 提权被取消则保持原样，避免服务仍在运行却把登记清空。
        let service_gone = matches!(deleted.status, ServiceCommandStatus::Success)
            || (deleted.status == ServiceCommandStatus::Failed && deleted.exit_code == Some(1060));
        if !service_gone {
            return deleted;
        }

        match self.clear_installer_sid() {
            Ok(()) => deleted,
            Err(reason) => ServiceCommandResult {
                message: format!(
                    "{} The service pipe ACL registration could not be cleared ({reason}).",
                    deleted.message
                ),
                ..deleted
            },
        }
    }
}

fn command_result(output: CommandOutput) -> ServiceCommandResult {
    result_from_output(
        "sc.exe",
        output,
        &translate_exit_code(Some(0)),
        "Elevation was cancelled",
        |_| false,
        |output| translate_exit_code(output.exit_code),
    )
}

fn sc_stop() -> Vec<String> {
    vec!["stop".to_string(), SERVICE_NAME.to_string()]
}

fn sc_start() -> Vec<String> {
    vec!["start".to_string(), SERVICE_NAME.to_string()]
}

fn sc_delete() -> Vec<String> {
    vec!["delete".to_string(), SERVICE_NAME.to_string()]
}

pub(crate) fn build_install_arguments(worker: &Path, config: &Path) -> Result<Vec<String>, String> {
    validate_path(worker)?;
    validate_path(config)?;
    Ok(vec![
        "create".to_string(),
        SERVICE_NAME.to_string(),
        "binPath=".to_string(),
        bin_path(worker, config),
        "start=".to_string(),
        "auto".to_string(),
    ])
}

pub(crate) fn build_reconfig_arguments(
    worker: &Path,
    config: &Path,
) -> Result<Vec<String>, String> {
    validate_path(worker)?;
    validate_path(config)?;
    Ok(vec![
        "config".to_string(),
        SERVICE_NAME.to_string(),
        "binPath=".to_string(),
        bin_path(worker, config),
        "start=".to_string(),
        "auto".to_string(),
    ])
}

fn bin_path(worker: &Path, config: &Path) -> String {
    format!(
        "\"{}\" --mode service --config \"{}\"",
        worker.display(),
        config.display()
    )
}

fn validate_path(path: &Path) -> Result<(), String> {
    let raw = path.to_string_lossy();
    if raw.trim().is_empty() {
        return Err("Service path must not be empty".to_string());
    }
    if raw
        .chars()
        .any(|ch| matches!(ch, '"' | '`' | '$' | '|' | '>' | '<' | '&' | '\0'))
    {
        return Err(format!(
            "Service path contains characters that cannot be passed to sc.exe: {raw}"
        ));
    }
    if !path.is_absolute() {
        return Err(format!("Service path must be absolute: {raw}"));
    }
    Ok(())
}

fn is_worker_executable(worker: &Path) -> bool {
    worker
        .file_name()
        .map(|name| {
            name.to_string_lossy()
                .eq_ignore_ascii_case("PhotoPrivacyWorker.exe")
        })
        .unwrap_or(false)
}

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(std::iter::once(0)).collect()
}

fn join_windows_args(args: &[String]) -> String {
    args.iter()
        .map(|arg| quote_windows_arg(arg))
        .collect::<Vec<_>>()
        .join(" ")
}

/// MSVC 运行库的 argv 引用规则：反斜杠只在引号前转义，结尾反斜杠加倍。
fn quote_windows_arg(arg: &str) -> String {
    if !arg.is_empty()
        && !arg
            .chars()
            .any(|ch| matches!(ch, ' ' | '\t' | '"' | '\n' | '\u{b}'))
    {
        return arg.to_string();
    }

    let mut quoted = String::from("\"");
    let mut backslashes = 0usize;
    for ch in arg.chars() {
        match ch {
            '\\' => backslashes += 1,
            '"' => {
                quoted.push_str(&"\\".repeat(backslashes * 2 + 1));
                quoted.push('"');
                backslashes = 0;
            }
            _ => {
                quoted.push_str(&"\\".repeat(backslashes));
                backslashes = 0;
                quoted.push(ch);
            }
        }
    }
    quoted.push_str(&"\\".repeat(backslashes * 2));
    quoted.push('"');
    quoted
}

fn token_is_elevated() -> bool {
    unsafe {
        let mut token: HANDLE = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return false;
        }

        let mut elevation = TOKEN_ELEVATION { TokenIsElevated: 0 };
        let mut returned = 0u32;
        let ok = GetTokenInformation(
            token,
            TokenElevation,
            &mut elevation as *mut _ as *mut c_void,
            std::mem::size_of::<TOKEN_ELEVATION>() as u32,
            &mut returned,
        );
        CloseHandle(token);
        ok != 0 && elevation.TokenIsElevated != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;
    use std::sync::Mutex;

    #[derive(Default)]
    struct QueueRunner {
        outputs: Mutex<VecDeque<CommandOutput>>,
        calls: Mutex<Vec<Vec<String>>>,
        programs: Mutex<Vec<String>>,
    }

    impl QueueRunner {
        fn new(outputs: Vec<CommandOutput>) -> Self {
            Self {
                outputs: Mutex::new(outputs.into()),
                calls: Mutex::new(Vec::new()),
                programs: Mutex::new(Vec::new()),
            }
        }

        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.lock().unwrap().clone()
        }

        fn programs(&self) -> Vec<String> {
            self.programs.lock().unwrap().clone()
        }
    }

    impl ProcessRunner for QueueRunner {
        fn run(&self, program: &str, args: &[String], _: Duration) -> CommandOutput {
            self.programs.lock().unwrap().push(program.to_string());
            self.calls.lock().unwrap().push(args.to_vec());
            self.outputs
                .lock()
                .unwrap()
                .pop_front()
                .unwrap_or_else(|| CommandOutput::spawn_error("no scripted output"))
        }
    }

    struct FixedProbe(ServiceRuntimeState);

    impl ServiceProbe for FixedProbe {
        fn state(&self) -> ServiceRuntimeState {
            self.0
        }
    }

    struct FixedAdmin(bool);

    impl AdminChecker for FixedAdmin {
        fn is_admin(&self) -> bool {
            self.0
        }
    }

    #[derive(Default)]
    struct RecordingElevated {
        calls: Mutex<Vec<Vec<String>>>,
        programs: Mutex<Vec<String>>,
        outcome: Mutex<Option<ElevatedOutcome>>,
    }

    impl RecordingElevated {
        fn new(outcome: ElevatedOutcome) -> Self {
            Self {
                calls: Mutex::new(Vec::new()),
                programs: Mutex::new(Vec::new()),
                outcome: Mutex::new(Some(outcome)),
            }
        }

        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.lock().unwrap().clone()
        }

        fn programs(&self) -> Vec<String> {
            self.programs.lock().unwrap().clone()
        }
    }

    impl ElevatedRunner for RecordingElevated {
        fn run(&self, program: &str, args: &[String], _: Duration) -> ElevatedOutcome {
            self.programs.lock().unwrap().push(program.to_string());
            self.calls.lock().unwrap().push(args.to_vec());
            self.outcome
                .lock()
                .unwrap()
                .clone()
                .unwrap_or_else(|| ElevatedOutcome::Failed("no scripted outcome".to_string()))
        }
    }

    const TEST_INSTALLER_SID: &str = "S-1-5-21-111111111-222222222-333333333-1001";

    /// 安装者 SID 登记的测试替身：可分别模拟 SID 解析失败、登记失败、清理失败（#200 / #205 ④）。
    #[derive(Default)]
    struct FakeAcl {
        sid: Mutex<Option<Result<String, String>>>,
        register_error: Mutex<Option<String>>,
        clear_error: Mutex<Option<String>>,
        registered: Mutex<Vec<String>>,
        registration: Mutex<InstallerRegistration>,
        clear_calls: Mutex<u32>,
    }

    impl FakeAcl {
        fn healthy() -> Self {
            Self::default()
        }

        fn unresolved(reason: &str) -> Self {
            let acl = Self::default();
            *acl.sid.lock().unwrap() = Some(Err(reason.to_string()));
            acl
        }

        fn rejecting_registration(reason: &str) -> Self {
            let acl = Self::default();
            *acl.register_error.lock().unwrap() = Some(reason.to_string());
            acl
        }

        fn rejecting_clear(reason: &str) -> Self {
            let acl = Self::default();
            *acl.clear_error.lock().unwrap() = Some(reason.to_string());
            acl
        }

        fn registered(&self) -> Vec<String> {
            self.registered.lock().unwrap().clone()
        }

        /// 预置一次「安装前已存在的登记」，用于验证失败恢复。
        fn with_existing_registration(sid: &str) -> Self {
            let acl = Self::default();
            *acl.registration.lock().unwrap() = InstallerRegistration {
                installer_sid: Some(sid.to_string()),
                allowed_sids: Some(vec![sid.to_string()]),
            };
            acl
        }

        fn registration(&self) -> InstallerRegistration {
            self.registration.lock().unwrap().clone()
        }

        fn clear_calls(&self) -> u32 {
            *self.clear_calls.lock().unwrap()
        }
    }

    impl InstallerSidRegistry for FakeAcl {
        fn installer_sid(&self) -> Result<String, String> {
            match self.sid.lock().unwrap().clone() {
                Some(result) => result,
                None => Ok(TEST_INSTALLER_SID.to_string()),
            }
        }

        fn register(&self, sid: &str) -> Result<(), String> {
            if let Some(error) = self.register_error.lock().unwrap().clone() {
                return Err(error);
            }
            self.registered.lock().unwrap().push(sid.to_string());
            *self.registration.lock().unwrap() = InstallerRegistration {
                installer_sid: Some(sid.to_string()),
                allowed_sids: Some(vec![sid.to_string()]),
            };
            Ok(())
        }

        fn clear(&self) -> Result<(), String> {
            *self.clear_calls.lock().unwrap() += 1;
            match self.clear_error.lock().unwrap().clone() {
                Some(error) => Err(error),
                None => {
                    *self.registration.lock().unwrap() = InstallerRegistration::default();
                    Ok(())
                }
            }
        }

        fn snapshot(&self) -> Result<InstallerRegistration, String> {
            Ok(self.registration())
        }

        fn rollback(
            &self,
            previous: &InstallerRegistration,
            attempted_sid: &str,
        ) -> Result<(), String> {
            let mut current = self.registration.lock().unwrap();
            if current.installer_sid.as_deref() != Some(attempted_sid) {
                // 已被并发安装者改写：保留对方的值，不拿旧快照覆盖。
                return Ok(());
            }
            *current = previous.clone();
            drop(current);
            *self.clear_calls.lock().unwrap() += 1;
            match self.clear_error.lock().unwrap().clone() {
                Some(error) => Err(error),
                None => Ok(()),
            }
        }
    }

    /// 防裸名劫持（#58）：提权通道里的程序必须来自 System32 的系统程序。
    fn is_system32_program(program: &str) -> bool {
        let lower = program.to_ascii_lowercase();
        if !lower.contains("\\system32\\") {
            return false;
        }
        lower.ends_with("\\sc.exe") || lower.ends_with("\\powershell.exe")
    }

    fn ok() -> CommandOutput {
        CommandOutput {
            exit_code: Some(0),
            ..CommandOutput::default()
        }
    }

    fn code(code: i32) -> CommandOutput {
        CommandOutput {
            exit_code: Some(code),
            ..CommandOutput::default()
        }
    }

    fn temp_worker() -> std::path::PathBuf {
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let seq = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let dir =
            std::env::temp_dir().join(format!("photoprivacy-windows-{}-{seq}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("PhotoPrivacyWorker.exe");
        std::fs::write(&path, b"").unwrap();
        path
    }

    fn cleanup_worker(worker: &Path) {
        let _ = std::fs::remove_file(worker);
        if let Some(dir) = worker.parent() {
            let _ = std::fs::remove_dir(dir);
        }
    }

    fn ops(
        state: ServiceRuntimeState,
        outputs: Vec<CommandOutput>,
        admin: bool,
        elevated: RecordingElevated,
    ) -> (WindowsOps, Arc<QueueRunner>, Arc<RecordingElevated>) {
        let (ops, runner, _, elevated) =
            ops_with_acl(state, outputs, admin, elevated, FakeAcl::healthy());
        (ops, runner, elevated)
    }

    fn ops_with_acl(
        state: ServiceRuntimeState,
        outputs: Vec<CommandOutput>,
        admin: bool,
        elevated: RecordingElevated,
        acl: FakeAcl,
    ) -> (
        WindowsOps,
        Arc<QueueRunner>,
        Arc<FakeAcl>,
        Arc<RecordingElevated>,
    ) {
        let runner = Arc::new(QueueRunner::new(outputs));
        let elevated = Arc::new(elevated);
        let acl = Arc::new(acl);
        let ops = WindowsOps::new(
            runner.clone(),
            elevated.clone(),
            Arc::new(FixedAdmin(admin)),
            Arc::new(FixedProbe(state)),
            acl.clone(),
        );
        (ops, runner, acl, elevated)
    }

    #[test]
    fn install_arguments_match_old_sc_command() {
        // 防：binPath 的引号层级漂移，Worker 收到被截断的 --config 参数（#58，旧 ServiceManagerTests）。
        let args = build_install_arguments(
            Path::new(r"C:\Program Files\Photo Privacy\PhotoPrivacyWorker.exe"),
            Path::new(r"D:\cfg path\config.json"),
        )
        .unwrap();

        assert_eq!(
            args,
            vec![
                "create",
                "PhotoPrivacyCleaner",
                "binPath=",
                "\"C:\\Program Files\\Photo Privacy\\PhotoPrivacyWorker.exe\" --mode service --config \"D:\\cfg path\\config.json\"",
                "start=",
                "auto",
            ]
        );
    }

    #[test]
    fn install_rejects_unsafe_or_relative_paths() {
        // 防：路径里的引号/管道符被拼进 sc 命令行，形成额外参数（#58，旧 BuildReconfigArguments 校验）。
        assert!(
            build_install_arguments(
                Path::new(r"C:\app\PhotoPrivacyWorker.exe"),
                Path::new(r#"C:\cfg\"x"\config.json"#)
            )
            .is_err()
        );
        assert!(
            build_install_arguments(
                Path::new(r"relative\PhotoPrivacyWorker.exe"),
                Path::new(r"C:\cfg\config.json")
            )
            .is_err()
        );
    }

    #[test]
    fn worker_executable_check_is_case_insensitive() {
        // 防：大小写差异让合法 Worker 被拒绝，或任意 exe 被安装成服务（#58）。
        assert!(is_worker_executable(Path::new(
            r"C:\app\PhotoPrivacyWorker.exe"
        )));
        assert!(is_worker_executable(Path::new(
            r"C:\app\photoprivacyworker.EXE"
        )));
        assert!(!is_worker_executable(Path::new(r"C:\app\PhotoPrivacy.exe")));
    }

    #[test]
    fn sc_exe_resolves_to_absolute_system32_path() {
        // 防：裸名 sc.exe 在当前目录可写时被同名二进制劫持（CWE-427，#58 CodeRabbit）。
        let path = sc_exe_path().expect("System32 should resolve");
        assert!(path.is_absolute());
        assert!(
            path.to_string_lossy()
                .to_ascii_lowercase()
                .ends_with("\\system32\\sc.exe")
        );
    }

    #[test]
    fn probe_and_ops_use_absolute_sc_path() {
        // 防：探测或普通/提权操作回退到裸名 sc.exe，重新引入当前目录劫持面（#58）。
        let runner = Arc::new(QueueRunner::new(vec![CommandOutput {
            exit_code: Some(0),
            stdout: "        STATE              : 4  RUNNING\n".to_string(),
            ..CommandOutput::default()
        }]));
        let probe = WindowsProbe::new(runner.clone());
        assert_eq!(probe.state(), ServiceRuntimeState::Running);
        assert!(
            runner.programs()[0]
                .to_ascii_lowercase()
                .ends_with("\\system32\\sc.exe")
        );

        let (ops_elevated, runner, elevated) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
        );
        let _ = ops_elevated.uninstall();
        assert!(runner.programs().is_empty(), "非管理员时卸装应走提权路径");
        assert!(
            elevated
                .programs()
                .iter()
                .all(|program| is_system32_program(program))
        );

        let (ops_direct, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );
        let _ = ops_direct.uninstall();
        assert!(
            runner
                .programs()
                .iter()
                .all(|program| is_system32_program(program))
        );
    }

    #[test]
    fn install_stops_and_deletes_existing_service_before_create() {
        // 防：服务已运行时直接 create 得到 1073，安装按钮永久失败（#58，旧 Auto_Retry 测试）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        let calls = runner.calls();
        assert_eq!(calls.len(), 3);
        assert_eq!(calls[0], ["stop", "PhotoPrivacyCleaner"]);
        assert_eq!(calls[1], ["delete", "PhotoPrivacyCleaner"]);
        assert_eq!(calls[2][0], "create");
        cleanup_worker(&worker);
    }

    #[test]
    fn install_requires_worker_and_config_options() {
        // 防：缺少 Worker/config 上下文时仍拉起提权进程，用户只看到平台错误（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::NotInstalled,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        assert_eq!(ops.install(None, None).status, ServiceCommandStatus::Failed);
        assert_eq!(
            ops.install(
                Some(Path::new(r"C:\definitely-missing\PhotoPrivacyWorker.exe")),
                Some(Path::new(r"C:\cfg\config.json"))
            )
            .status,
            ServiceCommandStatus::Failed
        );
        assert!(runner.calls().is_empty());
    }

    #[test]
    fn install_retries_after_service_already_exists() {
        // 防：清理与 create 之间服务被重新装回时不再重试，安装停在 1073（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok(), code(1073), ok(), ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(runner.calls().len(), 6);
        cleanup_worker(&worker);
    }

    #[test]
    fn install_does_not_reprompt_after_cancelled_precheck() {
        // 防：用户取消预清理的 UAC 后再次弹 create 提权框，四态反馈丢失（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Running,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
        );
        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");

        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert_eq!(elevated.calls().len(), 1);
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn start_fails_fast_when_service_is_not_installed() {
        // 防：未安装时仍跑 sc config/start，把 1060 包成含混的失败（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::NotInstalled,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let result = ops.start(Some(worker.as_path()), None);

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(result.exit_code, Some(1060));
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn start_requires_worker_option() {
        // 防：没有 Worker 上下文时仍尝试 sc config/start，启动结果无法解释（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            true,
            RecordingElevated::default(),
        );

        assert_eq!(ops.start(None, None).status, ServiceCommandStatus::Failed);
        assert!(runner.calls().is_empty());
    }

    #[test]
    fn start_reconfigures_then_starts() {
        // 防：启动前不写回最新 Worker/config 路径，服务仍用旧配置（#58）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Stopped,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.start(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        let calls = runner.calls();
        assert_eq!(calls.len(), 2);
        assert_eq!(calls[0][0], "config");
        assert_eq!(calls[1], ["start", "PhotoPrivacyCleaner"]);
        cleanup_worker(&worker);
    }

    #[test]
    fn uninstall_stops_running_service_before_delete() {
        // 防：运行中直接 delete 得到 1072/1055，卸载失败（#58，旧 Uninstall_Should_Stop 测试）。
        let (ops, runner, _) = ops(
            ServiceRuntimeState::Running,
            vec![ok(), ok()],
            true,
            RecordingElevated::default(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(
            runner.calls(),
            vec![
                vec!["stop".to_string(), "PhotoPrivacyCleaner".to_string()],
                vec!["delete".to_string(), "PhotoPrivacyCleaner".to_string()],
            ]
        );
    }

    #[test]
    fn elevation_cancelled_is_not_reported_as_failure() {
        // 防：用户取消 UAC 被归为 Failed，四态反馈退化成三态（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
        );

        let result = ops.stop();

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert!(runner.calls().is_empty());
        assert_eq!(
            elevated.calls(),
            vec![vec!["stop".to_string(), "PhotoPrivacyCleaner".to_string()]]
        );
    }

    #[test]
    fn elevated_sc_uses_runas_when_not_admin() {
        // 防：非管理员直接跑 sc.exe，权限不足（5）而不是弹提权提示（#58）。
        let (ops, runner, elevated) = ops(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
        );

        let worker = temp_worker();
        let result = ops.start(Some(worker.as_path()), None);

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert!(runner.calls().is_empty());
        assert_eq!(elevated.calls().len(), 1);
        assert_eq!(
            elevated.calls()[0],
            vec!["start".to_string(), "PhotoPrivacyCleaner".to_string()]
        );
        cleanup_worker(&worker);
    }

    #[test]
    fn quote_windows_arg_matches_msvc_rules() {
        // 防：提权路径把所有参数拼成一条命令行时引号/反斜杠转义错误，sc.exe 收到错误 binPath（#58）。
        assert_eq!(quote_windows_arg("simple"), "simple");
        assert_eq!(quote_windows_arg(""), "\"\"");
        assert_eq!(quote_windows_arg("a b"), "\"a b\"");
        assert_eq!(quote_windows_arg("a\"b"), "\"a\\\"b\"");
        assert_eq!(
            quote_windows_arg(r"C:\path with space\"),
            "\"C:\\path with space\\\\\""
        );
    }

    #[test]
    fn install_aborts_when_installer_sid_cannot_be_resolved() {
        // 防：#205 ④ —— SID 解析失败仍把服务装成 current-user-only，安装者桌面会话永远连不上。
        let (ops, runner, acl, elevated) = ops_with_acl(
            ServiceRuntimeState::Running,
            vec![ok(), ok(), ok()],
            false,
            RecordingElevated::default(),
            FakeAcl::unresolved("ConvertSidToStringSidW failed (87)"),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert!(
            result
                .message
                .contains("Unable to resolve the installing user SID")
        );
        assert!(
            result
                .message
                .contains("ConvertSidToStringSidW failed (87)")
        );
        assert!(runner.calls().is_empty(), "abort must not touch sc.exe");
        assert!(elevated.calls().is_empty(), "abort must not prompt for UAC");
        assert!(acl.registered().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn install_registers_installer_sid_when_ui_is_elevated() {
        // 防：#200 —— 安装链不登记安装者 SID，服务管道 DACL 回落 current-user-only。
        let (ops, runner, acl, _) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![ok()],
            true,
            RecordingElevated::default(),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(result.message, translate_exit_code(Some(0)));
        assert_eq!(runner.calls().len(), 1);
        assert_eq!(acl.registered(), vec![TEST_INSTALLER_SID.to_string()]);
        cleanup_worker(&worker);
    }

    #[test]
    fn install_aborts_when_sid_registration_fails() {
        // #205 ④：登记写不进就不装，不能留一个「安装成功但只有提权会话能连」的半成品。
        let (ops, runner, acl, _) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![ok()],
            true,
            RecordingElevated::default(),
            FakeAcl::rejecting_registration("access denied (5)"),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert!(
            result
                .message
                .contains("Unable to register the service pipe ACL")
        );
        assert!(result.message.contains("access denied (5)"));
        assert!(result.message.contains("the service was not installed"));
        assert!(
            runner.calls().is_empty(),
            "registration failure must abort before sc.exe"
        );
        assert!(acl.registered().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn install_registers_sid_through_elevation_when_ui_is_not_admin() {
        // #200：sc.exe 的 runas 只提升子进程，UI 自身仍非管理员——登记也必须走提权通道，
        // 否则 HKLM 写入必然 access denied，等于没修。
        let (ops, runner, acl, elevated) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![ok()],
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert!(
            acl.registered().is_empty(),
            "non-elevated UI must not write HKLM from its own token"
        );

        let programs = elevated.programs();
        assert_eq!(programs.len(), 2, "registration + create: {programs:?}");
        assert!(programs[0].ends_with(r"WindowsPowerShell\v1.0\powershell.exe"));
        let sc_program = programs[1].to_ascii_lowercase();
        assert!(sc_program.ends_with(r"\system32\sc.exe"));
        assert_eq!(runner.calls().len(), 0, "both steps must go through runas");

        let registration_args = &elevated.calls()[0];
        assert!(registration_args.contains(&"-NoProfile".to_string()));
        let script = registration_args.last().cloned().unwrap_or_default();
        assert!(script.contains(INSTALLER_SID_VALUE));
        assert!(script.contains(ALLOWED_SIDS_VALUE));
        assert!(script.contains(TEST_INSTALLER_SID));
        cleanup_worker(&worker);
    }

    #[test]
    fn install_aborts_when_elevated_registration_is_cancelled() {
        // 防：用户取消登记那一次 UAC 后仍继续建服务，得到与 #200 同样的坏形态。
        let (ops, runner, _, elevated) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![ok()],
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert!(result.message.contains("elevation was cancelled"));
        assert_eq!(elevated.calls().len(), 1, "must not reach sc.exe create");
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn install_rolls_back_registration_when_create_fails() {
        // 防：create 失败后留下指向不存在服务的 SID 授权值。
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![code(5)],
            true,
            RecordingElevated::default(),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(acl.registered(), vec![TEST_INSTALLER_SID.to_string()]);
        assert_eq!(
            acl.clear_calls(),
            1,
            "failed create must roll the registration back"
        );
        assert_eq!(
            acl.registration(),
            InstallerRegistration::default(),
            "rollback must not leave the SID of a service that was never created"
        );
        cleanup_worker(&worker);
    }

    #[test]
    fn install_failure_restores_the_registration_it_found() {
        // 返修（PR #217 review）：既有登记是别人的授权，安装失败要恢复它，不能连带清空。
        let previous_sid = "S-1-5-21-111111111-222222222-333333333-1002";
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![code(5)],
            true,
            RecordingElevated::default(),
            FakeAcl::with_existing_registration(previous_sid),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(
            acl.registration().installer_sid.as_deref(),
            Some(previous_sid)
        );
        assert_eq!(
            acl.registration().allowed_sids.as_deref(),
            Some(&[previous_sid.to_string()][..])
        );
        cleanup_worker(&worker);
    }

    #[test]
    fn install_failure_after_removal_retry_restores_the_registration() {
        // 返修（PR #217 review）：1073 兜底重试里停/删失败也属「服务没建成」，
        // 本次写下的登记同样必须撤回，否则被装回来的服务会拿它当管道 ACL 来源。
        let previous_sid = "S-1-5-21-111111111-222222222-333333333-1002";
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::Running,
            vec![code(0), code(0), code(1073), code(5)],
            true,
            RecordingElevated::default(),
            FakeAcl::with_existing_registration(previous_sid),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(
            acl.registration().installer_sid.as_deref(),
            Some(previous_sid),
            "cancelled or failed removal retry must roll the registration back"
        );
        cleanup_worker(&worker);
    }

    #[test]
    fn rollback_keeps_a_registration_written_by_another_installer() {
        // 返修（PR #217 review）：并发安装者已改成自己的 SID，旧快照回滚不得覆盖它。
        let other_sid = "S-1-5-21-111111111-222222222-333333333-1002";
        let acl = FakeAcl::healthy();
        acl.register(other_sid).unwrap();
        let snapshot = InstallerRegistration::default();

        acl.rollback(&snapshot, TEST_INSTALLER_SID).unwrap();

        assert_eq!(acl.registration().installer_sid.as_deref(), Some(other_sid));
        assert_eq!(acl.clear_calls(), 0, "must not touch another install's SID");
    }

    #[test]
    fn install_keeps_existing_registration_when_precheck_fails() {
        // #200 返修：旧服务停不下来时不得先写新登记，否则既有授权被换成新 SID，
        // 服务下次启动会拿它去拒绝原本被授权的用户。
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::Running,
            vec![code(5)],
            true,
            RecordingElevated::default(),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert!(
            acl.registered().is_empty(),
            "a failed precheck must leave the existing registration untouched"
        );
        assert_eq!(acl.clear_calls(), 0);
        cleanup_worker(&worker);
    }

    #[test]
    fn install_does_not_register_when_cleanup_elevation_is_cancelled() {
        // 防：用户取消「停掉旧服务」的 UAC 后，新 SID 仍被写进 HKLM。
        let (ops, runner, _, elevated) = ops_with_acl(
            ServiceRuntimeState::Running,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
            FakeAcl::healthy(),
        );

        let worker = temp_worker();
        let config = std::path::PathBuf::from(r"C:\cfg\config.json");
        let result = ops.install(Some(worker.as_path()), Some(config.as_path()));

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert_eq!(
            elevated.calls().len(),
            1,
            "cancelled cleanup must not be followed by a registration script"
        );
        assert!(runner.calls().is_empty());
        cleanup_worker(&worker);
    }

    #[test]
    fn string_values_are_written_with_a_terminating_null() {
        // #200 返修：REG_SZ 的字节数必须含结尾 NUL，否则按 REG_SZ 读回的值没有终止符。
        let bytes = string_value_bytes(TEST_INSTALLER_SID);
        assert_eq!(bytes.len(), (TEST_INSTALLER_SID.len() + 1) * 2);
        assert_eq!(&bytes[bytes.len() - 2..], [0, 0]);
    }

    #[test]
    fn uninstall_clears_pipe_acl_after_delete() {
        // 防：#200 ② —— 卸载只删服务不清登记，重装到别的账户后陈旧 SID 仍被当作授权来源。
        let (ops, runner, acl, _) = ops_with_acl(
            ServiceRuntimeState::Stopped,
            vec![ok()],
            true,
            RecordingElevated::default(),
            FakeAcl::healthy(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(runner.calls()[0], ["delete", "PhotoPrivacyCleaner"]);
        assert_eq!(acl.clear_calls(), 1);
    }

    #[test]
    fn uninstall_clears_stale_pipe_acl_when_service_is_already_missing() {
        // #200 ②：服务已不存在（1060）时也要清遗留登记，否则下次换账户安装语义错乱。
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::NotInstalled,
            vec![code(1060)],
            true,
            RecordingElevated::default(),
            FakeAcl::healthy(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Failed);
        assert_eq!(result.exit_code, Some(1060));
        assert_eq!(acl.clear_calls(), 1);
    }

    #[test]
    fn uninstall_keeps_pipe_acl_when_elevation_is_cancelled() {
        // 防：用户取消卸载的 UAC 后登记被清空，仍在运行的服务重建管道时掉回 current-user-only。
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::Running,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Cancelled),
            FakeAcl::healthy(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::ElevationCancelled);
        assert_eq!(acl.clear_calls(), 0);
    }

    #[test]
    fn uninstall_reports_clear_failure_without_hiding_the_delete_result() {
        // 防：清理失败把成功的卸载改成失败，或反过来把失败的卸载说成成功。
        let (ops, _, acl, _) = ops_with_acl(
            ServiceRuntimeState::Stopped,
            vec![ok()],
            true,
            RecordingElevated::default(),
            FakeAcl::rejecting_clear("access denied (5)"),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert!(result.message.contains("could not be cleared"));
        assert!(result.message.contains("access denied (5)"));
        assert_eq!(acl.clear_calls(), 1);
    }

    #[test]
    fn uninstall_clears_sid_through_elevation_when_ui_is_not_admin() {
        // #200 AC2：非提权的 UI 也必须能清掉 HKLM 登记（脚本卸载通道同样清），
        // 否则换账户重装后陈旧 SID 仍在白名单里。
        let (ops, runner, acl, elevated) = ops_with_acl(
            ServiceRuntimeState::Stopped,
            Vec::new(),
            false,
            RecordingElevated::new(ElevatedOutcome::Exited(0)),
            FakeAcl::healthy(),
        );

        let result = ops.uninstall();

        assert_eq!(result.status, ServiceCommandStatus::Success);
        assert_eq!(
            acl.clear_calls(),
            0,
            "non-elevated UI must not clear HKLM directly"
        );
        assert_eq!(runner.calls().len(), 0);

        let programs = elevated.programs();
        assert_eq!(programs.len(), 2, "delete + cleanup: {programs:?}");
        let sc_program = programs[0].to_ascii_lowercase();
        assert!(sc_program.ends_with(r"\system32\sc.exe"));
        assert!(programs[1].ends_with(r"WindowsPowerShell\v1.0\powershell.exe"));

        let cleanup_script = elevated.calls()[1].last().cloned().unwrap_or_default();
        assert!(cleanup_script.contains(INSTALLER_SID_VALUE));
        assert!(cleanup_script.contains(ADVERTISED_PIPE_VALUE));
        assert!(
            cleanup_script.contains("-ErrorAction Stop"),
            "cleanup must surface real removal failures instead of reporting success: {cleanup_script}"
        );
    }

    #[test]
    fn sid_string_validation_rejects_malformed_values() {
        // #200：写进 DACL 白名单值的字符串必须先过 SID 形态校验。
        assert!(is_sid_string(TEST_INSTALLER_SID));
        assert!(is_sid_string("S-1-5-18"));
        assert!(!is_sid_string(""));
        assert!(!is_sid_string("S-1-"));
        assert!(!is_sid_string("S-1-5-21-abc"));
        assert!(!is_sid_string("*"));
        assert!(!is_sid_string(r"S-1-5-21-1);Remove-Item C:\"));
    }
}
