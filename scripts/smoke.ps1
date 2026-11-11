# DLL-first smoke (arc-recovery round 3).
# Launch the built Worker DLL directly instead of triggering a project rebuild,
# capture worker stdout/stderr into temp files, and print both tails on exit so
# failures (e.g. empty output) are diagnosable at the point of failure.
# -WorkerPath 直接指定 Worker 可执行文件（发布链单文件 exe 或 DLL），跳过本地 DLL 探测：
# 没有本地构建、只用 CI 发布产物验证时走这条路（#6）。
param(
  [Parameter(Mandatory = $true)][string]$HotFolder,
  [Parameter(Mandatory = $true)][string]$AuditFolder,
  [string]$ConfigPath = "",
  [string]$DryRun = "true",
  [string]$ExifToolPath = "",
  [string]$WorkerPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Smoke test start"

function Get-WorkerPath {
  # 显式给了 -WorkerPath 就只认它（存在即用，exe/dll 都行）；否则回退 DLL 探测。
  if (-not [string]::IsNullOrWhiteSpace($WorkerPath)) {
    if (-not (Test-Path $WorkerPath)) {
      throw "WorkerPath not found: $WorkerPath"
    }
    return (Resolve-Path $WorkerPath).Path
  }
  $candidates = @(
    [System.IO.Path]::Combine($repoRoot, "src", "PhotoPrivacy.Worker", "bin", "Debug", "net10.0", "PhotoPrivacyWorker.dll"),
    [System.IO.Path]::Combine($repoRoot, "src", "PhotoPrivacy.Worker", "bin", "Release", "net10.0", "PhotoPrivacyWorker.dll")
  )
  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) { return $candidate }
  }
  throw ("Worker DLL not found; DLL-first smoke requires a prior build. Looked for:" + [Environment]::NewLine +
    ($candidates -join [Environment]::NewLine) + [Environment]::NewLine + "Run: dotnet build PhotoPrivacy.sln")
}

function Get-OutputTail([string]$Path, [int]$MaxChars = 4000) {
  if (-not (Test-Path $Path)) { return "" }
  $text = [System.IO.File]::ReadAllText($Path)
  if ($text.Length -le $MaxChars) { return $text }
  return "... (truncated) " + $text.Substring($text.Length - $MaxChars)
}

function Write-WorkerTails([string]$StdoutPath, [string]$StderrPath) {
  Write-Host "----- worker stdout (tail) -----"
  Write-Host (Get-OutputTail $StdoutPath)
  Write-Host "----- worker stderr (tail) -----"
  Write-Host (Get-OutputTail $StderrPath)
}

$workerTarget = Get-WorkerPath
$isDll = $workerTarget.EndsWith(".dll", [System.StringComparison]::OrdinalIgnoreCase)
$dryRunEnabled = $DryRun -match '^(1|true|yes|on)$'

$workerArgString = '--mode cli --hot-folder "' + $HotFolder + '" --audit-folder "' + $AuditFolder + '" --once true --dry-run ' + $dryRunEnabled.ToString().ToLowerInvariant()
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  $workerArgString += ' --config "' + $ConfigPath + '"'
}
if (-not [string]::IsNullOrWhiteSpace($ExifToolPath)) {
  $workerArgString += ' --exiftool-path "' + $ExifToolPath + '"'
}

Write-Host ("Smoke worker args: " + $workerArgString)

$outFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-smoke-stdout-" + [Guid]::NewGuid().ToString("N") + ".log")
$errFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-smoke-stderr-" + [Guid]::NewGuid().ToString("N") + ".log")

try {
  # Redirect to files, never to unread pipes: worker output can exceed the 4KB pipe buffer
  # and would deadlock the worker on stdout writes (root cause).
  # DLL 走 dotnet 宿主；发布链单文件 exe 自带运行时，直接启动。
  $launchPath = if ($isDll) { "dotnet" } else { $workerTarget }
  $launchArgs = if ($isDll) { '"' + $workerTarget + '" ' + $workerArgString } else { $workerArgString }
  $proc = Start-Process -FilePath $launchPath -ArgumentList $launchArgs -WorkingDirectory $repoRoot -NoNewWindow -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
  $null = $proc.Handle # force handle acquisition: ExitCode is null otherwise
  $proc.WaitForExit()
  if ($proc.ExitCode -ne 0) {
    throw ("Worker CLI failed (exit code " + $proc.ExitCode + "); worker stdout/stderr tails printed above.")
  }
}
finally {
  Write-WorkerTails $outFile $errFile
  if (Test-Path $outFile) { Remove-Item $outFile -Force }
  if (Test-Path $errFile) { Remove-Item $errFile -Force }
}

Write-Host "Smoke framework: net10.0"
Write-Host "Smoke test done"
