# IPC 协议 v 字段（可选默认 1）

WorkerIpcRequest/Response record 加 int? V 字段（默认 1，nullable，可选）。老客户端发不含 v 的请求 → server 解析为 null → 按 v1 处理，原样继续。新客户端可发 v=2 协商新能力。新功能以方法名扩展为主；v 字段用于协议能力协商。

## Considered Options

- **独立握手 method**：多一次往返，过度工程化。被否决。
- **不改协议**：破坏扩展性。被否决。
- **可选 v 字段默认 1（当前选择）**：利用 record JSON 序列化自动 nullable，向后兼容。

## Consequences

- WorkerIpcRequest record 加 int? V = 1。
- WorkerIpcResponse record 加 int? V = 1。
- 需测试验证老消息（不带 v）仍可解析。
