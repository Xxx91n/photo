# 报告 — 票 31 轮询下沉 IHostedService（架构恢复第七轮）

- 日期：2026-09-05　窗口：票 31 工作窗　分支：GitButler 虚拟分支（WORKFLOW §4.2，commit 以 git log 实物为准）　未 push 不 PR。
- 启动器：prompts/31-polling-hosted-service.md；必读清单已逐份读全（启动器 / handoffs/31 / issues/31 / spec.md 研究输入 Q1 结论 4 + Testing Decisions + Out of Scope / WORKFLOW §4.2 §4.4 / ADR 0025 心跳模式全文 / ADR 0039·0053 相关段索引）。
- Blocked by 现状：票 30 已收口（HEAD a10f2b6，MainWindow.axaml.cs blob 1158 行实测），阻塞解除，issue 31 ready-for-agent。
- 动栈前快照：本票动栈操作仅 additivity（but branch new 叠栈 + but commit），无历史改写/丢弃；提交前保险快照 photo-snapshots/20260906-011402（93 文件 / 416576 字节 + manifest SHA256），并补 .gitignore 忽略 photo-snapshots/（zz 池防刷屏，随票附带提交 lvy）。
- 提交实物（git log 为准）：票 31 主提交 change ID `ttw`（分支 arc-recovery/31-polling-hosted-service，叠于 arc-recovery/30-settings-vm-sync 之上——依赖 29/30 的组合根/镜像基座，GitButler 依赖冲突报错后按技能恢复流程 --anchor 建栈）；附带提交 `lvy`（.gitignore）。

## 1. 声明 → 证据 → 结论

| # | 声明（issue checkbox） | 证据 | 结论 |
|---|---|---|---|
| 1 | 版本轮询与服务状态轮询脱离 MainWindow code-behind，下沉宿主服务并保持退避/取消语义 | 新建 src/PhotoPrivacy.Ui/Services/WindowPollingHostedService.cs（175 行，sealed : IHostedService）：PollVersionAsync（1s Task.Delay + ExifToolVersionSnapshot.TryReadChangedAsync + Dispatcher.UIThread.Post，循环体逐字保持）、Activate()（窗口就绪后拉起两循环）、StopAsync（版本→服务模式顺序 CancelAsync → Dispose → null，await 容忍 OperationCanceledException）。MainWindow.axaml.cs 删五字段（_versionSnapshot/_versionPollCts/_versionPollTask/_serviceModePollCts/_serviceModePollTask）+ PollVersionAsync + ApplyExifToolVersionFromIpcAsync 两方法 + OnClosed 约 46 行轮询释放段，OnClosed 改一行 await _pollingHostedService.StopAsync(CancellationToken.None) | ✅ |
| 2 | 行为不变（循环体/周期/取消/投递语义逐字保持） | 服务状态轮询循环体 ServiceModeController.PollServiceModeTransitionAsync（issue 06 既有）零改动，宿主服务只接管生命周期；一次性 IPC 版本读取 ApplyExifToolVersionFromIpcAsync 保持在 AuditTail.Start() 后原时序位（262 行）；版本轮询拉起从 tray 前移至 tray/可见性策略后（296 行 Activate），两循环均先 Task.Delay(1s/3s) 再轮询，拉起晚数十毫秒无行为差异（报告如实记录）| ✅（语义级等价） |
| 3 | 检查点 B：MainWindow code-behind 行数继续下降（git blob 实物为准） | git blob 实测：HEAD a10f2b6 = 1158 行 → 工作区 1057 行，**-101 行（-8.7%）**；git diff --stat：3 文件 34 insertions / 123 deletions（净 -89，新文件两枚未计入 stat）| ✅ |
| 4 | CI 验证分支守卫测试全绿 | 本机 CI-only 政策零 build/test 运行。静态门禁（与守卫同构口径）全绿：6 文件字符串感知括号平衡 0 偏差、全 LF 无 CRLF、BOM 仅 MainWindow.axaml.cs 既有基线（git blob 比对一致，新文件均无 BOM）、git diff --check 干净、被删符号全仓 grep 零残留引用、全部守卫断言 node 沙箱逐字预演 ALL-PASS（含 ReadStripped 剥注释口径）。**CI 云端背书由大脑推送验证分支确认** | ✅（静态口径；云端待大脑） |
| 5 | 报告双轨沉淀 | 本文件 + docs/process/reports/31-polling-hosted-service.md 副本随 commit | ✅ |

## 2. 关键改动清单（5 文件：3 改 + 2 新增）

| 文件 | 变更 |
|---|---|
| src/PhotoPrivacy.Ui/Services/WindowPollingHostedService.cs（新，175 行） | IHostedService 宿主服务：生产构造（VM 单例 + 惰性版本源工厂 Func<Func<CancellationToken, Task<string>>>，读 App.RuntimeOptions.GetExifToolVersionAsync 当前值——与原 InitializeRuntime 建快照时序语义一致）+ internal 测试构造（直注委托）+ AttachServiceModePoll(Func<CancellationToken, Task>) 委托挂载 + Activate/ApplyExifToolVersionFromIpcAsync/PollVersionAsync/StartAsync(CompletedTask)/StopAsync。StartAsync 不拉起——轮询依赖窗口就绪，InitializeRuntime 显式 Activate（与原 Task.Run 拉起时序一致，类注释载明） |
| src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs（1158→1057） | 8 处手术：构造第 5 参 pollingHostedService + 字段 + AttachServiceModePoll 挂载（new ServiceModeController 后一行委托）；InitializeRuntime 快照构建段删（迁 Activate）、onExifToolExePathDetected 与 AuditTail 后一次性版本读取改经宿主服务、tray/可见性策略后 _pollingHostedService.Activate()；OnClosed 约 46 行释放段改一行 StopAsync；文件尾两方法删留考古注释 |
| src/PhotoPrivacy.Ui/Composition/AppComposition.cs（+8 行） | AddSingleton(sp => new WindowPollingHostedService(sp.GetRequiredService<MainWindowViewModel>(), () => App.RuntimeOptions.GetExifToolVersionAsync))——只依赖 VM 单例，无环 |
| tests/PhotoPrivacy.IntegrationTests/Ui/WindowPollingHostedServiceSourceTests.cs（新，6 例） | T1 不回潮（5 字段名 + PollVersionAsync + new ExifToolVersionSnapshot( + PollServiceModeTransitionAsync( 剥注释后零出现）；T2 接线锁（构造参 + Attach + Activate + Apply + await StopAsync 五锚点）；T3 语义锁（IHostedService/1s Delay/TryReadChangedAsync/Post/Activate/Attach）；T4 释放锁（CancelAsync/.Dispose()/OperationCanceledException）；T5 破环锁（三形态 Controller 依赖禁 + 静态 NormalizeExifToolStatus 合法）；T6 行数锁 ≤1100（基线 1158 放宽 5%，原始全文口径） |
| tests/PhotoPrivacy.IntegrationTests/Ui/CompositionRootSourceTests.cs（+3 断言） | 单例注册清单加 WindowPollingHostedService( 工厂形；补 new WindowPollingHostedService( 与 () => App.RuntimeOptions.GetExifToolVersionAsync 两锚点（工厂形 + 惰性版本源） |

### 装配破环（本票关键裁决）

MS DI 死环：MainWindow 构造注入宿主服务 → 宿主服务需 ServiceModeController → Controller 构造需 MainWindowUiHost(this)/MainWindowViewModelView(this)（MainWindow 实例）→ 环。裁决：宿主服务不依赖 Controller，服务模式轮询经 **AttachServiceModePoll 委托**由 MainWindow 构造内挂载（new 完 Controller 后一行 _pollingHostedService.AttachServiceModePoll(_serviceModeController.PollServiceModeTransitionAsync)）。宿主服务只剩 VM 单例 + 惰性版本源两依赖，容器可直线解析；Controller 保持窗口手写 new（票 29 现状，spec Out of Scope：全量 DI 迁移后续票）。NormalizeExifToolStatus 为 Controller 静态方法，宿主服务合法直引（T5 守卫同时锁死三形态实例依赖禁令 + 静态调用白名单）。

## 3. 守卫口径与票 30-fix 教训延续

- 全部 .cs 断言用 SourceLint.ReadStripped（剥 // 行注释）——考古注释「票 31:XXX已迁移」不再击穿断言（run 33964621208 红灯根因预防）。
- 行数锁 T6 用原始全文口径（.Replace("\r\n","\n").Split('\n')，与 SettingsVmSyncSourceTests 票 30 写法同构）。
- AXAML 断言零新增（本票不碰 axaml）。

## 4. CI-only 边界与验证声明

- 本机零 build/test/lint 运行（CI-only 政策）；第 4 项声明的「全绿」为静态复核口径（守卫断言 node 沙箱逐字预演、括号平衡/LF/BOM/残留 grep 程序化比对），不替代 CI 云端证据。
- 需大脑推送验证分支触发 workflow：预期 Core 183 + Integration 279（273 既有 + 新 6 例）全绿。
- 若 CI 红，预期红点集中在：(a) Microsoft.Extensions.Hosting 命名空间解析（IHostedService 经 Core 项目 Microsoft.Extensions.Hosting.Abstractions 10.0.6 传递引用，Ui.csproj 无需新增包——已核对包引用链）；(b) 新守卫字符串与实际源口径差（已落盘后逐字预演核对）；(c) Dispatcher/UIThread 相关静态编译警告。返修启动器交大脑按 .scratch 流程处理。

## 5. 与并行窗口的隔离（WORKFLOW §4.3）

- 本票触碰面：MainWindow.axaml.cs / AppComposition.cs / WindowPollingHostedService.cs（新）/ 两个测试文件——均在票 31 票面定义的轮询下沉链内；ServiceModeController.cs / ExifToolVersionSnapshot.cs 零改动（循环体与变化检测权威不动）。
- issue 31 Blocked by 30（共享 MainWindow code-behind）已解除：票 30 收口 commit a10f2b6 已在栈上，本票 diff 基于其工作区。
