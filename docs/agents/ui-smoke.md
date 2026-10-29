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
| A1 | 配置页主题下拉三态（system/light/dark）可见 | | |
| A2 | light↔dark 切换即时生效，无需重启 | | |
| A3 | 明暗与语言双轴独立：切主题不改语言、切语言不改主题 | | |
| A4 | 切 dark → 重启后保持（config.json `ui.theme_variant` 持久化） | 未探活 | 需真实点击切换主题；仅验证过 dark 配置启动不崩 |
| A5 | 运行中手改 config.json 主题 → 约 1s 内热重载生效 | 未探活 | 热重载无进程外可观测证据，需目检配色变化 |

### B 语言（10 locale 复用）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| B1 | 语言下拉逐一切换 10 locale，导航/标题/行标签/按钮即时翻译 | | |
| B2 | （并入 B1）页面文案跟随当前 locale | | |
| B3 | 选 العربية 布局翻转为 RTL（`ui/crates/photoprivacy-ui` 方向随 `ui.locale` 驱动） | | |
| B4 | 切 English → 重启后保持（`ui.locale` 持久化） | 未探活 | 需真实点击切换语言后重启目检 |
| B5 | 下拉选项文案（主题/日志级别）随当前语言刷新 | | |

### C 中键滚动（5 项）——不迁移

- **处置（#49 D11）**：旧栈观感专项，新栈裁决不迁移，五项一并核销为「不迁移」。新 UI 长内容滚动走滚轮/滚动条。

### D 侧栏拖宽（3 项）——不迁移

- **处置（#49 D11）**：同 C 组，核销为「不迁移」。新 UI 侧栏宽度固定。

### E 日志流（新 UI 日志页，T8 审计尾巴双通道）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| E1 | 空日志时日志页显示空态占位 | | |
| E2 | 向监控目录放入照片，Worker 处理后日志页即时出新条目（无需手动刷新） | | |
| E3 | 不同事件类型的呈现可区分（级别/事件类型） | | |
| E4 | 清空后列表立即空；重启后旧日志不回灌 | 未探活 | 清空需点击；进程级无法断言列表内容 |
| E5 | 日志级别切换生效（高档位下低级别事件不再出现） | | |

### F 按钮与窗口（新栈锚点口径）

| # | 新栈对应 | 结果 | 证据 |
|---|---|---|---|
| F1 | 导航/ghost/primary/danger 按钮 hover 有反馈（新栈 theme.rs token，按 #49 T4 六锚目检） | | |
| F2 | 按下态有视觉反馈，松开恢复 | | |
| F3 | 不可用操作呈禁用态且点击无响应（如服务未安装时的启动/停止） | | |
| F4 | 键盘 Tab 焦点有可见指示 | | |
| F5 | 标题栏拖动/双击最大化/三键功能正确；关窗为驻守（见 T3） | | |

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
| T1 | 托盘图标出现，菜单 Open Window 唤起主窗口 | 未探活 | 菜单项需真实点击；唤起链路本身已由 T5 等价覆盖（ShowWindow 管道 + 窗口重建） |
| T2 | 托盘 Exit 退出干净（进程结束，无 Worker 孤儿） | 未探活 | 菜单项需真实点击 |
| T3 | 关闭主窗口进程驻守（销毁+重建；主题/连接状态/日志缓冲/服务快照保留，位置/尺寸回中——#59 已登记取舍） | 通过 | 2026-10-28 实机：`CloseMainWindow()` 后进程存活、无 PhotoPrivacy 主窗、Worker 保持 1 个；再启动第二实例（exit 0）主窗重建 |
| T4 | `ui.hide_main_window_on_startup=true` 时启动仅托盘不弹窗 | 通过 | 2026-10-28 实机：hide=true 启动后无 PhotoPrivacy 主窗（`MainWindowTitle` 仅托盘辅助窗）、进程与 Worker 正常 |
| T5 | 二次启动不重开：第二实例退出并唤起既有窗口（`Global\PhotoPrivacyUi_Instance`） | 通过 | 2026-10-28 实机 ×3：第二实例 6s 内退出（exit code 0），第一实例存活且主窗可见（title=PhotoPrivacy） |

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
- 未探活（需人工/系统级动作，清单见上文各表）：A1-A5、B1-B5、E1-E5、F1-F5、S1-S8、T1/T2；
  其中 S2 服务安装为系统级提权动作，归用户执行。
