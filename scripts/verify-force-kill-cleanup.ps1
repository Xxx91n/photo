param(
  [string]$Version = "0.1.0-preview",
  [int]$WaitBeforeKillSeconds = 4,
  [int]$WaitAfterKillSeconds = 3
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$zipPath = Join-Path $repoRoot ("publish\PhotoPrivacy-{0}-win-x64.zip" -f $Version)
if (-not (Test-Path $zipPath)) {
  throw "Package not found: $zipPath"
}

Get-Process exiftool -ErrorAction SilentlyContinue | Stop-Process -Force

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-force-kill-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

try {
  Expand-Archive -Path $zipPath -DestinationPath $workDir -Force

  $configDir = Join-Path $workDir "config"
  New-Item -ItemType Directory -Force -Path $configDir | Out-Null
  $configPath = Join-Path $configDir "config.json"
  Copy-Item (Join-Path $repoRoot "config\config.sample.json") $configPath -Force

  $hot = Join-Path $workDir "hot"
  $audit = Join-Path $hot "_audit"
  $quarantine = Join-Path $hot "_quarantine"
  New-Item -ItemType Directory -Force -Path $hot | Out-Null
  New-Item -ItemType Directory -Force -Path $audit | Out-Null
  New-Item -ItemType Directory -Force -Path $quarantine | Out-Null

  $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
  $cfg.watch.hot_folder = $hot
  $cfg.audit.log_directory = $audit
  $cfg.quarantine.directory = $quarantine
  $cfg.exiftool.dry_run = $false
  $cfg | ConvertTo-Json -Depth 8 | Set-Content $configPath

  $exe = Join-Path $workDir "PhotoPrivacy.exe"
  if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe"
  }

  $parent = Start-Process -FilePath $exe -WorkingDirectory $workDir -ArgumentList @("--mode", "cli", "--config", $configPath) -PassThru
  Start-Sleep -Seconds $WaitBeforeKillSeconds

  $candidate = Get-Process exiftool -ErrorAction SilentlyContinue |
    Where-Object { $_.StartTime -ge $parent.StartTime.AddSeconds(-2) } |
    Sort-Object StartTime -Descending |
    Select-Object -First 1

  if ($null -eq $candidate) {
    throw "No child ExifTool process detected before force-kill"
  }

  $childPid = $candidate.Id
  Stop-Process -Id $parent.Id -Force
  Start-Sleep -Seconds $WaitAfterKillSeconds

  $childStillAlive = [bool](Get-Process -Id $childPid -ErrorAction SilentlyContinue)

  Write-Host "PARENT_PID=$($parent.Id)"
  Write-Host "CHILD_EXIFTOOL_PID=$childPid"
  Write-Host "CHILD_STILL_ALIVE=$childStillAlive"

  if ($childStillAlive) {
    throw "FAIL: ExifTool child process survived parent force-kill"
  }

  Write-Host "PASS: ExifTool child process is cleaned after force-kill"
}
finally {
  Get-Process exiftool -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "*ExifTool.exe" } |
    Stop-Process -Force -ErrorAction SilentlyContinue

  if (Test-Path $workDir) {
    Remove-Item -Recurse -Force $workDir
  }
}
