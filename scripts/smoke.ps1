param(
  [Parameter(Mandatory = $true)][string]$HotFolder,
  [Parameter(Mandatory = $true)][string]$AuditFolder
)

Write-Host "Smoke test start"
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --hot-folder "$HotFolder" --audit-folder "$AuditFolder" --once true
if ($LASTEXITCODE -ne 0) {
  throw "CLI failed"
}
Write-Host "Smoke test done"
