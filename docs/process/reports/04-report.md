# 报告 — 票 04 IPC 面：认证分层 + 承诺改写 + 部署配套（correctness-round）

> 覆盖 A-004（D-002 不变量③最小攻击面 + ⑤宣称-实现一致）｜ 轮次 correctness-round
> 分支：`04-ipc-auth-layered`（GitButler 虚拟分支，与票 01/02/03 栈并行）
> 状态：窗口执行完毕，停住等首脑复核。CI 行为测试绿为最终凭证（CI-only 纪律，推送由大脑执行）。

## 0. 开工闸（WORKFLOW/prompt 要求）

- **Blocked by 票 03：已解除。** `but status` 显示栈内 `ck [03-backup-mirror-layout]`（提交 `vpn 票 03 备份面：镜像相对路径布局 + 内容分歧旁路版本化（覆盖 A-006）`）在栈；`.scratch/architecture-recovery/README.md` 波次表 W3 登记 `done（ACCEPT 2026-09-16：并集树 run 35010033448 第三轮绿；A-006 结算 implemented）`，W4（本票）= 当前 frontier `pending` → 闸解除。附带事实：票 01/02/03 提交经 `git merge-base --is-ancestor` 三连判定均 `NOT IN MAIN`，系 GitButler 虚拟分支 + CI-only「不 push 不 PR」纪律的正常形态，非闸未开。
- **必读清单十一份全读**（按 prompt 顺序）：`prompts/04-ipc-auth-layered.md` / `handoffs/04-ipc-auth-layered-handoff.md` / `issues/04-ipc-auth-layered.md` / `spec.md`（ID4 节）/ `WORKFLOW.md`（§4.2 版本控制 / §4.3 共享文件停手 / §4.4 双轨持久化）/ A 系 `decision-ledger.md`（A-004）/ D 系 `decision-ledger.md`（D-002 五不变量、D-007 认证方案）/ `docs/adr/0035` / `0019` / `0026` / `0029` / `0046`。
- **补读（非清单但必需）**：`.codex-tmp/锐评.txt`（§4 IPC 段，A-004 指控原文）、`reports/03-report.md`（报告结构范本）、`docs/adr/0057`（ADR 格式范本）、`.scratch/architecture-recovery/README.md`（波次表）。
- **开工复述已完成**（会话正文）：Blocked by 状态 + 必读清单逐份确认 + 本票覆盖 **A-004 → D-002 不变量③（最小攻击面，全档执行不降档）**，次映射 **⑤（宣称-实现一致）**。
- **通用调研要求**：atomcode 深度调研单发串行已执行（10 分钟上限），裁决表见 §4。

## 1. 验收对照表（issue Acceptance criteria 逐条 → 证据 → 结论）

| # | 验收标准（issue 原文） | 证据 | 结论 |
|---|---|---|---|
| 1 | 冒烟实验留证在报告（两种提取取值各一次） | `.scratch/architecture-recovery/reports/04-smoke-evidence.log`（4110 B / 69 L）：OS 临时目录独立最小控制台工程（**不入仓库**），`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false` 于 `IncludeNativeLibrariesForSelfExtract=true` 与 `=false` **两变体各跑一次**，6 组探针 A1/A2/A3/B1/C1/D1/E1 全部 OK 且两变体逐行一致。**关键事实：重复 `FirstPipeInstance` 抛 `UnauthorizedAccessException`（非 IOException）**；`CurrentUserOnly` 未复现 ADR 0035 坑 → **Windows 主机制不降级**。**裁决范围限定**：探针 A3 仅验证**同用户** roundtrip，**未覆盖跨用户** —— 故该裁决只覆盖「同用户路径 + ADR 0035 坑未复现」，不覆盖 Windows service 模式跨用户路径（见 §6 张力 2） | ✅（同用户路径） |
| 2 | 认证主机制在 CI 可测范围内行为正确（首实例抢占 fail-fast 等） | 新增 `tests/PhotoPrivacy.IntegrationTests/Ipc/IpcAuthenticationLayeringTests.cs`（10 Fact）：Unix 侧 0600 / 0660 文件模式断言、真实对端凭据接纳同用户、伪造 uid 拒绝外来对端、凭据不可读 fail-closed、service 模式组内接纳 / 组外拒绝；Windows 侧首实例抢占 fail-fast（断言 `UnauthorizedAccessException or IOException`，两种口径都收）、同用户连接；工厂 OS 选择 + service 模式选路。Unix 断言在 CI（ubuntu-latest）实跑；Windows 断言 CI 不覆盖（见 §6 张力 3）。**本机静态口径全绿；CI 云端为唯一凭证** | ✅（静态）/ ⏳CI |
| 3 | AGENTS.md 承诺行与实现口径一致（对账） | L159 承诺行改写为「**IPC 认证依赖 OS 身份边界** — 内核访问控制（Windows `PipeOptions.CurrentUserOnly`；Unix socket 文件模式 background 0600 / service 0660+组边界、目录 0750）+ 对端凭据校验（`SO_PEERCRED` / `getpeereid`）+ 首实例防抢占（`PipeOptions.FirstPipeInstance`，**仅 ListenAsync 预建的首实例**）；**同 OS 用户进程不在防御范围内**（见 ADR 0067）」；L163 第 5 条由「NamedPipe 不使用 ACL」收紧为「禁止 `NamedPipeServerStreamAcl.Create`…同用户边界改用 `PipeOptions.CurrentUserOnly`」；§6 新增伪边界禁令行；§9 新增认证分层检查项。新增对账守卫 `IpcAuthClaimReconciliationTests`（4 组分类器：AgentsMd 承诺 / UiSingleInstance 无 ACL 调用 / systemd unit 模板 / FirstPipeInstance 仅首实例）**双向锁定**：既锁新承诺在场，亦锁旧未兑现承诺（`1. **命名管道需认证** — 验证连接方身份`）不回潮 | ✅ |
| 4 | 既有 IPC 行为（GetRecentLogs/Pause/Resume/ReloadConfig）语义不变，仅加认证层 | `WorkerIpcServerHostedService` 全票唯一改动 = 一行 `CreateServer(endpoint, _runtime.Mode == RuntimeMode.Service)`（`git diff --numstat` = `1 1`）；`HandleRequest` / `ReadRecentAuditLogs` / 各命令分支逐字未动；`UnixDomainSocketIpcTransport.DisposeAsync` 逐字保留（`ownsEndpoint` / `only by the endpoint owner` / `B11 finding`），以过既有 source-lint 守卫。**限定**：消息语义确实未变，但 Windows service 模式跨用户**信道**被 `CurrentUserOnly` 按构造关闭（见 §6 张力 2） | ✅（消息语义）/ ⚠（Windows service 跨用户信道） |
| 5 | 既有守卫不红；双轨报告 + 首脑复核可受理 | 静态复演全部 IPC 相关既有守卫：`UdsEndpointOwnershipGuardTests` 复刻分类器绿源 violations=0（3/3 突变变红）；`SingleInstanceIpcTests` / `UiProgramSourceSingleInstanceTests` 关注的 `MutexName` / `DefaultPipeName` 逐字保留；触及 IPC 的 11 个测试文件中 9 个为间接引用、零断言冲突；`UnixFileMode` / `PipeOptions` / `NamedPipe` 断言仅存在于本票新增 2 文件。双轨报告 = 本文件 + `docs/process/reports/04-report.md` 受控副本随票提交 | ✅（静态）/ ⏳首脑复核 |

## 2. 实现落位（文件 → 角色）

| 文件 | 改动 |
|---|---|
| `src/PhotoPrivacy.Ipc/UnixNativeInterop.cs`（**新**，5871 B / 202 L） | libc P/Invoke 封装（`internal static`）：`Ucred{pid,uid,gid}` 结构体；`geteuid` / `getsockopt`（`SOL_SOCKET=1` / `SO_PEERCRED=17`）/ `getpeereid`（macOS，`out euid, out egid`）/ `getgrnam` / `getpwnam`；入口 `TryGetOwnUid` / `TryGetPeerUid(Socket)`（macOS 走 `getpeereid`，Linux 走 `SO_PEERCRED`）/ `ReadGroupMemberNames`（`gr_mem` 偏移 `3*IntPtr.Size`）/ `TryGetUidByName`（`pw_uid` 偏移 `2*IntPtr.Size`）。全部 **fail-closed**：捕获 `DllNotFound` / `EntryPointNotFound` / `SEHException` 后返回 false / 空集，绝不抛出 |
| `src/PhotoPrivacy.Ipc/UnixPeerCredentialPolicy.cs`（**新**，1771 B / 44 L） | 可覆写凭据策略（三个 `virtual` 方法 `ReadOwnUid` / `ReadPeerUid` / `ReadGroupMemberUids`），供行为测试注入伪造 uid，无需真实跨用户进程 |
| `src/PhotoPrivacy.Ipc/UnixDomainSocketIpcTransport.cs`（重写，3087 → 6762 B） | 新增 `enum UnixSocketAccessMode{BackgroundOnly,ServiceShared}`；ctor 四参（socketPath / accessMode / serviceGroupName=`photoprivacy` / peerCredentialPolicy）；公开 `AccessMode` / `AppliedSocketMode` / `AllowedPeerUids`；`ListenAsync` 显式 `File.SetUnixFileMode`（background `UserRead\|UserWrite`=0600 / service `+= GroupRead\|GroupWrite`=0660）+ 解析白名单；`AcceptClientAsync` 读对端 uid，不在白名单 → `client.Dispose()` + 抛 `UnauthorizedAccessException`；**`DisposeAsync` 逐字保留**（过既有 source-lint 守卫） |
| `src/PhotoPrivacy.Ipc/NamedPipeIpcTransport.cs`（重写，2649 → 4301 B） | `ServerOptions(bool isFirstInstance)`：非 Windows 仅 `Asynchronous`；Windows `\|= CurrentUserOnly`，且 `isFirstInstance` 时 `\|= FirstPipeInstance`。`ListenAsync` 调 `isFirstInstance: true`；`AcceptClientAsync` 预建下一实例传 `false`（**结构约束：accept 后预建实例不得加标志**）。`ClientOptions` 加 `CurrentUserOnly` |
| `src/PhotoPrivacy.Ipc/IpcTransportFactory.cs`（改，1580 → 2078 B） | 新增 `CreateServer(string endpointName, bool isServiceMode = false)`；Linux/macOS 按 isServiceMode 选 `ServiceShared` / `BackgroundOnly`；单参重载委托 `false`（保持既有调用点不变） |
| `src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs`（改，**1 行**） | `IpcTransportFactory.CreateServer(endpoint, _runtime.Mode == RuntimeMode.Service)` |
| `src/PhotoPrivacy.Worker/UiSingleInstanceIpc.cs`（重写，1456 B） | 客户端侧同享保护：新增 `ClientOptions`（Windows `\|= CurrentUserOnly`） |
| `src/PhotoPrivacy.Ui/Services/UiSingleInstance.cs`（重写，3087 → 4483 B，**保留 BOM**） | **移除 `NamedPipeServerStreamAcl.Create` 分支及 `System.Security.AccessControl` / `System.Security.Principal` using**（消除 AGENTS.md §5 第 5 条宣称-实现脱节）；新增 `ServerPipeOptions` / `ClientPipeOptions`（Windows `\|= CurrentUserOnly`）；`CreatePipeServer` 收敛为单条 `new NamedPipeServerStream(name, In, 1, Byte, options)`；`MutexName` / `DefaultPipeName` 逐字保留 |
| `AGENTS.md`（改，4 处锚定） | §5 第 1 条承诺行改写 + 第 5 条收紧 + §6 新增伪边界禁令行 + §9 新增检查项（详见 §1#3） |
| `scripts/install-systemd-service.sh`（改，21 行新增，`bash -n` 通过） | 新增 `GUI_USER="${SUDO_USER:-}"` + `--gui-user` 解析 + usage；组供给块（`id -u` 校验 → `usermod -aG photoprivacy "$GUI_USER"`，缺用户 / 缺参数两路 Warning 到 stderr）；unit 模板新增 `RuntimeDirectoryMode=0750`（紧随 `RuntimeDirectory=photoprivacy`） |
| `docs/adr/0067-ipc-authentication-layering.md`（**新**，8501 B / 92 L） | 本票架构决策：三层结构 + 冒烟实验表 + Considered Options（A/B 采纳、C 不做、D 否决、PID 校验否决、abstract namespace 否决、ACL API 维持禁用）+ 威胁模型硬边界 + 4 条已知限制 |
| `tests/.../Ipc/IpcAuthenticationLayeringTests.cs`（**新**，13340 B / 348 L） | 10 Fact 行为测试（详见 §1#2） |
| `tests/.../Ipc/IpcAuthClaimReconciliationTests.cs`（**新**，10944 B / 262 L） | 4 组对账守卫 + 4 个负向自证 Fact（详见 §1#3） |

## 3. 失效即红预演（新增 / 既有守卫）

`04-evidence-red-forecast.log`（本机静态口径复演，CI-only 纪律下零 dotnet 零 build）：

| 组 | 守卫 | 绿源 | 突变 | 结果 |
|---|---|---|---|---|
| 1 | 既有 `UdsEndpointOwnershipGuardTests.ClassifyDisposeAsync` | violations=0 PASS | 3 组（ownership 标志短路 / File.Delete 非所有权守卫 / 标志与 listener 解耦） | 3/3 RED |
| 2 | 新增 `AgentsMd_Ipc_Auth_Claim_Should_Match_Implementation_Scope` | PASS | 3 组（认证口径删 / 主机制登记删 / 旧未兑现承诺回潮） | 3/3 RED |
| 3 | 新增 `UiSingleInstance_Should_Not_Call_The_Acl_Pipe_Api` | PASS（去注释后文本；文档注释引用 API 名说明理由，经 `SourceLint.ReadStripped` 剥离，**不构成假阳性**） | 2 组（ACL 调用回潮 / 同用户边界删） | 2/2 RED |
| 4 | 新增 `Systemd_Unit_Template_Should_Pin_RuntimeDirectoryMode_And_Group_Supply` | PASS | 2 组（目录模式放宽 / 组供给移除） | 2/2 RED |
| 5 | 新增 `NamedPipeTransport_Should_Apply_FirstPipeInstance_Only_To_The_First_Instance` | PASS | 2 组（首实例调用点改为后续实例 / FirstPipeInstance 守卫短路） | 2/2 RED |

**汇总：5 组分类器绿源全部 PASS；负向自证 12/12 突变均变红。**

## 4. 调研与裁决表（atomcode，单发串行，10 分钟内完成）

- 命令：`atomcode -p "<研究问题>"`（单发；硬护栏「同会话最多 1 个 atomcode 在途」遵守；未杀进程、未并行）。
- 研究问题（仅研究问题本身）：跨平台本机 IPC（命名管道 / Unix 域套接字）的工业级对端认证与权限收紧方案对比，以及分层选择的取舍。
- 证据规模：searches 9 / full reads 7（learn.microsoft.com ×2、man7.org、github.com、bleepingcomputer.com、man.openbsd.org、andrewlock.net）/ 12 信源。

| 结论 | 置信度 | 本票落位 |
|---|---|---|
| 三层结构（内核身份边界 → 对端凭据 → 首实例 fail-fast）为工业级主流 | 高 | 全量落位（ADR 0067 三层决策） |
| `PipeOptions.CurrentUserOnly` 为 .NET 官方同用户门；存在 dotnet/runtime #123903（误用 `Owner` SID 而非 `User` SID，2018 至今未修），**偏差方向为「过紧」而非「过松」** | 高 | 采纳为主机制；#123903 登记为 ADR 0067 已知限制 |
| Linux `SO_PEERCRED` 由内核在 connect 时刻固化，不可伪造；macOS `getpeereid` 无 pid | 高 | 采纳为第 2 层；按平台分支实现 |
| man7 unix(7)：POSIX 不保证 socket 权限位生效，「可移植程序不应依赖此特性做安全」 | 高 | 故不单靠 0600/0660，必须叠加对端凭据校验（纵深） |
| abstract namespace 完全无权限语义 | 高 | 否决（AGENTS.md §6 新增禁令行） |
| 以客户端 PID / 进程路径做强校验可被伪造 | 高 | 否决（AGENTS.md §6 新增禁令行） |
| .NET ≤10 无「bind 后自动 0600」加固（.NET 11 Preview 4 才引入） | 高 | 文件模式由传输层显式负责（`File.SetUnixFileMode`） |

- **与 A-xxx / D-xxx 的一致性**：结论与 **D-007 完全同向，零冲突 → 无 revised，无需停下呈报。**
- **调研缺口（如实登记）**：①macOS XNU `LOCAL_PEERCRED` 仅有二手 SO 引证，无第一手头文件确认；②Windows `CurrentUserOnly` 抢占行为属本机实测范畴，外部无公开数据 → 已由前置闸冒烟实验（§1#1）自证补位。

## 5. 不动项核验（本票未触碰，逐项留证）

| 不动项 | 核验方式 | 结论 |
|---|---|---|
| `GetRecentLogs` / `Pause` / `Resume` / `ReloadConfig` 语义 | `git diff --numstat src/PhotoPrivacy.Worker/WorkerIpcServerHostedService.cs` = `1 1`（单行）；`HandleRequest` / `ReadRecentAuditLogs` 未入 diff | ✅ 未动 |
| `UnixDomainSocketIpcTransport.DisposeAsync` 所有权语义 | 逐字保留 `bool ownsEndpoint = _listener is not null;` / `if (ownsEndpoint)` / 注释 `only by the endpoint owner` / `B11 finding`；复刻 `UdsEndpointOwnershipGuardTests` 分类器绿源 violations=0 | ✅ 未动 |
| `UiSingleInstance.MutexName` / `DefaultPipeName` | 逐字保留（`Global\PhotoPrivacyUi_Instance`） | ✅ 未动 |
| ADR 0001–0066 编号与内容 | 新增文件为 `0067-…md`，未改写任何既有 ADR（`ls docs/adr \| wc -l` = 67） | ✅ 未动 |
| `IIpcTransport` 契约 | 接口文件未入 diff（`git status` 无 `IIpcTransport.cs`） | ✅ 未动 |
| 既有 IPC 相关测试 9 个间接引用文件 | 全量扫描确认无 `UnixFileMode` / `PipeOptions` / `NamedPipe` 断言；`UnixFileMode` 断言仅存在于本票新增 2 文件 | ✅ 未动 |
| `WorkerIpcClient`（纯客户端） | 未入 diff；单参工厂调用点由重载默认值承接（`isServiceMode: false`） | ✅ 未动 |

## 6. 张力与呈报（待首脑裁定）

1. **`CreateServer` 第二参数对纯客户端路径无意义但被传入。** `WorkerIpcClient` 复用 `IpcTransportFactory.CreateServer` 构造 transport 仅用于 `ConnectAsync`（客户端不 `Listen`，`AccessMode` 不参与任何判定）。当前实现以重载默认值 `isServiceMode: false` 承接，行为正确、命名有歧义。**建议**：不动（改名会扩大 diff 面并触及共享契约）；或由首脑决定是否后续票引入 `CreateClient` 显式入口。**登记为不动项，非缺陷。**
2. **【复核发现 · P1 级】Windows service 模式跨用户 IPC 被本票按构造关闭。** 复核本票 diff 时发现的**功能影响**，需首脑裁定：
   - **事实**：`NamedPipeIpcTransport` 服务端由 `PipeOptions.Asynchronous` → `ServerOptions` 增 `CurrentUserOnly`；客户端 `ConnectAsync` 同步增 `CurrentUserOnly`。`CurrentUserOnly` 要求对端**同用户 SID**。
   - **影响面**：`scripts/install-service.ps1:44` 以 `New-Service`（**无 `-Credential`**）安装 → 服务以 **LocalSystem** 运行；`WorkerProcessManager.cs:25` 与 `ServiceModeController.cs:368` 证实 UI（交互用户）主动连接 `WorkerIpcEndpointNames.ServicePipe`（`PhotoPrivacyCleaner.Service`）。两侧 `CurrentUserOnly` ⇒ 该跨用户连接**按构造被拒**，UI 侧 service 模式的状态探测 / Pause / Resume / ReloadConfig / GetRecentLogs 全部失效。
   - **既有状态（非本票引入的功能回退）**：`docs/adr/0035` companion 段「NamedPipeIpcTransport 移除 ACL」已移除服务端 ACL，并明言「**service 模式需要跨用户时再单独引入可配置 ACL（未来 ADR）**」⇒ 跨用户 service 模式在改前即**已被显式推迟**。改前服务端无安全描述符（默认 DACL = SYSTEM + Administrators），尚存「管理员提权 UI 可连」的可能；改后语义由「未定义」变为「确定拒绝」。
   - **证据缺口**：前置闸冒烟实验探针 A3 仅验证**同用户** roundtrip，**未覆盖跨用户** —— 而 Windows service 模式的正确性恰取决于跨用户行为。故 §1#1 的「不降级」裁决应限定为「同用户路径 + ADR 0035 坑未复现」。
   - **处置建议（呈报，本窗不自行改向 —— handoff 明令「禁止静默改向——标记 revised 并停下呈报」）**：①**接受**并把 ADR 0067 已知限制升格为与 ADR 0035「未来 ADR」显式耦合；②开**后续票**实现 Windows 可配置 DACL（服务账户 + 交互用户/专用组）+ `install-service.ps1` 组供给，与 Unix 侧 0660+组模型对齐，届时同步放宽客户端 `CurrentUserOnly`。
3. **CI 平台覆盖不对称。** `.github/workflows/ci.yml` 与 `release.yml` 的 test job 均 `runs-on: ubuntu-latest`（单平台）。故：Unix 侧断言（0600/0660 文件模式 + 对端凭据接纳/拒绝/fail-closed）为 **CI 实跑主证据**；Windows 侧断言（首实例 fail-fast + 同用户连接）在 CI **不执行**，仅由前置闸冒烟实验（§1#1）在本机自证。**呈报**：Windows 行为无 CI 回归网，属已知缺口（ADR 0067 已知限制第 1 条）。

## 7. 过程违规呈报（主动自曝）

1. **CI-only 纪律 vs 票面强制的前置闸冒烟实验。** handoff 要求「本机零构建零测试」；issue 第 1 项 What-to-build 强制「单文件冒烟实验 true/false 各一次留证」，本质必须本机 `dotnet publish` + 运行。**处置**：判定为**票面自身授权的例外**（issue 优先级高于 handoff 通则），但为最小化污染，实验在 **OS 临时目录**（`%TEMP%\pp-ipc-smoke`）建独立最小控制台工程，**不入仓库、不引用仓库源码**，仓库内零 build 零 test。**仍如实登记为纪律例外。**
2. **本机静态复演作为 CI 凭证的临时替代。** §3 失效即红预演与 §5 不动项核验均为**本机静态口径**（纯文本 / 分类器复算，零 dotnet），非真实测试执行。**CI 云端行为测试绿为唯一终审凭证**，本报告一切 ✅ 均标注「（静态）」，最终结论待大脑推送 `ci-verify/correctness-round-t04` 后以 CI run 为准。

3. **前置闸冒烟实验未覆盖跨用户场景。** 票面第 1 项要求实验「裁决 Windows 路线」，但探针组只含同用户 roundtrip（A3），无跨用户对照。事后复核发现 Windows service 模式的正确性恰取决于跨用户行为（见 §6 张力 2），该实验不足以裁决该路径。**登记为实验设计的覆盖缺口**（若首脑裁定需补验，可在 Windows 上以「服务账户 + 交互用户」双身份补做一次）。

## 8. 提交与 CI（已完成提交）

- 分支：`04-ipc-auth-layered`（GitButler 虚拟分支，与栈内 `03-backup-mirror-layout` / `02-config-fallback-and-exclude-glob` / `01-wipe-unknown-format-and-claims` 并行，互不干扰）。
- 提交：`but commit -b 04-ipc-auth-layered -m "票 04 IPC 面：认证分层 + 承诺改写 + 部署配套（覆盖 A-004）"`（首提 16 个文件单元一次入栈，**首提物理哈希 `232d551`**；but slug `vko` 不作数，按 WORKFLOW §7 教训 3 以 git log 实物为准）。
- 终态：**17 个文件**（8 个既有文件修改 + 9 个新增：ADR 0067 / UnixNativeInterop / UnixPeerCredentialPolicy / 2 个测试 / 轨 1 四份沉淀）；具体增删行数以 `git show --stat 04-ipc-auth-layered` 为准。本报告副本的哈希回填、证据沉淀与**提交后复核修正**共经三次 amend 并入同一提交；报告文字**不内嵌易变计数与终态哈希**，以防自指递归。
- **不 push 不 PR**（WORKFLOW §4.2）；CI 推送由大脑执行，触发 `ci-verify/correctness-round-t04` 后以 CI run 为终审凭证。
## 9. 验证记录（本机静态口径）

| 检查 | 命令 / 方式 | 结果 |
|---|---|---|
| 行尾 | `git diff --check` | exit 0，CLEAN |
| CRLF 扫描 | 13 个改动/新增文件逐字节探测 | 全部 CRLF=0（纯 LF） |
| BOM 扫描 | 首 3 字节 `EF BB BF` 探测 | 仅 `UiSingleInstance.cs` BOM=true（**原样保留**），其余 12 个无 BOM |
| 语法括号平衡 | 10 个 C# 文件词法级状态机（剥字符串/注释） | 大括号 / 圆括号全平衡 |
| shell 语法 | `bash -n scripts/install-systemd-service.sh` | 通过 |
| systemd 渲染 | heredoc 渲染预演 | `RuntimeDirectoryMode=0750` 位于 `RuntimeDirectory=photoprivacy` 之后 |
| 安全扫描 | `semgrep scan --config p/csharp --config p/security-audit`（13 文件） | **0 findings / 0 errors**（`04-semgrep.json` 2055 B） |
| 文档结构 | AGENTS.md 首标题 + §10 段在场（`WorkflowSnapshotScriptGuardTests` 依赖） | 完整 |
| 快照 | `node scripts/workflow-snapshot.js`（§4.4 轨 2） | `D:\Aworker\photo-snapshots\20260916-115812`（131 文件 / 589887 字节 + manifest.json SHA256） |
| 提交后复核 | `vulnscan diff`（code-vulnscan skill，Layer 2）| **CLI 为存根**（仅打印 banner，零状态落盘）→ 不可用，已降级为人工证据链复核；Layer 3 `perseus` 不在 PATH。人工复核结论见 §6 张力 2（发现 1 项 P1 级功能影响） |
| 双轨沉淀 | 轨 1 副本 | `docs/process/reports/04-report.md` + 3 份证据（2 份 log + `04-semgrep.json` 原产物，随票提交） |
