# UI 心跳服务 + 自动重连退避

选择 UI 进程后台 IHostedService 心跳，定时调 WorkerIpcClient.IsAliveAsync()（复用现有 IPC Ping）。成功后 UI 状态切 Connected；失败切 Reconnecting，指数退避（1s→2s→4s→8s→16s→32s→60s cap）。状态通过 IConnectionStateService : INotifyPropertyChanged 暴露给 ViewModel，UI 按钮绑定状态门控（CanInvokeAction = State == Connected）。零新增依赖，零新增协议（复用 Ping）。

## Considered Options

- **服务主动 push 状态到 GUI**：双向 IPC 流式，新增协议，复杂。被否决。
- **不做心跳**：用户无法得知服务正在重启，体验差。被否决。
- **IHostedService 心跳 + 指数退避 + INPC（当前选择）**：复用现有 Ping，零新协议。

## Consequences

- 新增 HeartbeatHostedService : IHostedService + IConnectionStateService + ConnectionStateService 实现。
- UI ViewModel 注入 IConnectionStateService，按钮 IsEnabled 绑定 State == Connected。
- 需测试验证退避序列和状态切换。
