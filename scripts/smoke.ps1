param(
  [Parameter(Mandatory = $true)][string]$HotFolder,
  [Parameter(Mandatory = $true)][string]$AuditFolder,
  [string]$ConfigPath = "",
  [string]$DryRun = "true"
)

Write-Host "Smoke test start"

$dryRunEnabled = $DryRun -match '^(1|true|yes|on)$'

$args = @("--hot-folder", "$HotFolder", "--audit-folder", "$AuditFolder", "--once", "true", "--dry-run", $dryRunEnabled.ToString())
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  $args += @("--config", "$ConfigPath")
}

dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- @args
if ($LASTEXITCODE -ne 0) {
  throw "CLI failed"
}
Write-Host "Smoke test done"
