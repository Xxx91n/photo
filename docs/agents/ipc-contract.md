# IPC 契约（UI ⇄ Worker）

> 定位：UI ⇄ Worker IPC 契约速查，供新 UI（`ui/`）与 Worker 改动前查阅。
> 事实源：服务端为 .NET Worker 实现（`src/PhotoPrivacy.Ipc/`、`src/PhotoPrivacy.Worker/`），客户端为 Rust 实现（`ui/crates/photoprivacy-ipc/`）；本文件是契约速查缓存，协议或两端改动时同步更新。

## 传输与端点

| 平台 | 后台端点 | 服务端点 |
| --- | --- | --- |
| Windows | 命名管道 `PhotoPrivacyCleaner.Background` | `PhotoPrivacyCleaner.Service` |
| Linux | `/run/photoprivacy/worker-background.sock` | `/run/photoprivacy/worker.sock` |
| macOS | `~/Library/Application Support/photoprivacy/worker-background.sock` | 同目录 `worker.sock` |

出处：`src/PhotoPrivacy.Ipc/WorkerIpcEndpointNames.cs`、`src/PhotoPrivacy.Ipc/IpcTransportFactory.cs`。
客户端必须按 OS 解析端点（旧 Avalonia UI 全程硬编码 Windows 管道名，Linux/macOS 因此连不上）。

## 帧与客户端约束

| 约束 | 值 | 出处 |
| --- | --- | --- |
| 请求 | UTF-8 JSON + `\n`，`{method,id,v}` | `WorkerIpcContracts.cs` |
| 响应 | `{ok,data,logs,message,id,v}`，**没有结尾换行**，以连接关闭（EOF）定界 | `WorkerIpcServerLoop.cs` |
| 读取 | 循环读到 EOF；行内 CR 丢弃；达到 64 KB 即丢弃整条响应 | `IpcLineReader.cs` |
| 连接超时 | 700 ms（每次尝试都受剩余预算约束） | `ui/crates/photoprivacy-ipc/src/client.rs` |
| 读超时 | 3 s（普通请求） | `ui/crates/photoprivacy-ipc/src/client.rs` |
| `ReloadConfig` 读超时 | 10 s（与普通请求分离，避免热重载被 3 s 误杀） | `ui/crates/photoprivacy-ipc/src/client.rs` |
| 并发 | 单飞：服务端同一时刻只预建一个管道实例 | `NamedPipeIpcTransport.cs` |
| 重试 | `ERROR_PIPE_BUSY`(231) 与 `ERROR_FILE_NOT_FOUND`(2，属主重建实例的空窗)；Unix 为瞬时错误 | `ui/crates/photoprivacy-ipc/src/client.rs` |
| 一连接一请求 | 不复用连接，不帧化 | `WorkerIpcServerLoop.cs` |

UI 侧并发探测（#59）：新 UI 另起短连接预算客户端（连接 250 ms，读仍为 3 s，
`IpcClient::with_connect_timeout`）每 3 s 探测服务端点；版本轮询也用独立客户端每 1 s 调
`GetExifToolVersion`，避免与心跳的单飞锁互相拖慢。服务端单飞契约不变；客户端侧多个
`IpcClient` 各自串行，争用同一管道实例时仍靠 `ERROR_PIPE_BUSY` 重试兜底。

## 方法

`Ping`、`GetStatus`、`Pause`、`Resume`、`GetExifToolVersion`、`Shutdown`、`ReloadConfig`、`GetRecentLogs`。
`Shutdown` 仅 CLI 模式可用（服务/后台模式返回 `ok:false`，退出不要依赖它）。协议版本 `v` 当前为 1。
`ReloadConfig` 在服务模式与后台（托盘）模式下走同一方法、同一客户端实现，差异只是调用方当前连上的端点。

## UI 侧轮询

- 心跳：`GetStatus` 每 1 s；失败按 1→2→4→8→16→32→60 s 退避（`ui/crates/photoprivacy-ipc/src/monitor.rs`）。
- 版本：独立 1 s `GetExifToolVersion` 轮询（`watch_version`），不随心跳退避停摆。
- 服务态：每 3 s 探测服务端点可达性；完整 SCM/launchd/systemd 安装态探测归 #58。

## Worker 启动

`PhotoPrivacyWorker --mode background [--config <config.json>]`。UI 侧连接顺序：服务端点 → 后台端点 → 启动后台 Worker 并轮询 5 s（`ui/crates/photoprivacy-ipc/src/monitor.rs`；旧栈实现 `src/PhotoPrivacy.Ui/Services/WorkerProcessManager.cs` 已随旧 Ui 工程退役，#61）。

## 认证边界与已知缺陷

- Windows：后台管道 `PipeOptions.CurrentUserOnly`；Unix：0600/0660 + 对端 uid 白名单（fail-closed）。
- 服务模式既有缺陷：服务以 `LocalSystem` 运行，服务管道仍是 `CurrentUserOnly`，桌面用户会被拒绝——另见 #70；新 UI 因此会回落到后台 Worker。
- 客户端**不做**旧 UI 的 `PipeOptions.CurrentUserOnly` 属主校验（服务模式下自相矛盾）；理由见 `ui/crates/photoprivacy-ipc/src/client.rs` 注释。

## UI 单实例唤起通道（非 Worker 协议）

- Mutex：`Global\PhotoPrivacyUi_Instance`（POSIX 为 `$TMPDIR/photoprivacy-Global-PhotoPrivacyUi_Instance.lock`）。
- 唤起管道：`PhotoPrivacyUi_ShowWindow`，消息 `SHOW_WINDOW\n`，500 ms 超时；属主只写打开兼容旧 UI 的 `PipeDirection.In`。

## 改动规则

协议扩展**只加 method 名**，不改既有字段语义（`Design(#49)`）。改完同步：本文件、`ui/crates/photoprivacy-ipc/src/protocol.rs`、对应 Rust 测试与 .NET 侧契约测试。
