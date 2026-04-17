param(
  [string]$ServiceName = "PhotoPrivacyCleaner",
  [string]$ExePath = ".\publish\cli\0.1.0-preview\win-x64\PhotoPrivacy.exe",
  [string]$ConfigPath = "",
  [string]$DisplayName = "PhotoPrivacy Cleaner",
  [string]$Description = "Hot-folder metadata cleaner (ExifTool stay_open)."
)

$ErrorActionPreference = "Stop"

$resolvedExe = Resolve-Path $ExePath
$binaryPath = '"{0}" --mode service' -f $resolvedExe

if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  $resolvedConfig = Resolve-Path $ConfigPath
  $binaryPath = '{0} --config "{1}"' -f $binaryPath, $resolvedConfig
}

$serviceExists = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $serviceExists) {
  New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -Description $Description -StartupType Automatic
  Write-Host "Service created: $ServiceName"
}
else {
  sc.exe config $ServiceName binPath= $binaryPath start= auto | Out-Null
  Write-Host "Service updated: $ServiceName"
}

Start-Service -Name $ServiceName
Write-Host "Service started: $ServiceName"
