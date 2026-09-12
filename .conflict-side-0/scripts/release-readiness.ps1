param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64",
  [string]$IncludeForceKillCheck = "false"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($Runtime -match '^(all|any)$') {
  throw "Runtime '$Runtime' is not valid. Use a concrete RID like win-x64 or linux-x64."
}

# Ticket 09 / B13: capture child script stdout/stderr into temp files and print both tails,
# so gate failures (e.g. empty output) carry their diagnostics without a manual rerun.
function Get-ChildOutputTail([string]$Path, [int]$MaxChars = 4000) {
  if (-not (Test-Path $Path)) { return "" }
  $text = [System.IO.File]::ReadAllText($Path)
  if ($text.Length -le $MaxChars) { return $text }
  return "... (truncated) " + $text.Substring($text.Length - $MaxChars)
}

function Invoke-ScriptWithCapture([string]$Label, [string]$ScriptPath, [string]$ArgumentString) {
  $outFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-ready-stdout-" + [Guid]::NewGuid().ToString("N") + ".log")
  $errFile = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-ready-stderr-" + [Guid]::NewGuid().ToString("N") + ".log")
  try {
    $proc = Start-Process -FilePath "powershell" -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $ScriptPath + '" ' + $ArgumentString) -WorkingDirectory $repoRoot -NoNewWindow -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $null = $proc.Handle # force handle acquisition: ExitCode is null otherwise
    $proc.WaitForExit()
    Write-Host "----- $Label stdout (tail) -----"
    Write-Host (Get-ChildOutputTail $outFile)
    Write-Host "----- $Label stderr (tail) -----"
    Write-Host (Get-ChildOutputTail $errFile)
    if ($proc.ExitCode -ne 0) {
      throw ($Label + ' failed (exit code ' + $proc.ExitCode + '); stdout/stderr tails printed above.')
    }
  }
  finally {
    if (Test-Path $outFile) { Remove-Item $outFile -Force }
    if (Test-Path $errFile) { Remove-Item $errFile -Force }
  }
}

Write-Host "[1/3] dotnet test PhotoPrivacy.sln"
# 票16: dotnet test 经临时包装脚本纳入 Invoke-ScriptWithCapture，失败打印双尾部后 throw（四步全链路捕获）。
$testScript = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-ready-dotnet-test-" + [Guid]::NewGuid().ToString("N") + ".ps1")
[System.IO.File]::WriteAllText($testScript, '& dotnet test "' + $repoRoot + '\PhotoPrivacy.sln"' + [Environment]::NewLine + 'exit $LASTEXITCODE', (New-Object System.Text.UTF8Encoding($false)))
try {
  Invoke-ScriptWithCapture -Label "dotnet test" -ScriptPath $testScript -ArgumentString ""
}
finally {
  if (Test-Path $testScript) { Remove-Item $testScript -Force }
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-release-smoke-" + [Guid]::NewGuid().ToString("N"))
$hotFolder = Join-Path $tmpRoot "hot"
$auditFolder = Join-Path $tmpRoot "audit"

New-Item -ItemType Directory -Force -Path $hotFolder | Out-Null
New-Item -ItemType Directory -Force -Path $auditFolder | Out-Null

try {
  Write-Host "[2/3] smoke test (dry-run)"
  Invoke-ScriptWithCapture -Label "smoke.ps1" -ScriptPath (Join-Path $repoRoot "scripts\smoke.ps1") -ArgumentString ('-HotFolder "' + $hotFolder + '" -AuditFolder "' + $auditFolder + '" -DryRun 1')
}
finally {
  if (Test-Path $tmpRoot) {
    Remove-Item -Recurse -Force $tmpRoot
  }
}

Write-Host "[3/3] publish app + worker"
$framework = "net10.0"
Invoke-ScriptWithCapture -Label "publish-app.ps1" -ScriptPath (Join-Path $repoRoot "scripts\publish-app.ps1") -ArgumentString ('-Version "' + $Version + '" -Runtime "' + $Runtime + '" -Framework "' + $framework + '" -SelfContained true -Zip true')

$includeForceKill = $IncludeForceKillCheck -match '^(1|true|yes|on)$'
if ($includeForceKill) {
  if ($Runtime -notlike "win-*") {
    throw "Force-kill gate currently supports only win-* runtime"
  }

  Write-Host "[4/4] force-kill cleanup gate"
  Invoke-ScriptWithCapture -Label "verify-force-kill-cleanup.ps1" -ScriptPath (Join-Path $repoRoot "scripts\verify-force-kill-cleanup.ps1") -ArgumentString ('-Version "' + $Version + '"')
}

Write-Host "Release readiness passed."
