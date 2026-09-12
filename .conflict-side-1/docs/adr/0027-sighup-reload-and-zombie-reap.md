# SIGHUP 配置重载 + ExifTool 僵尸进程 reap

PosixSignalHooks 新增 SIGHUP 注册，信号到来调 _runtimeControl.ReloadConfig()（与 IPC ReloadConfig 复用同一代码路径）。systemd unit 加 ExecReload=/bin/kill -HUP $MAINPID。ExifTools 子进程 ProcessExifToolProcess 已设 EnableRaisingEvents=true，新增订阅 Exited 事件，事件内调 _process.WaitForExit() 同步回收退出码避免 zombie。StopAsync 主路径不动。

## Considered Options

- **新增独立 SIGHUP-only 加载逻辑**：与 IPC ReloadConfig 重复代码。被否决。
- **轮询 _process.HasExited 检测僵尸**：噪声大延迟大。被否决。
- **复用 IPC ReloadConfig 代码路径 + Exited 事件 reap（当前选择）**：零新增逻辑分支。

## Consequences

- PosixSignalHooks 新增 SIGHUP case；systemd unit 加 ExecReload。
- ProcessExifToolProcess 订阅 Exited 事件（已有 EnableRaisingEvents=true 前提）。
- 需测试验证 SIGHUP 触发后 Config 被 reload；验证 ExifTool 非自然退出时无 zombie。
