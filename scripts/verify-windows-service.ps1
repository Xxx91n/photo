#Requires -Version 5.1
#Requires -PSEdition Desktop,Core
<#
.SYNOPSIS
  Windows Service lifecycle verification harness (virtual environment on CI or real Windows machine).
.DESCRIPTION
  Verifies the end-to-end Windows Service lifecycle for PhotoPrivacy:
    Pre-check -> Install -> Register -> Run -> Pipe IPC RoundTrip -> Logs -> Stop/Start -> Uninstall -> Clean.
  Aligned with scripts/install-service.ps1 and docs/agents/ipc-contract.md.
#>
param(
  [string]$ReleaseDir = "",
  [string]$EvidenceDir = "",
  [string]$ServiceName = "PhotoPrivacyCleaner"
)

$ErrorActionPreference = "Stop"

function Check-Administrator {
  $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
  return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Check-Administrator)) {
  throw "This verification harness must be run as Administrator (elevated)."
}

if ([string]::IsNullOrWhiteSpace($ReleaseDir)) {
  throw "Missing required -ReleaseDir parameter."
}

if (-not (Test-Path $ReleaseDir)) {
  throw "Release directory not found: $ReleaseDir"
}

$workerExe = Join-Path $ReleaseDir "PhotoPrivacyWorker.exe"
if (-not (Test-Path $workerExe)) {
  throw "Worker executable not found in release dir: $workerExe"
}

$configSample = Join-Path $ReleaseDir "config\config.sample.json"
$configJson = Join-Path $ReleaseDir "config\config.json"
if (-not (Test-Path $configJson)) {
  if (Test-Path $configSample) {
    Copy-Item $configSample $configJson -Force
  } else {
    throw "Config file not found in release dir: $configJson"
  }
}

if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
  $EvidenceDir = Join-Path (Get-Location) "logs\windows-service-verify"
}
New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
$summaryFile = Join-Path $EvidenceDir "summary.txt"
Set-Content -Path $summaryFile -Value "" -Encoding utf8

$passCount = 0
$failCount = 0

function Rec([string]$Status, [string]$Message) {
  $line = "{0}  {1}" -f $Status, $Message
  Write-Host $line
  Add-Content -Path $summaryFile -Value $line -Encoding utf8
  if ($Status -eq "PASS") {
    $script:passCount++
  } elseif ($Status -eq "FAIL") {
    $script:failCount++
  }
}

function Capture([string]$FileName, [scriptblock]$Action) {
  $outFile = Join-Path $EvidenceDir $FileName
  try {
    $output = & $Action 2>&1
    Add-Content -Path $outFile -Value ($output | Out-String) -Encoding utf8
  } catch {
    Add-Content -Path $outFile -Value ("ERROR: " + $_.Exception.Message) -Encoding utf8
  }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installScript = Join-Path $scriptDir "install-service.ps1"
if (-not (Test-Path $installScript)) {
  throw "Install script not found: $installScript"
}

# 目录名刻意带空格（CodeRabbit 返修 / #218 验收）：安装、启停、卸载、SCM 命令行解析、
# worker 归属匹配全链路都在"含空格的自定义路径"上跑，这才是真实安装路径的形态；
# 无空格路径会掩盖引号解析/令牌截断一类缺陷。
$testInstallDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pp svc verify " + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $testInstallDir | Out-Null

$serviceCreatedByHarness = $false

function Cleanup-TestEnvironment {
  if ($serviceCreatedByHarness) {
    try {
      if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
      }
    } catch { }

    $regPath = "HKLM:\SOFTWARE\PhotoPrivacy"
    if (Test-Path $regPath) {
      Remove-ItemProperty -Path $regPath -Name "ServicePipeInstallerSid" -ErrorAction SilentlyContinue
      Remove-ItemProperty -Path $regPath -Name "ServicePipeAllowedSids" -ErrorAction SilentlyContinue
      Remove-ItemProperty -Path $regPath -Name "ServicePipeName" -ErrorAction SilentlyContinue
    }
  }

  if (Test-Path $testInstallDir) {
    Remove-Item -Recurse -Force $testInstallDir -ErrorAction SilentlyContinue
  }
}

try {
  # ---------- 0a. Uninstall worker ownership regression (string-only, no service state) ----------
  # #218: uninstall also reaps workers that already left the SCM, but ONLY the ones owned by this
  # installation (CodeRabbit: killing every PhotoPrivacyWorker process harms other installs and
  # processes the user started). The rule lives in service-worker-match.ps1, which
  # install-service.ps1 dot-sources too, so script and assertions cannot drift apart.
  # Covered: a spaced config path is parsed through the quoted segment (never truncated to a
  # prefix), `/` vs `\` and case differences are the same file, unquoted --config works, while a
  # prefix-sharing config, a sibling install dir and daemon-mode workers must NOT match.
  . (Join-Path $scriptDir "service-worker-match.ps1")
  $ownedMatchCases = @(
    @{
      Name        = "spaced config path (quoted)"
      CommandLine = '"C:\Temp\pp svc\PhotoPrivacyWorker.exe" --mode service --config "C:\Temp\pp svc\config\config.json"'
      OwnedPaths  = @("C:\Temp\pp svc\config\config.json")
      Expect      = $true
    },
    @{
      Name        = "separator and case differences are the same file"
      CommandLine = '"C:\Temp\pp svc\PhotoPrivacyWorker.exe" --mode service --config "c:/temp/PP SVC/config/config.json"'
      OwnedPaths  = @("C:\Temp\pp svc\config\config.json")
      Expect      = $true
    },
    @{
      Name        = "unquoted --config without spaces"
      CommandLine = "C:\Temp\pp\PhotoPrivacyWorker.exe --mode service --config C:\Temp\pp\config\config.json"
      OwnedPaths  = @("C:\Temp\pp\config\config.json")
      Expect      = $true
    },
    @{
      Name        = "prefix-sharing config does not match"
      CommandLine = '"C:\Temp\pp svc\PhotoPrivacyWorker.exe" --mode service --config "C:\Temp\pp svc\config\config.json.bak"'
      OwnedPaths  = @("C:\Temp\pp svc\config\config.json")
      Expect      = $false
    },
    @{
      Name        = "sibling install dir does not match"
      CommandLine = '"C:\Temp\pp svc 2\PhotoPrivacyWorker.exe" --mode service --config "C:\Temp\pp svc 2\config\config.json"'
      OwnedPaths  = @("C:\Temp\pp svc\config\config.json")
      Expect      = $false
    },
    @{
      Name        = "daemon-mode worker is not a service worker"
      CommandLine = '"C:\Temp\pp svc\PhotoPrivacyWorker.exe" --mode daemon --config "C:\Temp\pp svc\config\config.json"'
      OwnedPaths  = @("C:\Temp\pp svc\config\config.json")
      Expect      = $false
    },
    @{
      Name        = "unknown ownership kills nothing"
      CommandLine = '"C:\Temp\pp svc\PhotoPrivacyWorker.exe" --mode service --config "C:\Temp\pp svc\config\config.json"'
      OwnedPaths  = @()
      Expect      = $false
    }
  )
  foreach ($matchCase in $ownedMatchCases) {
    $matched = Test-OwnedServiceWorkerCommandLine -CommandLine $matchCase.CommandLine -OwnedConfigPaths $matchCase.OwnedPaths
    if ($matched -eq $matchCase.Expect) {
      Rec "PASS" ("worker ownership: " + $matchCase.Name)
    } else {
      Rec "FAIL" ("worker ownership: " + $matchCase.Name + " (expect " + $matchCase.Expect + ", got " + $matched + ")")
    }
  }
  $wqlLiteral = ConvertTo-WqlLiteral -Value "o'brien"
  if ($wqlLiteral -eq "o\'brien") {
    Rec "PASS" "worker ownership: WQL service name escapes single quotes"
  } else {
    Rec "FAIL" ("worker ownership: WQL escape returned " + $wqlLiteral)
  }

  # ---------- 0. 环境留证据 ----------
  Capture "00-env.txt" {
    [System.Environment]::OSVersion.VersionString
    whoami /all
  }

  # ---------- 1. 检查安装前状态（服务未安装且无残留共享注册表配置） ----------
  $preService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  $sharedRegPath = "HKLM:\SOFTWARE\PhotoPrivacy"
  $sharedValueExists = $false
  foreach ($valueName in @("ServicePipeInstallerSid", "ServicePipeAllowedSids", "ServicePipeName")) {
    if ((Test-Path $sharedRegPath) -and ($null -ne (Get-ItemProperty -Path $sharedRegPath -Name $valueName -ErrorAction SilentlyContinue))) {
      $sharedValueExists = $true
      break
    }
  }

  if ($null -eq $preService -and -not $sharedValueExists) {
    Rec "PASS" "Pre-install baseline check: Service '$ServiceName' is not installed."
  } else {
    Rec "FAIL" "Pre-install check failed: Service '$ServiceName' or shared PhotoPrivacy registry values already exist. Aborting to avoid altering existing service."
    throw "Pre-existing service or registry state detected: $ServiceName"
  }

  # ---------- 2. Staging 发布包到独立测试目录 ----------
  Copy-Item -Path (Join-Path $ReleaseDir "*") -Destination $testInstallDir -Recurse -Force
  $stagedWorker = Join-Path $testInstallDir "PhotoPrivacyWorker.exe"
  $stagedConfig = Join-Path $testInstallDir "config\config.json"
  if (Test-Path $stagedWorker) {
    Rec "PASS" "Staged release package to test directory: $testInstallDir"
  } else {
    Rec "FAIL" "Failed to stage release package to $testInstallDir"
    throw "Staging failed"
  }

  # ---------- 3. 安装服务 (install-service.ps1) ----------
  $currentUserSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
  $serviceCreatedByHarness = $true
  Capture "01-install.txt" {
    & $installScript `
      -Action "install" `
      -ServiceName $ServiceName `
      -ExePath $stagedWorker `
      -ConfigPath $stagedConfig `
      -InstallerSid $currentUserSid 6>&1
  }

  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  if ($null -ne $svc) {
    Rec "PASS" "Service '$ServiceName' registered in Windows SCM."
  } else {
    Rec "FAIL" "Service '$ServiceName' was not registered in SCM."
  }

  # ---------- 4. 运行状态与 Worker 进程常驻 ----------
  $waitCount = 0
  while ($waitCount -lt 15) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $svc -and $svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
      break
    }
    Start-Sleep -Seconds 1
    $waitCount++
  }

  if ($null -ne $svc -and $svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
    Rec "PASS" "Service '$ServiceName' is in Running state."
  } else {
    # CodeRabbit 返修：服务可能根本没注册（$svc 为 $null），严格模式下访问 $svc.Status
    # 会抛 PropertyNotFoundStrict 直接中断整个验证脚本。这里显式给出可读状态再交给 Rec。
    if ($null -ne $svc) {
      $startFailureStatus = [string]$svc.Status
    } else {
      $startFailureStatus = "NotRegistered"
    }
    Rec "FAIL" "Service '$ServiceName' failed to reach Running state (status: $startFailureStatus)."
  }

  $workerProcess = Get-Process "PhotoPrivacyWorker" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $stagedWorker -and $_.SessionId -eq 0 } |
    Select-Object -First 1

  if ($null -ne $workerProcess) {
    Rec "PASS" "Worker process is alive in Session $($workerProcess.SessionId) (PID: $($workerProcess.Id))."
  } else {
    Rec "FAIL" "Worker process not found running in Session 0."
  }

  Capture "02-service-status.txt" {
    sc.exe query $ServiceName
    sc.exe qc $ServiceName
  }

  # ---------- 5. 命名管道 IPC 连通性测试 (验证 #70 修复的 DACL 与通信) ----------
  # 等待服务建管道并通告注册表 ServicePipeName（没有通告就走静态名）
  $pipeName = "PhotoPrivacyCleaner.Service"
  $regPath = "HKLM:\SOFTWARE\PhotoPrivacy"
  $pipeResolved = $false
  for ($i = 0; $i -lt 10; $i++) {
    if (Test-Path $regPath) {
      $advertised = (Get-ItemProperty -Path $regPath -Name "ServicePipeName" -ErrorAction SilentlyContinue).ServicePipeName
      if (-not [string]::IsNullOrWhiteSpace($advertised)) {
        $pipeName = $advertised
        $pipeResolved = $true
        break
      }
    }
    Start-Sleep -Milliseconds 500
  }

  Rec "INFO" "Resolved service pipe name: $pipeName (advertised=$pipeResolved)"

  # 客户端通过命名管道发起 Ping 握手往返（对齐 NamedPipeIpcTransport 与协议 v1）
  $pipeConnected = $false
  $pipeResponse = ""
  try {
    # 客户端连接命名管道（动态解析 PipeAccessRights 细粒度掩码 0x12019B，兼顾 pwsh 与 Windows PowerShell）
    $pipeClient = $null
    $rightsType = "System.IO.Pipes.PipeAccessRights" -as [type]
    if ($null -ne $rightsType) {
      try {
        $clientAccessMask = [System.Enum]::ToObject($rightsType, 1180059)
        $pipeClient = [System.IO.Pipes.NamedPipeClientStream]::new(
          ".",
          $pipeName,
          $clientAccessMask,
          [System.IO.Pipes.PipeOptions]::Asynchronous,
          [System.Security.Principal.TokenImpersonationLevel]::Identification,
          [System.IO.HandleInheritability]::None
        )
      } catch { }
    }
    if ($null -eq $pipeClient) {
      $pipeClient = [System.IO.Pipes.NamedPipeClientStream]::new(
        ".",
        $pipeName,
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous,
        [System.Security.Principal.TokenImpersonationLevel]::Identification,
        [System.IO.HandleInheritability]::None
      )
    }

    $pipeClient.Connect(10000)
    # 帧编码必须无 BOM：`[Text.Encoding]::UTF8` 会先写 EF BB BF，服务端按行读到
    # U+FEFF 开头的 JSON 便反序列化失败，回 `invalid_request`——这正是 run
    # 42206839426 里「管道连上却拿不到合法 Ping 回包」的真因（#218），与 DACL 无关。
    $pipeEncoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.IO.StreamWriter]::new($pipeClient, $pipeEncoding)
    $writer.AutoFlush = $true
    $reader = [System.IO.StreamReader]::new($pipeClient, $pipeEncoding)

    # 发送 Ping 请求：契约只有 v/id/method 三字段（ipc-contract.md），不带 params。
    $pingRequest = '{"v":1,"id":"verify-1","method":"Ping"}'
    $writer.WriteLine($pingRequest)

    # 限制读取超时（5s），避免 Worker 挂起时阻塞
    $readTask = $reader.ReadLineAsync()
    if ($readTask.Wait(5000)) {
      $pipeResponse = $readTask.Result
      if (-not [string]::IsNullOrWhiteSpace($pipeResponse)) {
        try {
          $json = ConvertFrom-Json -InputObject $pipeResponse
          if ($json.v -eq 1 -and $json.id -eq "verify-1" -and $json.ok -eq $true) {
            $pipeConnected = $true
          } else {
            Capture "03-pipe-error.txt" { "Unexpected response payload: $pipeResponse" }
          }
        } catch {
          Capture "03-pipe-error.txt" { "Failed to parse JSON response: $pipeResponse" }
        }
      }
    } else {
      Capture "03-pipe-error.txt" { "Timeout waiting for response from named pipe '$pipeName'" }
    }
    $pipeClient.Dispose()
  } catch {
    Capture "03-pipe-error.txt" { $_.Exception.ToString() }
  }

  if ($pipeConnected) {
    Rec "PASS" "Named pipe connection to '$pipeName' succeeded and returned valid Ping response."
    Capture "03-pipe-response.txt" { $pipeResponse }
  } else {
    Rec "FAIL" "Failed to connect or exchange data through named pipe '$pipeName'."
  }

  # ---------- 6. 服务日志输出 ----------
  $programDataLogDir = Join-Path $env:ProgramData "PhotoPrivacy\logs"
  $hasLogs = $false
  if (Test-Path $programDataLogDir) {
    # 强制数组化：严格模式下只有一个文件时 Get-ChildItem 返回单对象（无 Count），
    # 无匹配时返回 $null，两种形态 .Count 都会抛错（run 42264782486）。
    $logFiles = @(Get-ChildItem -Path $programDataLogDir -Filter "worker-*.log" -ErrorAction SilentlyContinue)
    if ($logFiles.Count -gt 0) {
      $hasLogs = $true
      Capture "04-service-log.txt" {
        Get-Content $logFiles[0].FullName -Tail 30
      }
    }
  }

  if ($hasLogs) {
    Rec "PASS" "Service log file created and contains logs in $programDataLogDir."
  } else {
    Rec "INFO" "No ProgramData logs found; checking local install log folder."
    $localLogs = Join-Path $testInstallDir "logs"
    if (Test-Path $localLogs) {
      Capture "04-service-log.txt" {
        Get-ChildItem -Path $localLogs
      }
    }
  }

  # ---------- 7. 停止与重启 ----------
  $stopSuccess = $false
  Capture "05-stop-start.txt" {
    Stop-Service -Name $ServiceName -Force
    for ($i = 0; $i -lt 15; $i++) {
      Start-Sleep -Seconds 1
      $stoppedSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
      if ($null -ne $stoppedSvc -and $stoppedSvc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        $script:stopSuccess = $true
        "Stopped status confirmed: " + $stoppedSvc.Status
        break
      }
    }
    if (-not $script:stopSuccess) {
      "Failed to reach Stopped status after Stop-Service"
    } else {
      Start-Service -Name $ServiceName
      for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        $restartedSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $restartedSvc -and $restartedSvc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
          "Restarted status: " + $restartedSvc.Status
          break
        }
      }
    }
  }

  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  if ($stopSuccess -and $null -ne $svc -and $svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
    Rec "PASS" "Service '$ServiceName' successfully stopped and restarted."
  } else {
    Rec "FAIL" "Service '$ServiceName' stop/restart cycle failed (stopped=$stopSuccess)."
  }

  # ---------- 8. 卸载与清理验证 ----------
  # 卸载失败会以终止性错误抛出（install-service.ps1 不再假报 Uninstalled，CodeRabbit 返修）；
  # 这里捕获后作为断言 FAIL 记录，避免整段验证脚本被中断——后面的"服务已消失/无孤儿进程"
  # 断言仍要跑完，才能同时看到卸载失败和残留状态。
  $script:uninstallFailure = $null
  Capture "06-uninstall.txt" {
    try {
      & $installScript `
        -Action "uninstall" `
        -ServiceName $ServiceName 6>&1
    } catch {
      $script:uninstallFailure = $_.Exception.Message
      Write-Output ("uninstall reported failure: " + $script:uninstallFailure)
    }
  }

  # 等待 SCM 标记删除完成（Windows 删除服务通常需要等全部句柄释放，带轮询）
  # 分工：install-service.ps1 内部已有 10s 有界轮询（删除是异步的，单次复查会误判失败）；
  # 这里再兜一层，覆盖"脚本 throw 之后 SCM 才清干净"的边界，不把已消失的服务报成残留。
  $uninstalled = $false
  for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $uninstalledSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $uninstalledSvc) {
      $uninstalled = $true
      break
    }
  }

  if ($uninstalled -and $null -eq $script:uninstallFailure) {
    Rec "PASS" "Service '$ServiceName' successfully uninstalled."
  } elseif ($uninstalled) {
    Rec "FAIL" ("Uninstall reported a failure even though the service left SCM: " + $script:uninstallFailure)
  } else {
    Rec "FAIL" "Service '$ServiceName' still exists in SCM after uninstall."
  }

  # 注册表清理检查
  $sidCleared = -not (Get-ItemProperty -Path $regPath -Name "ServicePipeInstallerSid" -ErrorAction SilentlyContinue)
  if ($sidCleared) {
    Rec "PASS" "Registry ServicePipeInstallerSid cleaned upon uninstall."
  } else {
    Rec "FAIL" "Registry ServicePipeInstallerSid not cleaned upon uninstall."
  }

  # 残留进程检查（带轮询等待进程彻底退出）
  $processesCleaned = $false
  for ($i = 0; $i -lt 10; $i++) {
    # 数组化：单进程时 Get-Process 返回单对象（无 Count），严格模式下 .Count 会抛错。
    $remainingProcesses = @(Get-Process "PhotoPrivacyWorker" -ErrorAction SilentlyContinue |
      Where-Object { $_.Path -eq $stagedWorker })
    if ($remainingProcesses.Count -eq 0) {
      $processesCleaned = $true
      break
    }
    Start-Sleep -Seconds 1
  }

  if ($processesCleaned) {
    Rec "PASS" "No orphaned PhotoPrivacyWorker process remains after uninstall."
  } else {
    Rec "FAIL" "Found orphaned PhotoPrivacyWorker process after uninstall."
  }

  # ---------- 9. 卸载回归：服务已不在 SCM 时不得谎报成功 ----------
  # 卸载失败必须是终止性错误，但"服务本来就不存在"是正常空操作：既不能 throw，
  # 也不能输出 Uninstalled——否则调用方无法区分"真的卸载掉了"和"根本没卸成功"。
  $absentService = "PhotoPrivacySvcAbsent-" + ([Guid]::NewGuid().ToString("N").Substring(0, 8))
  $script:absentFailure = $null
  $script:absentOutput = ""
  Capture "07-uninstall-absent.txt" {
    try {
      $script:absentOutput = (& $installScript `
          -Action "uninstall" `
          -ServiceName $absentService 6>&1 | Out-String)
      Write-Output $script:absentOutput
    } catch {
      $script:absentFailure = $_.Exception.Message
      Write-Output ("uninstall of absent service threw: " + $script:absentFailure)
    }
  }

  if ($null -ne $script:absentFailure) {
    Rec "FAIL" ("Uninstall of an absent service threw instead of reporting 'Not found': " + $script:absentFailure)
  } elseif ($script:absentOutput -match "Not found") {
    Rec "PASS" "Uninstall of an absent service reports 'Not found' without throwing."
  } else {
    Rec "FAIL" ("Uninstall of an absent service printed neither 'Not found' nor an error: " + $script:absentOutput.Trim())
  }

  if ($script:absentOutput -match "Uninstalled") {
    Rec "FAIL" "Uninstall of an absent service must not report 'Uninstalled'."
  } else {
    Rec "PASS" "Uninstall of an absent service does not report 'Uninstalled'."
  }

} finally {
  Cleanup-TestEnvironment
}

# ---------- 总结 ----------
$conclusion = "CONCLUSION: PASS={0} FAIL={1}" -f $passCount, $failCount
Add-Content -Path $summaryFile -Value $conclusion -Encoding utf8
Write-Host "---------------- summary ----------------"
Get-Content -Path $summaryFile

if ($failCount -gt 0) {
  exit 1
}
exit 0
