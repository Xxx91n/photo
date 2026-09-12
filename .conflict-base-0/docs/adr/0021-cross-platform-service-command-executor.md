# 跨平台服务生命周期管理（IServiceCommandExecutor 三平台 + 提权脚本调用）

选择抽象 IServiceCommandExecutor 接口 + 三平台实现 + 提权辅助进程模式。Windows 现有 sc.exe + UAC Verb 保持；Linux 新增 SystemdCommandExecutor（pkexec 调用 install-systemd-service.sh）；macOS 新增 LaunchdCommandExecutor（osascript 'do shell script with administrator privileges' 调用 install-launchd-service.sh）。统一接口已存在（IServiceCommandExecutor.Execute(ProcessStartInfo)），新增两个实现 + 工厂切换。脚本随发布包放到 release/<rid>/scripts/。

## Considered Options

- **UI 层硬编码 if/else**：散落 ViewModel，违反抽象。被否决。
- **独立管理服务常驻接收操作请求**：过度工程化，新增常驻进程。被否决。
- **抽象 + 三实现 + 提权脚本（当前选择）**：复用已有接口，新增两个平台实现，零新增 NuGet 依赖。

## Consequences

- 新增 SystemdCommandExecutor、LaunchdCommandExecutor 两个类 + 工厂。
- install-systemd-service.sh / install-launchd-service.sh 随发布包复制到 release/<rid>/scripts/（见 ADR 0024）。
- 结果通过 Process.ExitCode 返回，stdout/stderr 不重定向（操作成功/失败以 exit code 为准）。
- pkexec 未安装时 UI 提示「请手动 sudo 运行脚本」。
