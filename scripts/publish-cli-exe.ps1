param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64",
  [string]$SelfContained = "true",
  [string]$Zip = "true"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot "publish"
$targetDir = Join-Path $publishRoot ("cli/{0}/{1}" -f $Version, $Runtime)

if (Test-Path $targetDir) {
  Remove-Item $targetDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$selfContainedEnabled = $SelfContained -match '^(1|true|yes|on)$'
$zipEnabled = $Zip -match '^(1|true|yes|on)$'

$selfContainedValue = if ($selfContainedEnabled) { "true" } else { "false" }
$isLinuxRuntime = $Runtime -like "linux-*"
$framework = if ($Runtime -like "win-*") { "net10.0-windows" } else { "net10.0" }

Write-Host "Publishing PhotoPrivacy.Cli ..."
dotnet publish "$repoRoot\src\PhotoPrivacy.Cli\PhotoPrivacy.Cli.csproj" `
  -c Release `
  -f $framework `
  -r $Runtime `
  --self-contained $selfContainedValue `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishTrimmed=false `
  /p:Version=$Version `
  -o $targetDir

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed"
}

Copy-Item "$repoRoot\config\config.sample.json" (Join-Path $targetDir "config.sample.json") -Force
Copy-Item "$repoRoot\README.md" (Join-Path $targetDir "README.md") -Force

if ($zipEnabled) {
  if ($isLinuxRuntime) {
    $tarPath = Join-Path $publishRoot ("PhotoPrivacy-{0}-{1}.tar.gz" -f $Version, $Runtime)
    if (Test-Path $tarPath) {
      Remove-Item $tarPath -Force
    }

    tar -czf "$tarPath" -C "$targetDir" .
    if ($LASTEXITCODE -ne 0) {
      throw "tar packaging failed"
    }

    Write-Host "Package created: $tarPath"
  }
  else {
    $zipPath = Join-Path $publishRoot ("PhotoPrivacy-{0}-{1}.zip" -f $Version, $Runtime)
    if (Test-Path $zipPath) {
      Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $targetDir "*") -DestinationPath $zipPath -Force
    Write-Host "Package created: $zipPath"
  }
}

Write-Host "Publish done: $targetDir"
