# ADR 0057: WorkerIpcClient 传输不复用 — 测量裁决关闭 B05

**状态**: 已接受（测量驱动关票）
**日期**: 2026-09-01
**来源**: docs/backlog/B05-ipc-transport-reuse.md（架构恢复第二轮 票05，"先测量后决策，非必改"）

## 背景

ADR 0056 遗留事项第5条指出：WorkerIpcClient.SendAsync 每次调用都经 IpcTransportFactory.CreateServer 新建传输实例并新建 NamedPipeClientStream，用完即弃（WorkerIpcClient.cs SendAsync finally DisposeAsync）。担忧为"轮询/心跳场景重复建连开销"。唯一持续高频消费方是 ConnectionStateService 心跳（1s 周期，ADR 0025）。

## 测量（2026-09-01，Windows 11 26100，.NET 10.0.5，8 核）

完整数据与环境见 .scratch/architecture-recovery/benchmarks/05-ipc-transport-bench-2026-09-01.md。
方法要点：harness 以 Compile Include 链接真实生产源 WorkerIpcClient.cs（非复制品），服务端逐行复刻 WorkerIpcServerHostedService 循环；PERCALL=生产现状路径，KEEPALIVE=单条预连接上的同等往返（复用收益上限）；warmup 300 + 3000 次采样 × 2 轮。

| 指标 | 现状（每次新建传输） | 复用上限（预连接往返） |
|---|---|---|
| mean | 1.1662ms / 0.9142ms（两轮） | 0.8669ms / 0.8860ms |
| p95 | 1.71ms / 1.15ms | 1.14ms / 1.16ms |
| 托管分配/次 | ~7.4KB | ~4.4KB |
| 传输新建+连接净开销 | **两轮 0.30ms → 0.03ms（噪声区）** | — |
| 1Hz 心跳负载 | 全路径 ~0.1% 单核；3000 次仅 9–10 次 gen0 GC | — |

## 跨平台证据（2026-09-02，Linux/WSL2 追测，架构恢复第三轮票11）

完整数据与环境见 .scratch/architecture-recovery/benchmarks/11-uds-transport-bench-2026-09-02.md。
Kali 2026.1 on WSL2（2 vCPU），.NET 10.0.400，socket 在 tmpfs；方法与 Windows bench 对齐（Compile Include 链接真实生产源 + 服务端逐行复刻 + warmup 300 / 3000 采样 × 2 轮 × 2 独立进程）。

| 指标 | Windows 命名管道（2026-09-01） | Linux UDS（2026-09-02） |
|---|---|---|
| PERCALL mean | 1.1662 / 0.9142ms（两轮） | 0.69–1.81ms（四轮，2 核 VM 噪声大） |
| 复用上限（KEEPALIVE）mean | 0.8669 / 0.8860ms | 0.47–0.91ms |
| 传输新建+连接净开销 | 两轮 0.30 → 0.03ms（噪声区） | 直接口径 0.028–0.062ms；差值口径 0.13–1.11ms |
| 托管分配/次 | ~7.4KB / ~4.4KB | ~7.8KB / ~3.9KB |
| 1Hz 心跳负载 | ~0.1% 单核 | 0.07–0.18% 单核 |

**判读：结论跨平台成立**——UDS 建连净开销同为亚毫秒/噪声区（直接口径 ~0.03–0.06ms），成本主体仍是逐字节读循环与往返调度；"不复用、不帧化"裁决不触发重估。

**追测附带发现（可用性 bug，已修）**：修复前 `UnixDomainSocketIpcTransport.DisposeAsync` 无条件删除 socket 文件；客户端每次 SendAsync 用完即弃，首调后服务端端点文件被删，后续连接全部失败（AddressNotAvailable）。修复为仅端点属主（服务端实例）清理。该缺陷不影响本 ADR 的性能结论，但意味着修复前 Linux/macOS 实际不可用——属票11 测量得以成立的前提修复。

## 决策：不值得改，关闭 B05（无证据不优化）

1. **净收益在噪声区**：传输新建+连接的净开销两轮 mean 为 0.30ms/0.03ms，与轮间波动同量级；单次调用 ~0.9ms 的成本主体是逐字节有界读循环与往返调度，与传输创建无关。
2. **绝对负载可忽略**：唯一高频方 1Hz 心跳全路径仅 ~0.1% 单核、~7.4KB/s 分配；其余方法（GetStatus/Pause/Resume/Shutdown/ReloadConfig/GetRecentLogs/ProbeStatusSafeAsync）均由用户动作或重连触发。
3. **复用不是纯客户端改动，成本不对称**：WorkerIpcServerHostedService 每连接只处理 1 个请求后 `await using` 断开，且响应无 '\n' 帧、客户端靠 EOF 定界读。复用需要：服务端 keep-alive 循环 + 帧化协议（破坏性协议变更，V 字段需升版）+ 客户端断线重建语义 + 双端守卫测试更新。而收益上限仅每次 ~0.3ms 与 ~3KB。
4. **ADR 0035 语义零接触**：本次不改任何代码，ProbeStatusSafeAsync 容错降级、3s 读超时、ConfigureAwait(false) 源断言守卫（WorkerIpcClientSourceTests）原样保持。

## 触发重估条件（若未来成立则另立新票）

- 出现高频轮询需求（如 ≥10Hz 的状态轮询或批量日志回灌循环）；
- 逐字节读循环本身被优化（缓冲读），使传输创建占比显著上升；
- IPC 协议已因其他需求升级帧化（V2），届时复用的服务端改造成本已被支付。

## 后果

- docs/backlog/B05 第3条验收达成：测量数据沉淀（benchmarks/05-…md）+ 本 ADR 关票。
- WorkerIpcClient 代码零改动；票05 无 src diff，验收清单第2条以"不改"分支（mini-ADR 记录数据关票）完成。
- guard：WorkerIpcClientSourceTests / WorkerProcessManagerTests.ConnectOrLaunchAsync_Status_Probe_Must_Stay_IO_Resilient_Via_Client / ConnectionStateServiceTests 全部保持绿（票05 报告附运行证据）。
