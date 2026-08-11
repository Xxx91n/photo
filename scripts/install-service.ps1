#Requires -Version 5.1
#Requires -PSEdition Desktop,Core
<#
.SYNOPSIS
  Install/uninstall PhotoPrivacy Windows service (ADR 0024).
#>
param(
  [string]$Action = "install",
  [string]$ServiceName = "PhotoPrivacyCleaner",
  [string]$ExePath = "",
  [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"

function Remove-ServiceCompat([string]$Name) {
  if ($PSVersionTable.PSEdition -eq 'Core') {
    try { Remove-Service -Name $Name -ErrorAction Stop } catch { sc.exe delete $Name }
  } else {
    sc.exe delete $Name
  }
}

switch ($Action.ToLower()) {
  "install" {
    $binPath = """ + $ExePath + """ --mode service --config """" + $ConfigPath + """
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
    }
    if ($PSVersionTable.PSEdition -eq "Core") {
      New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName "PhotoPrivacy Cleaner" -StartupType Automatic
    } else {
      sc.exe create $ServiceName binPath= $binPath start= auto
    }
    Start-Service -Name $ServiceName
    Write-Host "Installed and started $ServiceName"
  }
  "uninstall" {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
      Write-Host "Uninstalled $ServiceName"
    } else { Write-Host "Not found" }
  }
  default { Write-Host "Usage: -Action install|uninstall -ExePath <p> -ConfigPath <p>"; exit 1 }
}
