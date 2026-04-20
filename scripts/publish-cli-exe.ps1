param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Args
)

$ErrorActionPreference = "Stop"

Write-Warning "scripts/publish-cli-exe.ps1 已废弃，正在转发到 scripts/publish-app.ps1"

$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repoRoot "scripts\publish-app.ps1"
if (-not (Test-Path $scriptPath)) {
  throw "Missing script: $scriptPath"
}

& $scriptPath @Args
exit $LASTEXITCODE
