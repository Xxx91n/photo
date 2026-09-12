# B05 — WorkerIpcClient 传输实例复用

- **优先级**: 中　**来源**: report-04 遗留风险

## 问题

`WorkerIpcClient` 每次 `SendAsync` 都 `IpcTransportFactory.CreateServer` 新建传输实例（票 04 前原实现即如此，票 04 未改）。轮询/心跳场景存在重复建连开销。另：心跳/ConnectionStateService 持有独立 WorkerIpcClient 实例，单例化属本票可选范围。

## 验收

1. 评估命名管道短连接实际开销（先测量，再定方案；非必改）。
2. 若改：传输复用 + 断线重建语义不回归，`ConnectOrLaunchAsync_Status_Probe_Must_Stay_IO_Resilient_Via_Client` 等守卫保持绿。
3. 若结论为"不值得改"，将测量数据沉淀进 ADR 并关闭本票（删除胜过保留的反向应用：无证据不优化）。
