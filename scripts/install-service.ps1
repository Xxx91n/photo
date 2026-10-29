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
  [string]$InstallerSid = ""
)

# 服务管道显式 ACL 的注册表落点：服务（LocalSystem）运行时读取该值构建 DACL；
# 键默认继承允许 Users 读，SID 字符串非敏感。缺失时服务 fail-closed 回退 CurrentUserOnly。
$RegistryPath = "HKLM:\SOFTWARE\PhotoPrivacy"
$RegistryValueName = "ServicePipeInstallerSid"

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

switch ($Action.ToLower()) {
  "install" {
    if ([string]::IsNullOrWhiteSpace($InstallerSid)) {
      $InstallerSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
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
    # 先登记 SID 再启动服务：服务首次建管道时即按最新安装者构建 DACL（重装/升级覆盖旧值）。
    if (-not (Test-Path $RegistryPath)) { New-Item -Path $RegistryPath -Force | Out-Null }
    New-ItemProperty -Path $RegistryPath -Name $RegistryValueName -Value $InstallerSid -PropertyType String -Force | Out-Null
    Start-Service -Name $ServiceName
    Write-Host "Installed and started $ServiceName (service pipe ACL SID: $InstallerSid)"
  }
  "uninstall" {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
      Remove-ItemProperty -Path $RegistryPath -Name $RegistryValueName -ErrorAction SilentlyContinue
      Write-Host "Uninstalled $ServiceName"
    } else { Write-Host "Not found" }
  }
  default { Write-Host "Usage: -Action install|uninstall -ExePath <p> -ConfigPath <p>"; exit 1 }
}
