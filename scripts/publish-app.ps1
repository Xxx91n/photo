param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64",
  [string]$Framework = "",
  [string]$SelfContained = "true",
  [string]$Zip = "true"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repoRoot "release"
$targetDir = Join-Path $releaseRoot $Runtime

if (Test-Path $targetDir) {
  Remove-Item $targetDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$selfContainedEnabled = $SelfContained -match '^(1|true|yes|on)$'
$selfContainedValue = if ($selfContainedEnabled) { "true" } else { "false" }
$zipEnabled = $Zip -match '^(1|true|yes|on)$'

# gpui 新 UI（#49 T4 裁决）：Windows 仅支持 x86_64；win-x86 / win-arm64 不再受支持。
# Linux/macOS 由对应平台的 publish.sh 产出（gpui 不支持交叉编译）。
$validRuntimes = @("win-x64")
if ($validRuntimes -notcontains $Runtime) {
  throw "Invalid Runtime '$Runtime'. Valid: $($validRuntimes -join ', ') (win-x86/win-arm64 不受 gpui 支持；" +
        "linux-*/osx-* 须在对应平台用 scripts/publish.sh 产出)"
}

$defaultFramework = "net10.0"
$resolvedFramework = if ([string]::IsNullOrWhiteSpace($Framework)) { $defaultFramework } else { $Framework.Trim() }

if ($resolvedFramework -ne "net10.0") {
  throw "Runtime '$Runtime' must use framework 'net10.0'"
}

# Design(#61): 默认入口切换为新 UI——cargo（windows-gnu target）产物 photoprivacy-ui.exe
# 复制为 PhotoPrivacy.exe 作为发布入口。MSVC 目标不嵌应用清单（gpui 依赖 comctl32 v6，
# 缺清单启动即失败），见 ui/crates/photoprivacy-ui/build.rs；发布链必须走 windows-gnu + windres。
$uiTarget = "x86_64-pc-windows-gnu"

$windres = Get-Command "windres" -ErrorAction SilentlyContinue
if (-not $windres) {
  foreach ($candidate in @("C:\msys64\mingw64\bin\windres.exe", "C:\ProgramData\mingw64\mingw64\bin\windres.exe")) {
    if (Test-Path $candidate) {
      $env:WINDRES = $candidate
      $windres = $candidate
      break
    }
  }
}
if (-not $windres) {
  throw "windres not found in PATH or at known MinGW locations; windows-gnu 构建需要 MinGW windres（或设 WINDRES 环境变量）"
}

rustup target add $uiTarget
if ($LASTEXITCODE -ne 0) {
  throw "rustup target add $uiTarget failed"
}

Write-Host "Building new UI (cargo, $uiTarget) + publishing PhotoPrivacy.Worker ..."
Write-Host "Runtime: $Runtime | Framework: $resolvedFramework"

cargo build --release --locked --target $uiTarget --manifest-path (Join-Path $repoRoot "ui" | Join-Path -ChildPath "Cargo.toml") -p photoprivacy-ui
if ($LASTEXITCODE -ne 0) {
  throw "cargo build (photoprivacy-ui) failed"
}

$workerPublishDir = Join-Path $targetDir "worker"
New-Item -ItemType Directory -Force -Path $workerPublishDir | Out-Null

dotnet publish (Join-Path $repoRoot "src" | Join-Path -ChildPath "PhotoPrivacy.Worker" | Join-Path -ChildPath "PhotoPrivacy.Worker.csproj") `
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

# Copy install scripts to release/<rid>/scripts/
$scriptsDir = Join-Path $targetDir "scripts"
New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
Copy-Item (Join-Path $repoRoot "scripts" | Join-Path -ChildPath "install-service.ps1") $scriptsDir -Force

$appExeName = "PhotoPrivacy.exe"
$workerExeName = "PhotoPrivacyWorker.exe"
$uiBinPath = Join-Path $repoRoot "ui" | Join-Path -ChildPath "target" | Join-Path -ChildPath $uiTarget | Join-Path -ChildPath "release" | Join-Path -ChildPath "photoprivacy-ui.exe"

if (-not (Test-Path $uiBinPath)) {
  throw "UI executable not found: $uiBinPath"
}
$workerAppHostPath = Join-Path $workerPublishDir $workerExeName
if (-not (Test-Path $workerAppHostPath)) {
  throw "Worker executable not found: $workerAppHostPath"
}

Copy-Item $uiBinPath (Join-Path $targetDir $appExeName) -Force
Copy-Item $workerAppHostPath (Join-Path $targetDir $workerExeName) -Force

$configDir = Join-Path $targetDir "config"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
Copy-Item (Join-Path $repoRoot "config" | Join-Path -ChildPath "config.sample.json") (Join-Path $configDir "config.sample.json") -Force
# Copy config.sample.json as config.json so the release has a working default config
Copy-Item (Join-Path $repoRoot "config" | Join-Path -ChildPath "config.sample.json") (Join-Path $configDir "config.json") -Force
Copy-Item (Join-Path $repoRoot "README.md") (Join-Path $targetDir "README.md") -Force

if (Test-Path $workerPublishDir) {
  Remove-Item $workerPublishDir -Recurse -Force
}

if ($zipEnabled) {
  $zipPath = Join-Path $releaseRoot ("PhotoPrivacy-{0}-{1}.zip" -f $Version, $Runtime)
  if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
  }

  Compress-Archive -Path (Join-Path $targetDir "*") -DestinationPath $zipPath -Force
  Write-Host "Package created: $zipPath"
}

Write-Host "Publish done: $targetDir"
