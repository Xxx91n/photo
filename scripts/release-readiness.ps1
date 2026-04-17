param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "[1/3] dotnet test PhotoPrivacy.sln"
dotnet test "$repoRoot\PhotoPrivacy.sln"
if ($LASTEXITCODE -ne 0) {
  throw "Tests failed"
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("photo-release-smoke-" + [Guid]::NewGuid().ToString("N"))
$hotFolder = Join-Path $tmpRoot "hot"
$auditFolder = Join-Path $tmpRoot "audit"

New-Item -ItemType Directory -Force -Path $hotFolder | Out-Null
New-Item -ItemType Directory -Force -Path $auditFolder | Out-Null

try {
  Write-Host "[2/3] smoke test (dry-run)"
  powershell -ExecutionPolicy Bypass -File "$repoRoot\scripts\smoke.ps1" -HotFolder $hotFolder -AuditFolder $auditFolder -DryRun 1
  if ($LASTEXITCODE -ne 0) {
    throw "Smoke test failed"
  }
}
finally {
  if (Test-Path $tmpRoot) {
    Remove-Item -Recurse -Force $tmpRoot
  }
}

Write-Host "[3/3] publish cli exe"
powershell -ExecutionPolicy Bypass -File "$repoRoot\scripts\publish-cli-exe.ps1" -Version $Version -Runtime $Runtime -SelfContained true -Zip true
if ($LASTEXITCODE -ne 0) {
  throw "Publish failed"
}

Write-Host "Release readiness passed."
