# 票 06 收口报告：黑名单点状升级 + 对账收口 + 整轮审计

日期：2026-09-16 ｜ 执行窗口：correctness-round W6 ｜ 覆盖：A-007、A-008、A-009（+ T1 承接、UI.csproj/ADR 0035 顺带发现）

## 0. 开工闸

- **Blocked by 解除证据**：票 05 = ACCEPT（README 波次表：计费解除后 CI run 35061555907 绿，重审核子代理实物核验 Core 244 + Integration 366 = 610 零失败；A-005 结算 implemented）；frontier 重算「W6 解锁，票 06 可开工」。
- **必读清单逐份确认**：prompts/06、handoffs/06、issues/06、spec.md（ID6/ID7）、WORKFLOW.md §4.2/§4.3/§4.4、A 系账本 A-001~A-009、D 系账本（D-002 五不变量、D-003.4 点状升级、D-008.6 票 6 口径）、`.codex-tmp/锐评.txt` §7.1/7.2/7.3、reports/05、轮 README——全部已读。
- **A-xxx ↔ 不变量映射**：A-008（守卫枚举漏报）→ 不变量⑤（宣称-实现一致：组合根承诺由机器断言兑现）；A-007（source-lint 占比）→ 不变量⑤（存量守卫零删缩 + 行为测试在场核验）；A-009（宣称-实现脱钩）→ 不变量⑤（对账断言全量收口）。

## 1. 动工前调研（atomcode，一轮一问已执行）

问题：组合根（DI 装配点）防回潮守卫三方式对比 + 合法容器外手写构造（视图缝/DI 环）的工业处置。9 次搜索、4 角度（Official/Comparative/Criticism/Community）、6 篇全文；来源含 Seemann 组合根原文、NetArchTest 仓库、SEMastery/Marek 对比、arXiv 2408.13855、Microsoft DI 指南。

| 调研结论 | 置信度 | 本票落位 |
|---|---|---|
| 枚举字符串黑名单：漏报是系统性缺陷（词汇绕过/开放空间不可穷举/膨胀即死亡） | 高 | 否决「黑名单补三条」方案——本票三处点名 new 恰是合法视图缝，补入即把合法构造打红 |
| 封闭白名单结构性断言：翻转判定方向（只允许已登记位置），防漏报最强形式；白名单成受管资产，每次改动=显式架构评审 | 高 | **采纳**：观测集合 == 登记集合 双向钉死；类型宇宙由 Services/ 目录推导而非手工清单 |
| 架构测试框架（NetArchTest/ArchUnitNET）：白名单方式的语言化；原版 2023 停更；谓词选错会空转 | 高 | 否决——引入新依赖且超「点状」范围（D-003.4 禁全局整改） |
| 合法容器外构造：工业界第一优先是 Abstract Factory/ViewFactory 收编；无法收编时封闭白名单显式豁免登记（arXiv 实证：豁免未及时登记本身占误报成因 10.9%） | 高 | 视图缝显式登记在案（每缝附 DI 环理由注释）；「工厂收编重构」登记为张力→后续票候选 |
| 三种方式非三选一，工业实践分层叠加 | 中-高 | 存量黑名单断言保留（D-003.1）+ 白名单结构性断言叠加 |

## 2. 实现落位

| # | 文件 | 变更 |
|---|---|---|
| 1 | `tests/PhotoPrivacy.IntegrationTests/Ui/CompositionRootSourceTests.cs` | 新增结构性断言 `Out_Of_Composition_Service_News_Should_Match_Declared_View_Seams` + 负向自证 `View_Seam_Guard_Negative_Self_Proof`（7 Facts）；存量 5 Facts 零删改 |
| 2 | `src/PhotoPrivacy.Worker/PosixSignalHooks.cs` | `onReload: Action` → `Func<Task>`；SIGHUP 回调内 `Task.Run` 编组 fire-and-forget（信号线程立即返回） |
| 3 | `src/PhotoPrivacy.Worker/Program.cs` | :139 POSIX onReload 撤 `GetAwaiter().GetResult()` → async lambda + try/catch→`Log.Error` |
| 4 | `tests/PhotoPrivacy.IntegrationTests/WorkerIpc/WorkerIpcSyncOverAsyncGuardTests.cs` | GuardedFiles 扩展纳入 `Program.cs`+`PosixSignalHooks.cs`；抽 `ClassifySyncOverAsync` + 新增自证 Fact（4 Facts）；文档注释更新（「范围外」登记项转正） |
| 5 | `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj` | 移除 SelfContained/PublishSingleFile/IncludeNativeLibrariesForSelfExtract/PublishTrimmed（ADR 0035 收口）；保留 `DebugType=embedded`（非 ADR 0035 点名的发布属性，构建期调试信息设置，边界裁定见 §9） |
| 6 | `scripts/publish-app.ps1` + `scripts/publish.sh` | UI publish 调用补齐 `/p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false`（与 Worker 调用对齐；原由 csproj 持有属实际承重——脚本不传三枚标志） |
| 7 | `tests/PhotoPrivacy.IntegrationTests/PublishPropertiesCsprojGuardTests.cs` | 新建守卫（3 Facts）：生产 csproj 禁持发布属性 + 双脚本两条 publish 调用带齐标志 + 负向自证 |
| 8 | `docs/adr/0068-backup-mirror-layout.md` | 新立：票 03 备份布局决策补记（票 03 复核移交项「收口时立备份布局 ADR」兑现） |
| 9 | `docs/adr/0006-backup-retention-size-limit.md` | 顶部加修订登记行（指向 0068；MaxSizeMb 保留生效） |

## 3. 验收对照表

| 验收项 | 证据 | 结论 |
|---|---|---|
| CompositionRoot 守卫捕获三处手写 new（或等价结构性断言） | 结构性断言观测集 == 登记集：MainWindow 观测到 6 缝（含点名三处 ServiceModeController/AuditTailService/ConfigFileWatcher + MainWindowUiHost/MainWindowViewModelView/TrayHost）、Program.cs 观测到启动缝 UiSingleInstance、App.axaml.cs 空集——任一未登记服务层 new 即红 | ✅ |
| 存量 source-lint 守卫零删缩 | 对照基线 6bf244e：守卫文件 35→39（+4 新增），逐文件断言计数零缩水（无 deleted、无 shrunk） | ✅ |
| 失效即红预演留证 | `reports/06-evidence-red-forecast.log`：四守卫基线全绿、7 组定向篡改全红、边界用例（非服务层 new DirectoryInfo）不误伤；分类器首轮曾以 throw 字符串误报自证守卫非空转（已修为行首调用判定） | ✅ |
| A-001~A-009 全结算 | §5 九条全部 implemented（实物复核逐条留证） | ✅ |
| 对账断言收口核验 | allowed 34 == mapped 34、双向差集为空、无重复、sample==Default、README 数字=34 同步钉（FormatCoverageReconciliationTests 5 Facts 在场）；AGENTS.md IPC 口径由 IpcAuthClaimReconciliationTests 钉在场 | ✅ |
| UI.csproj/ADR 0035 核对 | 矛盾确认属实且为承重漂移（UI publish 脚本不传三枚 /p: 标志）；已上提脚本+移除 csproj+新守卫钉死 | ✅ |
| 票 05 移交项呈报 | T1 已修复并入守卫面；T2/T3 呈报见 §9；flaky 测试呈报见 §9；串行纪律呈报见 §10 | ✅ |
| 波次表 + README 登记 + 双轨沉淀 | 轮 README W6 行已更新；本报告双轨 `docs/process/reports/06-report.md`；轮 README/spec 受控副本随票沉淀 | ✅ |
| 硬验收走 CI | 窗口零本地 dotnet build/test（CI-only 纪律）；提交已上 GitButler 独立分支，待大脑推 ci-verify 取证 | ⏳ 待首脑推送 |

## 4. 失效即红预演（静态复算口径）

方法：守卫分类器逐字镜像为 JS，对真实文件基线扫描→内存篡改→复扫→还原。结果（详见 `reports/06-evidence-red-forecast.log`）：

- **守卫1（视图缝白名单）**：基线 3 文件全绿；MUT 注入未登记 `new WorkerProcessManager(`→红；缝清单漏登 TrayHost→红；源删 ConfigFileWatcher 缝→红；Program 删 UiSingleInstance 缝→红；良性 `new DirectoryInfo(`→不误伤。
- **守卫2（sync-over-async 扩展面 5 文件）**：基线全绿；注入 `GetAwaiter().GetResult()`/`.Result`/`.Wait()` 各形态均红。
- **守卫3（csproj 发布属性）**：src 下 4 个 csproj 全绿；注入 `<PublishSingleFile>`→红。
- **守卫4（发布脚本标志）**：双脚本各 2 条调用全绿；抹 UI 调用 PublishSingleFile 标志→红。

另：测试内 `*_Negative_Self_Proof` Facts 使同等突变在 CI 内可执行（非仅一次性预演）。

## 5. 整轮审计（A-001~A-009 逐条实物复核，全部 implemented）

| A | 裁定 | 实物复核证据 |
|---|---|---|
| A-001 格式黑洞挂死 | implemented（票 01） | `ExifToolBridge.cs:133` 未知族早退→`WipeResult.UnknownFormat`；`FileTaskPipeline.cs:167` 落 `wipe_skipped_unknown` 审计；`ExifToolCommandBuilder.cs:44-66` skip 直接抛错不产 SKIP_ no-op 块；`ProtocolLevelFakeExifToolProcess` 协议级 fake 替换同义反复 |
| A-002 空配置径落 CWD | implemented（票 02） | `AppConfigLoader.cs:69-76` quarantine/audit 空串回落 `DefaultPaths`（对齐 Backup.Suffix 先例）；`AppConfigValidator.cs:53-62` 第二层拒空给可读错误；`AppConfigLoaderTests` 行为覆盖在场 |
| A-003 排除清单假接口 | implemented（票 02） | `RuleEngine.cs:83+` `MatchesPattern` 真通配 `*`/`?` 双指针；`AppConfigValidator.cs:114+` 不可能匹配模式逐条告警；`RuleEngineTests` 行为覆盖在场 |
| A-004 IPC 无认证 | implemented（票 04） | `NamedPipeIpcTransport.cs` `CurrentUserOnly`+仅首实例 `FirstPipeInstance`（:26/:38/:68-94）；`UnixDomainSocketIpcTransport.cs` 0600/0660+`SO_PEERCRED` 对端 uid 白名单（:75-133）；AGENTS.md §5 口径改写；`IpcAuthClaimReconciliationTests`/`IpcAuthenticationLayeringTests` 在场；systemd 0750+组供给部署配套在场 |
| A-005 性能三件 | implemented（票 05） | CI run 35061555907 绿 610/610；健康探针+_startLock+真 await+缓冲读落地；**T1 移交项本票已修**（见 §6.2） |
| A-006 备份静默互覆盖 | implemented（票 03） | `BackupPathResolver.cs` 镜像相对路径+`_unsorted` 短哈希兜底+`ResolveSidecarPath` 时间戳旁路+`ContentsEqual` 逐字节比对+`IsReparsePoint`/`IsSameOrUnderRoot` 防护；`FileTaskPipeline.WriteBackupAsync`(:316-388) 三分流全部落审计；`BackupMirrorLayoutTests` 在场；ADR 0068 已补立 |
| A-007 source-lint 占比 | implemented（票 06 核定） | 存量守卫零删缩（基线 35 文件全在场、逐文件断言零缩水、+4 新文件）；票 1–5 行为测试逐票在场；占比议题按 D-003 裁定不在本轮范围 |
| A-008 守卫枚举漏报 | implemented（票 06） | 结构性断言替代枚举盲区；点名三处与其余视图缝全部登记；双向钉死+负向自证 |
| A-009 宣称-实现脱钩 | implemented（票 01 落地 + 票 06 核验） | allowed 34 == mapped 34（双向差集空、无重复、sample==Default）；README=34 同步钉；`FormatCoverageReconciliationTests` 5 Facts 在场 |

## 6. 顺带承接

### 6.1 UI.csproj ↔ ADR 0035 核对（A-004 顺带发现项）

**矛盾确认属实**：ADR 0035 定稿口径「csproj 不持有 SelfContained/PublishSingleFile，发布属性由脚本 /p: 传递（single source of truth）」，而 `PhotoPrivacy.Ui.csproj` 直接持有四枚发布属性（SelfContained/PublishSingleFile/IncludeNativeLibrariesForSelfExtract/PublishTrimmed）。溯源：属性系 3d29089（2026-08-11 跨平台发布）引入，ADR 0035 的修订只清理了 Worker.csproj，UI 侧属残留漂移。

**关键细节——属性是承重的**：`publish-app.ps1:52-59` 与 `publish.sh:80-87` 的 UI publish 调用**不传**三枚 /p: 标志（Worker 调用传）——UI 的单文件发布实际由 csproj 属性承重。故「直接删属性」会改变发布形态，「只改 ADR 文档追认」则把漂移追认为设计（恰是本轮锐评批评的形态）。

**收口处置**：三枚标志上提至两脚本 UI publish 调用（与 Worker 调用同形），csproj 四属性移除——发布命令行等价、产出形态不变、`dotnet build`/`dotnet test` 不再吃 SelfContained 的 RID 输出路径污染；新增 `PublishPropertiesCsprojGuardTests` 双向钉死（csproj 禁持 + 双脚本双调用带齐）。`DebugType=embedded` 保留——ADR 0035 点名的是 /p: 四元组，DebugType 为构建期调试信息设置、脚本亦未传参，保留与否属边界裁定（§9 呈报）。

### 6.2 T1 承接（票 05 移交）：Worker Program.cs:139 POSIX onReload sync-over-async

已修复：`PosixSignalHooks.Register` 的 `onReload` 形参 `Action`→`Func<Task>`，SIGHUP 注册点改为 `Task.Run` 编组（回调线程立即返回，不再阻塞数秒）；调用侧 async lambda 内 try/catch→`Log.Error`（异常即时记录，不等 `UnobservedTaskException` 的 GC 时机）。重入串行化由 `MetadataCleanerWorker._reloadGate`（SemaphoreSlim(1,1)，MetadataCleanerWorker.cs:29/426-443）兜底。`WorkerIpcSyncOverAsyncGuardTests` 守卫面扩展纳入 `Program.cs`+`PosixSignalHooks.cs`，文档注释「范围外登记」转正。

### 6.3 其余移交项呈报（不在本票动，§9 张力节登记）

- T2（ADR 0057 缓冲读后重估条件已达）+ T3（旧测量基线存疑）：呈报大脑裁定是否开重估票。
- `ConnectionStateServiceTests` flaky（票 03 登记项）：本票未动——修测试竞态需选方案（轮询断言 vs 订阅先行），建议立微票；见 §9。

## 7. 离轨/复算（对原始代码复核指控）

- **A-008 指控复核**：基线 `CompositionRootSourceTests`（票 29 版）黑名单仅 4 枚 token（`new ServiceManagerOps(`/`new WorkerProcessManager(`/`new WorkerIpcClient(`/`CreateServiceModeController`），而 `MainWindow.axaml.cs` 实际手写 `new` 服务层类型 6 处——**指控属实**，枚举式黑名单对未枚举项结构性失明。
- **离轨自查**：本票全部变更落在票面范围内；T1 承接与 UI.csproj 核对均为票文明示移交项；ADR 0068/0006 修订登记为票 03 复核移交项兑现。无静默扩张。

## 8. 不动项核验

- 存量 source-lint 守卫：基线 6bf244e 的 35 个守卫文件全在场、逐文件 `Assert.` 计数零缩水（机器比对，见 §5 A-007）。
- 行为测试在场逐票核验：票 01 `UnsupportedFormatPipelineTests`+协议级 fake；票 02 `AppConfigLoaderTests`/`AppConfigValidatorTests`+真通配；票 03 `BackupMirrorLayoutTests`；票 04 `IpcAuthenticationLayeringTests`+冒烟留证；票 05 `WorkerIpcResidentStabilityTests`+`IpcLineReaderTests`。
- `AGENTS.md` 未动（§5 IPC 口径已为 ADR 0067 现行文，IpcAuthClaimReconciliationTests 钉住）；`README.md` 未动（34 数字本已正确）；`CONTEXT.md` 未动。
- `src/PhotoPrivacy.Ui/Program.cs` 未动（`new UiSingleInstance` 为合法启动缝，登记而非迁移）。

## 9. 张力与呈报（待首脑/大脑裁定）

1. **视图缝收编重构（工业界第一优先解）**：atomcode 调研指出合法容器外构造的首选处置是 Abstract Factory/ViewFactory 收编（工厂内部 new 合法且被接口隔离），白名单豁免是第三优先。本票按点状范围选了显式豁免登记；若要把 ServiceModeController/AuditTailService/ConfigFileWatcher/TrayHost 收编为工厂注入，需运行时构造链重构——建议立后续票，不宜并入收口票。
2. **T2/T3**：ADR 0057 重估条件（缓冲读落地后）与旧测量基线存疑——是否开重估票由大脑裁定。
3. **ConnectionStateServiceTests flaky**：三测同构竞态（票 03 登记），建议微票（改轮询断言或订阅先行）。
4. **DebugType=embedded 边界裁定**：UI.csproj 保留该属性（不在 ADR 0035 点名的 /p: 四元组内，脚本亦未传参）；若首脑认定「发布属性」应全覆盖含 DebugType，则补删+脚本补 /p:DebugType=embedded——当前裁定=保留并呈报。
5. **UI Program.cs:108 `showPipeTask.GetAwaiter().GetResult()`**：主入口关停路径的同步等待（非 UI 线程运行期、带 CTS 取消），本轮未动，登记观察项。
6. **轮收口 ADR**：仓库惯例有轮收口总结 ADR（0056/0058-0060/0063/0064/0066），但合并拓扑未定（未 push/未合并）——建议由大脑在合并后立，本票不预写。
7. **栈拓扑残态**：票 02/03 与 01/04/05 双栈并行（历史既成）；本票分支串在 05 之上。如需六票线性化需另行栈手术（先快照）。

## 10. 过程违规呈报（不追认不美化）

- **本窗口**：无违规——一票一分支（`06-closeout-guards-reconciliation`）、中文带票号 commit、未 push、未建 PR、动栈零次（无需快照级操作；提交前已做 §4.4 快照保险）。
- **历史呈报（票 03/04/05 轮内已记）**：多票并行锚定偏离「一票一票串行 frontier」既定序；票 05 报告 §10 详记栈线性化手术经过。本票如实转呈，不追认为正常。
- **共享文件说明**：`WorkerIpcSyncOverAsyncGuardTests.cs` 属票 05 交付物，本票因 T1 承接扩展之——票 05 已 ACCEPT 无并发窗口冲突，§4.3 纪律下呈报备案。

## 11. 提交与 CI

- 分支：`06-closeout-guards-reconciliation`（GitButler 独立分支，串于 05 之上）。
- 提交：中文带票号单提交（见 git log）。
- **CI 终判待大脑推送**：本窗口不 push（§4.2）。推送 `ci-verify/correctness-round-t06-union`（或大脑选定名）后预期新 Facts +6（CompositionRoot 2 + SyncOverAsync 1 + PublishProps 3）与既有 610 全绿；若红则以 CI 日志为准诊断，窗口不以本地复跑替代。
- CI filter 口径：`Category!=Smoke&Category!=ExifTool`——本票新增守卫均非 Smoke/ExifTool 类，入 CI 覆盖。

## 12. 验证记录

| 项 | 证据 |
|---|---|
| 静态失效即红预演 | `reports/06-evidence-red-forecast.log`（四守卫基线绿/篡改红/还原绿全记录） |
| 安全扫描 | `semgrep --config p/csharp --config p/security-audit` 对改动 src 文件：**0 findings / 0 errors**（`.codex-tmp/06-semgrep.json`，副本 `docs/process/reports/06-semgrep.json`） |
| 实物复核 | §5 逐条 file:line；allowed/mapped 集合双向差集=0 实物重算 |
| 本机构建/测试 | **零次**（CI-only 纪律）；C# 语法经逐行复核（含 lambda `_` 参数遮蔽修正、xunit 2.9.3 无 `Assert.Fail` 规避） |

## 13. 收尾声明

票 06 窗口交付完毕：A-007/A-008/A-009 收口、A 系九条全部 implemented、T1 承接修复、UI.csproj/ADR 0035 矛盾收口、双轨沉淀随票。**停住等首脑复核**——不自动推进下一票，不 push，不建 PR。CI 终判待大脑推送后取证。
