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
| E4 | 清空后列表立即空；重启后旧日志不回灌 | 失败 | 2026-10-29 实机目检：点击清空后列表立即变空，但重启之后旧日志又重新出现（核查为旧时间的日志，属于新旧 UI 重构前就存在的既有 BUG；复现步骤：放入图片产生日志 → 点击清空 → 关闭并重启 UI → 旧日志回灌） |
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
| S1 | 未安装态：服务页显示未安装，启动/停止禁用 | | |
| S2 | 安装服务（提权 UAC/pkexec/osascript 流） | | |
| S3 | 启动服务 → 状态变运行中 | | |
| S4 | 服务运行中 UI 经服务端点连接（服务页/日志页数据来自服务 Worker） | | |
| S5 | 服务模式下改配置保存 → ReloadConfig 热生效 | | |
| S6 | 停止服务 → 状态回落 | | |
| S7 | 卸载服务 → 回到未安装态 | | |
| S8 | 服务模式日志通道在日志页可见（与后台模式区分） | | |

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

### 首次配置待机 4 项（#100 定形，#113 补自动化并交人工目检）

> 场景：发版包的 `config/config.json` 是 `config.sample.json` 的拷贝（`scripts/publish-app.ps1`），
> 出厂 `watch.hot_folder: ""`。Worker 按 #100 保持驻守待命，UI 状态栏引导用户完成首次配置。
> 自动化侧（#113 AC1，已进 CI 门禁）：`WorkerReloadConfigRegressionTests`
> `.Standby_GetStatus_Through_Background_Endpoint_Should_Reply_With_Empty_Watch_Directory`
> 在真实传输绑定 + accept 环 + 帧协议往返下断言空配置回 `ok:true` 且 `watchDirectory=""`。
> **口径提醒**：CI 的 dotnet test 只跑 ubuntu，本用例在 CI 走 Unix socket 分支；
> Windows 命名管道分支（实机形态）与下面四项的界面呈现一样，只能实机验证。
> 另：该用例沿用本类既有夹具的 `exiftool.dry_run = true`，而发版包出厂是 `false`——
> 非 dry-run 的待机链路不在它射程内，归 #108。

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| K1 | 双击 `PhotoPrivacy.exe`（出厂空 `hot_folder`）→ 状态栏连接态显示「已连接」，不是「连接中/已断开」 | 未探活 | — |
| K2 | 状态栏出现 warning 色提示「请前往配置页设置监控目录」（`status.configure_watch_folder`，切 locale 后各自可见） | 未探活 | — |
| K3 | 点该提示 → 直接进配置页（`app.rs` 的 `configure-watch-folder` on_click 置 `Page::Config`） | 未探活 | — |
| K4 | 选有效目录保存 → 提示消失、监控启动；放入照片被处理（日志页出条目、审计目录 `processed.ndjson` 增长） | 未探活 | — |

- 提示只在 `ConnectionState::Connected && watch_directory` 为空时渲染，因此 K1/K2 同源：
  Worker 待机时回包把「已连接 + 空目录」一起交给 UI，UI 才谈引导。
- **K4 有已知阻断（#108）**：发布包不含 ExifTool 且模板 `exiftool.path` 指向 `C:\Program Files\ExifTool\exiftool.exe`、`dry_run: false`，
  裸机保存有效目录后 `ReloadConfig` 会因 `exiftool.path not found` 校验失败、监控起不来。
  验 K4 时若命中这条，属 #108 已登记的缺陷，不按 #113 AC3 另开票。
  **两条取证口径要分清**：票面 K4 说的是「出厂包双击」——手改包内 `exiftool.dry_run` 为 true 之后拿到的
  「监控启动、文件被处理」不是 K4 本体证据（dry-run 不写真文件），只能作为「待机 → 重载唤醒」这条链路的
  旁证记在证据列并注明改过什么；正面证据要等 #108 落地（包内自带 ExifTool）后在出厂原样包上取。
  本机装好 ExifTool 并把 `exiftool.path` 指过去，则仍是非出厂形态，同样要在证据列写明。
- **别把 #117 算进 K 组**：验完 K1-K4 关掉 UI / 停服务时，仍在待机态的 `StopAsync` 会挂住，
  表现为退出要等满 10 s（`HostOptions.ShutdownTimeout`）甚至留下未拆干净的任务——那是 #117，不是 K 组判据。

### K 组人工验收指引（#113 定形，交人工目检）

> gpui 窗口在本机 UIA descendant count=0、RDP/GPU 会话下 `PrintWindow` 只回黑帧（#56 起的既有限制），
> 四项的界面呈现只能由人眼判；自动化侧只覆盖到上文那条真实传输级待机连通。

1. **取产物**：用 Release workflow（`create_release=false`）跑目标 head 的 `release-win-x64` artifact，
   解压到一个私有目录。并行窗口共用发布链时不要把产物拷进 `ui/target` 之类的共享构建目录。
2. **造出待机态**：保持包内 `config/config.json` 出厂原样（`"watch.hot_folder": ""`）。
   若这台机器之前验过别的票、目录已填过，先把 `config.json` 删掉再从同目录的 `config.sample.json` 拷一份——
   残留的有效目录会让 Worker 直接进监控，待机引导根本不出现。
3. **K1**：双击 `PhotoPrivacy.exe` → 看概览/状态栏连接态是否「已连接」。
   （不应再有控制台黑窗：#99 之后 release 构建带 `windows_subsystem = "windows"`，
   看到黑窗说明拿的是 debug 产物或旧包，先别往下验。）
4. **K2**：同一屏找 warning 色提示「请前往配置页设置监控目录」；切一次语言（配置页语言组）确认该文案随 locale 变。
5. **K3**：点该提示文本，是否直接落到配置页（不是只高亮、也不是无响应）。
6. **K4**：在配置页选一个有效目录并保存 → 提示应消失、监控应启动；往该目录放一张带元数据的照片 →
   日志页出现处理条目、审计目录生成 `processed.ndjson` 且新增记录。命中 #108 的校验失败按 #108 记，不重复开票。
7. **回填**：逐行把 K1-K4 的「结果」改成 通过 / 失败 / 未探活，并在「证据」列写 `日期 + 实机目检 + 看到的具体现象`；
   截图只作辅助（黑帧问题下不得当证据），**无证据不虚构**。

#### K 组产物记录（2026-10-30，#113 窗口：已备好产物，人工验收未执行）

- 产物：Release workflow run [41073704339](https://github.com/Xxx91n/photo/actions/runs/41073704339)
  （ref `issue-113-standby-ipc-verify`，head `67aa17e4`，version `0.1.0-verify113`，`create_release=false`）
  的 `release-win-x64` artifact，已下载并解压到本机私有目录 `D:\Aworker\photo-smoke-113\`，
  双击入口 `D:\Aworker\photo-smoke-113\win-x64\PhotoPrivacy.exe`。
  该 head 与本票后续提交之间只差 `tests/`+`docs/`（`src/`、`ui/` 未动），产品二进制等价。
- 包内实测（出厂原样，未改）：`config/config.json` 的 `watch.hot_folder` 为 `""`、
  `exiftool.dry_run` 为 `false`、`exiftool.path` 指向 `C:\Program Files\ExifTool\exiftool.exe`——
  K4 的 #108 阻断就来自后两项。
- 结果：K1-K4 全部**未探活**——gpui 窗口的界面呈现本窗口读不到（UIA descendant=0、`PrintWindow` 黑帧），
  按「无证据不虚构」不填任何通过项。自动化侧只到上文那条真实传输级待机连通用例。

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
  - **#57 日志页联动状态核销**：
    - E1/E2/E3/E5 视觉呈现与交互功能核销通过；
    - E4 历史日志回灌确认存在缺陷（换栈前既有），由独立缺陷票跟进修复；
    - `#57` AC2（大批量日志滚动/浏览不卡顿）：**未探活**（本次目检仅验证小批量即时处理与清空，未灌入大批量日志流，该项 AC 保持未探活，留待后续专项验证，不提前宣布完整通过）。
- 伴随发现与勘误记录：
  1. **清单文案勘误**：原清单中关于主题、语言、日志级别的“下拉菜单”表述为推测失真；实机均为平铺选项组（Segmented/Radio-like buttons），无原生下拉框，本轮已在清单中更正。
  2. **终端黑框弹出**：`photoprivacy-ui` 的入口 `main.rs` 未声明 `#![windows_subsystem = "windows"]`，双击启动时系统默认分配控制台黑窗口，随后弹出 GUI。（勘误，2026-10-30：该形态已随 #99/PR #106 修复，release 构建现在带 `windows_subsystem = "windows"`；本条只作历史记录。）
  3. **初始配置待机行为（#100，代码已实现，实机未探活）**：打包附带的 `config/config.json` 默认 `"watch.hot_folder": ""` 为空时，Worker 按 #100 起保持 IPC 连接并待机，UI 引导前往配置页选择监控目录，保存有效目录后经热重载启用监控。本条目前只有代码与集成测试口径，尚无实机目检证据，复验前不得记作通过。#113 已把这条路径定形为上文的 K1-K4 四项（实机仍未探活），并把「真实传输级待机连通」补成进 CI 门禁的自动化用例。
  4. **GUI 文本不可划选复制**：gpui 纯文本展示未支持鼠标划选高亮与系统剪贴板复制，报错无法直接复制排障。
  5. **主题设置入口冗余**：右上角快捷切换与设置页平铺切换并存，交互体验重叠。
