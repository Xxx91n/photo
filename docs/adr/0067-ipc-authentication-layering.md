# ADR 0067: IPC 认证分层 — 内核身份边界 + 对端凭据校验 + 首实例防抢占

**状态**: 已接受
**日期**: 2026-09-16
**来源**: correctness-round 票 04（A-004 对账闸 / D-007 裁定 / spec ID4）

## 背景

锐评对 main@586a29a 的第 4 条指控（复核维持原判）：一个隐私清理工具的 IPC 端点无认证地把审计日志吐出去。全仓零认证原语，唯一访问控制是 socket 文件 0660；`GetRecentLogs` 原样返回最近 50 行审计 JSONL（内含 `source_path_masked` / `source_path_hash`，即「用户刚处理了哪些文件」）；AGENTS.md §5 第 1 条「命名管道需认证 — 验证连接方身份」未兑现。

前置事实（本轮 atomcode 深度调研，12 信源 7 篇一手全文，高置信）：

- Windows 命名管道的默认安全描述符对 Everyone/匿名账户授读；DACL 是唯一内核强制的门。
- .NET `PipeOptions.CurrentUserOnly` 是内核级同用户门，但其 Windows 实现存在 dotnet/runtime #123903（误用 `Owner` SID 而非 `User` SID）——偏差方向是「过紧」而非「过松」，安全上无害但可能破坏服务↔GUI 互通。
- Unix 侧「文件权限即安全」是流传最广的误解：man7 unix(7) 明言 POSIX 不保证 socket 权限位生效，「可移植程序不应依赖此特性做安全」；abstract namespace 更是完全无权限语义。
- 对端凭据（SO_PEERCRED / getpeereid）只做「识别」不做「拦截」，内核不会替你拒绝——必须应用层自行比对并立即 close。
- 首实例抢占是两平台共同 race，正确模式是启动失败即 fail-fast，绝不降级重试到其他名字。
- .NET ≤ 10 无「bind 后自动 0600」加固（.NET 11 Preview 4 才引入）——文件模式必须由传输层显式负责。

## 前置闸：单文件冒烟实验（2026-09-16，Windows 26100，.NET 10.0.201）

实验证据：`.scratch/architecture-recovery/reports/04-smoke-evidence.log`（OS 临时目录独立最小工程，不入仓库；`IncludeNativeLibrariesForSelfExtract` true / false 各一次，均 `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false`）。

| 探针 | 取值 true | 取值 false |
|---|---|---|
| A1 首实例 ctor(Asynchronous \| CurrentUserOnly \| FirstPipeInstance) | OK 未抛 | OK 未抛 |
| A2 重复 FirstPipeInstance | OK 抛 UnauthorizedAccessException（防抢占生效） | 同左 |
| A3 同用户 roundtrip | OK PING→PONG | 同左 |
| B1 对照组 NamedPipeServerStreamAcl.Create | OK（ADR 0035 坑本次未复现） | 同左 |
| C1 泄漏探针（300 次 create/dispose > 254 上限） | OK 零失败 | 同左 |
| D1 首实例(FirstPipeInstance) + 后续实例(仅 CurrentUserOnly) | OK 可共存 | 同左 |

**裁决：两种提取取值下 CurrentUserOnly 均未复现 ADR 0035 坑 → Windows 主机制不降级（不用 P/Invoke / 不用显式 SetAccessControl）。** 附带事实：A2 的 fail-fast 异常类型为 `UnauthorizedAccessException`（非 IOException），已固化为行为测试的断言口径。

## Considered Options

- **A 主机制（采纳）**：内核身份边界 + 首实例防抢占。
- **B 纵深（采纳，分层）**：accepted socket 上对端凭据校验；Unix 必落，Windows 由 DACL/CurrentUserOnly 基本覆盖。
- **C 握手令牌（不做）**：防的是威胁模型外的同用户对手；SMB 下令牌随文件泄漏。未来出现 LocalSystem 服务↔多用户 UI 形态时再议。
- **D 纯文档改写（否决）**：单独不成立，仅作为 A+B 落地后的承诺口径修正。
- **依赖 pipe 名保密 / 客户端 PID 或进程路径做强校验（否决）**：PID 可伪造（Project Zero 2019），均为伪边界。
- **abstract socket namespace（否决）**：无权限语义、无文件可锁，抢注防护一并消失。
- **`NamedPipeServerStreamAcl.Create`（维持禁用）**：ADR 0035 已证单文件解压上下文抛 UnauthorizedAccessException 并泄漏管道实例；同用户边界改用 CurrentUserOnly 即可，无需 ACL API。

## 决策：三层结构

### 第 1 层 — 内核强制身份边界（主防线）

| 平台 | 机制 |
|---|---|
| Windows | `PipeOptions.CurrentUserOnly`（内核在 connect 时校验用户 SID）；不用默认 SD，不用 ACL API |
| Linux background | socket 文件模式 **0600** |
| Linux service | socket 文件模式 **0660** + 组边界；`/run/photoprivacy` 目录 **0750**（`RuntimeDirectoryMode=0750`）+ GUI 用户组供给 |
| macOS | socket 在 `~/Library/Application Support/photoprivacy/`（用户家目录，无 root 需求）；background 0600 / service 0660 |

.NET 10 无 bind 后自动加固，文件模式由传输层显式负责（`UnixDomainSocketIpcTransport.ListenAsync` 内 `File.SetUnixFileMode`）。

### 第 2 层 — 对端凭据校验（纵深）

accepted socket 上读取对端 uid 并比对白名单；白名单策略：

- **background**：`{ 本进程 euid }`。
- **service**：`{ 本进程 euid } ∪ { socket 组成员 uid }`（成员经 `getgrnam` + `getpwnam` 解析；组名默认 `photoprivacy`，与安装脚本 `GROUP_NAME` 对齐）。

**fail-closed**：凭据不可读、或 uid 不在白名单内 → 立即 close 并抛 `UnauthorizedAccessException`（被 accept 循环记 warning，不变量④可见）。组解析失败退化为「仅本用户」，宁可拒绝合法连接也不放行未识别对端（不变量①）。

### 第 3 层 — 首实例防抢占 fail-fast

Windows：`PipeOptions.FirstPipeInstance` **仅施加于 `ListenAsync` 预建的首实例**；`AcceptClientAsync` 中 accept 后预建的下一实例**不得加该标志**（内核语义上仅首实例可声明，且名称已被占据，再加即建例失败）。

Unix：socket 路径 bind 先到即占位（受控目录内）；进程级单实例由 `ISingleInstanceGuard`（ADR 0036 独立 Mutex/flock）承担，本次不改 ADR 0026 的陈旧 socket 清理语义。

## 威胁模型硬边界（写进 AGENTS.md 承诺行）

认证依赖 OS 身份边界（文件权限 + 对端凭据）+ 首实例防抢占；**同 OS 用户进程不在防御范围内**——同用户进程具备同等文件/管道权限，任何 IPC 方案都无法区分。该边界是承诺口径的实质内容，而非免责声明。

## Consequences

- `IpcTransportFactory.CreateServer` 新增 `isServiceMode` 重载；`WorkerIpcServerHostedService` 传入真实运行模式；`WorkerIpcClient` 维持单参调用（客户端不 bind，访问策略无意义）。
- 新增 `UnixPeerCredentialPolicy`（可覆写，供测试注入伪造凭据模拟跨用户场景）+ `UnixNativeInterop`（libc P/Invoke：geteuid / getsockopt(SO_PEERCRED) / getpeereid / getgrnam / getpwnam）。
- `UiSingleInstance` 移除 `NamedPipeServerStreamAcl.Create` 分支，改用 `PipeOptions.CurrentUserOnly`——同时消除 AGENTS.md §5 第 5 条「NamedPipe 不使用 ACL」与实际实现的宣称-实现脱节。
- `install-systemd-service.sh` 补 `RuntimeDirectoryMode=0750` + `--gui-user` 组供给（`usermod -aG`），组供给缺失时显式告警而非静默。
- 既有 IPC 行为（GetRecentLogs / Pause / Resume / ReloadConfig / Ping / GetStatus）**消息语义**不变，仅前置认证层；但 Windows service 模式跨用户**信道**被 `CurrentUserOnly` 按构造关闭（见「已知限制」4）。
- 行为测试：`IpcAuthenticationLayeringTests`（真实 bind/connect + 真实文件模式 + 可模拟跨用户拒绝 + Windows 首实例 fail-fast）；对账守卫：`IpcAuthClaimReconciliationTests`（含失效即红预演）。

## 已知限制（登记在案）

1. **CI 平台覆盖不对称**：`.github/workflows/ci.yml` 的 test job 为 ubuntu-latest，Windows 专属断言（FirstPipeInstance fail-fast / CurrentUserOnly 真实连接）在 CI 不执行，证据为上述冒烟实验留证。
2. **UiSingleInstance 在 Unix 上的边界未收紧**：该管道走 `NamedPipeServerStream`（Unix 下为 FIFO），不经 `IIpcTransport` 抽象，故本次仅落实 Windows 侧；其载荷仅为「显示窗口」触发器，无数据外泄面。
3. **组解析限于 NSS 默认路径**：`getgrnam` 对 LDAP/SSSD 等外部组的成员列表依赖本地 NSS 配置；解析不到即 fail-closed 退化为仅本用户（伴随 warning）。
4. **Windows service 模式跨用户 IPC 被按构造关闭（确定，非条件）**：`PipeOptions.CurrentUserOnly` 在服务端与客户端双侧施加，要求对端同用户 SID。`scripts/install-service.ps1` 以 `New-Service`（无 `-Credential`）安装 → 服务为 **LocalSystem**，UI 为交互用户，故 `PhotoPrivacyCleaner.Service` 端点的跨用户连接**确定被拒**（UI 侧状态探测 / Pause / Resume / ReloadConfig / GetRecentLogs 失效）。
   **此非本 ADR 引入的功能回退**：`docs/adr/0035` companion 段已移除服务端 ACL，并明言「service 模式需要跨用户时再单独引入可配置 ACL（**未来 ADR**）」—— 跨用户 service 模式在改前即已被显式推迟；改前服务端无安全描述符（默认 DACL = SYSTEM + Administrators）尚存「管理员提权 UI 可连」的可能，改后语义由「未定义」变为「确定拒绝」。另注 dotnet/runtime #123903（误用 `Owner` SID 而非 `User` SID）方向同为过紧。
   **补救路径**：后续票实现 Windows 可配置 DACL（服务账户 + 交互用户/专用组）+ `install-service.ps1` 组供给，与 Unix 侧 0660+组模型对齐，届时同步放宽客户端 `CurrentUserOnly`。
5. **前置闸冒烟实验未覆盖跨用户**：探针组仅含同用户 roundtrip（A3），未设跨用户对照；而 Windows service 模式的正确性恰取决于跨用户行为。故该实验只裁决了「同用户路径 + ADR 0035 坑未复现」，不足以裁决跨用户路径（见限制 4）。
