# IPC 传输层抽象 + 双实现（NamedPipe + Unix Domain Socket）

选择抽象 IIpcTransport 接口 + Windows NamedPipeServerStream / Linux/macOS UnixDomainSocket 双实现，按 OperatingSystem.IsLinux()/IsMacOS()/IsWindows() 切换。WorkerIpcRequest/Response JSON 协议不变。Linux 路径 /run/photoprivacy/worker.sock，macOS 路径 ~/Library/Application Support/photoprivacy/worker.sock，Windows 保持现有管道名。

## Considered Options

- **统一 NamedPipeStream（依赖 .NET Unix FIFO 模拟）**：零新代码但 Linux/macOS 并发连接模型、权限边界与 Windows 不一致，已知兼容性隐患。被否决。
- **引入 gRPC + Kestrel**：功能强但新增 2 个 NuGet 包（Grpc.Net.Client、Grpc.AspNetCore），与 Ponytail 最小依赖原则相悖。被否决。
- **抽象层 + 双实现（当前选择）**：零新增 NuGet 依赖（System.Net.Sockets 是 stdlib），保留现有 JSON 协议，按 OS 切换传输后端，UI 层零改动。

## Consequences

- 新增 IIpcTransport 接口 + 2 个实现 + 工厂。NamedPipe 实现包现有代码，UnixDomainSocket 实现新增。
- Unix socket 路径需要 stale cleanup（见 ADR 0026）和 RuntimeDirectory 配置（见 ADR 0029）。
- 现有 WorkerIpcServerHostedService 内部改为通过 IIpcTransport 抽象调用。
