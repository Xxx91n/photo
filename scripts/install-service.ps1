#Requires -Version 5.1
#Requires -PSEdition Desktop,Core
<#
.SYNOPSIS
  Install/uninstall PhotoPrivacy Windows service.
#>
param(
  [string]$Action = "install",
  [string]$ServiceName = "PhotoPrivacyCleaner",
  [string]$ExePath = "",
  [string]$ConfigPath = "",
  # 服务管道 DACL 使用的安装者 SID（#70，决策 #82 D3）。留空则取当前（提权）账户的 SID。
  # 若安装用管理员与日常使用用户不是同一账户，须显式传日常用户 SID，否则其 UI 连不上服务管道。
  [string]$InstallerSid = "",
  # 服务管道 DACL 的额外授权用户（#1 opt-in）：账户名（DOMAIN\user / machine\user）或 SID
  # 字符串，可多个。安装者本身始终在授权集合内（保底单用户行为不变）；传入的账户追加进
  # 集合后其 UI 可连服务管道。解析不到的账户会中止安装（fail-closed，不静默漏授权）。
  # 注意：被授权账户能读到服务侧审计信息（GetRecentLogs），只授予信任用户；
  # 未列入集合的账户仍被拒。留空 = 不 opt-in，行为与 #70 一致（仅安装者）。
  [string[]]$AuthorizedUsers = @()
)

# 服务管道显式 ACL 的注册表落点：服务（LocalSystem）运行时读取该值构建 DACL；
# 键默认继承允许 Users 读，SID 字符串非敏感。缺失时服务 fail-closed 回退 CurrentUserOnly。
$RegistryPath = "HKLM:\SOFTWARE\PhotoPrivacy"
$RegistryValueName = "ServicePipeInstallerSid"
# 授权 SID 集合（安装者 + -AuthorizedUsers，#1 opt-in）；服务端优先读该 MULTI_SZ 值，
# 值不存在时回退安装者单值（未 opt-in 不扩权）。卸载时两个值一并清除。
$RegistryAllowedValueName = "ServicePipeAllowedSids"
# 服务管道通告名（#7 命名熵）：服务进程（LocalSystem）建例后写入，UI 动态解析服务端点优先读它；
# 不由安装脚本登记，但卸载时一并清除（服务被删后残留的随机名对 UI 是「探不到」的陈旧值）。
$RegistryAdvertisedValueName = "ServicePipeName"

$ErrorActionPreference = "Stop"

# worker 归属匹配的纯函数（#218 CodeRabbit 返修）：与 verify-windows-service.ps1 共用同一份，
# 避免"卸载脚本里一套口径、回归断言里另一套"。随包发布由 scripts/publish-app.ps1 一并复制。
. (Join-Path $PSScriptRoot "service-worker-match.ps1")

# Auto-detect exe and config relative to this script (release/<rid>/scripts/)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDir = Split-Path -Parent $scriptDir
if ([string]::IsNullOrWhiteSpace($ExePath)) {
  $ExePath = Join-Path $releaseDir "PhotoPrivacyWorker.exe"
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
  $ConfigPath = Join-Path $releaseDir "config\config.json"
}
# 卸载只动 SCM / 注册表 / 进程，不读 worker 与 config；
# 校验放在 install 分支内，卸载才不会被"发布产物已移走"挡在开关之前（#218）。
function Remove-ServiceCompat([string]$Name) {
  if ($PSVersionTable.PSEdition -eq 'Core') {
    try { Remove-Service -Name $Name -ErrorAction Stop } catch { sc.exe delete $Name }
  } else {
    sc.exe delete $Name
  }
}

# 账户名或 SID 字符串 → SID 字符串（#1）。已是合法 SID 直接返回；否则经 NTAccount 解析。
# 解析不到（账户不存在/拼写错误）抛错终止安装：宁可安装失败也不静默漏授权。
function Resolve-SidString([string]$NameOrSid) {
  try {
    return ([System.Security.Principal.SecurityIdentifier]::new($NameOrSid)).Value
  } catch [System.ArgumentException] { }
  try {
    $account = [System.Security.Principal.NTAccount]::new($NameOrSid)
    return ($account.Translate([System.Security.Principal.SecurityIdentifier])).Value
  } catch {
    throw "Account or SID cannot be resolved: '$NameOrSid' (account not found or invalid SID)"
  }
}

switch ($Action.ToLower()) {
  "install" {
    if (-not (Test-Path $ExePath)) { throw "Worker exe not found: $ExePath" }
    if (-not (Test-Path $ConfigPath)) { throw "Config not found: $ConfigPath" }
    if ([string]::IsNullOrWhiteSpace($InstallerSid)) {
      $InstallerSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    }
    # 授权集合 = 安装者保底 + -AuthorizedUsers 显式授权（#1 opt-in）：去重后统一解析为 SID。
    $authorizedNames = @($AuthorizedUsers | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($authorizedNames.Count -gt 0) {
      # 集合模式（#1 opt-in）：安装者条目同样经解析——账户名/typo 直接中止安装，
      # 避免「集合里有合法 SID 但安装者被服务端静默剔除」的假成功（CodeRabbit 返修）。
      # 不带 -AuthorizedUsers 时保持 #70 既有行为（InstallerSid 原样登记，非法时服务端
      # fail-closed 回退 CurrentUserOnly）。
      $InstallerSid = Resolve-SidString $InstallerSid
    }
    $allowedSids = [System.Collections.Generic.List[string]]::new()
    [void]$allowedSids.Add($InstallerSid)
    foreach ($name in $authorizedNames) {
      $sid = Resolve-SidString $name
      if (-not $allowedSids.Contains($sid)) { [void]$allowedSids.Add($sid) }
    }
    $binPath = '"{0}" --mode service --config "{1}"' -f $ExePath, $ConfigPath
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      Remove-ServiceCompat $ServiceName
    }
    if ($PSVersionTable.PSEdition -eq "Core") {
      New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName "PhotoPrivacy Cleaner" -StartupType Automatic
    } else {
      sc.exe create $ServiceName binPath= $binPath start= auto
    }
    # 先登记 SID 再启动服务：服务首次建管道时即按最新授权集合构建 DACL（重装/升级覆盖旧值）。
    if (-not (Test-Path $RegistryPath)) { New-Item -Path $RegistryPath -Force | Out-Null }
    New-ItemProperty -Path $RegistryPath -Name $RegistryValueName -Value $InstallerSid -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $RegistryPath -Name $RegistryAllowedValueName -Value $allowedSids.ToArray() -PropertyType MultiString -Force | Out-Null
    Start-Service -Name $ServiceName
    Write-Host "Installed and started $ServiceName (service pipe ACL: $($allowedSids.Count) authorized SID(s))"
  }
  "uninstall" {
    # 注册表清理刻意不放在这里（CodeRabbit 返修）：服务仍注册时清掉 SID 登记，
    # 下一次启动的服务管道会回退到 CurrentUserOnly，把已授权的桌面用户挡在门外。
    # 清理统一放到卸载结论之后——确认服务已从 SCM 消失、或本来就不存在才清。
    # 本次安装的归属依据（CodeRabbit 返修）：优先用 SCM 里记着的 binPath——安装时写死的
    # `--config` 才是这台服务真正读的配置（verify 用临时 staged 目录安装，与本脚本位置推导
    # 出的默认 -ConfigPath 并不相同）。服务已被手工删掉时只剩本次 -ConfigPath 可用。
    $ownedConfigs = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) { [void]$ownedConfigs.Add($ConfigPath) }
    # 服务名来自参数，WQL 字面量必须转义单引号，否则带 `'` 的名字会让 Filter 语法错误、
    # 查不到自己这台服务（CodeRabbit 返修）。
    $svcFilter = "Name = '{0}'" -f (ConvertTo-WqlLiteral -Value $ServiceName)
    $svcRecord = Get-CimInstance Win32_Service -Filter $svcFilter -ErrorAction SilentlyContinue |
      Select-Object -First 1
    if ($null -ne $svcRecord) {
      # 解析走共享函数（含空格路径按引号配对取全，无引号取完整令牌），避免把 `C:\Te` 当配置路径。
      $installedConfig = Get-ConfigPathArgument -CommandLine $svcRecord.PathName
      if (-not [string]::IsNullOrWhiteSpace($installedConfig) -and
        -not $ownedConfigs.Contains($installedConfig)) {
        [void]$ownedConfigs.Add($installedConfig)
      }
    }
    $uninstallRequested = $false
    # "确认不存在"与"查询失败"必须区分（CodeRabbit 返修）：Get-Service 抛错既可能是服务不存在
    # （正常形态），也可能是查询本身故障。前者可以往下走并清注册表，后者什么都不能断言——
    # 既不能删服务，也不能清 SID 登记，只能终止并让调用方重试。
    $existingSvc = $null
    try {
      $existingSvc = Get-Service -Name $ServiceName -ErrorAction Stop
    } catch [Microsoft.PowerShell.Commands.ServiceCommandException] {
      $existingSvc = $null
    } catch {
      $queryFailure = "Cannot determine whether service '$ServiceName' exists: $($_.Exception.Message)"
      Write-Warning $queryFailure
      throw $queryFailure
    }
    $serviceConfirmedGone = $false
    if ($null -ne $existingSvc) {
      # -NoWait：Stop-Service 默认会阻塞到服务进 Stopped（StopPending 上最长可达 30s），
      # 下面那句 15s 有界等待就永远轮不到——卸载会卡在"等 SCM 停服务"，走不到超时清理
      # 分支（CodeRabbit 返修）。先立即返回，再由有界等待裁决"停没停住"。
      Stop-Service -Name $ServiceName -Force -NoWait -ErrorAction SilentlyContinue
      # SCM 删除服务要等 worker 真正退出（句柄释放）才算完；stop 超时会让 `sc delete`
      # 只把服务标成 deleting，紧接着的卸载断言就会看到"服务仍在"（#218）。
      $svcToStop = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
      if ($null -ne $svcToStop) {
        try {
          $svcToStop.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(15))
        } catch {
          Write-Host "Service '$ServiceName' did not reach Stopped within 15s; deleting anyway."
        }
      }
      Remove-ServiceCompat $ServiceName
      # 不在这里直接报 Uninstalled（CodeRabbit 返修）：worker 还握着服务句柄时 `sc delete`
      # 只把服务标成"待删除"，服务仍留在 SCM 里。真正的结论等 worker 清理之后的复核给出。
      $uninstallRequested = $true
    } else {
      $uninstallRequested = $false
      # 查询没报错且返回空 = 服务确认不存在，下面才允许清 SID 登记。
      $serviceConfirmedGone = $true
      Write-Host "Not found"
    }
    # 兜底：SCM 删服务不会结束已经脱离的进程（Stop-Service 超时、进程不响应 SERVICE_STOP 时
    # 会留下孤儿 worker，占住审计目录并让下次安装的命名管道抢占失败，#218）。
    # 归属限定（CodeRabbit 返修）：只清"本次安装"的 worker——命令行必须是 `--mode service`
    # 且 `--config` 命中上面收集到的归属路径。服务不存在时，另一个同名服务 / 另一份安装留下的
    # worker 与本安装无关，不得被顺手杀掉；归属完全无法确定（既无服务记录又无 -ConfigPath）时
    # 一个都不杀。
    # 匹配走共享函数 Test-OwnedServiceWorkerCommandLine：先把命令行里的 `--config` 解析出来，
    # 再与归属路径规范化比较（`/` 与 `\` 等价、忽略大小写、尾分隔符无关）。不拼正则，
    # 含空格路径与分隔符差异都不会漏杀/误杀（CodeRabbit 返修）。
    $ownedConfigArray = $ownedConfigs.ToArray()
    $leftovers = @()
    try {
      $leftovers = @(Get-CimInstance Win32_Process -Filter "Name = 'PhotoPrivacyWorker.exe'" -ErrorAction Stop |
        Where-Object {
          Test-OwnedServiceWorkerCommandLine -CommandLine $_.CommandLine -OwnedConfigPaths $ownedConfigArray
        })
    } catch {
      # 查询失败不能当成"没有残留"：审计目录可能仍被占住（CodeRabbit 返修）。
      Write-Warning "Cannot query PhotoPrivacyWorker processes: $($_.Exception.Message)"
    }
    if ($leftovers.Count -gt 0) {
      foreach ($leftover in $leftovers) {
        try {
          Stop-Process -Id $leftover.ProcessId -Force -ErrorAction Stop
          Write-Host "Killed leftover service worker (pid $($leftover.ProcessId))"
        } catch {
          Write-Warning "Failed to stop leftover service worker (pid $($leftover.ProcessId)): $($_.Exception.Message)"
        }
      }
      # 复查：Stop-Process 不抛错也可能有进程没退干净（句柄未释放/正在退出），
      # 残留会让审计目录继续被占用，必须在卸载输出里如实报出来而不是假报成功。
      # 不用 exit 非零：verify-windows-service.ps1 用 `& $installScript` 调用本脚本，
      # 脚本内 exit 会连带终止调用方验证脚本；残留由验证侧"孤儿进程"断言独立判 FAIL。
      $survivors = @($leftovers | Where-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
      if ($survivors.Count -gt 0) {
        $pids = ($survivors | ForEach-Object { $_.ProcessId }) -join ', '
        Write-Warning "Leftover service worker still running after uninstall (pid $pids); audit directory may stay locked."
      }
    }
    # 删除复核（CodeRabbit 返修）：worker 清理后再查服务是否真的从 SCM 消失。
    # `sc delete` 遇上未释放的句柄只会把服务标成待删除（注册表 DeleteFlag=1），服务仍在，
    # 此时报 Uninstalled 会骗过调用方。但删除是异步的：句柄刚释放的那几百毫秒里服务仍可枚举，
    # 单次复查会把"正在删除"误判成"卸载失败"，所以先做有界轮询，到期仍存在才按失败上报。
    if ($uninstallRequested) {
      $deleteDeadline = [DateTime]::UtcNow.AddSeconds(10)
      $remainingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
      while ($null -ne $remainingSvc -and [DateTime]::UtcNow -lt $deleteDeadline) {
        Start-Sleep -Milliseconds 500
        $remainingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
      }

      if ($null -ne $remainingSvc) {
        $pendingDelete = $false
        $svcRegKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        if (Test-Path -LiteralPath $svcRegKey) {
          $svcKeyProps = Get-ItemProperty -LiteralPath $svcRegKey -ErrorAction SilentlyContinue
          if ($null -ne $svcKeyProps -and $null -ne $svcKeyProps.PSObject.Properties['DeleteFlag']) {
            $pendingDelete = ([int]$svcKeyProps.PSObject.Properties['DeleteFlag'].Value -ne 0)
          }
        }
        $remainingStatus = [string]$remainingSvc.Status
        if ($pendingDelete) {
          $uninstallFailure = "Failed to uninstall service '$ServiceName': still marked for deletion (DeleteFlag=1, status: $remainingStatus). It disappears only after the worker releases its handle; retry the uninstall once it exits."
        } else {
          $uninstallFailure = "Failed to uninstall service '$ServiceName': still registered in SCM (status: $remainingStatus)."
        }
        # 失败路径保留注册表值：服务还在，SID 登记必须留着，否则重启后的服务管道会降级授权。
        Write-Warning $uninstallFailure
        # 终止性错误（CodeRabbit 返修）：只写 Warning 时脚本会正常返回，直接调用方（含 CI）
        # 会把卸载当成功。不用 exit——本脚本在验证流程里被 `& $installScript` 同进程调用，
        # exit 会连带终止调用方脚本；throw 只把失败传播给能捕获的调用方。
        throw $uninstallFailure
      }

      Write-Host "Uninstalled $ServiceName"
      $serviceConfirmedGone = $true
    }

    # 只有"确认服务已从 SCM 消失"才清 SID 登记（CodeRabbit 返修）：查询失败时服务状态未知，
    # 清掉登记会让下次启动的服务管道回退到 CurrentUserOnly。
    if ($serviceConfirmedGone) {
      Remove-ItemProperty -Path $RegistryPath -Name $RegistryValueName -ErrorAction SilentlyContinue
      Remove-ItemProperty -Path $RegistryPath -Name $RegistryAllowedValueName -ErrorAction SilentlyContinue
      Remove-ItemProperty -Path $RegistryPath -Name $RegistryAdvertisedValueName -ErrorAction SilentlyContinue
    } else {
      Write-Warning "Service '$ServiceName' state could not be confirmed; keeping registry SID values untouched."
    }
  }
  default { Write-Host "Usage: -Action install|uninstall -ExePath <p> -ConfigPath <p> [-AuthorizedUsers <user...>]"; exit 1 }
}
