# 报告 — 票 05 驻守稳定性：健康探针纠偏 + 撤 sync-over-async + 缓冲读（correctness-round）

票号：W5 ｜ 分支：05-resident-stability ｜ 覆盖锐评条目：A-005（三件）
主标尺：D-002 不变量④（驻守可观测）；次涉②（用户数据零意外丢失）

## 0. 开工闸（WORKFLOW / prompt 要求）

### 0.1 Blocked by 票 04 解除证据（三条实物）

| # | 证据 | 内容 |
|---|---|---|
| 1 | `but status` | 栈内存在 `ip [04-ipc-auth-layered]`，提交 `vko 票 04 IPC 面…` |
| 2 | README 波次表 W4 | `done（ACCEPT 2026-09-16：四票并集树 run 35055374835 绿；A-004 结算 implemented）` |
| 3 | README 波次表 W5 | 本票 = 当前 frontier，状态 `pending` |

三条一致，判定 Blocked by 已解除，允许动工。

### 0.2 必读清单逐份确认（8 份全读）

| # | 文件 | 确认 |
|---|---|---|
| 1 | `.scratch/architecture-recovery/handoffs/05-resident-stability-handoff.md` | 已读（40 行）|
| 2 | `.scratch/architecture-recovery/issues/05-resident-stability.md` | 已读（23 行）|
| 3 | `.scratch/architecture-recovery/spec.md` | 已读（76 行，ID5 节）|
| 4 | `.scratch/architecture-recovery/WORKFLOW.md` | 已读（39 行，§4.2/§4.3/§4.4）|
| 5 | `.scratch/architecture-recovery/decision-ledger.md` | 已读（114 行，A-005）|
| 6 | `.scratch/correctness-round/decision-ledger.md` | 已读（167 行，D-002/D-003/D-004/D-008.5）|
| 7 | `docs/adr/0057-ipc-transport-percall-no-reuse.md` | 已读（58 行）|
| 8 | `docs/adr/0025-ui-heartbeat-with-backoff.md` | 已读（15 行）|
| 补 | `.codex-tmp/锐评.txt` L194-231 | 已读（A-005 三件原文）|

### 0.3 本票覆盖的 A-xxx 与不变量映射

- **A-005(a)** 健康检查误杀（每文件重启 exiftool，摧毁 stay_open 收益）+ `RestartAsync` 的 `_startLock` 持锁缺失 → **不变量④**（驻守可观测：进程反复重启则不可观测、不可诊断）。
- **A-005(c)** accept 循环内 sync-over-async（`ReloadConfigAsync().GetAwaiter().GetResult()` 堵死数秒 > UI 心跳 3s 读超时 → 误判掉线）→ **不变量④**（驻守可观测：被堵期间服务不可达）。
- **A-005(b)** IPC 逐字节读（两份重复实现，约六万次 await）→ **不变量④** 的延迟/可观测性侧；与 ADR 0057 裁决面相邻。
- **次涉不变量②**：restart 路径不得丢在途任务（`_startLock` + 锁内二次健康探测 + `DrainInFlightAsync` 收口）。

### 0.4 两条显式约束（不得静默改向）

1. **ADR 0057 per-call no-reuse 裁决面**：不得静默改向。本票保留「每次调用新建传输、不复用、不帧化」现状（§5 不动项）。
2. **不变量④**：修复后驻守必须更可观测（更少重启、不被长阻塞、可诊断）。

## 1. 验收对照表（issue Acceptance criteria 逐条 → 证据 → 结论）

| AC | 原文要点 | 实现证据 | 失效即红预演 | 结论 |
|---|---|---|---|---|
| AC1 | 健康检查不再以 exiftool 自身为输入；慢机不逐文件重启进程（行为测试在 CI 绿）| `ExifToolBridge.cs:399` 探针命令 = `$"-fast\n-echo1\n{marker}\n-execute\n"`（无文件参数）；新增 `EnsureStartedAsync_Should_Not_Restart_Per_File_When_Health_Probe_Is_File_Free` | B4 / C3：基线 GREEN，突变（恢复喂入路径）→ RED-OK | 实现完成，待 CI |
| AC2 | accept 循环无 sync-over-async；重启重载不阻塞后续 accept | `WorkerIpcRequestHandler.HandleAsync` 用真 await（`ConfigureAwait(false)`）；`WorkerIpcServerLoop.RunAsync` accept 后不 await，交 `_inFlight` 独立任务；新增 5 Fact（`WorkerIpcResidentStabilityTests`）| B1 / B2 / C1：基线 GREEN，突变（内联 await）→ RED-OK | 实现完成，待 CI |
| AC3 | 若 (b) 降级：报告中呈现调研证据与用户裁定记录，不得静默 | §4 调研裁决表；裁定 = **不降级**，缓冲读落地（与 ADR 0057 裁决面正交）| 不适用 | 不适用降级，已如实呈现调研与裁定 |
| AC4 | 既有守卫不红；双轨报告 + 首脑复核可受理 | §3 A 节 5 项全 GREEN；本报告 + `docs/process/reports/05-report.md` 副本 | §3 | 既有守卫零破坏，报告待复核 |

AC1/AC2 的「CI 绿」声明：本机零构建零测试（CI-only 纪律），已实现 + 已预演，最终绿灯以 CI 为准（§8）。

## 2. 实现落位（文件 → 角色）

### 2.1 修改（6 个）

| 文件 | 角色 |
|---|---|
| `src/PhotoPrivacy.Core/ExifTool/ExifToolBridge.cs` | (a) 健康探针去文件参数（`-fast\n-echo1\n{marker}\n-execute\n`）；(a) `RestartAsync` 改持 `_startLock` + 锁内二次健康探测（并发重启去重）|
| `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs` | 缩为薄接线层（212→66 行），抽出处理器与循环，职责分离 |
| `src/PhotoPrivacy.Worker/WorkerRuntimeContext.cs` | 加 `: IWorkerIpcRuntime`（1 行），使请求处理可注入假件 |
| `src/PhotoPrivacy.Ui/Services/WorkerIpcClient.cs` | (b) 读改调 `IpcLineReader.ReadBoundedLineAsync`；删除私有逐字节读；保留 `ReadTimeoutMs=3000` 与三处 `ConfigureAwait(false)` |
| `tests/PhotoPrivacy.Core.Tests/ExifTool/ExifToolBridgeTests.cs` | 合同翻转（`Contains`→`DoesNotContain`）+ 新增 2 Fact |
| `tests/PhotoPrivacy.Core.Tests/ExifTool/ProtocolLevelFakeExifToolProcess.cs` | 故障/成本注入：`FileInputParseDelayMs` / `StartDelayMs` / `MaxConcurrentStartCalls` + `HasFileInput` |

### 2.2 新增（7 个）

| 文件 | 角色 |
|---|---|
| `src/PhotoPrivacy.Ipc/IpcLineReader.cs` | **(b) 缓冲读唯一实现**（4KB 分块，有界语义与旧逐字节读逐字对齐）|
| `src/PhotoPrivacy.Worker/IWorkerIpcRuntime.cs` | 运行上下文接口（测试接缝）|
| `src/PhotoPrivacy.Worker/WorkerIpcRequestHandler.cs` | 请求处理（**(c) 真 await 取代 `GetAwaiter().GetResult()`**）|
| `src/PhotoPrivacy.Worker/WorkerIpcServerLoop.cs` | accept 循环（**accept 后不 await，独立任务驱动** + `DrainInFlightAsync` 收口）|
| `tests/PhotoPrivacy.IntegrationTests/Ipc/IpcLineReaderTests.cs` | 8 Fact：缓冲读有界语义 |
| `tests/PhotoPrivacy.IntegrationTests/WorkerIpc/WorkerIpcResidentStabilityTests.cs` | 5 Fact：**AC2 行为测试**（内存 `ScriptedIpcTransport`）|
| `tests/PhotoPrivacy.IntegrationTests/WorkerIpc/WorkerIpcSyncOverAsyncGuardTests.cs` | 3 Fact：回潮守卫 |

## 3. 失效即红预演（新增 / 既有守卫）

完整证据：`.scratch/architecture-recovery/reports/05-evidence-red-forecast.log`（9,276 B / 110 行）。

**方法（CI-only 约束下的口径）**：不调 `dotnet build` / `dotnet test`。既有守卫按其源码逐字复现匹配逻辑全仓扫描；新增守卫采用基线→定向突变（内存施加，不落盘）→复算三步。

### 3.1 既有守卫零破坏（基线应 0）

| 守卫 | 基线 | 结论 |
|---|---|---|
| `ProcessHygieneGuardTests`（3 正则 × src+tests 全 *.cs）| violations=0 | GREEN |
| `TestCodeVerbatimPathGuardTests`（drive-relative verbatim × tests）| violations=0 | GREEN |
| `HardcodedChineseScanTests`（Ui *.cs 剥注释后 CJK）| violations=0 | GREEN |
| `WorkerIpcClientSourceTests`（3 处 ConfigureAwait 逐字）| present=3/3 | GREEN |
| `WorkerProcessManagerTests`（ProbeStatusSafeAsync + 3 catch）| present=4/4 | GREEN |

关键判定（为何必然绿）：

- `ProcessHygieneGuardTests` 三条正则均属 `WaitForExit` 族；本票新增的 `_startLock.WaitAsync(...)` 与 `Task.WhenAll(...).WaitAsync(2s, None)` 不匹配。
- `HardcodedChineseScanTests` 先经 `SourceLint.StripLineComment` 剥除行注释；本票在 `WorkerIpcClient.cs:40-41` 新增的 2 行中文注释位于 `//` 内，不在扫描余量。
- `WorkerIpcSyncOverAsyncGuardTests` 的 3 个令牌在受测文件中的命中字符串仅存于注释（`WorkerIpcRequestHandler.cs:10 ///`、`:88 //`；`WorkerIpcServerLoop.cs:17 ///`），被剥除后基线必为 0。
- basename 级全 tests 扫描：仅 3 个守卫触及本票 13 个文件名（其余守卫读取集与本票无交集，含 `UdsEndpointOwnershipGuardTests`、`IpcAuthClaimReconciliationTests`）。

### 3.2 新增守卫失效即红（基线 GREEN → 突变 RED-OK）

| # | 守卫 | 基线 | 定向突变 | 结果 |
|---|---|---|---|---|
| B1 | 同步阻塞令牌回潮 | hits=0 GREEN | 注入真实 `GetAwaiter().GetResult()` | hits=1 **RED-OK** |
| B2 | accept 循环不得内联 await | [T,T,T] GREEN | `var task =` → `await ` | [T,F,T] **RED-OK** |
| B3 | 缓冲读唯一实现 | [T,T,T] GREEN | 换回 `new byte[1]` 逐字节读 | [T,F,F] **RED-OK** |
| B4 | 健康探针合同翻转 | 命令不含 Path GREEN | 恢复喂入 exiftool 路径 | 含 Path **RED-OK** |
| B5 | `IpcLineReaderTests` 8 Fact | 8/8 GREEN | 终止词 LF→CR | 3/8 **RED-OK**（5 Fact 失败）|
| C1 | reload 不阻塞后续 accept | Ping 已被服务 GREEN | accept 循环内联 await | Ping 被堵 **RED-OK** |
| C2 | 并发重启串行化 | maxConcurrent=1 GREEN | 移除 `_startLock` | maxConcurrent=3 **RED-OK** |
| C3 | 不逐文件重启 | restarts=0 GREEN | 探针喂入路径（900ms>500ms）| restarts=3 **RED-OK** |

→ **新增守卫 8 项均具备「失效即红」能力，且基线全绿；既有守卫零破坏。**

### 3.3 显式不覆盖项（避免守卫与实际不符）

- `src/PhotoPrivacy.Worker/Program.cs:139` POSIX `onReload` 回调内同形态 sync-over-async：A-005(c) 原文限定「accept 循环内」，该处属本轮范围外，**不纳入本守卫**，已登记 §6 T1。
- `ExifToolBridge.cs:202` `probeTcs.Task.IsCompleted ? probeTcs.Task.Result : null`：先判 `IsCompleted`，不会阻塞；存量代码，非本票引入。

## 4. 调研与裁决表（atomcode，单发串行，10 分钟内完成）

执行约束遵守：单发串行（`concurrency: 1`）、`timeout: 600000`、问题只写研究问题本身、未杀 atomcode 进程。
规模：7 段索引 / 8 个独立域名 / 6 次原文全读。

| # | 研究问题 | 结论 | 置信 | 对本票的裁定 |
|---|---|---|---|---|
| ① | 缓冲读是否值得做？是否与传输复用正交？ | 值得做，且与传输复用正交。本地证据：`WorkerIpcClient.cs:158-168` 与 Worker 服务端为同一份逐字节读；MS Learn 官方管道示例即 `StreamReader.ReadLine()`；修复方向 = 带缓冲读 + 保留 `maxBytes` 上限；明确「**不要**引入 PipeReader/ArrayPool 全套池化」 | 高 | **落地缓冲读**（`IpcLineReader`），不引入池化 |
| ② | ADR 0057「不复用传输」结论是否被缓冲优化触及？ | 机制上未被触及（两成本轴正交：`ConnectAsync` 握手与缓冲无关）；**但测量基线失效**——若旧实测是在逐字节读之下做的端到端测量，逐字节读本身即噪声主导项 | 中高 | **不启动传输复用改造**；登记实机复测缺口（§6 T2/T3）|
| ③ | 存活探针的工业级准则？ | liveness（<100ms、零外部依赖）/ readiness / startup 三探针分离；liveness 失败→重启，readiness 失败→摘流量不重启；周期 5–10s；initialDelay 取 p99 启动时间（K8s 官方 + Google Cloud + Geodocs 2026-05 三源交叉）| 高 | **本项目方向已正确**：`IsAliveAsync(Ping)` 已区分「进程活着」与「可服务」，ADR 0035 zombie window 降级合规；本票只需去掉文件参数 |

### 4.1 对检查点 (b) 的裁定

**裁定：不降级，(b) 缓冲读随票落地。** 依据：

1. 裁决面正交：ADR 0057 裁决对象是「**传输创建/复用**」（`ConnectAsync` 握手），本票改动是「**单次调用内部的读实现**」（缓冲 vs 逐字节）；二者不同层，不存在改向。
2. 触发重估条件第 2 条原文：「逐字节读循环本身被优化（缓冲读），使传输创建占比显著上升」——该条描述的是「条件成立后**应重估 ADR 0057**」，**不是**「不得做缓冲读」。因此本票落地缓冲读并不违反该约束。
3. 保留 `maxBytes` 有界语义与 `ReadTimeoutMs=3000`，不引入新依赖、不池化。
4. ADR 0057 裁决面不得静默改向 → 本票**不触碰传输创建/复用代码**（§5.1），且已显式登记触发条件已达（§6 T2）。

→ 与 A-005 / D-008.5 **无冲突**，未触发 revised，无需停下呈报。

## 5. 不动项核验（本票未触碰，逐项留证）

### 5.1 ADR 0057 裁决面（per-call no-reuse）

| 不动项 | 核验 | 结论 |
|---|---|---|
| 每次调用新建传输、不复用 | `WorkerIpcClient.SendAsync` 仍 `using var transport = ...` 每调用新建 | 未改 |
| 不帧化协议 | 仍为「一行 JSON + LF」有界行 | 未改 |
| `ConnectAsync` 超时 700ms | `TimeSpan.FromMilliseconds(700)` 逐字保留 | 未改 |

### 5.2 其他不动项

| 不动项 | 核验 | 结论 |
|---|---|---|
| `ReadTimeoutMs = 3000` | `WorkerIpcClient.cs:15` 保留 | 未改 |
| 三处 `ConfigureAwait(false)` | `WorkerIpcClientSourceTests` present=3/3 | 未改 |
| `ProbeStatusSafeAsync` 降级语义 | 签名 + 3 catch 全在，present=4/4 | 未改 |
| IPC 认证分层（票 04 面） | 未触碰 `NamedPipeIpcTransport.cs` / `UnixDomainSocketIpcTransport.cs` / `UiSingleInstance.cs` | 未改 |
| ADR 0025 UI 心跳退避 | 未触碰 Ui 心跳 HostedService | 未改 |
| `ExifToolCommandBuilder` 启动参数 | 未触碰（本票只改**健康探针**命令，不动 `BuildStartArguments`）| 未改 |
| stay_open 协议（`-echo1` + `-execute`）| 保留，未引入新标记 | 未改 |
| 既有守卫 | 一件未删未缩（仅 `ExifToolBridgeTests` 一个断言合同翻转，见 §7 V1）| 未删未缩 |

### 5.3 离轨复算（A-005 三件指控是否属实 —— 对原代码逐条复算）

| # | 指控 | 复算结果 | 判定 |
|---|---|---|
| (a) | 健康检查把 `_config.ExifTool.Path` 当待解析文件喂入 → 慢机 >500ms 触发 `health_timeout` → 每文件整进程重启 | 原文 `ExifToolBridge.cs:343` 确实将 exe 路径拼入探针命令；`RunningHealthTimeout = 500ms` | **属实** |
| (a′) | `RestartAsync` 未持 `_startLock` | 原 `RestartAsync` 体内无 `_startLock.WaitAsync` | **属实** |
| (b) | IPC 逐字节读两份重复实现 | `WorkerIpcClient.cs:158-172` 私有 `ReadBoundedLineAsync`（`new byte[1]`）≡ `WorkerIpcServerHostedService` 内同名实现 | **属实**（两份已归一）|
| (c) | accept 循环内 sync-over-async | 原 `WorkerIpcServerHostedService.HandleRequest` 内 `ReloadConfigAsync().GetAwaiter().GetResult()` | **属实** |

→ 四项指控均经原代码复算属实，本票修复面与指控面一致。

## 6. 张力与呈报（待首脑裁定）

| # | 张力 | 事实 | 本票处置 | 待裁定 |
|---|---|---|---|---|
| T1 | `Program.cs:139` POSIX `onReload` 回调内 `ReloadConfigAsync().GetAwaiter().GetResult()` | 实测 present=true | A-005(c) 原文限定「accept 循环内」，该处属本轮范围外 → 未修、未入守卫 | 是否开续票处理 |
| T2 | ADR 0057 触发重估条件第 2 条已达 | 本票已落地缓冲读（该条前提） | 不启动传输复用改造（越出本票面）；仅登记 | 是否开新票重估 ADR 0057 |
| T3 | ADR 0057 旧测量基线失效 | 旧实测（Windows 0.30ms/0.03ms、Linux UDS 0.028–0.062ms）若在逐字节读之下测得，则噪声主导项未隔离 | 本票**不宣称任何性能数字** | 是否需实机复测 |
| T4 | 「约六万次 await」数量级声明 | 源自锐评原文，本票未在 CI 复测 | 仅引用为动机，不作为验收依据 | 无 |

## 7. 过程违规呈报（主动自曝）

### V1（重大）既有守卫断言合同翻转

- **事实**：既有守卫 `ExifToolBridgeTests.EnsureStartedAsync_HealthCheck_Should_Use_Fast_Path_Probe_With_Marker` 的断言由
  `Assert.Contains(Config.ExifTool.Path, healthCommand, OrdinalIgnoreCase)`
  翻转为 `Assert.DoesNotContain(Config.ExifTool.Path, healthCommand, OrdinalIgnoreCase)`。
- **为何必需**：AC1 的合同变更本体就是「健康检查不再以 exiftool 自身为输入」，而旧断言恰好锁定**旧合同**；不翻转则新合同无法表达。属合同翻转，非守卫删除。
- **留痕**：已在测试文件内加 4 行「合同变更」注释。
- **请首脑知悉并裁定**。

### V2（中等）为可测性引入生产代码结构变更

- **事实**：抽取 `IWorkerIpcRuntime` 接口；将 `WorkerIpcServerHostedService`（212→66 行）拆为 `WorkerIpcRequestHandler` + `WorkerIpcServerLoop`。
- **为何必需**：AC2 行为测试须在 CI（`ubuntu-latest`）真跑「重载不阻塞后续 accept」；若不做接缝，则只能绑定真实命名管道/Unix socket，在 CI 不可移植。
- **风险控制**：已保持语义等价——`invalid request` / Shutdown 非 CLI 拒绝 / ReloadConfig 校验失败 / `reload_failed` / `unknown method` / `ReadRecentAuditLogs(50)` 逐条保留。
- **超出「最小改动」范畴，主动呈报**。

### V3（工具）ctx_execute 参数序列化缺陷

- **事实**：当 `code` 键先于 `language` 键且 payload 较大时，后续键被吞 → 校验报 `must have required property language`。
- **处置**：改用 `language` 优先 + 小 payload 分批。**非代码问题**，自曝留痕。

### V4（重大，已获用户授权）栈线性化 —— 移动了其它票的分支位置

- **事实**：为使本票提交可入栈，执行了 `but move 04-ipc-auth-layered --above 01-wipe-unknown-format-and-claims`（**移动了票 04 的分支位置**），随后 `but move 05-resident-stability --above 04-ipc-auth-layered`。

- **授权**：已就此事向用户提问，用户明确选择「线性化栈后提交（推荐）」。

- **影响面**：仅调整**本地虚拟分支的栈位置**；票 01/02/03/04 的**提交内容零改动**，无 amend/squash/discard/uncommit，无 push，无 PR；操作可逆。

- **拓扑变化**：`01 → 04 → 05`（线性）；02/03 保持原栈位。与 WORKFLOW 串行波次 W1→W5 的设计拓扑一致。

- **风险**：若其它窗口依赖原有并行栈位，需知悉；请首脑复核确认无冲突。
## 8. 提交与 CI（已完成提交）

- **分支**：GitButler 虚拟分支 `05-resident-stability`（一票一分支，WORKFLOW §4.2）。

- **commit 信息**：中文，带票号 —— `票 05 驻守稳定性：健康检查轻量探针 + 撤 sync-over-async + 缓冲读（覆盖 A-005）`。

- **首提物理哈希**：`b0807ad`（but slug `lsp` 不作数，按 WORKFLOW §7 教训 3 以 `git log` 实物为准；本报告若再经 amend 则哈希随之变化，终态以 `git log --all --oneline | grep 票 05` 为准）。

- **入栈机制**：GitButler 依赖分析判定本票改动**建立在他票内容之上**：

  - `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs` 依赖 `04-ipc-auth-layered`（vko）；

  - `tests/PhotoPrivacy.Core.Tests/ExifTool/ProtocolLevelFakeExifToolProcess.cs` 依赖 `01-wipe-unknown-format-and-claims`（vnz）。

  故 `but commit -b 05-resident-stability` 被直接拒。处置：`but commit --empty -b` 建空提交 → `but move 05-resident-stability --above 04-ipc-auth-layered` 定位 → `but amend -t 05-resident-stability` 将 15 个文件单元并入。栈位置调整见 §7 V4。

- **终态文件单元**：15 个（6 改 + 7 新增 + 2 轨 1 沉淀）；逐文件清单以 `but show 05-resident-stability` / `git show --stat` 为准。

- **不 push 不 PR**（CI 由推送方执行）。

- **CI-only 纪律**：本机零构建零测试；CI 编译为写盘有效性唯一凭证。

## 9. 验证记录（本机静态口径）

| 检查项 | 方法 | 结果 |
|---|---|---|
| 括号平衡 | ctx 沙箱 JS 词法状态机（剥注释/字符串）| 13 文件 `brace=0 paren=0 brk=0` |
| BOM | 字节首部实测 | `ExifToolBridge.cs` / `ExifToolBridgeTests.cs` = Y（原样保留），其余 11 = N |
| 行尾 | 字节级 CR 计数（`xxd` 口径，权威）| 13 文件 CR=0（纯 LF），与 `.gitattributes` + committed blob 一致 |
| 空白错误 | `git diff --check` | exit 0 |
| 守卫令牌 | 按 `ReadStripped` 语义模拟 | 3 个受测文件 `tokens.length == 0` |
| 关键片段逐字 | 字符串精确匹配 | 6/6 OK（探针命令 / `_startLock.WaitAsync` / 真 await / 独立 Task / `IpcLineReader.ReadBoundedLineAsync` / `DoesNotContain`）|
| 残留检查 | grep | `WorkerIpcServerHostedService.cs` 无 `new byte[1]` / 无 `GetAwaiter().GetResult()` / 无 `HandleRequest(`；`WorkerIpcClient.cs` 无 `new byte[1]` |
| 既有守卫复现 | 按守卫源码复现 | 5 项全 GREEN（§3.1）|
| 本机构建/测试 | —— | **零构建零测试**（CI-only 纪律遵守）|

### 9.1 文件规模（实测）

| 文件 | 行数 |
|---|---|
| `src/PhotoPrivacy.Ipc/IpcLineReader.cs`（新）| 59 |
| `src/PhotoPrivacy.Worker/IWorkerIpcRuntime.cs`（新）| 35 |
| `src/PhotoPrivacy.Worker/WorkerIpcRequestHandler.cs`（新）| 149 |
| `src/PhotoPrivacy.Worker/WorkerIpcServerLoop.cs`（新）| 144 |
| `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs`（重写）| 212 → 66 |
| `tests/…/Ipc/IpcLineReaderTests.cs`（新）| 86 |
| `tests/…/WorkerIpc/WorkerIpcResidentStabilityTests.cs`（新）| 326 |
| `tests/…/WorkerIpc/WorkerIpcSyncOverAsyncGuardTests.cs`（新）| 74 |

`git diff --stat`（既有文件）：6 files changed, 196 insertions(+), 215 deletions(-)。

---

**收尾声明**：本报告已落盘至 `.scratch/architecture-recovery/reports/05-report.md`（主本）与 `docs/process/reports/05-report.md`（受控副本，随票提交）。
**停住等首脑复核，不自动续票。**

