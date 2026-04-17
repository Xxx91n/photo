# PhotoPrivacy CLI 成熟发行版 EXE 路线图（2026-04-17）

## 目标定义

“用户可用的成熟发行版 EXE”定义为：

1. 能在 Windows 10/11 x64 上开箱即用（无需本机安装 .NET 运行时）；
2. 具备稳定的热文件夹处理、失败重试与隔离、完整审计日志；
3. 具备发布前自动验收脚本（测试 + smoke + publish）；
4. 有可执行的回归检查与发布节奏，不依赖手工临时步骤。

## 当前状态（截至 2026-04-17）

- 已完成：
  - stay_open 单进程桥接、FSW 事件驱动、恢复扫描、审计日志。
  - ExifTool 生命周期审计：`exiftool_started` / `exiftool_restarted` / `exiftool_version_warning`。
  - 生效配置导出：`--print-effective-config true`。
  - 预发布脚本：`scripts/release-readiness.ps1`（test + smoke + publish）。
  - 打包脚本：`scripts/publish-cli-exe.ps1`。
- 已验证：`dotnet test PhotoPrivacy.sln` 全绿；预发布脚本全流程可跑通。

## 发行时间规划

### 阶段 A：Release Candidate（RC）
- 时间：2026-04-18 ~ 2026-04-24（1 周）
- 交付：`v0.9.0-rc` ZIP 包
- 退出条件：
  1. 连续 3 天，每天执行 `scripts/release-readiness.ps1` 成功；
  2. 在真实热文件夹场景下完成 1000 小文件 + 100 大文件压测；
  3. 修复 RC 阶段发现的 P1/P2 问题。

### 阶段 B：稳定化与回归收口
- 时间：2026-04-25 ~ 2026-05-01（1 周）
- 交付：`v0.9.5-rc2` ZIP 包
- 退出条件：
  1. 关键路径无超时/死锁回归（尤其是 ExifTool 与 host 退出流程）；
  2. 审计事件与 README 行为一致，支持用户排障；
  3. 发布脚本参数与输出结构冻结。

### 阶段 C：成熟发行版
- 目标发布时间：**2026-05-08（周五）**
- 交付：`v1.0.0` 成熟发行版 EXE（ZIP）
- 发布门槛（必须同时满足）：
  1. `scripts/release-readiness.ps1` 在干净环境通过；
  2. 无 P1/P2 未解决缺陷；
  3. README 安装/配置/验证步骤可由普通用户复现；
  4. 用户侧试运行反馈通过（至少 1 轮）。

## 发布产物规范（v1.0.0）

- 文件：`PhotoPrivacy.Cli-1.0.0-win-x64.zip`
- 解压后包含：
  - `PhotoPrivacy.Cli.exe`
  - `config.sample.json`
  - `README.md`

## 风险与缓冲

- 风险 1：真实环境 ExifTool 版本差异引发兼容问题。
  - 缓冲：保留 `exiftool_version_warning`，并在 RC 阶段扩大样本机验证。
- 风险 2：高频文件风暴下 FSW 丢事件。
  - 缓冲：持续压测 + 恢复扫描审计核对。
- 风险 3：发布脚本在不同 PowerShell 会话参数兼容性差异。
  - 缓冲：脚本参数全部改为字符串并内部归一化解析。

## 用户可执行的一键发布前检查

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\release-readiness.ps1 -Version 1.0.0 -Runtime win-x64
```
