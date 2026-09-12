# Stale Unix Domain Socket 清理

UnixDomainSocketIpcTransport server 启动时无条件清理残留 socket 文件：if (File.Exists(_socketPath)) File.Delete(_socketPath)（try/catch 忽略不存在），然后 Bind + Listen。Windows 不涉及（NamedPipe 是内核对象，进程退出自动清理）。

## Considered Options

- **先 connect 探活再决定删**：多一次往返，且探活时已有实例可能正是我们自己（竞态）。被否决。
- **用 Linux abstract socket namespace**：macOS 不支持，破坏抽象。被否决。
- **无条件清理（当前选择）**：一行防御代码，零依赖，标准做法。

## Consequences

- UnixDomainSocketIpcTransport 构造/启动逻辑加 File.Delete 防御。
- 需测试模拟残留 socket 文件场景（先创建空文件再启动 server，验证 bind 成功）。
