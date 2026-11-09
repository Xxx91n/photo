# Windows 服务 worker 归属匹配（#218 / CodeRabbit 返修）。
#
# 卸载时要保底清理"已经脱离 SCM 的 worker 进程"，但只能清本次安装的进程：别的同名服务、
# 另一份安装的 worker 都跟本安装无关，顺手杀掉会伤到别处（CodeRabbit Major）。
# 归属判断标准 = worker 命令行里 `--mode service` 且 `--config <path>` 与本次安装的配置路径等价。
#
# 这里只放纯函数（无任何副作用），供 install-service.ps1 dot-source 使用；同一份逻辑被
# verify-windows-service.ps1 拿去做回归断言，避免"脚本里一套、测试里另一套"。
#
# 解析规则（CodeRabbit 返修）：
#   * `--config` 参数优先按引号配对取，含空格的路径不会被截断成 `C:\Te`；
#   * 无引号时按完整令牌取（遇到空白或引号结束）；
#   * 比较前把 `/` 归一成 `\` 并忽略大小写——SCM binPath 与本次 `-ConfigPath` 可能用不同写法。

Set-StrictMode -Version Latest

# WQL 字符串字面量转义：`Get-CimInstance -Filter` 里的单引号必须成对，否则 "o'brien" 这类
# 服务名会让过滤条件直接语法错误，保底清理就查不到自己的服务。
function ConvertTo-WqlLiteral {
  param(
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value
  )
  # WQL 字面量用反斜杠转义（MS-WQL）：反斜杠本身要先转义，否则路径里的 `\` 会把
  # 后面那个单引号的转义吃掉。
  return $Value.Replace('\', '\\').Replace("'", "\'")
}

# 从命令行（服务 binPath 或进程 CommandLine）里取 `--config` 的值；取不到返回 $null。
function Get-ConfigPathArgument {
  param(
    [AllowNull()][string]$CommandLine
  )
  if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $null }
  # 引号配对优先：保住含空格的完整路径。
  if ($CommandLine -match '--config\s+"([^"]+)"') { return $Matches[1] }
  # 无引号：按令牌取到下一个空白，不带引号截断的半截路径。
  if ($CommandLine -match '--config\s+([^\s"]+)') { return $Matches[1] }
  return $null
}

# 配置路径比较用的规范形态：`/` 与 `\` 等价、Windows 下大小写不敏感、忽略尾分隔符。
function ConvertTo-NormalizedConfigPath {
  param(
    [AllowNull()][string]$Path
  )
  if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
  return $Path.Replace('/', '\').TrimEnd('\').ToLowerInvariant()
}

# 判定一条 worker 命令行是否属于本次安装（`--mode service` + 归属配置路径）。
function Test-OwnedServiceWorkerCommandLine {
  param(
    [AllowNull()][string]$CommandLine,
    [AllowNull()][string[]]$OwnedConfigPaths
  )
  if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $false }
  if ($null -eq $OwnedConfigPaths) { return $false }
  # 后台模式的 worker 与本安装无关：`--mode service` 后面必须是参数的结束。
  if ($CommandLine -notmatch '--mode\s+service(\s|$)') { return $false }
  $commandConfig = Get-ConfigPathArgument -CommandLine $CommandLine
  if ([string]::IsNullOrWhiteSpace($commandConfig)) { return $false }
  $candidate = ConvertTo-NormalizedConfigPath -Path $commandConfig
  foreach ($owned in $OwnedConfigPaths) {
    if ([string]::IsNullOrWhiteSpace($owned)) { continue }
    if ((ConvertTo-NormalizedConfigPath -Path $owned) -eq $candidate) { return $true }
  }
  return $false
}
