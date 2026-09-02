param(
  [Parameter(Mandatory = $true)][string]$HotFolder,
  [Parameter(Mandatory = $true)][string]$AuditFolder,
  [string]$ConfigPath = "",
  [string]$DryRun = "true",
  [string]$ExifToolPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Smoke test start"

function Get-WorkerDllPath {
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

$workerDll = Get-WorkerDllPath
$dryRunEnabled = $DryRun -match '^(1|true|yes|on)$'

$workerArgString = '--mode cli --hot-folder "' + $HotFolder + '" --audit-folder "' + $AuditFolder + '" --once true --dry-run ' + $dryRunEnabled.ToString()
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  $workerArgString += ' --config "' + $ConfigPath + '"'
}
if (-not [string]::IsNullOrWhiteSpace($ExifToolPath)) {
  $workerArgString += ' --exiftool-path "' + $ExifToolPath + '"'
}

$outFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-smoke-stdout-" + [Guid]::NewGuid().ToString("N") + ".log")
$errFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-smoke-stderr-" + [Guid]::NewGuid().ToString("N") + ".log")

try {
  # Redirect to files, never to unread pipes: worker output can exceed the 4KB pipe buffer
  # and would deadlock the worker on stdout writes (ticket 01 root cause).
  $proc = Start-Process -FilePath "dotnet" -ArgumentList ('"' + $workerDll + '" ' + $workerArgString) -WorkingDirectory $repoRoot -NoNewWindow -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
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
