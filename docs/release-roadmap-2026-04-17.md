# PhotoPrivacy 成熟发行版 EXE 路线图（2026-04-17）

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
  - 强杀收口：ExifTool 子进程接入 Job Object（`KILL_ON_JOB_CLOSE`）防孤儿进程。
  - 发布门禁扩展：`release-readiness.ps1` 可选第 4 步强杀验收（`-IncludeForceKillCheck true`）。
  - 生效配置导出：`--print-effective-config true`。
  - 预发布脚本：`scripts/release-readiness.ps1`（test + smoke + publish）。
  - 打包脚本：`scripts/publish-app.ps1`。
- 已验证：`dotnet test PhotoPrivacy.sln` 全绿；预发布脚本全流程可跑通。

## 过渡版里程碑（2026-04-20）

- GUI 过渡收口已完成：核心交互按成熟版本口径恢复（优先复用旧代码，不做全新视觉重构）。
- 三进程边界保持不回退：`PhotoPrivacy.exe`（GUI）+ `PhotoPrivacyWorker.exe`（background/service）。
- 用户交付路径收口：发布、安装、服务管理与强杀清理验收链路已统一。
- 当前阶段定位：成熟过渡版（可落地交付）；全新 GUI 视觉与信息架构重构进入下一阶段。

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
- 交付：`v1.0.0` 成熟发行版（GUI + Worker，ZIP）
- 发布门槛（必须同时满足）：
  1. `scripts/release-readiness.ps1` 在干净环境通过；
  2. 无 P1/P2 未解决缺陷；
  3. README 安装/配置/验证步骤可由普通用户复现；
  4. 用户侧试运行反馈通过（至少 1 轮）。

## 发布产物规范（v1.0.0）

- 文件：`PhotoPrivacy-1.0.0-win-x64.zip`
- 解压后包含：
  - `PhotoPrivacy.exe`（GUI）
  - `PhotoPrivacyWorker.exe`（Worker）
  - `config/config.sample.json`
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

## 下一阶段（UI 重构，Phase-1）

- 新增 `PhotoPrivacy.Ui`（Avalonia，`net10.0`）工程骨架，逐步替代 WinForms 托盘。
- Windows：默认后台托盘 + 服务管理器能力（Install/Uninstall/Start/Stop）。
- Linux：保留服务模式主路径，后续补齐 Avalonia 后台托盘（无服务管理器 Tab）。
- 详细分解计划：`docs/superpowers/plans/2026-04-19-photoprivacy-ui-refactor-phase1.md`

## 下一阶段（UI 重构，Phase-2 in progress）

- Avalonia 主窗口已接入状态栏与工具栏骨架：暂停/恢复、清空日志、打开配置目录。
- 已接入实时日志尾读（`_audit/audit-*.jsonl`）。
- 已接入跨平台托盘骨架（Avalonia `TrayIcon`）：暂停/恢复、打开主窗口、退出。
- Windows 服务管理器页签已接入基础动作：Install/Uninstall/Start/Stop（含 runas 提升）。
- `ExifToolVersion` 已改为运行时动态刷新（启动后轮询 bridge 实际版本，不再是启动快照占位）。
- 服务管理器参数构造已补测试覆盖：安装命令参数、`sc.exe` 启动参数与 runas 判定。
- Avalonia 依赖链已增加 `Tmds.DBus.Protocol 0.92.0` 覆盖，消除已知高危告警。
- 旧 WinForms 托盘过渡实现已移除，运行入口拆分为 `PhotoPrivacy.exe`（GUI）与 `PhotoPrivacyWorker.exe`（Worker）。
- 发布脚本已增加 Runtime 入参防呆：拒绝 `all/any`，要求显式 RID。
- 启动体验收口：Windows 构建默认 `WinExe`，避免 GUI 启动时控制台黑窗。
- 日志面板已改为 audit 实时尾随读取（按事件类型中文映射、颜色、详细事件开关）。
- 服务管理器已接入友好错误码翻译与安装前自动清理流程（规避 1073/1062 常见问题）。
- 下一批待完成：
  1. 完成 Linux 背景托盘实机验证与平台差异收口
  2. 完成跨平台打包与 smoke 文档的统一验收沉淀
  3. 继续扩大服务管理器动作失败路径覆盖（含更多 sc.exe 异常映射）
