# Mode-Scoped Shutdown（仅 CLI 可触发 IPC Shutdown）

WorkerIpcMethods.Shutdown 仅在 RuntimeMode.Cli 下可被 IPC 调用触发 _lifetime.StopApplication()。Service/Background 模式收到 Shutdown 请求直接返回 WorkerIpcResponse { Ok: false, Error: "请通过 systemctl/launchd 停止服务" }，保护多用户共享场景下用户 A 通过 IPC 关闭影响用户 B。

## Considered Options

- **全部 mode 共享 Shutdown**：多用户场景不安全。被否决。
- **Token-based 鉴权**：过度工程化。被否决。
- **Mode 范围限制 Shutdown（当前选择）**：零新增依赖，单行条件判断。

## Consequences

- WorkerIpcServerHostedService.HandleRequest 中 Shutdown method 加 if (_runtime.Mode != RuntimeMode.Cli) return error 分支。
- CLI 退出场景不受影响（CLI 模式仍可 Shutdown）。
- 需测试验证 Service 模式收到 Shutdown 返回错误、CLI 模式仍触发。
