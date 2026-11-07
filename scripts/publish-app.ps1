param(
  [string]$Version = "0.1.0-preview",
  [string]$Runtime = "win-x64",
  [string]$Framework = "",
  [string]$SelfContained = "true",
  [string]$Zip = "true",
  [string]$ExifToolVersion = "13.61",
  [string]$ExifToolZip = "",
  # #33: ExifToolPathOverride——已安装的 ExifTool 目录（含 exiftool.exe + exiftool_files/），
  # 直接打包跳过下载，对齐 ExifToolGUI 的 ETOverrideDir（自带 ExifTool、不下载）。
  [string]$ExifToolPathOverride = ""
)

$ErrorActionPreference = "Stop"
# Invoke-WebRequest 的逐块进度会刷满 CI 日志（11 MB 归档 ≈ 上千行，重试时成倍放大），全脚本静默。
$ProgressPreference = "SilentlyContinue"

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
  throw "Invalid Runtime '$Runtime'. Valid: $($validRuntimes -join ', '). win-x86/win-arm64 are not supported by gpui; " +
        "build linux-*/osx-* on the matching platform with scripts/publish.sh."
}

$defaultFramework = "net10.0"
$resolvedFramework = if ([string]::IsNullOrWhiteSpace($Framework)) { $defaultFramework } else { $Framework.Trim() }

if ($resolvedFramework -ne "net10.0") {
  throw "Runtime '$Runtime' must use framework 'net10.0'"
}

# Design(#49): 默认入口为新 UI——cargo（windows-gnu target）产物 photoprivacy-ui.exe
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
  throw "windres not found in PATH or at known MinGW locations; the windows-gnu build needs MinGW windres (or set the WINDRES environment variable)"
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
# install-service.ps1 会 dot-source 同目录的 service-worker-match.ps1（worker 归属匹配），
# 两个文件必须一起随包发布，否则卸载时的孤儿进程清理会因缺函数而失败。
foreach ($scriptName in @("install-service.ps1", "service-worker-match.ps1")) {
  Copy-Item (Join-Path $repoRoot "scripts" | Join-Path -ChildPath $scriptName) $scriptsDir -Force
}

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
Copy-Item (Join-Path $repoRoot "config" | Join-Path -ChildPath "README.md") (Join-Path $configDir "README.md") -Force
Copy-Item (Join-Path $repoRoot "README.md") (Join-Path $targetDir "README.md") -Force

# --- 内置 ExifTool（#108）---
# 裸机不再要求预装 ExifTool：Windows 版 exiftool.exe 自带私有 Perl 运行时，与同级
# exiftool_files/ 一起放进 <包根>/ExifTool/。配置模板的 exiftool.path 留空，运行时探测
# 优先命中这里（DefaultPaths.ExifToolPath 与 UI 的 resolve_default_exiftool_path 同口径）。
# 版本必须显式钉扎校验值：未登记的版本直接失败，避免把被替换或半截的归档打进发布包。
$exifToolSha256 = @{
  "13.61" = "ab75b37515385a753aa60a58f68ffffe1ff81e43b319df9540978fb19e0580e5"
  "13.60" = "548b1f4e0b713cd2a26824b34080c35db4e2d2edd166468eb38b1d55ada90c2e"
}

# #33: 覆盖模式下用户自带 ExifTool，版本未必是钉死的那个，不要求登记校验值；
# 默认下载通道仍强制钉扎，避免把被替换或半截的归档打进发布包。
$expectedSha = $null
if ([string]::IsNullOrWhiteSpace($ExifToolPathOverride)) {
  $expectedSha = $exifToolSha256[$ExifToolVersion]
  if (-not $expectedSha) {
    throw "No pinned SHA256 for ExifTool $ExifToolVersion. 更新 scripts/publish-app.ps1 的 `$exifToolSha256 表（校验值取自 https://exiftool.org/checksums.txt）后再发版。"
  }
}

$exifToolDir = Join-Path $targetDir "ExifTool"
$exifToolStage = Join-Path $env:TEMP ("photoprivacy-exiftool-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $exifToolStage | Out-Null

# 下载通道不稳（#164）：CI runner 上曾取到 SourceForge 的跳转/拦截页（约 5 s 就返回，
# 哈希必然不符）。故「校验不过就重取」，取满次数仍不符才失败——pin 值不变、判定标准不放宽，
# 镜像返回任何别的内容都会被 SHA256 拒掉。
# 顺序：先走直链镜像（`?viasf=1` 免跳转页），失败再换回带跳转的常规下载地址。
$exifToolUrls = @(
  ("https://master.dl.sourceforge.net/project/exiftool/exiftool-{0}_64.zip?viasf=1" -f $ExifToolVersion),
  ("https://downloads.sourceforge.net/project/exiftool/exiftool-{0}_64.zip?viasf=1" -f $ExifToolVersion),
  ("https://downloads.sourceforge.net/project/exiftool/exiftool-{0}_64.zip" -f $ExifToolVersion)
)
function Get-ExifToolArchive {
  param(
    [string[]]$Urls,
    [string]$Destination,
    [string]$ExpectedSha,
    [int]$Attempts = 3
  )
  $diagnostics = @()
  for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $url = $Urls[($attempt - 1) % $Urls.Count]
    try {
      if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
      Invoke-WebRequest -Uri $url -OutFile $Destination -UseBasicParsing `
        -UserAgent "PhotoPrivacyRelease/1.0" -MaximumRedirection 10 -TimeoutSec 300
      $length = (Get-Item -LiteralPath $Destination).Length
      $actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
      if ($actual -eq $ExpectedSha) {
        Write-Host ("ExifTool archive verified on attempt {0}/{1} ({2} bytes)" -f $attempt, $Attempts, $length)
        return $Destination
      }
      $diagnostics += ("attempt {0}: {1} -> {2} bytes, sha256 {3}" -f $attempt, $url, $length, $actual)
    } catch {
      $diagnostics += ("attempt {0}: {1} -> {2}" -f $attempt, $url, $_.Exception.Message)
    }
    if ($attempt -lt $Attempts) { Start-Sleep -Seconds (2 * $attempt) }
  }
  throw ("ExifTool archive download failed after {0} attempts (expected sha256 {1}).`n  {2}" -f `
      $Attempts, $ExpectedSha, ($diagnostics -join "`n  "))
}

if ([string]::IsNullOrWhiteSpace($ExifToolPathOverride)) {

$archivePath = if ([string]::IsNullOrWhiteSpace($ExifToolZip)) {
  $downloaded = Join-Path $exifToolStage ("exiftool-{0}_64.zip" -f $ExifToolVersion)
  # gpui 只支持 win-x64，故固定取 64 位包。
  Write-Host "Downloading ExifTool $ExifToolVersion ..."
  Get-ExifToolArchive -Urls $exifToolUrls -Destination $downloaded -ExpectedSha $expectedSha
} else {
  if (-not (Test-Path -LiteralPath $ExifToolZip)) {
    throw "ExifToolZip not found: $ExifToolZip"
  }
  (Resolve-Path -LiteralPath $ExifToolZip).Path
}

$archiveLength = (Get-Item -LiteralPath $archivePath).Length
$actualSha = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
  throw "ExifTool archive SHA256 mismatch for $ExifToolVersion. Expected $expectedSha, got $actualSha ($archiveLength bytes) from $archivePath"
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $exifToolStage -Force
$innerDir = Join-Path $exifToolStage ("exiftool-{0}_64" -f $ExifToolVersion)
if (-not (Test-Path -LiteralPath $innerDir)) {
  throw "Unexpected ExifTool archive layout: $innerDir not found"
}

New-Item -ItemType Directory -Force -Path $exifToolDir | Out-Null
# 官方包内可执行文件名为 "exiftool(-k).exe"（双击看文档的形态），命令行用必须改名。
Move-Item -LiteralPath (Join-Path $innerDir "exiftool_files") -Destination $exifToolDir -Force
Move-Item -LiteralPath (Join-Path $innerDir "exiftool(-k).exe") -Destination (Join-Path $exifToolDir "exiftool.exe") -Force
# README.txt 是 Windows 构建的来源与许可说明，保留随包（许可证本体在 exiftool_files/LICENSE）。
if (Test-Path -LiteralPath (Join-Path $innerDir "README.txt")) {
  Move-Item -LiteralPath (Join-Path $innerDir "README.txt") -Destination (Join-Path $exifToolDir "README.txt") -Force
}
} else {
  # #33: ExifToolPathOverride——从已安装目录直接打包，跳过下载与 SHA256 校验
  # （对齐 ExifToolGUI 的 ETOverrideDir：用户自带 ExifTool、不下载）。仅校验布局并做 -ver 版本探针。
  # 两个来源互斥：同时给会让 -ExifToolZip 被静默忽略。
  if (-not [string]::IsNullOrWhiteSpace($ExifToolZip)) {
    throw "ExifToolPathOverride 与 ExifToolZip 互斥：覆盖模式下不会读取 ExifToolZip，请只传一个。"
  }
  $overrideDir = (Resolve-Path -LiteralPath $ExifToolPathOverride).Path
  if (-not (Test-Path -LiteralPath $overrideDir -PathType Container)) {
    throw "ExifToolPathOverride 不是目录：$ExifToolPathOverride"
  }
  $overrideExe = Join-Path $overrideDir "exiftool.exe"
  $overrideFiles = Join-Path $overrideDir "exiftool_files"
  if (-not (Test-Path -LiteralPath $overrideExe -PathType Leaf) -or
      -not (Test-Path -LiteralPath $overrideFiles -PathType Container)) {
    throw "ExifToolPathOverride 布局不符：需 $overrideExe 与 $overrideFiles（Windows 版 exiftool.exe 必须与 exiftool_files 同级）"
  }

  New-Item -ItemType Directory -Force -Path $exifToolDir | Out-Null
  Copy-Item -LiteralPath $overrideExe -Destination (Join-Path $exifToolDir "exiftool.exe") -Force
  Copy-Item -LiteralPath $overrideFiles -Destination $exifToolDir -Recurse -Force
  $overrideReadme = Join-Path $overrideDir "README.txt"
  if (Test-Path -LiteralPath $overrideReadme) {
    Copy-Item -LiteralPath $overrideReadme -Destination $exifToolDir -Force
  }

  # 版本探针：对齐 ExifToolGUI 的 -ver 校验，确认覆盖的是可用 ExifTool（失败仅警告，不致命）。
  try {
    $overrideVer = & (Join-Path $exifToolDir "exiftool.exe") -ver 2>$null
    if ($overrideVer) {
      Write-Host "Bundled ExifTool from override ($overrideDir) version $overrideVer -> $exifToolDir"
    } else {
      Write-Host "WARNING: ExifToolPathOverride 的 -ver 未返回版本（可能安装损坏）"
    }
  } catch {
    Write-Host "WARNING: ExifToolPathOverride 的 -ver 探针失败：$($_.Exception.Message)"
  }
}

# 两条通道共用：临时目录统一清理，避免覆盖模式在 $env:TEMP 留下空 GUID 目录。
if (Test-Path -LiteralPath $exifToolStage) {
  Remove-Item -LiteralPath $exifToolStage -Recurse -Force
}

# 缺任何一半都会让裸机首次启用监控失败，故在打包期就断言（exiftool_files 内含 LICENSE）。
foreach ($required in @("exiftool.exe", "exiftool_files\exiftool.pl", "exiftool_files\LICENSE")) {
  if (-not (Test-Path -LiteralPath (Join-Path $exifToolDir $required))) {
    throw "Bundled ExifTool incomplete: missing $required under $exifToolDir"
  }
}
# 覆盖模式允许自带任意版本，末行不能仍打印钉死的 $ExifToolVersion；真实版本由上面 -ver 探针输出。
$bundledLabel = if ([string]::IsNullOrWhiteSpace($ExifToolPathOverride)) { $ExifToolVersion } else { "from override" }
Write-Host "Bundled ExifTool $bundledLabel -> $exifToolDir"

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
