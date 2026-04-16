param(
  [Parameter(Mandatory = $true)][string]$HotFolder,
  [Parameter(Mandatory = $true)][string]$AuditFolder,
  [string]$ConfigPath = "",
  [bool]$DryRun = $true
)

Write-Host "Smoke test start"

$args = @("--hot-folder", "$HotFolder", "--audit-folder", "$AuditFolder", "--once", "true", "--dry-run", $DryRun.ToString())
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  $args += @("--config", "$ConfigPath")
}

dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- @args
if ($LASTEXITCODE -ne 0) {
  throw "CLI failed"
}
Write-Host "Smoke test done"
