# B11 — 跨平台 UDS 传输测量补测

- **优先级**: 低　**来源**: 票05 遗留

## 问题与验收

ADR 0057 测量仅覆盖 Windows 命名管道。UDS 建连成本未测。验收：Linux/macOS 任一实测数据 + 若与结论相悖则回票重估。


## 完成记录（2026-09-02，架构恢复第三轮票11）

- Kali 2026.1 / WSL2 实测完成，数据落盘 .scratch/architecture-recovery/benchmarks/11-uds-transport-bench-2026-09-02.md（含 raw/ 原始输出）。
- 结论与 ADR 0057 相符（UDS 建连净开销直接口径 ~0.03–0.06ms，噪声区）→ ADR 0057 已追加跨平台证据段，不触发重估。
- 附带发现并修复可用性 bug：客户端 DisposeAsync 误删服务端端点文件（修复前 Linux/macOS 首次 IPC 调用后 Worker 不可达）。
- 门禁：build 0 错 0 SCS；Core.Tests 145/145；IntegrationTests 261/261；semgrep p/csharp 改动文件 0 findings。
