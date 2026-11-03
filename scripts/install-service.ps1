#Requires -Version 5.1
#Requires -PSEdition Desktop,Core
<#
.SYNOPSIS
  Install/uninstall PhotoPrivacy Windows service.
#>
param(
  [string]$Action = "install",
  [string]$ServiceName = "PhotoPrivacyCleaner",
  [string]$ExePath = "",
  [string]$ConfigPath = "",
  # 服务管道 DACL 使用的安装者 SID（#70，决策 #82 D3）。留空则取当前（提权）账户的 SID。
  # 若安装用管理员与日常使用用户不是同一账户，须显式传日常用户 SID，否则其 UI 连不上服务管道。
  [string]$InstallerSid = "",
  # 服务管道 DACL 的额外授权用户（#1 opt-in）：账户名（DOMAIN\user / machine\user）或 SID
  # 字符串，可多个。安装者本身始终在授权集合内（保底单用户行为不变）；传入的账户追加进
  # 集合后其 UI 可连服务管道。解析不到的账户会中止安装（fail-closed，不静默漏授权）。
  # 注意：被授权账户能读到服务侧审计信息（GetRecentLogs），只授予信任用户；
  # 未列入集合的账户仍被拒。留空 = 不 opt-in，行为与 #70 一致（仅安装者）。
  [string[]]$AuthorizedUsers = @()
)

# 服务管道显式 ACL 的注册表落点：服务（LocalSystem）运行时读取该值构建 DACL；
# 键默认继承允许 Users 读，SID 字符串非敏感。缺失时服务 fail-closed 回退 CurrentUserOnly。
$RegistryPath = "HKLM:\SOFTWARE\PhotoPrivacy"
$RegistryValueName = "ServicePipeInstallerSid"
# 授权 SID 集合（安装者 + -AuthorizedUsers，#1 opt-in）；服务端优先读该 MULTI_SZ 值，
# 值不存在时回退安装者单值（未 opt-in 不扩权）。卸载时两个值一并清除。
$RegistryAllowedValueName = "ServicePipeAllowedSids"

$ErrorActionPreference = "Stop"

# Auto-detect exe and config relative to this script (release/<rid>/scripts/)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDir = Split-Path -Parent $scriptDir
if ([string]::IsNullOrWhiteSpace($ExePath)) {
  $ExePath = Join-Path $releaseDir "PhotoPrivacyWorker.exe"
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
  $ConfigPath = Join-Path $releaseDir "config\config.json"
}
if (-not (Test-Path $ExePath)) { throw "Worker exe not found: $ExePath" }
if (-not (Test-Path $ConfigPath)) { throw "Config not found: $ConfigPath" }

function Remove-ServiceCompat([string]$Name) {
  if ($PSVersionTable.PSEdition -eq 'Core') {
    try { Remove-Service -Name $Name -ErrorAction Stop } catch { sc.exe delete $Name }
  } else {
    sc.exe delete $Name
  }
}

# 账户名或 SID 字符串 → SID 字符串（#1）。已是合法 SID 直接返回；否则经 NTAccount 解析。
# 解析不到（账户不存在/拼写错误）抛错终止安装：宁可安装失败也不静默漏授权。
function Resolve-SidString([string]$NameOrSid) {
  try {
    return ([System.Security.Principal.SecurityIdentifier]::new($NameOrSid)).Value
  } catch [System.ArgumentException] { }
  try {
    $account = [System.Security.Principal.NTAccount]::new($NameOrSid)
    return ($account.Translate([System.Security.Principal.SecurityIdentifier])).Value
  } catch {
    throw "Account or SID cannot be resolved: '$NameOrSid' (account not found or invalid SID)"
  }
}

switch ($Action.ToLower()) {
  "install" {
    if ([string]::IsNullOrWhiteSpace($InstallerSid)) {
      $InstallerSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    }
    # 授权集合 = 安装者保底 + -AuthorizedUsers 显式授权（#1 opt-in）：去重后统一解析为 SID。
    $authorizedNames = @($AuthorizedUsers | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($authorizedNames.Count -gt 0) {
      # 集合模式（#1 opt-in）：安装者条目同样经解析——账户名/typo 直接中止安装，
      # 避免「集合里有合法 SID 但安装者被服务端静默剔除」的假成功（CodeRabbit 返修）。
      # 不带 -AuthorizedUsers 时保持 #70 既有行为（InstallerSid 原样登记，非法时服务端
      # fail-closed 回退 CurrentUserOnly）。
      $InstallerSid = Resolve-SidString $InstallerSid
    }
    $allowedSids = [System.Collections.Generic.List[string]]::new()
    [void]$allowedSids.Add($InstallerSid)
    foreach ($name in $authorizedNames) {
      $sid = Resolve-SidString $name
      if (-not $allowedSids.Contains($sid)) { [void]$allowedSids.Add($sid) }
    }
    $binPath = '"{0}" --mode service --config "{1}"' -f $ExePath, $ConfigPath
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
    }
    if ($PSVersionTable.PSEdition -eq "Core") {
      New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName "PhotoPrivacy Cleaner" -StartupType Automatic
    } else {
      sc.exe create $ServiceName binPath= $binPath start= auto
    }
    # 先登记 SID 再启动服务：服务首次建管道时即按最新授权集合构建 DACL（重装/升级覆盖旧值）。
    if (-not (Test-Path $RegistryPath)) { New-Item -Path $RegistryPath -Force | Out-Null }
    New-ItemProperty -Path $RegistryPath -Name $RegistryValueName -Value $InstallerSid -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $RegistryPath -Name $RegistryAllowedValueName -Value $allowedSids.ToArray() -PropertyType MultiString -Force | Out-Null
    Start-Service -Name $ServiceName
    Write-Host "Installed and started $ServiceName (service pipe ACL: $($allowedSids.Count) authorized SID(s))"
  }
  "uninstall" {
    # 注册表清理不依赖服务是否仍在：服务已被手工删除时也要清掉孤儿 SID 登记。
    Remove-ItemProperty -Path $RegistryPath -Name $RegistryValueName -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $RegistryPath -Name $RegistryAllowedValueName -ErrorAction SilentlyContinue
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
      Write-Host "Uninstalled $ServiceName"
    } else { Write-Host "Not found" }
  }
  default { Write-Host "Usage: -Action install|uninstall -ExePath <p> -ConfigPath <p> [-AuthorizedUsers <user...>]"; exit 1 }
}
