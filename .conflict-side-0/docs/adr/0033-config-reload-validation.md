# Reload Config 前 validate，失败保留旧配置 + IPC 返回错误

Worker 收到 ReloadConfig IPC 时：(1) 用 AppConfigLoader.Load(configPath) 尝试加载；(2) 调用 AppConfigValidator.Validate(config) 验证 schema 和 ExifTool 路径合法性；(3) valid → swap 内部 _runtime.WatchDirectoryAccessor / 配置；(4) invalid → 保留旧配置，返回 WorkerIpcResponse { Ok: false, Error: "配置验证失败：<reasons>" }。GUI 收到失败后显示错误，不切换状态。

## Considered Options

- **不 validate 让 Worker 处理坏配置**：用户不知情，可能 crash。被否决。
- **GUI 在保存前 validate（已存在）但 reload 不做**：覆盖不完整。被否决。
- **reload 前 validate 失败保留旧（当前选择）**：完整闭环，复用已有 AppConfigLoader + 新增 AppConfigValidator。

## Consequences

- 新增 AppConfigValidator.Validate(config) 方法（检查 ExifTool 路径存在、hot-folder 非空等）。
- WorkerIpcServerHostedService.HandleRequest ReloadConfig 调用此验证。
- 需测试验证非法配置 reload 路径（保留旧+返回错误）。
