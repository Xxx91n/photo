# IPC 契约（UI ⇄ Worker）

> 定位：UI ⇄ Worker IPC 契约速查，供新 UI（`ui/`）与 Worker 改动前查阅。
> 实际行为以列出的代码为准：服务端为 .NET Worker 实现（`src/PhotoPrivacy.Ipc/`、`src/PhotoPrivacy.Worker/`），客户端为 Rust 实现（`ui/crates/photoprivacy-ipc/`）；本文件是契约速查缓存，协议或两端改动时同步更新。

## 传输与端点

| 平台 | 后台端点 | 服务端点 |
| --- | --- | --- |
| Windows | 命名管道 `PhotoPrivacyCleaner.Background` | 命名管道 `PhotoPrivacyCleaner.Service.<32hex>`（#7：每次监听随机化，先建首例再通告于 `HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName`；通告缺失/非法，或通告名连不上（端点不存在）时回退静态 `PhotoPrivacyCleaner.Service`） |
| Linux | `$XDG_RUNTIME_DIR/photoprivacy/worker-background.sock`（#201：per-user；变量缺失/空/非根路径时不可用） | `/run/photoprivacy/worker.sock`（权限规则见下方 Linux 段，#188） |
| macOS | `~/Library/Application Support/photoprivacy/worker-background.sock` | `/Library/Application Support/photoprivacy/worker.sock`（系统级固定落点，#167） |

出处：`src/PhotoPrivacy.Ipc/WorkerIpcEndpointNames.cs`、`src/PhotoPrivacy.Ipc/IpcTransportFactory.cs`、
`src/PhotoPrivacy.Ipc/NamedPipeIpcTransport.cs`（#7 随机化）、`ui/crates/photoprivacy-ipc/src/endpoints.rs`（客户端动态发现）。
客户端必须按 OS 解析端点（旧 Avalonia UI 全程硬编码 Windows 管道名，Linux/macOS 因此连不上）；
Windows 服务端点自 #7 起按通告名动态解析（每次探测/失败重解析；通告名连不上时再试静态名，
见认证边界节），其余端点静态。

Linux 背景端点自 #201 起使用 `$XDG_RUNTIME_DIR/photoprivacy/worker-background.sock`：
旧 `/run/photoprivacy` 仅在服务安装/启动时由 systemd 创建，未安装服务的普通用户无权 mkdir，
导致后台 Worker 启动失败。选择候选 1（用户会话运行时目录）；候选 2（tmpfiles.d 预建）仍依赖
root 安装动作，不能解决未安装服务形态，故不采用。`XDG_RUNTIME_DIR` 通常由 pam_systemd/logind
提供（如 `/run/user/<uid>`，0700 属主用户）；两端均按原值解析，不裁剪空白。
变量缺失、空或非根路径时 Rust 返回 `None`、C# 返回 `null`：UI 不启动后台 Worker，
Worker 记录变量诊断、置退出码 1 并停宿主；**CLI 模式同样承接背景端点要求**，无会话的容器/root shell
需显式提供指向用户可写运行时目录的 `XDG_RUNTIME_DIR`。不回落 `/run/photoprivacy`（原缺陷），
也不回落 `/tmp`（可预测路径预蹲及跨用户相撞风险）。子目录仍由传输层按 #187 的 `0750` 规则创建，
后台 socket 保持 `0600` 与同 UID 核验；服务端点 `/run/photoprivacy/worker.sock`、systemd
`RuntimeDirectory` 与服务 socket `0660` + socket 访问组边界均不变。双端路径行为测试与
`IpcAuthClaimReconciliationTests` 的字面量核对共同防止两端实现偏离这份契约；单实例锁不依赖 IPC socket 路径，无需迁移。

macOS 服务端点自 #167 起为系统级固定落点：LaunchDaemon 以服务用户运行（`NFSHomeDirectory=/opt/photoprivacy`），
GUI 用户按自身 home 拼同一相对路径只会指向各自的幽灵 socket，故服务端点不再参与 home 解析（与 Linux `/run/photoprivacy` 同构）。
目录由 `scripts/install-launchd-service.sh` 以 root 建为 `0750`（属主服务用户:socket 访问组 `photoprivacy-ipc`），
GUI 用户经 `--gui-user`（默认 `SUDO_USER`）**只加入 socket 访问组**，方可穿过目录并连上 `0660` 的 socket。
显式 `--gui-user` 是调用方的明确意图，该账号不存在或加不进 socket 组时安装以非零退出失败；
仅由 `SUDO_USER` 推断得来的用户只告警继续（推断噪声不升级为安装失败）。
socket 访问组与数据组（`hot`/`quarantine` 的 `2770` 组）严格分离：GUI 用户不进数据组，即拿不到组写，
无法绕过 IPC 直接删审计或隔离文件（PR #180 评审 CWE-732）。审计目录是唯一例外——本地 tail 工具
（`photoprivacy-logs`）与事后排查脚本以 GUI 用户身份直接读日志，故该目录组改为 socket 访问组并收成 `2750`
（组只读）、文件 `0640`，能看不能改。安装目录与 Worker 日志目录同样是权限面（R1）：`/opt/photoprivacy`
为 `0755`（属主服务用户，其他用户只读可执行、不可写）——不能收成 `0750`，GUI 用户要从这里执行 UI 二进制，
`0750` 下实测 `sudo: unable to execute …/PhotoPrivacy: Permission denied`，等于把 UI 一起收敛掉；
敏感面单独收紧：`config/` 为 `0750` + 文件 `0640`，Worker Serilog 落点 `logs/` 为 `2750` + 文件 `0640`
（属主服务用户:数据组，本地其他用户既不进目录也读不到请求 URL/文件名）。
服务端以 `UnixPeerCredentialPolicy` 的组白名单做第二层校验，
组名由 plist/systemd 注入 `PHOTOPRIVACY_SERVICE_GROUP`。**服务模式缺失/空白注入即拒绝启动**（fail-closed，
`InvalidOperationException` 点明变量名），不再回落任何组名——旧缺省值 `photoprivacy` 正好是数据组，
手起 Worker / unit 未更新 / 容器里运行会让 socket 组等于数据组，组隔离静默退化成 CWE-732 原始形态（#187 缺口 2）。
后台模式不看组（socket 0600 仅属主可连），缺注入照旧可用；两组默认名分别为数据组 `photoprivacy`、
socket 访问组 `photoprivacy-ipc`（与 `install-launchd-service.sh` 的 `SOCKET_GROUP` 对齐）。
**Linux 安装链（#188 已落地）**：`install-systemd-service.sh` 已把 socket 访问组和数据组分开——`Group=${SOCKET_GROUP}` 与
`Environment=PHOTOPRIVACY_SERVICE_GROUP=${SOCKET_GROUP}` 均指向 socket 访问组（缺省 `photoprivacy-ipc`），
不再注入数据组名；安装目录/config/logs/审计的权限收紧同票落地（规则见下方 Linux 段），
`verify-systemd-service.sh` 在 CI Ubuntu runner 逐项比对 mode/group。
socket 目录不由安装器建出时（手工运行等），传输层自己按 `0750` 创建（`SocketDirectoryMode`；macOS
`SERVICE_SOCKET_DIR` 同值，Linux systemd 侧是 `RuntimeDirectoryMode=0750`，安装链的规则已由 #188 统一，见下方 Linux 段）；
**已存在的目录不改权限**（属主/组/模式都以安装器与服务管理器为准，运维收紧到 0700 的目录不得被传输层这条保底路径放宽）。
裸 `Directory.CreateDirectory` 按 umask 建 0755 的做法已被 #187 堵死（#187 缺口 1）。

Linux 服务模式的权限规则与 macOS 相同（#188，PR #180 R1 同款）：`scripts/install-systemd-service.sh`
把安装目录 `/opt/photoprivacy` 收敛为 `0755`（属主服务用户，其他用户只读可执行、不可写——GUI 用户要从这里
执行 UI 二进制，`0750` 会把 UI 一起锁死，macOS 实测同款教训），敏感面单独收紧：`config/` 为 `0750` + 文件 `0640`，
Worker Serilog 落点 `logs/` 为 `2750` + 文件 `0640`（属主服务用户:数据组）。socket 访问组 `photoprivacy-ipc`
与数据组 `photoprivacy` 严格分离：GUI 用户经 `--gui-user`（默认 `SUDO_USER`）**只加入 socket 访问组**；
unit `Group=` 指向 socket 组——Worker 主 GID 决定 `/run/photoprivacy/worker.sock`（`0660`）与
`RuntimeDirectory`（`0750`）的属组，`UMask=0027` 让运行期新文件也落在 `0640`（macOS plist `Umask 23` 同款）。
审计目录 `/var/log/photoprivacy` 是唯一读例外：组换 socket 访问组、目录 `2750`、文件 `0640`（组只读，能看不能改）；
`hot`/`quarantine` 保持数据组 `2770`，GUI 用户拿不到组写。`--socket-group` 与数据组同名/为空/复用同一 GID 一律拒绝；
显式 `--gui-user` 在账号不存在或加不进组时直接报错，安装以非零退出码失败，`SUDO_USER` 推断只告警（与 macOS 同款）。
验证链：`scripts/verify-systemd-service.sh`（CI Ubuntu runner，`.github/workflows/systemd-verify.yml`）
逐项比对 mode/group，并以 GUI 用户语境 UI 探针核对 IPC 可达性。传输层的保底动作已由 #187 落地（socket 目录
缺失时按 `0750` 创建、服务模式缺注入 fail-closed 拒启）；已存在目录的权限仍以安装器为准，代码不改。

### 服务配置写入边界（#167：维持数据隔离）

socket 访问组只授予「调用服务」权限，**不授予「定义服务行为」权限**。服务模式配置仍只由服务账户 / root
写入；`config/` 的组写不授予 socket 访问组，GUI 用户（socket 组成员）既不可读也不可写。

这条边界必须保持：配置含 `exiftool.path` 这类可执行路径，Worker 会以服务身份拉起该程序；一旦 socket 组
可写配置，组员就能把路径指向恶意二进制，以服务身份访问 `hot`/`audit`/`quarantine` 并按服务权限删改——
即 CWE-732 的「文本编辑器式提权」（CWE-732 缓解条款要求配置文件仅管理员可写；Apple Secure Coding Guide
要求 helper 不信任调用者、避免被 puppeteering）。

现状与后果：UI 是 `config.json` 的写入方，服务模式下它的默认配置路径就是
`/opt/photoprivacy/config/config.json`；该文件对 GUI 用户不可读也不可写，因此配置页只能退回默认值 /
初始加载失败，保存会回显失败——即**服务模式暂不支持 GUI 读写配置**。不要把放宽 `config/` 权限当作修复
——那等于把「调用权」升级成「服务身份代码执行权」。GUI 改配置需要受控配置 / 规则通道（可执行路径白名单
+ 签名 / 前缀校验），涉及协议扩展与契约调整；在该通道落地前，不得放宽 `config/` 权限。
决策出处：#167（PR #180）。#188 起 Linux 安装链用同样的规则（`config/` `0750`+`0640` 数据组），
该边界同样适用于 Linux 服务模式。

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
`IpcClient` 各自串行，争用同一管道实例时仍靠 `ERROR_PIPE_BUSY` 重试保底。

**请求行必须 UTF-8 且不带 BOM**（#218 根因归档）：服务端按行读到首字符即交给
`JsonSerializer`，`EF BB BF` 会让它抛 `JsonException`，连接循环随即回
`{ok:false,message:"invalid_request",v}`（`id` 为 null）。踩坑点集中在 PowerShell：`[Text.Encoding]::UTF8`
是**带 BOM** 的实例，`StreamWriter` 会先写三字节前缀，表现为「管道连上了、DACL 也放行，却只拿到
`invalid_request`」。脚本侧必须用 `[Text.UTF8Encoding]::new($false)`；`verify-windows-service.ps1`
的 Ping 帧同理只用 `{v,id,method}` 三字段，不加 `params`。

## 方法

`Ping`、`GetStatus`、`Pause`、`Resume`、`GetExifToolVersion`、`Shutdown`、`ReloadConfig`、`GetRecentLogs`。
`Shutdown` 仅 CLI 模式可用（服务/后台模式返回 `ok:false`，退出不要依赖它）。协议版本 `v` 当前为 1，
唯一出处是 `WorkerIpcProtocol.Version`（`src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs`）。 会话/启动顺序边界的版本协商语义见本文件「版本协商（#42）」。
`ReloadConfig` 在服务模式与后台（托盘）模式下走同一方法、同一客户端实现，差异只是调用方当前连上的端点。
重载失败（校验失败、重建失败）回 `ok:false` 并带 `message`；只有真正生效才回 `ok:true`
——失败不得再被吞成成功（#85）。处理链任何异常都必须仍回包（`ok:false`），响应写入不得依赖日志 provider 健康；
处理器自身未产出响应对象时，连接循环保底统一回 `{ok:false,message:"internal_error",id,v}`（不改变任何既有字段语义）。
请求行无法反序列化时回 `{ok:false,message:"invalid_request",v}`；超长行与空行仍按帧规则丢弃、不回包。

重建阶段失败必须回滚（#112）：拆卸后重建抛错时，worker 用上一份已生效配置把旧运行时（watcher/pipeline/
store/audit）建回来，再按上述契约回 `ok:false`（重建失败带 `reload_failed: …（已回滚到上一份生效配置）`）。
回滚会按上一份配置重建去抖队列并把旧目录全量补漏扫一遍（已处理文件由去重表挡住），拆卸窗口内在途、
未处理的文件不会因失败尝试而丢失。回滚成功时恢复到上一份已生效配置：`GetStatus.data.watchDirectory`
仍是原监控目录，旧目录继续处理新文件（上一份是空目录待机时回到待机）；只有回滚本身也失败，worker
才可能进入「可应答但无运行时」的降级态，需再次 `ReloadConfig` 或重启恢复。协议字段与状态位不变（D7）。
重载与「一轮文件处理」互斥：若正在处理文件，重载最多等 8 s；超时回 `ok:false` 并带 `reload_busy`，
此时旧配置与组件保持原样（不做半拆）。

### `exifToolVersion` 字段（`GetStatus` / `GetExifToolVersion` 的 `data`）

- 取值来源：`WorkerIpcContracts.cs` 的 `WorkerStatusDto.exifToolVersion`；单桥取 `ExifToolBridge.VersionText`，`stay_open_pool_size>1` 时由 `PooledExifToolBridge` 返回首个已解析出的子桥版本。
- **按需解析**：桥自 M4 起惰性启动（首个文件才拉 `stay_open` 进程），因此首个文件到达前该字段为 `"unknown"`。状态快照读到 `"unknown"` 时，服务端在后台驱动一次版本解析：**进程真的没了**才启动（启动自带版本探针），进程还在就只补探——状态路径一律**不做健康检查**（健康检查的副作用是重启，会打断在途擦除），也绝不对活进程重启。响应不等待探针，真实版本由下一拍轮询带出（#86）。凡是返回状态快照的方法（`Ping`/`GetStatus`/`GetExifToolVersion`/`Pause`/`Resume` 等）都走同一入口。补探**不进文件处理热路径**，也不为一次状态查询拉起整个池。
- **有意取舍**：拿到真实版本的唯一途径是让 ExifTool 跑起来（`stay_open` 常驻本就是产品约束），因此一次状态查询会触发进程启动并记 `exiftool_started` 审计；`paused` 只表示不处理文件，不阻止版本解析。不想付这个代价就不要轮询该字段。
- **降级语义**：ExifTool 不可用（路径失效 / 进程起不来 / 探针超时）时字段保持 `"unknown"` 并记 warning 日志；补探最多每 5 s 重试一次，下一拍到点后自动再试。`"unknown"` 只表示「尚未解析出版本」，不表示「ExifTool 不存在」——可用性判定不看这个字段。dry-run 模式恒为 `"dry-run"`，不触发补探。停机（`ApplicationStopping`）后的补探被取消；已 `StopAsync` 的桥（停机或配置热重载）进入终态，拒绝再次拉起进程——退役与启动互斥（同一把启动锁，锁内重检终态），既不会在热重载窗口留下无人回收的子进程，也不会让已准入的启动在退役后继续创建子进程。
- 字段语义与线格式不变；本轮只补上报链路，不扩协议。

## UI 侧轮询

- 心跳：`GetStatus` 每 1 s；失败按 1→2→4→8→16→32→60 s 退避（`ui/crates/photoprivacy-ipc/src/monitor.rs`）。
- 版本：独立 1 s `GetExifToolVersion` 轮询（`watch_version`），不随心跳退避停摆。
- 服务态：每 3 s 探测服务端点可达性；完整 SCM/launchd/systemd 安装态探测归 #58。

## Worker 启动

`PhotoPrivacyWorker --mode background [--config <config.json>]`。UI 侧连接顺序：服务端点 → 后台端点 → 启动后台 Worker 并轮询 5s（`ui/crates/photoprivacy-ipc/src/monitor.rs`；旧栈实现 `src/PhotoPrivacy.Ui/Services/WorkerProcessManager.cs` 已随旧 Ui 工程退役，#61）。
当 `watch.hot_folder` 为空时，Worker 仍建立 IPC 并保持待机；`GetStatus.data.watchDirectory` 为空，UI 应引导用户到配置页选择监控目录。保存有效目录后通过 `ReloadConfig` 启动监控，无需重启 Worker（#100）。

连接全部失败时，UI 按最近一次真实失败原因三态分类呈现（#169，编排顺序与轮询预算不变）：服务端点连接被管道 DACL 拒（`ERROR_ACCESS_DENIED`，端点存在的证明）→「未被授权使用服务模式」，行动指引**按平台分开给**——Windows 指向安装脚本的 `-AuthorizedUsers` 集合，Unix 指向服务 socket 的 0660+共享组边界（`install-systemd-service.sh --gui-user` / launchd 同构）；UI 拉起的后台 Worker 未就绪（spawn 失败 / 已退出——含 #163 单写者准入门让位退出码 1 / 5s 超时）→ Worker 缺失语义并附退出细节；未找到可拉起的 Worker 可执行文件 → 引导安装。分类经 `ConnectionSnapshot.connect_failure` 广播，心跳连上即清空；心跳掉线不清（授权拒绝是编排期事实）。

服务启动超时的 1053 归因（#202 / #218）：SCM 等不到 RUNNING 只能回 1053，成因既可能是服务 Worker
撞上单写者准入门让位，也可能是 ExifTool/配置/提权类真超时。UI 只有在**三个条件同时成立**时才把它
归因为单写者让位并换让位文案——① 退出码 1053；② 此刻真的连着后台 Worker（`ConnectionState::Connected`
+ `worker_mode == Some(Background)`，不认连接失败后残留的握手值）；③ **两边审计目录相同**：后台 Worker 用
UI 内存里那份 `config.audit.log_directory`，服务用安装时落盘的 `config.json`（`--config` 指向的那份），
归一化（去尾分隔符、Windows 下忽略大小写）后逐字相同。目录不同说明两个 Worker 并不争同一份目录，
1053 只能按真超时处理，否则用户照着让位文案关掉后台 Worker 也修不好；配置读不到同样按真超时保守处理。
判断标准的实现见 `ui/crates/photoprivacy-ui/src/app.rs` 的 `start_timeout_cause` / `connected_audit_dirs_match`。

## 认证边界与已知缺陷

- Windows：后台管道 `PipeOptions.CurrentUserOnly`；Unix：0600/0660 + 对端 uid 白名单（fail-closed）。
- Windows 服务管道（#70，决策 #82 D3；#1 opt-in 授权集合）：显式 DACL 取代 `CurrentUserOnly`——
  安装脚本（`scripts/install-service.ps1`）把安装者 SID 写入 `HKLM\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid`，
  并把「安装者 + `-AuthorizedUsers` 显式授权用户」的完整 SID 集合写入 `ServicePipeAllowedSids`
  （REG_MULTI_SZ，#1 opt-in：不传该参数则集合仍含安装者保底，默认不扩权）；
  服务端经 managed 正典 `NamedPipeServerStreamAcl.Create` 建例（传 `PipeSecurity`，不传
  `CurrentUserOnly`——它会给创建者专属 DACL 并忽略传入值），授予集合内每个 SID
  FILE_GENERIC_READ|WRITE **剥掉单个权限位后的掩码**（#7，见下）+ SYSTEM/Administrators 完全控制。
  DACL 必须给服务身份（SYSTEM/Administrators 的 FILE_ALL_ACCESS）保留
  FILE_CREATE_PIPE_INSTANCE（0x4）：服务建第 2..N 个实例时内核按 DACL 对创建者令牌做该项检查
  ——38142f9b 的「单文件 UnauthorizedAccessException + 254 实例泄漏」实为旧 WorldSid-ReadWrite
  DACL 缺此位（`PipeAccessRights.Write` 刻意不含它），非单文件问题。
  解析优先级：授权集合值缺失/空/全非法 → 回退安装者单值（#70 默认行为）→ 仍缺失才回退
  `CurrentUserOnly`；集合内非法条目逐条剔除，不放大为整体失效。
  ACL 代码按 `PP_WINDOWS_HOST`（csproj 按 `'$(OS)'=='Windows_NT'` 注入，Ipc 与测试工程用同一个开关）
  编译：CI test job 只跑 ubuntu，Windows 编译覆盖由 release.yml win-x64 构建与发版机承担；
  交叉发布（非 Windows 宿主产出 win-x64）时解析与建例均被编译排除，fail-closed 回退
  `CurrentUserOnly`。SID 缺失/无效 fail-closed 回退 `CurrentUserOnly` 并记 warning；
  重装/升级覆盖登记值，ACL 随最新授权集合。
  授权集合内账户的非提权 UI 可完成服务端点往返；集合外账户仍被拒（授权语义归 #1——
  被授权账户可读服务侧 GetRecentLogs 审计信息，只应授予信任用户）。
  C# 传输层 `ConnectAsync` 客户端与所连模式同界：后台模式带 `CurrentUserOnly`（不变），
  服务模式不带（与服务端 DACL 同界，否则会在客户端把对 SYSTEM 服务的合法连接整体拒绝）
  且自 #7 起改走细粒度掩码重载（见下）；
  服务端点的产品客户端只有 Rust UI（本就不做属主校验）。
- Windows 服务管道防 Hijack 加固（#7，CWE-732 + 命名熵）：
  - **从掩码里剥掉单个权限位**：授权用户 ACE 从 FILE_GENERIC_READ|WRITE（0x12019F）剥除
    FILE_CREATE_PIPE_INSTANCE（0x4，与客户端 GENERIC_WRITE 必需位 FILE_APPEND_DATA 同值——
    微软 Named Pipe 文档登记的 API 位碰撞缺陷）→ 0x12019B；FirstPipeInstance 只保护首实例，
    没剥掉这个位之前，被授权用户可建后续实例（冒充服务端/抢占连接/DoS，PR #160 审查发现登记于 #7）。
  - **客户端双端同步细粒度打开**（与剥掉权限位后的 DACL 同值 0x12019B，GENERIC 打开会因 0x4 位被
    AccessCheck 整体拒绝）：C# 走 `NamedPipeClientStream(serverName, pipeName, PipeAccessRights, …)`
    重载（.NET 10 实有该 API；#7 票内评论所记「无 access-rights 重载可用」已过时，无需 P/Invoke）
    + `TokenImpersonationLevel.Identification`（.NET 内部生成 SECURITY_SQOS_PRESENT|Identification）；
    Rust 侧 tokio 1.54 无 `ClientOptions::desired_access`（评论所记 API 不存在，docs.rs 核实）→
    原生 CreateFileW（share=0、OPEN_EXISTING、SQOS 三件套 `SECURITY_SQOS_PRESENT|
    SECURITY_IDENTIFICATION|FILE_FLAG_OVERLAPPED`，与 tokio `ClientOptions::new` 默认 flags
    同构；SECURITY_SQOS_PRESENT 是微软 CreateFileW 文档的前置条件，漏它则 Identification
    级别不生效——审查返修 M1 钉住）后经 `NamedPipeClient::from_raw_handle` 注册；busy/not-found
    重试语义与错误码不变。双端冒充级别固定 Identification：防伪服务端（预蹲者）对客户端发起
    ImpersonateNamedPipeClient 冒充；产品服务端从不冒充客户端，收紧不改变合法链路。
  - **命名熵**：服务每次监听生成 crypto-random 后缀（基名.32hex），**先建首例**
    （FirstPipeInstance fail-fast 传感器保留：名被占即绑定失败）**再通告**
    `HKLM\SOFTWARE\PhotoPrivacy\ServicePipeName`（REG_SZ，键默认仅管理员/SYSTEM 可写——
    发现通道完整性 ≥ 管道客户端集合，非提权无法伪造指针）。随机名使预蹲者无从得知未诞生的
    名字，剥掉权限位使建例后无法加实例，HKLM 权限使指针不可伪造——三者闭环。通告失败（非
    LocalSystem 服务账户/注册表损坏）回退静态名重绑：fail-safe 可用性优先（与 #70 的 SID
    缺失回退同构），Worker 记 warning 观测，此时防抢占面回到 #7 前、剥掉权限位后的 DACL 仍生效；
    回退时 best-effort 清除残留通告值（审查返修 m1）。清除失败也不再把 UI 钉死：客户端在
    通告名连不上时会再试静态名（残余⑤，见下）。
    优雅停机清除通告值；崩溃残留由下次启动覆盖/卸载清除（卸载脚本一并清三个登记值）。
  - **UI 侧动态发现**：服务端点解析随拍/失败重读通告名（3s 可达性探测、心跳失败重解析、
    1s 版本轮询）；连接编排在启动时读一次候选。通告缺失/非法（字符集/长度校验防注入）回退
    静态名——旧版服务兼容。**通告名不存在时也再试一次静态名**（#7 残余⑤）：连接编排、3s
    探测、1s 版本轮询和心跳失败后的下一次尝试都走候选；版本轮询优先用心跳已落定的端点
    （可能是静态名），只有它不存在时才轮到通告名候选。忙/超时（端点还在，只是这一拍被占或
    慢）不换名，授权拒绝更不换（那是端点存在但账户不在授权集合的证据）。服务重启换名后心跳
    失败重解析并推送 endpoint watch，版本轮询下一拍跟随；backfill 每轮 5s 重试时重读 endpoint
    watch。Windows 管道名不区分大小写，候选去重与「同一个失败端点」判断都按不区分大小写处理。
  - **已知残余**（如实登记）：① 已闭环——后台管道同用户抢先建面由客户端核验服务端进程身份
    覆盖（见下方「后台管道服务端身份核验」）；新上限随之登记在该节。② 混合版本窗口：新服务
    + 旧 UI 表现为「服务不可达→回退后台」——**接受不修**：发布目录同体、升级先关 UI，
    实际窗口≈0；为旧 UI 保留静态名监听会把预蹲面加回来，得不偿失。③ UI 启动恰逢服务重启的
    毫秒级通告空窗内可能读到陈旧通告名——**可达性探测**下一拍重读通告名即恢复显示，纯函数用例
    `service_pipe_endpoint_after_failure` 的 NEW/OLD 分支钉住「新名来了就选新名」；但启动时的
    连接编排只读一次，若那一拍已回落到后台 Worker，本版不会自动升回服务模式（重启 UI 才切），
    这是既有编排语义，不在本票内改。④ 同机授权用户以客户端身份连服务管道发任意请求仍是
    信任边界内的既有行为（#1 语义）。⑤ **已闭环**——通告持续失败（权限性）且回退时的残留
    清除也失败时，UI 不再钉死在陈旧通告值上：通告名在连接预算内连续找不到就换静态名再试。
    **新上限**：判定口径是「整个连接预算（700ms）内最后一次可重试错误仍是 raw 2/NotFound」
    ——服务停了、崩了、或卡过重建空窗预算都算；同一预算里先忙（231）后缺失的极端组合按最后
    一次错误算，可能被当成缺失。兜底不改写健康服务（通告名还在、只是忙/超时）的预蹲保护；
    但服务确实不在、静态名又被同用户进程抢占时，兜底会连到抢占者，且只要抢占者应答，UI 会
    停在它上面直到下一次失败或 UI 重启（成功期不主动换回）——这与通告缺失/非法时既有的静态
    兜底是同一暴露面，不宣称防同用户恶意软件（同 ④ 的信任边界口径）。
- 后台管道服务端身份核验（#7 残余① 闭环，客户端认证服务端）：
  后台 Worker 由 UI 拉起，发现通道（拉起参数、同用户可写位置、管道枚举）同用户均可伪造，
  命名熵无法闭环——改由**客户端核验服务端进程身份**，在连接建立后、首笔业务请求写入前完成
  （占用同一份连接预算，一连接一请求与帧格式不变；AC3/AC4）。
  - **Windows**：`GetNamedPipeServerProcessId` 取服务端 PID（Vista+，客户端句柄可用、无需
    特权；Chromium mojo 的 `VerifyServerPrivilege` 同路径）→ 首选比对 UI 本次拉起的 Worker
    子进程 PID（拉起后登记；Worker 崩溃重启由新一轮拉起覆盖）→ 回退
    `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` 比对
    已安装的 `PhotoPrivacyWorker.exe`（UI 重启但后台 Worker 仍在）。
  - **Linux**：tokio `UnixStream::peer_cred()` 的 `SO_PEERCRED` pid，走同一判定链；回退读
    `/proc/<pid>/exe`。
  - **macOS**：spec 原假设「`LOCAL_PEERCRED` 的 xucred 不含 pid = 平台能力缺口」已按事实修正
    ——std/tokio 在 macOS 经 `LOCAL_PEEREPID` 取 pid（rust-lang/rust#79387），`UCred::pid`
    可取值，PID 直配可用。**已知期望 PID 却对不上时按 Rejected 作废连接**（抢建场景正是这一支
    ——否则「让 PID 不符」即可绕过核验）；能力缺口只在无期望 PID（UI 重启、Worker 仍在）时
    生效。首版未实现 `proc_pidpath` 镜像回退，该回退场景按平台策略处置。
  - **失败语义**：PID/镜像不符或查询失败 → 该连接作废、不重试，计入既有 #169 三态的 Worker
    启动失败（细节前缀 `untrusted IPC server:`），warning 可观测（含 PID 不符 / 进程已退出 /
    查询失败的区分）；核验瞬间进程消失（`OpenProcess` 返回 `ERROR_INVALID_PARAMETER`、
    `/proc` ENOENT）→ 按瞬时连接失败在连接预算内重试，预算耗尽也只报连接失败（不升级为授权
    失败）；平台能力缺口或无期望基准 → 按平台策略：**Windows/Linux fail-closed**（核验不了即
    拒该连接，AC2）、**macOS fail-open + warning**（D2）。
  - **范围**：只作用于后台端点。服务管道不叠加同核验（D3：命名熵 + 剥掉权限位后的 DACL + HKLM 通告
    完整性已闭环，叠加只会扩大面与回归风险）。
  - **C# 传输层同构**：`NamedPipeServerIdentity`（P/Invoke `SafePipeHandle`）+
    构造期可选 `ServerProcessIdentityExpectation`，走同一判定链（PID 直配 → 镜像回退 →
    查询失败即拒）；未注入时零行为变化。差异如实登记：C# 无连接编排循环，核验期间进程消失
    不重试，按连接失败上抛；非 Windows 宿主该 API 恒返回 false（等价 fail-closed，Rust 侧的
    macOS fail-open 策略不适用于 C# 传输层）。C# 客户端随旧 UI 退役（#61），产品客户端只有
    Rust UI；该传输层保留以维持契约同构与行为测试。.NET 10 没有任何托管 API 能取管道服务端
    PID（P/Invoke 为唯一路径）；Rust 侧 tokio 无 `ClientOptions::desired_access` 的旧结论
    不受影响。
  - **镜像回退基准**：取 UI 同目录的 `PhotoPrivacyWorker`（发布目录同体，见 AGENTS.md 布局）；
    非同目录部署时镜像回退失效，Windows/Linux 按 fail-closed 拒绝。
  - **新上限（不虚标）**：同用户恶意软件仍可注入 UI、读 UI 内存——同用户本就是无隔离级差的
    信任域；PID 快照核验存在固有的 PID 复用窗口，且期望 PID 登记后不清除——**窗口覆盖整段连接
    生命周期**（Worker 退出后同 PID 被任一同用户进程复用且抢建同名管道即可被误信；根治需
    签名/共享密钥，未入首版 D4）；镜像路径是 speed bump 而非不可伪造凭据；WinVerifyTrust
    签名核验未入首版；macOS 镜像回退 fail-open；macOS 上「UI 重启 + Worker 被手工重启」的
    罕见组合会因已知 PID 不符且镜像不可核验而被拒（重启 UI 即恢复）。出处：
    `ui/crates/photoprivacy-ipc/src/verify.rs`、`client.rs`（Rust 产品客户端）、
    `src/PhotoPrivacy.Ipc/NamedPipeServerIdentity.cs`（C# 同构）。
- Unix 服务模式不变（0660 + 组白名单）；Linux/macOS 的服务管道访问按各自机制另行核对（#70 AC）。
- 客户端**不做**旧 UI 的 `PipeOptions.CurrentUserOnly` 属主校验（服务模式下自相矛盾）；理由见 `ui/crates/photoprivacy-ipc/src/client.rs` 注释。后台端点自 #7 残余① 闭环起另做服务端进程身份核验（见上）；服务端点不叠加。

## UI 单实例唤起通道（非 Worker 协议）

- Mutex：`Global\PhotoPrivacyUi_Instance`（POSIX 为 `$TMPDIR/photoprivacy-Global-PhotoPrivacyUi_Instance.lock`）。
- 唤起管道：`PhotoPrivacyUi_ShowWindow`，消息 `SHOW_WINDOW\n`，500 ms 超时；属主只写打开兼容旧 UI 的 `PipeDirection.In`。

## 版本协商（会话/启动顺序边界，#42）

UI 可能先于 Worker 启动，双方通过请求/响应的 `v` 字段交换协议版本，协商本次会话实际使用的版本：

- 客户端每条请求携带自身版本 `v`（默认 1，`PROTOCOL_VERSION`）；服务端每条响应携带自身版本 `V`（`WorkerIpcProtocol.Version`）。
- 会话版本取 `min(客户端, 服务端)`：客户端在首次成功往返后观察服务端 `V`，与自身版本不等时按较低版本对齐后续请求，并在 stderr 记一次告警（如「协议版本不一致：客户端 v1，服务端 v2，本次会话已按 v1 运行」），不重复告警。
- 服务端按 fail-closed 处理：客户端申报的 `v` 高于服务端所能说话的版本时，**不执行方法**，回 `ok:false` 并带 `message: "protocol_version_mismatch"`，响应仍回带 `V` 字段（服务端版本），供客户端据此下调。客户端收到此拒绝且响应携带更低的 `V` 时，立即用 `min` 版本重新建连并重试一次。
- 当前协议版本为 1，双端均为 v1 时与既有行为完全一致：不告警、不额外往返、语义不变。此规则只为未来版本号变动时的窗口期定行为，不引入新 method，也不改既有字段语义。

## 改动规则

协议扩展**只加 method 名**，不改既有字段语义（#49 定下的规矩）。改完同步：本文件、`ui/crates/photoprivacy-ipc/src/protocol.rs`、对应 Rust 测试与 .NET 侧契约测试。
