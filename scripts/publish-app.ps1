param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64",
  [string]$Framework = "",
  [string]$SelfContained = "true",
  [string]$Zip = "true"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot "publish"
$targetDir = Join-Path $publishRoot ("app/{0}/{1}" -f $Version, $Runtime)

if (Test-Path $targetDir) {
  Remove-Item $targetDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$selfContainedEnabled = $SelfContained -match '^(1|true|yes|on)$'
$zipEnabled = $Zip -match '^(1|true|yes|on)$'

$selfContainedValue = if ($selfContainedEnabled) { "true" } else { "false" }
$isLinuxRuntime = $Runtime -like "linux-*"

if ($Runtime -match '^(all|any)$') {
  throw "Runtime '$Runtime' is not valid. Use a concrete RID like win-x64 or linux-x64."
}

$defaultFramework = "net10.0"
$resolvedFramework = if ([string]::IsNullOrWhiteSpace($Framework)) { $defaultFramework } else { $Framework.Trim() }

if ($resolvedFramework -ne "net10.0") {
  throw "Runtime '$Runtime' must use framework 'net10.0'"
}

Write-Host "Publishing PhotoPrivacy.Ui + PhotoPrivacy.Worker ..."
Write-Host "Runtime: $Runtime | Framework: $resolvedFramework"

$uiPublishDir = Join-Path $targetDir "ui"
$workerPublishDir = Join-Path $targetDir "worker"
New-Item -ItemType Directory -Force -Path $uiPublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $workerPublishDir | Out-Null

dotnet publish "$repoRoot\src\PhotoPrivacy.Ui\PhotoPrivacy.Ui.csproj" `
  -c Release `
  -f $resolvedFramework `
  -r $Runtime `
  --self-contained $selfContainedValue `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishTrimmed=false `
  /p:Version=$Version `
  /p:UseAppHost=true `
  -o $uiPublishDir

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish (ui) failed"
}

dotnet publish "$repoRoot\src\PhotoPrivacy.Worker\PhotoPrivacy.Worker.csproj" `
  -c Release `
  -f $resolvedFramework `
  -r $Runtime `
  --self-contained $selfContainedValue `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishTrimmed=false `
  /p:Version=$Version `
  /p:UseAppHost=true `
  -o $workerPublishDir

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish (worker) failed"
}

$uiSourceExeName = if ($isLinuxRuntime) { "PhotoPrivacy.Ui" } else { "PhotoPrivacy.Ui.exe" }
$appExeName = if ($isLinuxRuntime) { "PhotoPrivacy" } else { "PhotoPrivacy.exe" }
$workerExeName = if ($isLinuxRuntime) { "PhotoPrivacyWorker" } else { "PhotoPrivacyWorker.exe" }

$uiAppHostPath = Join-Path $uiPublishDir $uiSourceExeName
$workerAppHostPath = Join-Path $workerPublishDir $workerExeName
if (-not (Test-Path $uiAppHostPath)) {
  throw "UI executable not found: $uiAppHostPath"
}
if (-not (Test-Path $workerAppHostPath)) {
  throw "Worker executable not found: $workerAppHostPath"
}

Copy-Item $uiAppHostPath (Join-Path $targetDir $appExeName) -Force
Copy-Item $workerAppHostPath (Join-Path $targetDir $workerExeName) -Force

$configDir = Join-Path $targetDir "config"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
Copy-Item "$repoRoot\config\config.sample.json" (Join-Path $configDir "config.sample.json") -Force
Copy-Item "$repoRoot\README.md" (Join-Path $targetDir "README.md") -Force

if (Test-Path "$repoRoot\scripts\install-service.ps1") {
  Copy-Item "$repoRoot\scripts\install-service.ps1" (Join-Path $targetDir "install-service.ps1") -Force
}
if (Test-Path "$repoRoot\scripts\install-systemd-service.sh") {
  Copy-Item "$repoRoot\scripts\install-systemd-service.sh" (Join-Path $targetDir "install-systemd-service.sh") -Force
}

if (Test-Path $uiPublishDir) {
  Remove-Item $uiPublishDir -Recurse -Force
}
if (Test-Path $workerPublishDir) {
  Remove-Item $workerPublishDir -Recurse -Force
}

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
