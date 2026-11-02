# 新 UI 手工冒烟

> 定位：新 UI 尚未进入发布链，也没有 headless UI 测试；本文件记录可重复的手工冒烟步骤与最近一次执行结果。
> 视觉验收按 #49 seam 4：锚点 + 冒烟，不建 CI 像素基线。

## 规则页（#56）

1. 每次从全新的私有 `config` 目录开始（删除 `rules.json` 或先点 `Reset`），否则上一次保存的值会让开关起始状态相反；
   启动本票构建的 `photoprivacy-ui`，点击导航 `Rules`。
2. 检查：五族 × 六开关全部可见；PDF/EPS 行显示 `Caution`；专家开关只切换提示条，六开关本身始终可点。
3. 点 JPEG 的 `Strip all`（默认 true → false）与 `EXIF`（默认 false → true），点 `Save`。
4. 期望 `<exe dir>/config/rules.json` 含 30 个 `{family}_{group}` 布尔键：
   `jpeg_strip_all=false`、`jpeg_strip_exif=true`，其余族保持编码默认值。
5. 关闭并重启 UI，再进 `Rules`：JPEG 两个开关保持第 3 步的值，且无加载告警、无“Unsaved changes”。
6. 旧 UI 互读：CI 的 `FormatRulesStoreTests.Load_Reads_Rules_Written_By_The_New_Ui`
   用同一份 golden fixture 走冻结的 `FormatRulesStore.Load()`。

最近一次执行（2026-10-27，#56 实现窗口）：

- 二进制：本票 `cargo build -p photoprivacy-ui` 产物复制到私有目录后运行
  （并行 worktree 共用 `ui/target`，直接跑共享产物会被其他窗口的构建覆盖）。
- 步骤 4：rules.json 30 键与预期一致；步骤 5：重启后界面回读一致。
- 限制：RDP/GPU 会话下 `PrintWindow` 对 flip-model 窗口只返回黑帧；本冒烟以真实点击 + 文件断言为准，
  截图只作辅助，不入库。
  （勘误，2026-10-31 #125：黑帧只限按窗口句柄取图的 `PrintWindow`；整屏 `CopyFromScreen` 能拿到 gpui 窗口真实像素，
  本条「截图只作辅助，不入库」的前提因此不再成立，取证通道见下文「K 组取证指引」。本条只作历史记录。）

## 切换日冒烟清单（#61 定形）

> 旧栈 report-32 清单（6 面 28 项 + G 组 6 项，已随治理清理出仓、git 历史可查）与新栈的对应关系；
> 服务模式 8 项原明细已不可考（backlog 指针 B13 无清单），本节按 #58 服务页能力重新定形。
> 回填口径沿用 report-32：**通过 / 失败 / 未探活 / 不迁移（裁决）**，无证据不虚构。

### 映射与处置总则

- 主题五色板、中键滚动、侧栏拖宽、nav hover 三根因（旧 C/D/G 组）是旧栈观感专项，
  #49 D11 已裁决不迁移（主题降级 light/dark 双态）；新 UI 视觉按 #49 T4 六锚验收，本清单不再承担。
- 原 28 项编号保留，逐项给新栈对应物或处置。

### A 主题（light/dark/system，替代旧五色板）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| A1 | 配置页主题平铺选项组（system/light/dark）三态可见 | 通过 | 2026-10-29 实机目检：设置页平铺选项组中 system/light/dark 三态可见 |
| A2 | light↔dark 切换即时生效，无需重启 | 通过 | 2026-10-29 实机目检：light↔dark 切换即时生效无需重启（注：右上角与设置页两处均可调整，设计重叠冗余） |
| A3 | 明暗与语言双轴独立：切主题不改语言、切语言不改主题 | 通过 | 2026-10-29 实机目检：切主题不改语言、切语言不改主题，双轴独立 |
| A4 | 切 dark → 重启后保持（config.json `ui.theme_variant` 持久化） | 通过 | 2026-10-29 实机目检：切 dark 后退出重启，界面保持 dark 配色与配置持久化 |
| A5 | 运行中手改 config.json 主题 → 约 1s 内热重载生效 | 通过 | 2026-10-29 实机目检：运行中手动修改 config.json 主题为 light 保存后约 1s 内自动热重载变色生效 |

### B 语言（10 locale 复用）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| B1 | 语言平铺选项组逐一切换 10 locale，导航/标题/行标签/按钮即时翻译 | 通过 | 2026-10-29 实机目检：语言平铺选项组逐一切换 10 locale，导航/标题/行标签/按钮即时翻译 |
| B2 | （并入 B1）页面文案跟随当前 locale | 通过（并入 B1） | 随 B1 验证通过 |
| B3 | 选 العربية 布局翻转为 RTL（`ui/crates/photoprivacy-ui` 方向随 `ui.locale` 驱动） | 通过 | 2026-10-29 实机目检：选 العربية 布局翻转为 RTL 镜像排布 |
| B4 | 切 English → 重启后保持（`ui.locale` 持久化） | 通过 | 2026-10-29 实机目检：切 English 后退出重启，界面保持英文 |
| B5 | 平铺选项文案（主题/日志级别）随当前语言刷新 | 通过 | 2026-10-29 实机目检：平铺选项文案随当前语言即时刷新翻译 |

### C 中键滚动（5 项）——不迁移

- **处置（#49 D11）**：旧栈观感专项，新栈裁决不迁移，五项一并核销为「不迁移」。新 UI 长内容滚动走滚轮/滚动条。

### D 侧栏拖宽（3 项）——不迁移

- **处置（#49 D11）**：同 C 组，核销为「不迁移」。新 UI 侧栏宽度固定。

### E 日志流（新 UI 日志页，T8 审计尾巴双通道）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| E1 | 空日志时日志页显示空态占位 | 通过 | 2026-10-29 实机目检：空日志时日志页显示空态占位提示 |
| E2 | 向监控目录放入照片，Worker 处理后日志页即时出新条目（无需手动刷新） | 通过 | 后端链路通过（放入照片由 stay_open ExifTool 清理并产生审计日志）；实机配置有效 hot_folder 后日志即时刷新 |
| E3 | 不同事件类型的呈现可区分（级别/事件类型） | 通过 | 2026-10-29 实机目检：不同事件类型的呈现可区分（级别与事件类型正常渲染） |
| E4 | 清空后列表立即空；重启后旧日志不回灌 | 通过（探针级） | 2026-10-29 实机目检：点击清空后列表立即变空，但重启之后旧日志又重新出现（核查为旧时间的日志，属于新旧 UI 重构前就存在的既有 BUG；复现步骤：放入图片产生日志 → 点击清空 → 关闭并重启 UI → 旧日志回灌）。复测（2026-11-01 #139）：随 #102 B′ 方案（清空写水位、重启按行内 UTC 时刻过滤）修复后做探针复现——**取证通道是 Rust 行为测试，非实机 UI 目检**：真实 `AuditFeed` 双通道 + `ClearWatermark` 落盘，同进程清空 → 重启读回水位 → Worker 末 50 行窗口的已清历史不回灌、清空后新行照常交付；含跨日、归档改名、#129 分叉时窗几何与 IPC 零协议守卫（`e4_probe_…`/`fork_window_…`/`method_surface_…` 在 CI 各自 ok）。CI run [41293932607](https://github.com/Xxx91n/photo/actions/runs/41293932607)（head `50d4fcdd`，PR #143）rust/test/ui-check/markers 四 job 全绿、全 crate 0 failed；实机 UI 全链路（点清空→重启 UI）未另目检，如需归 #29 复测 |
| E5 | 日志级别平铺选项组切换生效（高档位下低级别事件不再出现） | 通过 | 2026-10-29 实机目检：平铺日志级别切换生效，高档位下低级别事件被过滤隐藏 |

### F 按钮与窗口（新栈锚点口径）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| F1 | ghost 按钮 hover 有反馈（新栈 theme.rs token，按 #49 T4 六锚目检）；primary/danger 实心按钮按设计不改 hover 底色 | 通过 | 2026-10-29 实机目检：ghost 按钮（如导航栏、次要按钮）鼠标 hover 有清晰反馈；实心按钮保持底色 |
| F2 | 按下态有视觉反馈，松开恢复 | 通过 | 2026-10-29 实机目检：鼠标按住按钮有按下视觉反馈，松开恢复 |
| F3 | 不可用操作呈禁用态且点击无响应（如服务未安装时的启动/停止） | 通过 | 2026-10-29 实机目检：不可用操作呈禁用态（如服务未安装时启动/停止为灰色）且点击无响应 |
| F4 | 键盘 Tab 焦点有可见指示 | 失败 | 2026-10-29 实机目检：按键盘 Tab 键无反应，无任何焦点轮廓指示，不通过（复现步骤：启动 UI 后按 Tab 键尝试在各输入框与按钮间跳转，焦点不移动亦无指示框） |
| F5 | 标题栏拖动/双击最大化/三键功能正确；关窗为驻守（见 T3） | 通过 | 2026-10-29 实机目检：标题栏拖动、双击最大化与还原、右上角三键功能正常（关窗为驻守） |

### G nav hover（旧 6 项）——随旧栈退役

- **处置**：旧栈三根因专项；新 UI nav 反馈按 #49 T4 六锚在 F1 一并覆盖。

### 服务模式 8 项（#58 服务页口径重新定形）

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| S1 | 未安装态：服务页显示未安装，启动/停止禁用 | 通过 | 2026-11-03 桌面截图目检（产物见下文 S 组产物记录）：服务页「服务状态：未安装」+「每 3 秒刷新」；「启动 / 停止服务」行的启动、停止两钮文字置灰 (154,154,148)，对照「安装」钮蓝 (47,188,251)、「刷新」钮深色 (26,26,25) 可点——与 `button_state` 未安装态 start/stop=false 一致（`ui/crates/photoprivacy-service/src/state.rs`）。帧 `S1-service-page.png`；同帧服务未安装时总览连接态为「已连接 · PhotoPrivacyCleaner.Background · 后台模式」（连接编排按 #70 现状回落后台端点，`S1-launch.png`），服务页仍如实显示未安装 |
| S2 | 安装服务（提权 UAC/pkexec/osascript 流） | 未回填 | 不在 #88 AC 内；本轮仅作为 S3/S4/S8 的前置步骤用 `scripts/install-service.ps1`（agent 以 High IL 管理员运行，无 UAC 交互）执行，安装即登记 #70 的 `ServicePipeInstallerSid` |
| S3 | 启动服务 → 状态变运行中 | 通过 | 2026-11-03 桌面截图目检 + `sc query`：前态帧 `S3-before-stopped.png` 服务状态「已停止」，启动钮深色可点、停止/安装置灰、卸载红钮可点（与 `button_state` 一致）；点 UI「启动」后帧 `S3-after-running.png` 状态变「运行中」并出绿字反馈「Operation succeeded」，同时刻 `sc query PhotoPrivacyCleaner` STATE 4 RUNNING，服务 Worker 进程在 Services 会话（SYSTEM 上下文）存活；ProgramData 服务日志 04:09:55 Application started 后无 FTL/再停 |
| S4 | 服务运行中 UI 经服务端点连接（服务页/日志页数据来自服务 Worker） | 通过 | 2026-11-03 桌面截图目检：服务运行中重启 UI，总览连接态「已连接 · PhotoPrivacyCleaner.Service · 服务模式」（帧 `S4-overview-service-mode.png`），Worker 卡片 Worker 模式=服务模式、上报模式=service、服务 IPC=可达、协议=v1、ExifTool=13.60、往返次数=14（单帧读数）；进程侧仅一个 Worker（pid 8996）且在 Services 会话，UI 未拉起后台 Worker——UI 连的是服务端点（#70 修复后服务管道 DACL 生效） |
| S5 | 服务模式下改配置保存 → ReloadConfig 热生效 | 通过 | 2026-11-03 桌面截图 + 服务管道探针（产物见下文「S5 复测产物记录（#85 窗口）」）：服务运行中 UI 走服务端点（帧 `S5-00-overview-before.png`：「已连接 · PhotoPrivacyCleaner.Service · 服务模式」，进程侧仅一个 Worker 在 Services 会话）；配置页把「监控目录」改为 `…\photo-smoke-85\hot-s5` 后界面绿字「已保存 · Worker 已重载」（帧 `S5-08-e2e-timing.png`），端到端 **0.52 s 配置落盘 / 0.64 s 热生效**（读侧预算 10 s，`S5-08-e2e-timing.txt`）；投放 `sample.jpg` 后由服务 Worker 就地清洗 3,648→626 B，日志页出 `Cleaned attempt=1;wipe_result=Cleaned_Modified` + `backup_sidecar_version…`（帧 `S5-03-logs-after-reload.png`），重开配置页显示持久化值（`S5-05-config-persisted.png`）；不经 UI 的独立探针经 `\\.\pipe\PhotoPrivacyCleaner.Service` 两次 `ReloadConfig` 各回 `ok:true`/`mode=service`，往返 0.44–0.62 s（锚定那轮 0.59 / 0.55 s，`S5-06-service-pipe-probe.txt`） |
| S6 | 停止服务 → 状态回落 | 未回填 | 未探活（#88 范围外） |
| S7 | 卸载服务 → 回到未安装态 | 未回填 | 未探活（#88 范围外）；本轮收尾用 `install-service.ps1 -Action uninstall` 卸载并复核 SC 1060，但那是脚本通道，不是 S7 要验的 UI 提权链路 |
| S8 | 服务模式日志通道在日志页可见（与后台模式区分） | 通过 | 2026-11-03 桌面截图 + 文件系统取证（帧 `S8-logs-page-service.png`）：同一服务模式会话切日志页，04:09:55「Service started mode=live」、04:09:56「ExifTool started」在列；04:13:08–09 投入 `hot/` 的带 Artist/Copyright 样本被服务 Worker 就地清洗 3648→626 B，日志页实时出「Cleaned attempt=1/wipe_result=Cleaned, Modified」×2 +「backup_sidecar_version… content_diverged_sidecar_written」，exiftool 复查标签已空；条目与 `pkg/hot/_audit/audit-2026-11-03.jsonl`、`processed.ndjson`（两文件 mtime 均落在投放后的 04:13）及 ProgramData 服务日志同刻。与后台模式的区分锚在连接态：本会话状态卡是「PhotoPrivacyCleaner.Service · 服务模式」+ Worker 卡片「服务 IPC=可达」（K1 记录的后台形态是 Background · 不可达） |

### #49 D11 功能对等核销（切换判据）

| 判据 | 结论 | 证据/出处 |
|---|---|---|
| 四页（Config/Logs/Rules/Service） | 对齐 | #55→PR #77、#57→PR #75、#56→PR #78、#58→PR #74（各票实机冒烟留证在对应 PR 正文）；实机复核项归上文 A/B/E/F/S 各表 |
| 托盘 | 对齐（显示/退出 + 安装态门控） | #59→PR #76、#58 门控→PR #81（`tray.rs` 的 `should_show_tray_icon` / `should_start_hidden`：仅后台模式且服务未安装时显示托盘，服务模式不隐藏窗口）；**Pause/Resume 未迁移**，处置见下节第 1 条 |
| 单实例 | 对齐 | `photoprivacy-ipc/src/single_instance.rs`（`Global\PhotoPrivacyUi_Instance` 互斥 + ShowWindow 管道）；T5 进程级通过；cargo test 守护 |
| IPC 全方法 | 对齐（协议面） | `protocol.rs` 冻结 method 名与字段语义（Design(#49)）；`client.rs` 覆盖 GetStatus/ReloadConfig/GetExifToolVersion/审计/规则读写；Worker 侧协议测试族未动；进程级证据：UI 拉起 Worker 并保持连接（见执行记录） |
| 心跳/轮询 | 对齐 | `monitor.rs`：GetStatus 1s 心跳、退避 1→2→…→60s 封顶（与旧 UI 对齐）、独立 1s 版本轮询、服务可达性 3s |
| 主题 light/dark | 对齐（降级裁决） | `theme.rs` + `ui.theme_variant` system/light/dark；五色板不迁移（D11 降级） |
| 10 locale | 对齐 | #60→PR #79；`i18n.rs` include_str! 复用 10 语言键值目录 |

### #59/PR #76 残余 6 项处置

1. **托盘 Pause/Resume 未迁移** → 处置：不迁移。#49 D11 对等口径只含托盘显示/退出；如需恢复 Worker 暂停/恢复控制，由用户裁定另立票。
2. **服务模式托盘门控（#58）** → **已闭环（代码）**：门控随 PR #81 合并（main `33734bd`），`should_show_tray_icon`/`should_start_hidden` 按安装态收敛并有单测；#58 票本身仍 OPEN 仅剩其服务模式实机冒烟 AC，与本清单 S1-S8 同源，待人工实机后一并关闭。
3. **窗口重建重置位置/尺寸** → 处置：接受。#59 已登记取舍（gpui 无窗口隐藏 API，关窗驻守走销毁+重建）；如需严格对等另立窗口状态持久化票。
4. **macOS/Linux 托盘未实机验证** → **未解决**：本轮仅 Windows 实机；Linux（ksni）/macOS（原生菜单）待对应平台实机冒烟。
5. **28+8 清单缺托盘/驻守项** → 已处置：本文件补 T1-T5 并回填实机记录。
6. **CodeRabbit Docstring Coverage 警告**（64.81% vs 90%）→ 不阻塞：本仓无该门禁，留存记录。

### 托盘/驻守/单实例补项（#59 残余处置，原清单缺项）

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| T1 | 托盘图标出现，菜单 Open Window 唤起主窗口 | 通过 | 2026-10-29 实机目检：右键托盘图标点击 Open Window 正常唤起主窗口 |
| T2 | 托盘 Exit 退出干净（进程结束，无 Worker 孤儿） | 通过 | 2026-10-29 实机目检：右键托盘图标点击 Exit 彻底退出，无残留 Worker 或 UI 进程 |
| T3 | 关闭主窗口进程驻守（销毁+重建；主题/连接状态/日志缓冲/服务快照保留，位置/尺寸回中——#59 已登记取舍） | 通过 | 2026-10-28 实机：`CloseMainWindow()` 后进程存活、无 PhotoPrivacy 主窗、Worker 保持 1 个；再启动第二实例（exit 0）主窗重建 |
| T4 | `ui.hide_main_window_on_startup=true` 时启动仅托盘不弹窗 | 通过 | 2026-10-28 实机：hide=true 启动后无 PhotoPrivacy 主窗（`MainWindowTitle` 仅托盘辅助窗）、进程与 Worker 正常 |
| T5 | 二次启动不重开：第二实例退出并唤起既有窗口（`Global\PhotoPrivacyUi_Instance`） | 通过 | 2026-10-28 实机 ×3：第二实例 6s 内退出（exit code 0），第一实例存活且主窗可见（title=PhotoPrivacy） |
| T6 | 首次配置待机态下停机收敛（托盘 Exit / 停服务时 `StopAsync` 不挂满 `ShutdownTimeout`，`#117`） | 未回填 | 待 #117 PR 合并后按「#117 实机停机证据」段补证（自动化侧已由 #113 待机用例断言 CI 绿；#117 PR 当前 head 的 test job 通过，run [41349510663](https://github.com/Xxx91n/photo/actions/runs/41349510663)） |

### 首次配置待机 4 项（#100 定形，#113 补自动化，#125 桌面截图取证，#113 二轮在当前 main 重取 2026-11-03）

> 场景：发版包的 `config/config.json` 是 `config.sample.json` 的拷贝（`scripts/publish-app.ps1`），
> 出厂 `watch.hot_folder: ""`。Worker 按 #100 保持驻守待命，UI 状态栏引导用户完成首次配置。
> 自动化侧（#113 AC1）：`WorkerReloadConfigRegressionTests`
> `.Standby_GetStatus_Through_Background_Endpoint_Should_Reply_With_Empty_Watch_Directory`
> 在真实传输绑定 + accept 环 + 帧协议往返下断言空配置回 `ok:true` 且 `watchDirectory=""`。
> **取证口径**：CI 的 dotnet `test` job 在本票各 head 均绿（run 号见 PR 正文）；
> 整条 run 仍显示红，红的是 `markers` job——
> src 里两条存量 `Design(#100)` 标记指向 #100 关闭后的死号，门禁脚本在 main 工作树上同样红，
> 与 K 组和本用例无关，已单独记在 #118，别把它读成 AC1 未达。
> （勘误，2026-10-31 #125：这一句只对写下它时的 head 成立。`aabdfaa9` 已把那两条标记改指存续的 `Design(#82)`，
> 且该提交在本轮取证基线 `d07129a0` 之前，`bash scripts/check-design-markers.sh` 在 HEAD 退出 0——
> 现在按当前 main 重跑不会再有 markers 恒红这件事。#118 票本身仍 OPEN，其前提已在代码侧消失。）
> **口径提醒**：CI 的 dotnet test 只跑 ubuntu，本用例在 CI 走 Unix socket 分支；
> Windows 命名管道分支（实机形态）与下面四项的界面呈现一样，只能实机验证。
> 另：该用例沿用本类既有夹具的 `exiftool.dry_run = true`，而发版包出厂是 `false`——
> 非 dry-run 的待机链路不在它射程内，归 #108。
>
> **本表结论的当前基线（2026-11-03，`fb388507`）**：下面四行的「证据」已整轮换成本次重取的结果——
> 产物是 Release run [41495388043](https://github.com/Xxx91n/photo/actions/runs/41495388043)
> （ref `main`，head `fb388507`，version `0.1.0-verify113b`，`create_release=false`）的 `release-win-x64`。
> 2026-10-31 那一轮（`d07129a0`）的结论只作历史记录保留在下方产物记录里，不再作为当前判断依据：
> 该轮之后 `ui/` 净增 1521 行（`worker_connection.rs` +25 是 K1 读的连接态、`app.rs` +553）、
> `src/` 净增 374 行（`MetadataCleanerWorker.cs` +320 压在 K4 走的 `ReloadConfig` 路径上），
> 按本文件第 1 步的规矩已构成「需重跑」条件，本次即按该条件重取。

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| K1 | 双击 `PhotoPrivacy.exe`（出厂空 `hot_folder`）→ 状态栏连接态显示「已连接」，不是「连接中/已断开」 | 通过 | 2026-11-03 桌面截图目检（产物见下文 2026-11-03 产物记录）。**启动方式按判据字面走双击路径**：`cmd /c start "" …\PhotoPrivacy.exe` 经 ShellExecute 拉起包内 exe（与资源管理器双击同一条 API，非 `CreateProcess`），主窗 996×759、无控制台黑窗，UI（pid 5664）与 Worker（pid 7324）两进程均起——帧 `K1-window.png`（启动后约 8 s 的整屏帧 `K1-launch-standby.png` 是同一窗口更早一拍）：状态栏绿点 +「已连接 · PhotoPrivacyCleaner.Background · 后台模式」，右侧「上次往返 1 ms」；Worker 卡片 Worker 模式=后台模式、上报模式=background、服务 IPC=不可达、协议=v1、ExifTool=unknown（待机不拉 ExifTool 进程，属预期，非失败）；「往返次数」两帧读数不同（整屏帧 13 → 窗口帧 28），是 1 s 一次 GetStatus 心跳的递增，不是两个值矛盾 |
| K2 | 状态栏出现 warning 色提示「请前往配置页设置监控目录」（`status.configure_watch_folder`，切 locale 后各自可见） | 通过 | 2026-11-03 桌面截图目检：10 个 locale 逐一取概览页帧（`K2-locale-<code>.png` ×10，另附逐行拼合的 `K2-hint-strip.png`）。切语言走「外部改写 `config.ui.locale` → 500 ms 内容指纹轮询回灌」（`config_service.rs` 的 `ExternalReload`，代码内的正式路径），不点坐标，因此避开指引第 7 步记的「导航项宽度随 locale 漂移」。10 行全部是 warning 色（琥珀）落在连接态下一行：zh-CN「请前往配置页设置监控目录」、en「Go to Settings to set the watch folder」（与 `locales/en.json` 该键逐字一致）、ja「設定ページで監視フォルダーを設定してください」、ko/de/fr/es/pt/ru 各随其 locale；ar 在 RTL 镜像布局（导航靠右、正文右对齐）下同一提示。只判「该 locale 的文案存在、渲染在判据位置且颜色正确」——「通过」按票面判据「切 locale 后各自可见」计（10/10 可见）。**字形本身不判**：ar 帧上的阿拉伯文呈孤立字形、整行读起来词序是反的（`K2-hint-strip.png` 末行可见），而 B 组 B3 只覆盖「选 العربية 布局翻转为 RTL」、不含字形连写，所以这条观感当前**没有票覆盖**，本轮只如实记录、不据此判 K2 失败，也不自行开票。取证后 `config.json` 已逐字节还原（sha256 回到出厂 `aa2d36ee…`） |
| K3 | 点该提示 → 直接进配置页（`app.rs` 的 `configure-watch-folder` on_click 置 `Page::Config`） | 通过 | 2026-11-03 桌面截图目检（zh-CN 帧）：点提示文本前「总览」高亮（`K3-before-click.png`），点后导航高亮跳到「配置」、页面标题变「配置」（`K3-after-click-hint.png`），不是只高亮也不是无响应。点击用 `SetCursorPos` + `mouse_event`，坐标由 `GetWindowRect` 换算成窗口内偏移，非缩略图目测 |
| K4 | 选有效目录保存 → 提示消失、监控启动；放入照片被处理（日志页出条目、审计目录 `processed.ndjson` 增长） | 通过 | 2026-11-03 桌面截图 + 文件系统取证（产物见下文 2026-11-03 产物记录）。配置页「监控目录」填 `C:\Users\Administrator\photo-smoke-113r2\hot`（`K4-1-typed.png`）→ 500 ms 防抖写盘 + `ReloadConfig` 成功；回总览：琥珀提示消失、`ExifTool` 行由 `unknown` 变 `13.60`、连接仍「已连接」（`K4-3-overview-after-save.png`）；预置 3666 B 含 Artist/Copyright 的 jpg 被就地清为 626 B、标签查空，`hot/bak/k4-sample-src.jpg.bak` 留 3666 B 原件（标签仍在）；随后放入 790 B 新照片验证实时处理 → 634 B、标签清空，`processed.ndjson` 2→4 条，日志页 4→7 条并实时刷新（`K4-4-logs.png`、`K4-5-logs-live.png`，02:13:28~02:14:37，含 `ExifTool started` / `Cleaned` / `backup_sidecar_version…`）。**环境前提**：本机 ExifTool 装在模板默认路径（13.60），出厂包一字未改即可启用监控，故这是本体证据而非 dry-run 旁证；真裸机（未装 ExifTool）仍归 #108。**时刻**：本地 02:14＝UTC 前一日 18:14（两日期分叉），日志页照样出 7 条，即 #129 的修复在本产物上成立。**顺带观察**：每张照片产生 2 条 `Cleaned` + 1 条 sidecar（`#133` 形态，不按本票 AC3 另开票，见产物记录） |

- 提示只在 `ConnectionState::Connected && watch_directory` 为空时渲染，因此 K1/K2 同源：
  Worker 待机时回包把「已连接 + 空目录」一起交给 UI，UI 才谈引导。
- **K4 有已知阻断（#108）**：发布包不含 ExifTool 且模板 `exiftool.path` 指向 `C:\Program Files\ExifTool\exiftool.exe`、`dry_run: false`，
  裸机保存有效目录后 `ReloadConfig` 会因 `exiftool.path not found` 校验失败、监控起不来。
  验 K4 时若命中这条，属 #108 已登记的缺陷，不按 #113 AC3 另开票。
  **两条取证口径要分清**：票面 K4 说的是「出厂包双击启动」（K1 已按 `cmd /c start` 的 ShellExecute 双击路径重取，
  见 K1 行）——手改包内 `exiftool.dry_run` 为 true 之后拿到的
  「监控启动、文件被处理」不是 K4 本体证据（dry-run 不写真文件），只能作为「待机 → 重载唤醒」这条链路的
  旁证记在证据列并注明改过什么；**这条只约束未装 ExifTool 的真裸机**——那种机器上正面证据要等 #108 落地
  （包内自带 ExifTool）后在出厂原样包上取。
  本机装好 ExifTool 并把 `exiftool.path` 指过去，则仍是非出厂形态，同样要在证据列写明。
- **K4 的 #108 阻断在取证机上不成立（2026-10-31 实测）**：这台机器恰好装了 ExifTool，且路径就是模板默认的
  `C:\Program Files\ExifTool\exiftool.exe`——出厂包一字未改即可解析到它，因此本轮 K4 拿到的是**本体证据**
  （`dry_run` 保持 `false`，未手改任何配置项），不需要 #113 设想的 dry-run 旁证。
  这不等于 #108 已解决：真裸机（未装 ExifTool）保存有效目录仍会撞 `exiftool.path not found`，
  该票的「裸机解压发布包 → 配置目录 → 监控确实启动」AC 依旧未达，别拿本轮证据去关它。
- **#117 已修（2026-11-01 实现窗口）**：待机态 `StopAsync` 挂住曾由 .NET 10 的
  `SemaphoreSlim.Dispose` 语义导致——Dispose 只清空等待队列，不会让挂起的 `WaitAsync` 完成，
  首次配置待机这条路径靠「Dispose + catch ObjectDisposedException」兜不住。
  修复分两步：停机先显式取消待机等待（`_standbyStopCts.Cancel()`），且信号量 `Dispose` 必须等
  `ExecuteAsync` 观察到取消并返回之后再执行（Cancel 的取消回调与 Dispose 清空等待队列存在竞态——
  Dispose 抢跑会让等待者永久挂起，CI 稳定复现、本地间歇复现）。`ExecuteAsync` 待机分支立刻收敛，
  不再等满 10 s（`HostOptions.ShutdownTimeout`）。自动化侧已由 #113 待机用例改回
  「断言 `StopAsync` 在预算内完成」（CI 绿，run 号见 PR 正文）；实机停机证据（托盘 Exit /
  停服务）待 #117 PR 合并后回填 T6，见下文「#117 实机停机证据」段。

### #117 实机停机证据（待回填）

> **目的**：T6 的取证段。自动化侧（#113 待机用例）已断言「待机态 `StopAsync` 在预算内完成」并进 CI 门禁；
> 实机形态（托盘 Exit / 停服务）仍需按桌面截图 / 进程观察通道补证。
> 判据：出厂空 `hot_folder` 待机态下，托盘 Exit 或停服务后进程在
> `HostOptions.ShutdownTimeout`（10 s）内退出，无残留 Worker / UI 进程；观察方式同 T2。
> **未回填前不得把 T6 记作通过**（无证据不虚构）。待 #117 PR 合并后在独立取证窗口补证回填。

### K 组取证指引（原名「K 组人工验收指引」，#113 定形，#125 换用桌面截图通道）

> **勘误（2026-10-31，#125）**：本节原先写「gpui 窗口的界面呈现只能由人眼判」，前提取自
> 「UIA descendant count=0」+「`PrintWindow` 只回黑帧」。前者仍然成立——控件树取不到，元素位置只能从像素上认；
> 但后者只约束**按窗口句柄取图**这条路。改用整屏 `Graphics.CopyFromScreen`（BitBlt 已合成的桌面）
> 就能拿到 gpui 窗口的真实像素，四项的界面呈现因此可由 agent 自查取证，不必再交人工目检。
> 自动化侧的射程不变：仍只到上文那条真实传输级待机连通用例。
> **口径变更的授权**：本票标签是 `ready-for-human`，原样交人工是默认路径；换成 agent 自查由用户在 #125
> 实现窗口明确批准（连同「产物一律重跑当前 main」一起批的），不是 agent 自行改判。
> 这只改变**谁来读这张图**，不改变「无证据不虚构」——读不到仍须记未探活。

1. **取产物**：用 Release workflow（`create_release=false`）跑**当前 main** 的 `release-win-x64` artifact，
   解压到一个私有目录。并行窗口共用发布链时不要把产物拷进 `ui/target` 之类的共享构建目录。
   取证据前先核对 head：旧记录的 head 一旦落后 main，界面呈现就不是当前二进制（#125 核对旧产物记录时
   就撞上过一次，见下节勘误）。
2. **造出待机态**：保持包内 `config/config.json` 出厂原样（`"watch.hot_folder": ""`）。
   若这台机器之前验过别的票、目录已填过，先把 `config.json` 删掉再从同目录的 `config.sample.json` 拷一份——
   残留的有效目录会让 Worker 直接进监控，待机引导根本不出现。
3. **K1**：双击 `PhotoPrivacy.exe` → 看概览/状态栏连接态是否「已连接」。
   （不应再有控制台黑窗：#99 之后 release 构建带 `windows_subsystem = "windows"`，
   看到黑窗说明拿的是 debug 产物或旧包，先别往下验。）
4. **K2**：同一屏找 warning 色提示「请前往配置页设置监控目录」；把 **10 个 locale 逐一**过一遍，
   确认该文案在每个 locale 下都出现、且内容与该 locale 对应——口径与 B1 的「逐一切换 10 locale」一致，只切一个不算覆盖。
   **判据边界（2026-11-03 裁决）**：本项判的是「各自可见」，**不含字形连写质量**。ar 帧上阿拉伯文呈孤立字形、
   整行词序读起来是反的，属另一层缺陷（当前无票承接，见产物记录第 3 项观察），不据此把 K2 记失败；
   若要把「可读」并入本项判据，那是改需求，须先改票面再重判。
5. **K3**：点该提示文本，是否直接落到配置页（不是只高亮、也不是无响应）。
6. **K4**：在配置页选一个有效目录并保存 → 提示应消失、监控应启动；往该目录放一张带元数据的照片 →
   日志页出现处理条目、审计目录生成 `processed.ndjson` 且新增记录。命中 #108 的校验失败按 #108 记，不重复开票。
   **日志页那一条腿的取证时刻**：审计文件名日期基准已随 #129 统一为本地「今天」，写入端、
   IPC 回填、UI tail 三处同口径。2026-11-03 在本地 02:14（UTC 前一日 18:14，正是 00:00–07:59 分叉时窗）
   实测日志页实时出 7 条，**分叉时窗内已不再恒空**，故本步骤不再有「排在本地 08:00 之后」的限制；
   修复前旧记录里该时窗恒空是 #129 缺陷表现，别当成 K4 判据。#129 AC4 的正式复验由该票窗口核销。
7. **取证通道的操作细节**（#125 + #113 第二轮实测，少走一遍的弯路）：
   `SetForegroundWindow` 单独调用会被系统挡掉，要先 `AttachThreadInput` 到前台线程再补一发 ALT 的 `keybd_event`；
   别在序列里调 `ShowWindow(SW_RESTORE)`，它会把窗口弹回默认位置，坐标随之全错——点击一律按 `GetWindowRect`
   换算成窗口内偏移；合成 `SetCursorPos` + `mouse_event` 点击有效；
   **键入这一条在 #113 第二轮被推翻**：`SendInput` 的 Unicode 字符（`KEYEVENTF_UNICODE`）在 gpui 文本框上无反应，
   `INPUT` 结构体尺寸给错（联合体只声明 `KEYBDINPUT`，40 → 28 字节）时更会静默失败、`SendInput` 返回 0；
   可靠路径是**先写剪贴板再发 Ctrl+V**（`user32.keybd_event` 组合键），本轮 K4 的目录路径即由此键入。
   另：`SendInput` 的返回值必须检查，否则「敲了没反应」会被误记成 UI 缺陷；
   取坐标别凭缩略图目测像素，先叠一层带数字标注的网格截图标定一次。
   **导航项坐标不跨 locale 复用**：导航条按文案宽度排布，切一次语言第二项就能从 x=249 漂到 x=300 之外，
   照旧坐标点过去会落在空白处「点了没反应」；逐 locale 取证要么每切换一次重新标定，
   要么改用「手改 `config.json` 的 `ui.locale` → 热重载」切语言、只点固定的第一项回概览。
8. **回填**：逐行把 K1-K4 的「结果」改成 通过 / 失败 / 未探活，并在「证据」列写 `日期 + 取证通道 + 看到的具体现象`；
   整屏截图当证据成立（#125 起），**无证据不虚构**的口径不变。

#### K 组产物记录（2026-10-30，#113 窗口：已备好产物，人工验收未执行）

- 产物：Release workflow run [41073704339](https://github.com/Xxx91n/photo/actions/runs/41073704339)
  （ref `issue-113-standby-ipc-verify`，head `67aa17e4`，version `0.1.0-verify113`，`create_release=false`）
  的 `release-win-x64` artifact，已下载并解压到本机私有目录 `D:\Aworker\photo-smoke-113\`，
  双击入口 `D:\Aworker\photo-smoke-113\win-x64\PhotoPrivacy.exe`。
  该 head 与本票后续提交之间只差 `tests/`+`docs/`（`src/`、`ui/` 未动），产品二进制等价。
  **勘误（2026-10-31，#125）**：这句只在写下它的那一刻成立，且当时就漏了 `src/`。`git diff --numstat 67aa17e4 d07129a0`
  实际漂移：`src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs` +63/−5、`src/PhotoPrivacy.Worker/WorkerIpcRequestHandler.cs` +3/−2
  （PR #120 的 #112 重载回滚，正压在 K4 走的 `ReloadConfig` 路径上）；`ui/.../app.rs` +112/−17、`i18n.rs` +6/0、
  10 份 locale 各 +6/−3（PR #116 的 #103 一键复制）。产品二进制已不等价，「只差 tests/+docs/」不成立。
  逐 hunk 比对：#103 没改动渲染 `configure-watch-folder` 提示的那段状态栏代码，所以旧包对 K1-K3 未必给出不同结论；
  但 K4 的 `ReloadConfig` 路径确实被 #112 改过，取证一律重跑当前 main，不去赌「这几处没改应该无所谓」。
- 包内实测（出厂原样，未改）：`config/config.json` 的 `watch.hot_folder` 为 `""`、
  `exiftool.dry_run` 为 `false`、`exiftool.path` 指向 `C:\Program Files\ExifTool\exiftool.exe`——
  K4 的 #108 阻断就来自后两项。
- 结果：K1-K4 全部**未探活**——gpui 窗口的界面呈现本窗口读不到（UIA descendant=0、`PrintWindow` 黑帧），
  按「无证据不虚构」不填任何通过项。自动化侧只到上文那条真实传输级待机连通用例。
  **已被下文 2026-10-31 记录取代**：换整屏 `CopyFromScreen` 通道后界面呈现读得到，四项已实机取证。

#### K 组产物记录（2026-10-31，#125 窗口：桌面截图通道自查取证，K1-K4 全部通过）

- 产物：Release workflow run [41100201595](https://github.com/Xxx91n/photo/actions/runs/41100201595)
  （ref `main`，head `d07129a0`，version `0.1.0-verify125`，`create_release=false`）的 `release-win-x64` artifact，
  解压到私有目录 `D:\Aworker\photo-smoke-125\pkg\`，入口 `D:\Aworker\photo-smoke-125\pkg\PhotoPrivacy.exe`。
  包内实测出厂原样：`config/config.json` 与 `config.sample.json` 逐字节相同（`hot_folder: ""`、`dry_run: false`）。
- 取证通道：整屏 `Graphics.CopyFromScreen` 截图 + 合成点击/键入，界面呈现由 agent 读图判定；
  截图存 `D:\Aworker\photo-smoke-125\evidence\`，共 24 张、按判据命名、不入库
  （K1 双击路径帧 `K1-dblclick-standby.png` 与早前 `Start-Process` 帧 `K1-K2-standby-overview.png`；
  K2 逐 locale 概览帧 `K2-locale-<code>.png` ×10，
  其中 ar 那张中途误拍成规则页、已删除并以全页帧改名顶替，glob 与判据一一对得上；
  K3 帧 `K3-after-click-hint.png`；K4 帧 `recheck-*.png` ×4 与分叉时窗对照 `K4-logs-*.png` ×3；余为过程帧）。
  K2 的 10 locale 是逐一取证：前 5 个走配置页语言组点击，后 5 个改走「手改 `ui.locale` → 热重载」这条路
  （导航项宽度随 locale 变，固定坐标点不中，见指引第 7 步），两条路都取到概览页截图。
  取证结束把 `config.json` 还原成出厂样。
  **证据的可核验性是一条已接受的缺口**：图不入库，后来者无法独立复看 10 locale 覆盖与 K4 观察；
  当时仓内只留 `docs/agents/ui-smoke-evidence.sha256`（24 条哈希）作为「当时确实存在且内容未被替换」的锚。
  **该锚已随 2026-11-03 重取而下线**：同一路径现在装的是本轮 20 条哈希，`D:\Aworker\photo-smoke-125\evidence\`
  在当前取证机上不存在（无 D: 盘），旧条目留着只会让 `sha256sum -c` 恒红，故不在文件里保留。
  这 24 条哈希仍可从 git 历史取回（`git show fb388507:docs/agents/ui-smoke-evidence.sha256`，
  即本票第二轮起始基准那一版），但对应的 24 张图已无法找回——引用本记录时按这个前提打折，别把它当成可复核的原始证据。
  **勘误（2026-11-03 #88 窗口，随 S 组换轮）**：上面「现在装的是本轮 20 条哈希」只对 #152 合并后的
  main 顶成立；#88 的 S 组取证已把同一路径换成本票 6 条哈希，本轮 20 条改从
  `git show 4db2583:docs/agents/ui-smoke-evidence.sha256` 取回（对应 20 张图仍在取证机 `photo-smoke-113r2\evidence\`）。
- 结果：**K1 通过、K2 通过（10 locale 逐一）、K3 通过、K4 通过**。
  K4 的取证时刻是本地 10:48（UTC 02:48，本地日期＝UTC 日期），日志页那条腿因此成立；
  另在本地 02:03（UTC 前一日 18:03，两日期分叉）以同一产物复跑一次，日志页恒 0 条，
  仅把审计文件名从 `audit-2026-10-30.jsonl` 复制成 `audit-2026-10-31.jsonl` 就立刻出 5 条——
  该分叉缺陷独立登记为 #129，与本组判据无关，取证后复制出的文件已删除。
  **#129 修复状态**：该票已实现统一基准（文件名按本地「今天」，写入端/IPC/UI tail 三处同口径），
  代码侧由 `AuditLogFileNamesTests` 与写入端命名用例守卫；分叉时窗（本地 00:00–07:59）的修复后
  实机复验记录（#129 AC4）待修复产物在该时窗内重跑时补齐，当前未取。
- **基线时效（读这四项前必看）**：结论说的是 head `d07129a0`，不是当前 main。
  本 PR 合并 main 时带进来 `app.rs` +547/−6（#101 Tab 焦点）、`worker_connection.rs` +24/−1（K1 读的连接态）、
  `MetadataCleanerWorker.cs` +170/−43（K4 走的 ReloadConfig 路径），已按上文第 1 步的规矩构成「需重跑」条件；
  用户 2026-10-31 明确选择接受这个带基线的现状、不再重取一轮。
  所以这四项是**当时当地为真**的观察，不是当前 main 的回归保证——要在当前 main 上背书，得重出一版产物重跑一轮。
  **该缺口已于 2026-11-03 补齐**：按同一条「需重跑」规矩在 head `fb388507` 上整轮重取，四项再次全部通过，
  见下一节产物记录。本节自此只作历史记录。
- 环境前提见上文「K4 的 #108 阻断在取证机上不成立」那条：本机 ExifTool 落在模板默认路径，
  所以这轮 K4 是本体证据；真裸机路径未覆盖，#108 不因此轮取证而可关。
- 顺带观察：待机态概览页 `ExifTool` 行显示 `unknown`，监控启用后变 `13.59`——
  待机不拉 ExifTool 进程，版本探不到属预期，不按失败记。
- 目录副作用复现：待机态启动即在包目录生成 `hot/_audit`、`hot/_quarantine` 与 `logs/worker-*.log`
  （`config.json` 本身未被改写），与 #124 描述一致。
- 停机方式：本轮用 `Stop-Process` 结束 UI 与 Worker，未走正常退出路径，故不产生 #117 的停机挂起证据，
  该票状态不因本轮取证改变。

#### K 组产物记录（2026-11-03，#113 第二轮：在当前 main 重取，K1-K4 全部通过）

> 起因：#113 的 AC2 在 2026-10-31 只拿到「取证基线 `d07129a0`」的结论，而基线之后 `ui/` 净增 1521 行、
> `src/` 净增 374 行（含 K4 走的 `ReloadConfig` 路径），按本文件第 1 步的规矩已构成「需重跑」条件。
> 用户 2026-11-03 在 #113 实现窗口明确选择「先在当前 main 重取 K1-K4」，本轮即照此执行；
> 2026-10-31 那次「接受带基线现状」的决定由本次取代。

- **产物**：Release run [41495388043](https://github.com/Xxx91n/photo/actions/runs/41495388043)
  （ref `main`，head `fb388507`，version `0.1.0-verify113b`，`create_release=false`）的 `release-win-x64` artifact，
  解压到本机私有目录 `C:\Users\Administrator\photo-smoke-113r2\pkg\`，入口同目录 `PhotoPrivacy.exe`。
  本地未编译，包体全部来自远端 CI。
- **出厂原样核对**（未改任何一项）：`config/config.json` 与 `config/config.sample.json` 逐字节相同
  （`sha256 aa2d36ee5e856c579c1b64ad1cc0c7d365a4d5cd5b915bf04b46f6696d22c3a7`，1591 B），
  包内 `watch.hot_folder: ""`、`exiftool.dry_run: false`、`exiftool.path` 指向 `C:\Program Files\ExifTool\exiftool.exe`。
  二进制：`PhotoPrivacy.exe` `sha256 a49189b2…` 33172036 B、`PhotoPrivacyWorker.exe` `sha256 424acf03…` 90452428 B。
- **取证通道**：整屏 `Graphics.CopyFromScreen`（Pillow `ImageGrab`）+ 合成点击/键入；
  截图存 `C:\Users\Administrator\photo-smoke-113r2\evidence\`，20 张、按判据命名、不入库，
  哈希锚在 `docs/agents/ui-smoke-evidence.sha256`（清单文件头写了校验命令，实测 20/20 `OK`）。
- **结果**：**K1 通过、K2 通过（10 locale 逐一）、K3 通过、K4 通过**，逐项证据见上文四行。
- **执行环境的两个前提（读结论时必须带上）**：
  1. 取证机装了 ExifTool **13.60**，且落在出厂模板默认路径 `C:\Program Files\ExifTool\exiftool.exe`——
     出厂包一字未改即可启用监控，K4 是本体证据；**真裸机（未装 ExifTool）仍归 #108**，本轮不改变该票状态。
     本机原本没有 ExifTool，是本轮为取 K4 本体证据按官方发行包（13.60 64-bit）装到该默认路径的；
     这属于取证机环境准备，包体未动、也不是「手改 `dry_run` 换成旁证」。
  2. 取证时刻本地 02:13–02:14（UTC 前一日 18:13–18:14，**两日期分叉时窗**）。#129 修复后日志页在该时窗**仍出条目**，
     与被修复前的「分叉时窗恒空」相反——这条同时是 #129 AC4 的实机复验证据（该票 AC4 由 #129 窗口自行核销，本票只提供观察）。
- **本轮新观察的三项（均不按 #113 AC3 并入本票）**：
  1. **同一张照片被处理两次**：每张照片在 `processed.ndjson` 留 2 条、日志页出 2 条 `Cleaned` + 1 条
     `backup_sidecar_version…`（首次 3666→626 一条、紧接 626 一条）。这与 **#133** 描述的形态一致
     （去重键取入队时指纹，`same_as_source` 清洗后指纹变化即不再命中），#133 已有实现票/PR 在跑，本票只记观察。
  2. **待机启动仍创建审计目录**：出厂空配置启动后，包目录出现 `hot/_audit`（02:09，配置保存前；`_quarantine` 到 02:13 保存后才出现）。
     worker 侧 `EnsureConfiguredDirectories` 与 `JsonLineAuditLogger` 都在待机分支之后返回，能解释 `_quarantine` 却解释不了这条 `_audit`；
     代码里能对上的**候选**建目录点是 UI 日志尾随 `ui/crates/photoprivacy-logs/src/feed.rs:88` 的
     `fs::create_dir_all(&self.directory)`（本行是静态读码得出的候选，未做运行时归因证实）。
     若成立，它与 #124「配置加载改为纯读」的收口范围不重叠（另一个进程、另一个模块），属新发现的旁路副作用；
     是否成票待用户在 #113 窗口裁决，本轮**不自行开票、不并入本 PR 的修复范围**。
  3. **ar 文案字形**：`K2-locale-ar.png` 与 `K2-hint-strip.png` 末行的阿拉伯文呈孤立字形、整行词序读起来是反的。
     该观感不在 B 组 B3 的覆盖范围内（B3 只判「选 العربية 布局翻转为 RTL」），当前无票承接；本轮只记录，不据此判 K2 失败。
- **收尾状态**：K2 段为切语言改过 `ui.locale`，段末已逐字节还原（回到出厂 `aa2d36ee…`）；
  K4 段由 UI 正常保存写入 `watch.hot_folder`（正是判据要求的操作，不是取证污染），故取证结束时
  `config.json` 停在「已配置一个有效监控目录」这一状态，`locale` 仍是 `zh-CN`。
  `hot/` 下的两个测试样本、`bak/`、`pkg/hot/_audit` 均为运行产物，全部落在取证目录内，仓库工作区不受影响。


### S 组产物记录（2026-11-03，#88 窗口：agent 自查取证，S1/S3/S4/S8 全部通过）

> **口径变更的授权**：本票标签同为 `ready-for-human`（K 组同款）；换 agent 桌面截图自查由用户在 #88
> 实现窗口明确批准（2026-11-03，同 #125 先例），并批准为取 S3/S4/S8 证据在本机安装/卸载
> `PhotoPrivacyCleaner` 服务（S2 前置步骤，系统级提权操作，不在本票 AC 内）。
> 不改变「无证据不虚构」。

- **产物**：复用 Release run [41495388043](https://github.com/Xxx91n/photo/actions/runs/41495388043)
  （ref `main`，head `fb388507`，version `0.1.0-verify113b`，`create_release=false`）的 `release-win-x64`，
  即 #113 第二轮已解压到本机的 `C:\Users\Administrator\photo-smoke-113r2\pkg\`。本票起始基线与该产物同 head
  （`fb388507c082…`），二进制 sha256 与 2026-11-03 记录一致（`PhotoPrivacy.exe a49189b2…`、
  `PhotoPrivacyWorker.exe 424acf03…`，实测核对），故不重跑 Release——本地未编译，包体全部来自远端 CI。
- **取证通道**：整屏 `Graphics.CopyFromScreen`（PIL `ImageGrab`）+ 合成点击（`SetCursorPos` + `mouse_event`）；
  截图存 `C:\Users\Administrator\photo-smoke-88\evidence\`，6 张、按判据命名、不入库，
  哈希锚 `docs/agents/ui-smoke-evidence.sha256`（实测 6/6 `OK`）。
  本轮新增的两个操作细节（K 组指引第 7 步的补充）：
  1. **工具宿主会反复抢前台**：跨进程调用（Bash→python）之间 IDE/宿主窗口会重新激活，`AttachThreadInput`
     补前台不可靠；可靠做法是对 PhotoPrivacy 主窗 `SetWindowPos(HWND_TOPMOST)` 置顶后操作+截图，
     一轮交互（置顶→点击→等待→截屏）收在同一个进程里原子完成。
     另：ctypes 调 `SetWindowPos` 不声明 `argtypes` 时 HWND/-1 传参被静默弄错、返回 0 且 `GetLastError`=1400，
     看上去像 UIPI 拒绝，实际只是参数编组问题——先修 argtypes 再怀疑权限。
  2. **导航项坐标按暗像素聚类标定**（K 组指引第 7 步「网格标定」的替代）：对导航条扫描暗像素列并聚类，
     得到五个导航项的文字中心（总览 656 / 配置 703 / 服务管理 764 / 日志 824 / 规则 871，y≈189，
     窗口 (462,114) 下），一次到位；缩略图目测的坐标混入显示缩放会连续点空。
- **前置状态**：服务未安装（SC 1060）、无 UI/Worker 残留进程——恰为 S1 未安装态。
  `pkg/config/config.json` 保持 #113 二轮收尾状态（`watch.hot_folder` 指向取证目录内 hot\，未改一字），
  服务 Worker 因此直接进监控形态（对 S8 的实时日志条目是必要条件，对 S1/S3/S4 无影响）。
- **S2 前置步骤**：`install-service.ps1 -Action install`（High IL 管理员，无 UAC 交互），
  安装即写 `HKLM:\SOFTWARE\PhotoPrivacy\ServicePipeInstallerSid`（#70 的安装者 SID）再 `Start-Service`。
- **结果**：**S1 通过、S3 通过、S4 通过、S8 通过**（逐项证据见上文 S1-S8 行）；S2/S6/S7 不在本票 AC，未回填（S5 已由 #85 窗口单独复测，见下一节）。
- **本轮新观察（不自行开票，是否成票待用户裁决）**：
  1. **服务 Worker 与后台 Worker 同 config 并存时服务自停**：两形态 Worker 共用同一
     `hot/_audit/processed.ndjson`，后启动者在 `FileProcessedRecordStore` 构造处抛
     `IOException`（文件被另一进程占用），`HostOptions.BackgroundServiceExceptionBehavior=StopHost`
     令整个服务 Host 自停——`C:\ProgramData\PhotoPrivacy\logs` 03:52 两轮
     START→`BackgroundService failed`→FTL→STOP 可证（SCM 事件 7036 运行/停止成对出现）。
     该状态在当前产品里**可达**：服务已安装未运行时 UI 连接编排回落后台端点并拉起后台 Worker
     （`worker_connection.rs` 的 `connect_worker`，#70 Design 注释明示本版行为），此时在服务页点
     「启动」即触发上述冲突。本轮取证通过「先杀后台 Worker、仅留 UI，再点启动」绕开。
     这是 #70 连接编排的伴生缺陷候选，与本票 AC 无冲突（S3 的判据形态下服务正常驻留运行）。
  2. **审计事件 `service_started` 的口径**：它是「Worker 进程启动」事件，后台与服务的 Worker 都会写
     （本轮审计里 04:07:03 是后台 Worker、04:09:55 是服务 Worker），日志页上连排两条
     「Service started」不代表 Windows 服务重启了两次——归因要结合进程会话（Services vs Console）与
     ProgramData 日志的 Application started 行。
- **收尾状态**：服务已按脚本卸载（SC 1060 复核），`ServicePipeInstallerSid` 注册表值随 uninstall 清除；
  UI 已停、无 Worker 残留进程（任务表复核 0/0）；本轮投放的 `hot/s88-sample.jpg` 及其清洗产物已删除，
  hot\ 目录回到 #113 二轮收尾内容；`config.json` 未动。
  服务 Worker 的诊断日志留存在 `C:\ProgramData\PhotoPrivacy\logs\worker-20261103.log`
  （含两轮 #70 相关崩溃栈），未入仓库。

### S5 复测产物记录（2026-11-03，#85 窗口：服务模式配置热重载）

> **授权记录**：在本机执行实机取证（含安装/卸载 `PhotoPrivacyCleaner` 服务、合成点击驱动 UI）
> 由用户在 #85 实现窗口明确批准（2026-11-03）。不改变「无证据不虚构」：本节每条结论都对应
> 下文具体帧或探针输出。

- **产物**：复用 Release run [41495388043](https://github.com/Xxx91n/photo/actions/runs/41495388043)
  的 `release-win-x64`（已解压 `C:\Users\Administrator\photo-smoke-113r2\pkg\`，head `fb388507`）。
  与 #85 起始基线 `5298b6466` 的差异经 `git log fb388507..5298b64` 核对**仅 4 个 docs 提交**；
  二进制 sha256 实测与既有记录一致（`PhotoPrivacy.exe a49189b2…`、`PhotoPrivacyWorker.exe 424acf03…`），
  故结论对当前 main 的代码成立。本地未编译，包体全部来自远端 CI。
- **前置**：服务未安装（SC 1060）、无 UI/Worker 残留进程。`scripts/install-service.ps1 -Action install`
  （High IL 管理员，无 UAC 交互）安装并启动，安装即登记 `ServicePipeInstallerSid`（`S-1-5-21-…-500`）；
  `sc query` STATE 4 RUNNING，服务 Worker 落在 Services 会话（pid 3496，SessionId 0）。
- **取证通道**：整屏 `ImageGrab` + 合成点击/键入；截图与探针输出存
  `C:\Users\Administrator\photo-smoke-85\evidence\`（12 个文件，不入库），
  哈希锚 `docs/agents/ui-smoke-evidence.sha256`（实测 12/12 `OK`）。本轮新增的操作细节：
  1. **gpui 输入框不响应 Ctrl+A**：清空既有路径必须 `End` + 退格。首次按 Ctrl+A 再粘贴时，
     新路径被拼到旧值尾部（`…\photo-smoke-113r2\hotC:\Users\…`），界面照常落盘；
     该畸形路径触发的重载在服务管道回 `reload_failed`（`EnsureConfiguredDirectories` 抛
     `IOException`）且旧运行时保持原样——正好实证「失败不吞成成功」（#94）与「建目录失败在拆卸前
     拒绝」（#112/#124）在服务端点的行为（`S5-09-service-log.txt` 的 06:28:03 段）。
  2. 一轮交互（置顶 → 点击 → 等待 → 截屏）收在同一进程内，避免宿主抢前台（#88 已登记）。
- **步骤与结果**：
  1. 服务运行中启动 UI：连接态「已连接 · PhotoPrivacyCleaner.Service · 服务模式」，Worker 卡片
     「服务模式 / service / 服务 IPC=可达 / 协议 v1 / ExifTool 13.60」，进程侧只有服务 Worker，
     UI 未拉起后台 Worker——`S5-00-overview-before.png`；
  2. 配置页把「监控目录」从 `…\photo-smoke-113r2\hot` 改为 `…\photo-smoke-85\hot-s5`
     ——编辑态帧 `S5-02-field-edited.png`；UI 防抖后写盘，`config.json` 的 `watch.hot_folder`
     为新值（`stat` 实测 06:28:43.853；该时刻没有单独留帧，口径见本条）；
  3. 服务管道 `GetStatus` 回 `watchDirectory=C:\Users\Administrator\photo-smoke-85\hot-s5`
     （`mode=service`，`S5-06-service-pipe-probe.txt`），重开配置页显示持久化值
     ——`S5-05-config-persisted.png`；
  4. 向新目录投放 `sample.jpg`（3,648 B）：服务 Worker 于 06:29:18 就地清洗为 626 B，审计
     `processed.ndjson` 留 3,648 / 626 两条记录，日志页实时出
     `Cleaned attempt=1;wipe_result=Cleaned_Modified` 与
     `backup_sidecar_version… content_diverged_sidecar_written`——`S5-03-logs-after-reload.png`；
     同帧读数总览仍为服务模式、上次往返 8 ms——`S5-04-overview-after-reload.png`；
     审计与处理记录的原样输出见 `S5-10-audit-records.txt`（这两份文件收尾后仍在磁盘上，
     可事后原样取；截图与探针输出不可重取，故入锚）；
  5. **服务管道独立探针**（不经 UI，按帧协议直连 `\\.\pipe\PhotoPrivacyCleaner.Service`）：
     `GetStatus` 1–2 ms；三次运行的两次 `ReloadConfig` 往返落在 **0.44–0.62 s**（锚定的那一轮
     0.59 / 0.55 s），全部回 `ok:true`、`mode=service`、`v=1`，无一次触及 10 s 预算
     ——`S5-06-service-pipe-probe.txt`。这是事故形态「服务管道 10 s/15 s 无响应」在
     Windows 命名管道上的直接反证；
  6. **端到端计时**（第二轮回改为原目录）：粘贴生效后 **0.52 s** 配置落盘、**0.64 s**
     `GetStatus.watchDirectory` 切到新值，同帧界面绿字「已保存 · Worker 已重载」
     ——`S5-08-e2e-timing.png` / `S5-08-e2e-timing.txt`，读侧预算 10 s
     （该文件首行 `watched_before=` 为空串：退格清空与粘贴之间 UI 把空目录落盘了一次，
     worker 因而短暂回待机，属合成输入手法的产物，不参与判定）。
- **收尾状态**：UI 已退出、服务已卸载（SC 1060 复核）、`ServicePipeInstallerSid` 值随 uninstall 清除、
  任务表 0/0 残留；`pkg/config/config.json` 回到取证前的 `watch.hot_folder`（`…\photo-smoke-113r2\hot`），
  二进制未变；`hot-s5\` 内留 626 B 已清洗样本与其备份，全部落在取证目录内，仓库工作区不受影响。
- **边界**：仅 Windows 命名管道。Linux/macOS 服务端点（Unix socket 0660 + 组边界）未实机，仍归 #31/#32；
  CI `test` job 为 ubuntu，服务形态那一行走的是 Unix 策略——两侧证据互补，互不替代。

### 切换日实机执行记录（2026-10-28，#61 实现窗口）

- 产物：本票 `cargo build --release --locked --target x86_64-pc-windows-gnu -p photoprivacy-ui`
  （与 publish-app.ps1 发布口径一致，manifest 经 windres 嵌入）复制到私有目录
  `D:\Aworker\photo-smoke-t12\`，与 `dotnet publish` 产出的 `PhotoPrivacyWorker.exe`、私有 `config/config.json` 同目录。
- 通过项（进程/文件级）：
  - 启动与窗口：UI 正常启动，主窗 title=`PhotoPrivacy`（视觉观感未目检）。
  - **UI 拉起后台 Worker**（D11 IPC/连接编排证据）：UI 启动后 `PhotoPrivacyWorker` 进程出现且保持 1 个；
    单实例/关窗/唤起全程无重复 Worker、无孤儿。
  - T3 / T4 / T5：见上表。
- 环境限制与勘误：
  - RDP/GPU 会话 `PrintWindow` 黑帧口径沿用 #56；视觉项以人工目检为准。
    （勘误，2026-10-31 #125：本条前提已废，视觉项取证通道一律以下文「K 组取证指引」为准。）
  - `Get-Process ... MainWindowHandle/Title` 会命中托盘辅助窗口（title=exe 路径），不能单独作为
    「主窗可见」判据；本记录以「title 是否为 PhotoPrivacy 的可见顶层窗口」为准。
  - 单文件 `PhotoPrivacyWorker.exe` 与 `appsettings.json` 同目录时 Serilog 单文件程序集解析崩溃
    （既有形态：发布链只拷 Worker exe，不带 appsettings.json）；冒烟目录已移除该文件，非本票缺陷。
- 合入 PR #81（#58 托盘门控，main `33734bd`）后的复验（同一产物路径重建）：
  T3 关窗驻守、T4 静默启动、T5 单实例唤起、可见启动与 Worker 拉起全部复测通过；
  本冒烟机未安装服务（`Get-Service PhotoPrivacyCleaner` 不存在），「服务已安装 → 无托盘/必出窗」分支
  未在实机执行，归 S1-S8。
- 未探活（截至 2026-10-28 切换日）：A1-A5、B1-B5、E1-E5、F1-F5、S1-S8、T1/T2；
  其中 A/B/E/F/T 已在 2026-10-29 窗口完成人工目检（见下节），S1-S8 归服务模式票 #88。

### 人工冒烟实机执行记录（2026-10-29，#29 实现窗口）

- 产物：远端 CI/CD Release workflow run [40862597929](https://github.com/Xxx91n/photo/actions/runs/40862597929)（head `c5d51c8b`，合入 PR #94 后 main 分支产物 `release-win-x64`）下载解压至私有目录 `D:\Aworker\photo-smoke-t29\`，本机执行实机目检，严格遵循「禁止本地编译，只允许远端 CI/CD」。
- 实机环境：Windows 11 桌面环境，图形会话。
- 执行结论汇总：
  - **通过（19 项独立实机目检 + 1 项并入 + 既有 3 项进程级，共 23 项）**：
    - 主题 A 组 5 项全过：A1、A2（注：右上角与设置页两处可调，存在冗余）、A3、A4、A5；
    - 语言 B 组 4 项独立目检通过（B1、B3、B4、B5）+ B2（通过（并入 B1）随测通过）；
    - 日志 E 组 4 项通过：E1、E2（后端清理链路通 + 配置 hot_folder 后界面刷新）、E3、E5；
    - 交互 F 组 4 项通过：F1、F2、F3、F5；
    - 托盘 T 组 2 项目检通过：T1、T2；
    - 结合既有 T3–T5（3 项进程级），本轮冒烟核销通过 23 项。
  - **失败（2 项）**：
    - **F4（键盘 Tab 焦点）**：按 Tab 键在各输入框与按钮间无任何焦点跳转与高亮轮廓反馈（复现步骤：启动 UI 后按 Tab 键，焦点不移动亦无指示框）。根因：gpui 自定义组件未接入键盘焦点遍历链与焦点指示轮廓。
    - **E4（清空日志后重启回灌）**：点击清空按钮后日志列表当即变空，但重启软件后旧日志重新回灌出现（核查为历史时间日志）。经与旧 UI 对照，此为换栈前即存在的既有 BUG（复现步骤：放入图片产生日志 → 点击清空 → 关闭并重启 UI → 旧日志回灌）。
      （复测，2026-11-01 #139：已随 #102 B′ 方案修复并做探针级复现通过，证据见上文 E 组表 E4 行与 PR #143；本条的「失败」只描述写下它时的状态。）
  - **#57 日志页联动状态核销**：
    - E1/E2/E3/E5 视觉呈现与交互功能核销通过；
    - E4 历史日志回灌确认存在缺陷（换栈前既有），由独立缺陷票跟进修复；（复测，2026-11-01 #139：该缺陷即 #102，修复已探针级复现通过，见 E 组表 E4 行。）
    - `#57` AC2（大批量日志滚动/浏览不卡顿）：**未探活**（本次目检仅验证小批量即时处理与清空，未灌入大批量日志流，该项 AC 保持未探活，留待后续专项验证，不提前宣布完整通过）。
- 伴随发现与勘误记录：
  1. **清单文案勘误**：原清单中关于主题、语言、日志级别的“下拉菜单”表述为推测失真；实机均为平铺选项组（Segmented/Radio-like buttons），无原生下拉框，本轮已在清单中更正。
  2. **终端黑框弹出**：`photoprivacy-ui` 的入口 `main.rs` 未声明 `#![windows_subsystem = "windows"]`，双击启动时系统默认分配控制台黑窗口，随后弹出 GUI。（勘误，2026-10-30：该形态已随 #99/PR #106 修复，release 构建现在带 `windows_subsystem = "windows"`；本条只作历史记录。）
  3. **初始配置待机行为（#100）**：打包附带的 `config/config.json` 默认 `"watch.hot_folder": ""` 为空时，Worker 按 #100 起保持 IPC 连接并待机，UI 引导前往配置页选择监控目录，保存有效目录后经热重载启用监控。#113 已把这条路径定形为上文的 K1-K4 四项，并把「真实传输级待机连通」补成进 CI 门禁的自动化用例；**四项已于 2026-11-03 在 head `fb388507` 上实机取证通过**（见上文 2026-11-03 产物记录），本条不再是「只有代码与集成测试口径」。
     （勘误，2026-10-31 #125：K1-K4 已按桌面截图通道实机取证并全部通过，见上文「首次配置待机 4 项」表；本条的「未探活」只描述写下它时的状态。）
  4. **GUI 文本不可划选复制**：gpui 纯文本展示未支持鼠标划选高亮与系统剪贴板复制，报错无法直接复制排障。
  5. **主题设置入口冗余**：右上角快捷切换与设置页平铺切换并存，交互体验重叠。
