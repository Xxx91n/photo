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
| B3 | 选 العربية 布局翻转为 RTL（`ui/crates/photoprivacy-ui` 方向随 `ui.locale` 驱动） | 通过 | 2026-10-29 实机目检：选 العربية 布局翻转为 RTL 镜像排布。**已知限制（#154，2026-11-04 定位）**：本项只判布局镜像；镜像后阿拉伯文的**字形**（连写与词序）因上游 gpui 的 Windows RTL 落位缺陷全部不可读，触发条件与影响面见下文 K 组「#154 已知限制」段——那是上游缺陷，不据此把 B3 记失败 |
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
| E4 | 清空后列表立即空；重启后旧日志不回灌 | 通过（探针级 + 2026-11-05 实机全链路复测） | 2026-10-29 实机目检：点击清空后列表立即变空，但重启之后旧日志又重新出现（核查为旧时间的日志，属于新旧 UI 重构前就存在的既有 BUG；复现步骤：放入图片产生日志 → 点击清空 → 关闭并重启 UI → 旧日志回灌）。复测（2026-11-01 #139）：随 #102 B′ 方案（清空写水位、重启按行内 UTC 时刻过滤）修复后做探针复现——**取证通道是 Rust 行为测试，非实机 UI 目检**：真实 `AuditFeed` 双通道 + `ClearWatermark` 落盘，同进程清空 → 重启读回水位 → Worker 末 50 行窗口的已清历史不回灌、清空后新行照常交付；含跨日、归档改名、#129 分叉时窗几何与 IPC 零协议守卫（`e4_probe_…`/`fork_window_…`/`method_surface_…` 在 CI 各自 ok）。CI run [41293932607](https://github.com/Xxx91n/photo/actions/runs/41293932607)（head `50d4fcdd`，PR #143）rust/test/ui-check/markers 四 job 全绿、全 crate 0 failed。**严格口径实机复测（2026-11-05，与 #57 AC2 同轮同构建，head `b72d489` / Release run 41854940207，帧见下文「AC2/E4 组产物记录」）**：622 条灌入后点「清空」→ 计数 0 条 + 空态占位「暂无审计条目」（帧 `f01-after-clear.png`），`config` 同目录落盘 `logs-clear-watermark.json`（00:10:57，51 B）；`Stop-Process`（TerminateProcess，与 `taskkill /F` 同语义）完全退出 UI+Worker（进程数归 0）→ 重启 UI → 仅新会话自身 2 条（ExifTool started / Service started），622 条已清历史不回灌（帧 `f02-restart-no-refill.png`）；重启后追加 1 条新日志 4 s 内出现在列表顶部「Fresh line after restart (E4)」（帧 `f03-new-line-appears.png`）。「点清空 → 完全退出并重启 UI → 旧日志不回灌 → 新日志正常追加」完整 UI 路径通过 |
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
| S5 | 服务模式下改配置保存 → ReloadConfig 热生效 | 通过（当前 head，非提权 UI 已复测） | 2026-11-03 三轮实机，产物与逐帧证据见下文「S5 复测产物记录」三节：一轮基线 `fb388507`；二轮当前 head（`main@13a831c` + 发布脚本修复 #164，run 41563514193）复测——服务模式 UI 把监控目录从空改为 `…\photo-smoke-85head\hot-s5` 后绿字「已保存 · Worker 已重载」，**0.52 s 配置落盘 / 0.62 s 热生效**（读侧预算 10 s，`S5r2-02-e2e-timing.txt`）；投放样本由服务 Worker 清洗 3,648→626 B（`S5r2-03-logs-after-reload.png`、`S5r2-07-audit-records.txt`）；**标准用户**（`ppverify`，Medium IL、非管理员）经服务管道 `GetStatus` + 两次 `ReloadConfig` 全 `ok:true`/`mode=service`，往返 0.90 / 0.50 s（`S5r2-05-nonadmin-probe.txt`）；三轮以**标准用户身份启动 UI 本体**（`ppverify`，会话 2）重跑同一动作：0.80 s 内落盘并切换、样本 3,648→626 B、`ExifTool=13.60`——`S5r3-02`、`S5r3-03`、`S5r3-05` |
| S6 | 停止服务 → 状态回落 | 未回填 | 未探活（#88 范围外） |
| S7 | 卸载服务 → 回到未安装态 | 未回填 | 未探活（#88 范围外）；本轮收尾用 `install-service.ps1 -Action uninstall` 卸载并复核 SC 1060，但那是脚本通道，不是 S7 要验的 UI 提权链路 |
| S8 | 服务模式日志通道在日志页可见（与后台模式区分） | 通过 | 2026-11-03 桌面截图 + 文件系统取证（帧 `S8-logs-page-service.png`）：同一服务模式会话切日志页，04:09:55「Service started mode=live」、04:09:56「ExifTool started」在列；04:13:08–09 投入 `hot/` 的带 Artist/Copyright 样本被服务 Worker 就地清洗 3648→626 B，日志页实时出「Cleaned attempt=1/wipe_result=Cleaned, Modified」×2 +「backup_sidecar_version… content_diverged_sidecar_written」，exiftool 复查标签已空；条目与 `pkg/hot/_audit/audit-2026-11-03.jsonl`、`processed.ndjson`（两文件 mtime 均落在投放后的 04:13）及 ProgramData 服务日志同刻。与后台模式的区分锚在连接态：本会话状态卡是「PhotoPrivacyCleaner.Service · 服务模式」+ Worker 卡片「服务 IPC=可达」（K1 记录的后台形态是 Background · 不可达） |

> **macOS 探测项验收口径（#172）**：S1/S3/S6/S7 等服务页探测项在 macOS 侧不以人工读屏为准，以 `scripts/verify-launchd-service.sh` 的修后对拍为准——`ui_probe_report` 以 **GUI 用户**语境跑 `PhotoPrivacy --service-probe-report`（与截图同语境），与 CLI `probe_state`（`launchctl print system/<label>`，显式系统域）判定一致即探测项通过；截图与 legacy `launchctl list` 与 `launchctl print system/<label>` 对照留证仅供目检。对拍链由 `.github/workflows/launchd-verify.yml`（GitHub macOS runner 虚拟环境）执行。

### 连接失败三态提示 3 项（#169 定形）

> 场景：连接编排按 #82 顺序回落失败后，概览页状态卡按最近一次真实失败原因分类呈现，
> 不再把「服务授权拒绝」混同为「Worker 缺失」的 os error 2（`ui/crates/photoprivacy-ui/src/worker_connection.rs`
> 编排收集 → `ConnectionSnapshot.connect_failure` 广播 → 概览页 `connect_failure_text` 呈现；
> 连接顺序与轮询预算不变）。三态判据与文案键：服务端点 DACL 拒（raw 5，端点存在的证明）→
> `status.connect_denied`（warning 色，CodeRabbit 返修后**按平台分键**：Windows 指引加入安装脚本的
> `-AuthorizedUsers`，Unix 用 `status.connect_denied_unix` 指引加入服务授权组——0660+共享组边界）；
> 后台拉起未就绪（spawn 失败 / 已退出——含 #163 单写者
> 准入门让位 / 5s 超时）→ `status.worker_startup_failed`（附 spawn 错误或退出码细节）；
> 未找到可拉起的 Worker 程序 → `status.worker_not_installed`。心跳连上即清空（`apply_success`），
> 心跳掉线不清。三态的 Rust 行为侧（分类判据、退出码上报、10 locale 键集全等）由 CI 测试守护
> （`classify_connect_failure` 三态/退出码/未就绪、`is_service_access_denied` raw 5 判定、
> `connect_failure_texts_map_to_distinct_guidance` 文案映射）；本表锚实机目检（#169 AC3，
> Windows 平台口径）。
>
> **本表结论的构建口径（2026-11-03）**：`workflow_dispatch` 触发 release.yml 于本票分支
> `t169-conn-fail-semantics`（run [41614025539](https://github.com/Xxx91n/photo/actions/runs/41614025539)），
> `version=0.1.0-169smoke`、`create_release=false`——产物仅留 artifact，未发布 GitHub Release；
> 取 `win-x64` 产物实机测试。产物对应 head `9aa1c27`（即「授权拒绝指引按平台分键」返修后），
> 含全部 UI 行为改动（实现 `b718e63` + review 修复 `97be999` + fmt 对齐 `0c7b7f2` + 平台分键
> `9aa1c27`）；其后 `0e51291`（`Design(#32)` 注释治理 + merge main）与 `3016a37`（本表回填）
> 均无行为 diff，故目检结论对当前 HEAD 依然成立。本地零编译口径（同 #168 虚拟环境验证先例），
> agent 桌面截图自查（同 #125/#88 先例，用户已批准该取证方式）；产物解包于本机私有临时目录，
> 测试后服务已卸载、临时标准用户已删除、进程与注册表授权键已清，环境无残留。

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| C1 | 装有服务的机器上，未列入 `-AuthorizedUsers` 的标准用户启动 UI，等回落链走完（约 5 s+） | 通过（2026-11-03，`9aa1c27` 构建） | 实机（Windows，本机）：临时标准用户 `pp169-out`（仅 Users 组）+ 装服务（`-AuthorizedUsers pp169-ok`，2 授权 SID）下启动 UI → 状态卡 warning 色警告行逐字「当前账户未被授权使用服务模式，请联系安装者将账户加入 -AuthorizedUsers」，无任何 os error 2；后台 Worker 确实撞 #163 让位退出（服务侧审计 `instance_conflict`/`single_writer_gate`，`hot/_audit/audit-2026-11-03.jsonl`）；**对称验证**：同服务下授权用户 `pp169-ok` 启动 UI → 「已连接 · PhotoPrivacyCleaner.Service · 服务模式」（绿点、服务 IPC 可达、往返 495 次），无误报——截图 `C1-unauthorized.png` / `C1-symmetric-authorized.png`（桌面取证归档） |
| C2 | 真实缺 Worker 场景（发布包内移走 `PhotoPrivacyWorker.exe`、无服务）启动 UI | 通过（2026-11-03，`9aa1c27` 构建） | 实机（Windows，本机）：确认服务未装（sc query 1060）+ 移走 Worker.exe 后启动 UI → 状态卡红色错误行逐字「连接 Worker 失败：未找到 Worker 程序，请安装或修复后重试」，不误报为未授权——截图 `C2-worker-missing.png` |
| C3 | 断连恢复：UI 保持运行时后台 Worker 就绪（或账户获授权后重启 UI 连上服务端点） | 通过（2026-11-03，`9aa1c27` 构建） | 实机（Windows，本机）：C2 同一 UI 进程保持运行，放回 Worker.exe 并以 `--mode background` 拉起 → 状态回「已连接 · PhotoPrivacyCleaner.Background · 后台模式」（绿点、协议 v1、往返 8 次），分类文案消失（`apply_success` 清空生效），仅剩出厂待机的正常引导行「请前往配置页设置监控目录」——截图 `C3-recovered.png` |

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
| K2 | 状态栏出现 warning 色提示「请前往配置页设置监控目录」（`status.configure_watch_folder`，切 locale 后各自可见） | 通过 | 2026-11-03 桌面截图目检：10 个 locale 逐一取概览页帧（`K2-locale-<code>.png` ×10，另附逐行拼合的 `K2-hint-strip.png`）。切语言走「外部改写 `config.ui.locale` → 500 ms 内容指纹轮询回灌」（`config_service.rs` 的 `ExternalReload`，代码内的正式路径），不点坐标，因此避开指引第 7 步记的「导航项宽度随 locale 漂移」。10 行全部是 warning 色（琥珀）落在连接态下一行：zh-CN「请前往配置页设置监控目录」、en「Go to Settings to set the watch folder」（与 `locales/en.json` 该键逐字一致）、ja「設定ページで監視フォルダーを設定してください」、ko/de/fr/es/pt/ru 各随其 locale；ar 在 RTL 镜像布局（导航靠右、正文右对齐）下同一提示。只判「该 locale 的文案存在、渲染在判据位置且颜色正确」——「通过」按票面判据「切 locale 后各自可见」计（10/10 可见）。**字形本身不判**：ar 帧上的阿拉伯文呈孤立字形、整行读起来词序是反的（`K2-hint-strip.png` 末行可见），而 B 组 B3 只覆盖「选 العربية 布局翻转为 RTL」、不含字形连写，本轮记录时这条观感还没有票覆盖，只如实记录、不据此判 K2 失败，也不自行开票。**后续（2026-11-04）：已由 #154 定位为上游 gpui 的 Windows RTL 落位缺陷并登记为已知限制（见上文「#154 已知限制」段），K2 的「可见」判据边界不变。**取证后 `config.json` 已逐字节还原（sha256 回到出厂 `aa2d36ee…`） |
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
  （该结论是 2026-10-31 的事实；#108 落地后的判据变化见本节末尾「#108 落地后的判据变化」。）
- **#117 已修（2026-11-01 实现窗口）**：待机态 `StopAsync` 挂住曾由 .NET 10 的
  `SemaphoreSlim.Dispose` 语义导致——Dispose 只清空等待队列，不会让挂起的 `WaitAsync` 完成，
  首次配置待机这条路径靠「Dispose + catch ObjectDisposedException」兜不住。
  修复分两步：停机先显式取消待机等待（`_standbyStopCts.Cancel()`），且信号量 `Dispose` 必须等
  `ExecuteAsync` 观察到取消并返回之后再执行（Cancel 的取消回调与 Dispose 清空等待队列存在竞态——
  Dispose 抢跑会让等待者永久挂起，CI 稳定复现、本地间歇复现）。`ExecuteAsync` 待机分支立刻收敛，
  不再等满 10 s（`HostOptions.ShutdownTimeout`）。自动化侧已由 #113 待机用例改回
  「断言 `StopAsync` 在预算内完成」（CI 绿，run 号见 PR 正文）；实机停机证据（托盘 Exit /
  停服务）待 #117 PR 合并后回填 T6，见下文「#117 实机停机证据」段。

### #108 落地后的判据变化（代码侧已改，实机未重取）

> 写在这里是为了不把上面的历史证据读成「#108 之后仍然成立」。以下变化都在代码里，
> CI 侧由 `PhotoPrivacy.Core.Tests`（校验分层、模板一致性）与 `PhotoPrivacy.IntegrationTests`
> （非 dry-run 待机→启用、缺 ExifTool 拒绝）覆盖；**界面呈现与真裸机形态仍需按本文件规矩重取**。

1. **出厂包自带 ExifTool**：发布脚本把固定版本解到 `<包根>/ExifTool/`（Windows：`exiftool.exe` +
   `exiftool_files/`；Linux/macOS：`exiftool` + `lib/`，需系统 `perl`），模板 `exiftool.path` 改为空串，
   空串 = 运行时探测（内置优先，其次系统安装位置）。因此「出厂原样」的字节核对口径不变，
   但 `exiftool.path` 的期望值从 `C:\Program Files\ExifTool\exiftool.exe` 变成 `""`。
2. **真裸机 K4 的正向证据可重取**：不再需要「本机恰好装了 ExifTool」这个前提；
   下一轮实机取证应在没装 ExifTool 的机器上用出厂包跑 K1–K4，并在证据列写明「宿主未安装 ExifTool」。
3. **失败原因常驻**：`exiftool.path not found` / `exiftool_files directory missing` 这类重载失败原因
   不再随 3 s `SAVE_STATUS_TTL` 消失——概览页（`reload-blocked`）与配置页状态行都保持到下一次保存成功。
   判据：故意把 `<包根>/ExifTool/exiftool.exe` 改名后保存有效目录，概览页应出现常驻的失败行，
   点击可跳配置页；恢复文件并重新保存后该行消失。
4. **校验分层**：待机态（空 `hot_folder`）只跳过监控资源校验（ExifTool、`exiftool_files`/`lib`、
   隔离/审计目录、备份目录冲突）；连接池大小与被禁的 `extra_exiftool_args` 在任何形态下照常 fail-closed。

### #117 实机停机证据（待回填）

> **目的**：T6 的取证段。自动化侧（#113 待机用例）已断言「待机态 `StopAsync` 在预算内完成」并进 CI 门禁；
> 实机形态（托盘 Exit / 停服务）仍需按桌面截图 / 进程观察通道补证。
> 判据：出厂空 `hot_folder` 待机态下，托盘 Exit 或停服务后进程在
> `HostOptions.ShutdownTimeout`（10 s）内退出，无残留 Worker / UI 进程；观察方式同 T2。
> **未回填前不得把 T6 记作通过**（无证据不虚构）。待 #117 PR 合并后在独立取证窗口补证回填。

### #154 已知限制：阿语文案呈孤立字形、整行词序反（上游 gpui Windows RTL 落位缺陷）

> 定位窗口：#154（2026-11-04）。**UI 层观感证据：本票在当前 main 自取三帧**（Release run
> [41854940207](https://github.com/Xxx91n/photo/actions/runs/41854940207) / ref main /
> head `b72d4896` / version `0.1.0-verify154` / `create_release=false` 的 `release-win-x64`
> artifact；win-x64 job success（其余 test/linux-x64/linux-arm64/osx-x64 五腿也 success，
> 仅 osx-arm64 一腿 cancelled，故 run 总结论为 failure——与 Windows 产物无关）；
> 本地零编译）——总览（琥珀待机提示）/ 规则页长文案 / 配置页长文案各一帧：ar locale 下
> `--open-page` 启动即出页、整屏 `CopyFromScreen` 取帧，出厂 `config.json` 取证前后逐字节
> 原样核对与还原（`C1371F77…`）。#113 第二轮的 `K2-locale-ar.png`（取证基线 `fb388507`）
> 作历史参照保留（锚：`git show 4db2583:docs/agents/ui-smoke-evidence.sha256`）；
> 机理证据来自与 gpui 同调用序列的 DirectWrite 探针（Add-Type 内存编译，本地零编译，走 OS API 不涉仓库构建），
> 证据清单见本节末尾。

- **根因（票面三选一的判决：不是整形缺失，不是字体链，也不是 DWrite 层 bidi）**：
  `gpui-pre-windows`（Zed gpui 的 Windows 文本系统，本仓锁 `=0.3.10`）把 `IDWriteTextLayout::Draw`
  回调交付的 glyph run 转成 `ShapedRun` 时，**忽略 `baselineOriginX/Y`，也不读 glyph run 的 `bidiLevel`**，
  只用自维护的 `context.width` 从 0 向右累加（`direct_write.rs` 的 `DrawGlyphRun`）。
  DirectWrite 对 RTL run（`bidiLevel=1`）按「笔位自右向左推进」的语义交付——探针实测各 run 的
  基线原点递减（237.74 → 211.69 → 208.14 → …）——该累加把这些 run 当 LTR 从左往右铺：
  **整行词序镜像、词内字母序镜像**；字母的上下文连写笔画朝向与镜像后的邻居相反，视觉上字母彼此断开，
  在小字号下呈「孤立字形」观感。#113 第二轮记录的三个症状（孤立、断开、词序反）由此一并解释。
- **探针判决**（复刻 gpui 调用序列：`CreateTextFormat("Microsoft YaHei UI", locale=zh-CN)` →
  `CreateTextLayout(INFINITY, INFINITY)` → `Draw`；取证机 = Windows Server 2022（10.0.20348，zh-CN 区域，
  与 DWrite 同一代码基线），该机系统 UI 字体（`SPI_GETICONTITLELOGFONT`）即 `Microsoft YaHei UI`、
  用户默认 locale 即 `zh-CN`——与 gpui 在它上面的实际解析一致）：
  - **整形正常**：7 个阿语词 run 的实发字形 ID ≠ cmap 默认字形（如 U+064A 的 cmap 2347 → 实发 2292），
    即字母按上下文选形、不是孤立形式的码位；全部 run 的 `clusterMap` 严格 1:1（该文案无连字/合并簇）；
  - **DWrite bidi 正常**：`status.configure_watch_folder` 的阿语文案切分 13 个 run（7 词 + 6 空格），全部
    `bidiLevel=1`、基线原点递减；hitTest（证据 D 组）实测逻辑位 0 → x=237.74（行右端）、
    逻辑位 42 → x=30.06（行左端），视觉序正确；
  - **字体链正常**：系统字体回退生效——阿语字形由一个 5344 字形的回退 font face 供给，
    空格等由 YaHei（30209 字形）供给；每 run 的 `clusterMap` 从 0 升序，
    gpui 的 `ClusterAnalyzer` 按 usize 升序差值计算、不触发下溢；
  - **缺陷只在转换层**：gpui 的 `ShapedRun` 把逻辑位 0 的字形放到 x≈0（行左端），
    与 DWrite 的正确落位逐字镜像——词序反与「断开」的根源在这里。
- **触发条件**：Windows 构建（`gpui-pre-windows`；0.3.10 实测，上游最新 0.3.11（2026-11-02 发布）
  的同一 `DrawGlyphRun` 回调逐行未改，见证据清单的 diff）× 当前 UI 的任一 RTL 文案。
  其中「`is_rtl` 驱动的布局镜像」只有 `ar` locale 触发（`i18n.rs` 的 `is_rtl` 只判 ar）；
  但「字形缺陷」命中的是**文本内容**而非界面语言——LTR locale 的界面文案不受影响
  （探针英文对照：`bidiLevel=0`、起点 x=0，与既有 K2 九个 LTR locale 帧一致），而语言选择器的
  阿语自称标签 `العربية`（`config.rs` 的 `LOCALES` endonym，不随界面语言翻译，任何 locale 的
  配置页都渲染）同样命中本限制——探针 B 组即该字面串（`bidiLevel=1`，同一管线，无 locale 分支）。
- **影响面**：机制上命中应用内**全部**阿语文案——不止 `status.configure_watch_folder` 这条首次配置引导
  （`K154-04` 总览帧：琥珀提示行仍在判据位置渲染、导航右侧镜像，但字形不可读），
  规则页/配置页的长说明文同样形态（`K154-05`/`K154-06` 帧：页面结构完整、文案全部镜像错排——
  页面级长文案的现状即本限制的直接证据）；混入数字或拉丁 token 的行同样错排（探针 E 组实测：
  `'العربية ExifTool 13.60'` 的正确视觉序是右端阿语（`bidiLevel=1`）+ 左端拉丁段（`bidiLevel=0`），
  gpui 转换层对两种 level 一视同仁地按交付顺序向右累加，整行对调错排）
  （#100 的引导对 `ar` locale 因此失效）。B3 判的「布局镜像」（导航/对齐的 flex 方向翻转）是
  app 层实现，不经过该转换层，不受影响、仍然通过（三帧的导航右侧镜像即其通过证据）。
- **修复路径与「不修」决定（2026-11-04，#154 票面登记）**：缺陷在上游依赖内部（app 代码不可及），
  app 层也没有可行 workaround——对文案预反转会被 DWrite 的 bidi 重新处理、并按反转后的邻接选形，
  仍不可读；改用呈现形式码位是弃用做法且 bidi 类不变。故按票面登记为**已知限制（不修，等上游）**：
  后续升级 `gpui-pre` 版本时，必须先在 release 构建上对 ar 帧复验「连写 + 词序」再考虑撤销本条；
  撤销前 K2 各 locale 的「可见」判据照旧（2026-11-03 裁决的边界不变），不因字形质量记失败。
- **证据**（探针与日志不入库，存取证机 `C:\Users\Administrator\photo-smoke-154\evidence\`，
  哈希锚见 `docs/agents/ui-smoke-evidence.sha256`，实测 7/7 `OK`）：
  `K154-00-probe-result.txt`（五组探针完整日志：A 阿语提示 13 run / B `العربية` 字面 1 run /
  C 英文对照 1 run / D 同文案有限宽度布局的 hitTest 视觉序 / E 阿语+拉丁+数字混排）、
  `K154-01-ar-probe.cs` + `K154-02-run-probe.ps1`（探针工具）、
  `K154-03-upstream-0310-0311.diff`（上游 0.3.10→0.3.11 全量 diff，`DrawGlyphRun` 回调区域无改动）；
  UI 观感帧 `K154-04-overview-ar-hint.png`（总览：琥珀阿语提示行在连接态下一行，导航右侧镜像）、
  `K154-05-rules-ar-longtext.png`（规则页：五族表格 + 副标题长文案，标题 `القواعد` 帧上呈镜像序）
  `K154-06-config-ar-longtext.png`（配置页：分组 + 输入框 + 说明行长文案）——三帧均为
  当前 main 二进制、ar locale 下所取，帧内文字本身即镜像错排现状的直接证据；
  历史 UI 帧 `K2-locale-ar.png` 等仍在 `photo-smoke-113r2\evidence\`
  （锚：`git show 4db2583:docs/agents/ui-smoke-evidence.sha256`）。

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
   **判据边界（2026-11-03 裁决，2026-11-04 #154 定位）**：本项判的是「各自可见」，**不含字形连写质量**。ar 帧上阿拉伯文呈孤立字形、
   整行词序读起来是反的，属另一层缺陷——已由 **#154** 定位为上游 gpui 的 Windows RTL 落位缺陷并登记为已知限制
   （触发条件、影响面与证据见上文「#154 已知限制」段），不据此把 K2 记失败；
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
      该观感不在 B 组 B3 的覆盖范围内（B3 只判「选 العربية 布局翻转为 RTL」），当时无票承接；**2026-11-04 已由 #154 定位**：
      上游 gpui 的 Windows RTL 落位缺陷（整形/DWrite bidi/字体链三者经探针排除），登记为已知限制——
      根因、触发条件与证据见上文「#154 已知限制」段。本轮的记录仍只约束 K2 判据（可见，不含字形质量）。
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
>
> **基线口径（必读）**：本节结论的适用范围是**产物 head `fb388507` 的代码**。#108（PR #155）此后
> 并入 main，改动了 UI 的配置保存路径与校验（`ui/crates/photoprivacy-ui/src/config.rs` 的
> `exiftool.path` 归一化、`app.rs` 的保存状态、`AppConfigLoader` 的加载校验）——都在 S5 这条链路上。
> 因此本节只能按「旧基线证据」引用；**当前 head 的缺口已由下一节（二轮）用当前 head 产物补齐**，
> 两节合起来才是 S5 的完整结论。

- **产物**：复用 Release run [41495388043](https://github.com/Xxx91n/photo/actions/runs/41495388043)
  的 `release-win-x64`（已解压 `C:\Users\Administrator\photo-smoke-113r2\pkg\`，head `fb388507`）。
  与 #85 起始基线 `5298b6466` 的差异经 `git log fb388507..5298b64` 核对**仅 4 个 docs 提交**；
  二进制 sha256 实测与既有记录一致（`PhotoPrivacy.exe a49189b2…`、`PhotoPrivacyWorker.exe 424acf03…`），
  故结论对 **`fb388507` 的代码**成立。本地未编译，包体全部来自远端 CI。
  取证之后 main 又并入 #108（PR #155，改动 UI 保存路径与配置校验），结论的有效范围随之止于
  `fb388507`——当前 head 的复测待办见上方「基线口径」。
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
     「服务模式 / service / 服务 IPC=可达 / 协议 v1 / ExifTool 13.60」——`S5-00-overview-before.png`；
     同会话进程表只有一个 Worker（pid 3496、SessionId 0），UI 未拉起后台 Worker
     ——`S5-07-process-and-service.txt`；
  2. 配置页把「监控目录」从 `…\photo-smoke-113r2\hot` 改为 `…\photo-smoke-85\hot-s5`
     ——编辑态帧 `S5-02-field-edited.png`；UI 防抖后写盘，`config.json` 的 `watch.hot_folder`
     为新值，文件 mtime 06:28:43.853——`S5-07-process-and-service.txt` 的 `--- config ---` 段；
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
     `GetStatus` 1–2 ms；三轮各两次 `ReloadConfig` 的往返落在 **0.44–0.62 s**（锚定的那一轮
     0.59 / 0.55 s，另两轮控制台读数 0.589/0.567 与 0.437/0.623，未入锚），全部回 `ok:true`、
     `mode=service`、`v=1`，无一次触及 10 s 预算——`S5-06-service-pipe-probe.txt`。这是事故形态
     「服务管道 10 s/15 s 无响应」在 Windows 命名管道上的直接反证；
  6. **端到端计时**（第二轮回改为原目录）：粘贴生效后 **0.52 s** 配置落盘、**0.64 s**
     `GetStatus.watchDirectory` 切到新值，同帧界面绿字「已保存 · Worker 已重载」
     ——`S5-08-e2e-timing.png` / `S5-08-e2e-timing.txt`，读侧预算 10 s。该文案是
     `locales/zh-CN.json` 的 `saved_reloaded`，只在 `ConfigEvent::Saved` 的 `worker_error=None`
     （即 `ReloadConfig` 没回错）时出现（`ui/crates/photoprivacy-ui/src/config_service.rs`），
     所以它本身就是「UI 侧没拿到重载失败」的判据
     （文件首行 `watched_before=` 为空串：退格清空与粘贴之间 UI 把空目录落盘了一次，
     worker 因而短暂回待机，属合成输入手法的产物，不参与判定）。
- **收尾状态**：UI 已退出、服务已卸载（SC 1060 复核）、`ServicePipeInstallerSid` 值随 uninstall 清除、
  任务表 0/0 残留；`pkg/config/config.json` 回到取证前的 `watch.hot_folder`（`…\photo-smoke-113r2\hot`），
  二进制未变；`hot-s5\` 内留 626 B 已清洗样本与其备份，全部落在取证目录内，仓库工作区不受影响。
- **边界**：仅 Windows 命名管道。Linux/macOS 服务端点（Unix socket 0660 + 组边界）未实机，仍归 #31/#32；
  CI `test` job 为 ubuntu，服务形态那一行走的是 Unix 策略——两侧证据互补，互不替代。
  **勘误（2026-11-04 #31 窗口）**：Linux 侧已由 #31 的 L 组实机补齐（见下文「Linux systemd 实机」节，
  含 UDS 0660 组边界的成员/非成员双向证据）；macOS 侧仍归 #32。

### S5 复测产物记录（2026-11-03 二轮，#85 窗口：当前 head）

> **为什么要二轮**：一轮的产物 head `fb388507` 早于 #108（PR #155），而 #155 改的正是 UI 保存路径与
> 配置校验（CodeRabbit 对本 PR 的 Major，判定成立）。二轮用**当前 head 的产物**复测同一判据。

- **产物**：Release run [41563514193](https://github.com/Xxx91n/photo/actions/runs/41563514193)，
  ref `t164-exiftool-download-retry`、head `6d1307b`、version `0.1.0-verify85head2`、`create_release=false`。
  该 ref = `main@13a831c` + `scripts/publish-app.ps1` 的 ExifTool 下载重取修复（#164），**运行时二进制与
  main 同源**：本票分支合并 main 后（`caf8578`）与其代码一致，差异只有文档与注释编号。
  同一 ref 的首次 dispatch（run [41559854911](https://github.com/Xxx91n/photo/actions/runs/41559854911)）
  在打包步骤失败——ExifTool 归档哈希不符，即 #164；四个非 Windows 构建成功、win-x64 未出包。
- **二进制**：`PhotoPrivacy.exe` `ef2d3489…`、`PhotoPrivacyWorker.exe` `959e0109…`；包内
  `ExifTool/exiftool.exe` `44b336d7…`，`exiftool -ver` 实测 **13.60**；出厂 `config/config.json` 的
  `watch.hot_folder` 与 `exiftool.path` 均为空串（#108 形态：路径按运行时探测）。
- **取证通道**：同一轮（整屏 `ImageGrab` + 合成点击/键入）；证据 10 个文件存
  `C:\Users\Administrator\photo-smoke-85head\evidence\`（不入库），哈希锚为
  `docs/agents/ui-smoke-evidence.sha256` 的当前版（实测 10/10 `OK`）。
- **步骤与结果**：
  1. 服务安装（`install-service.ps1 -InstallerSid S-1-5-21-…-1002`，即标准用户 `ppverify`）并启动：
     `sc query` STATE 4 RUNNING、Worker pid 11772 在 Services 会话；服务日志 10:11:07 记
     「Worker is ready but no watch folder is configured」——出厂空配置的待机形态（#100/#108），无 FTL；
  2. UI（工作目录 `pkg\win-x64`）走服务端点：连接态「已连接 · PhotoPrivacyCleaner.Service · 服务模式」，
     首帧 `ExifTool=unknown`（桥惰性，尚未解析）且提示「请前往配置页设置监控目录」
     ——`S5r2-00-overview-standby.png`；
  3. 配置页：`ExifTool 路径` 留空、右侧提示「已自动检测到 …\pkg\win-x64\ExifTo…」（#108 的包内探测），
     `监控目录` 为空——`S5r2-01-config-page.png`；
  4. 填 `监控目录` = `C:\Users\Administrator\photo-smoke-85head\hot-s5` → 绿字「已保存 · Worker 已重载」，
     **0.52 s 配置落盘 / 0.62 s `watchDirectory` 切到新值**（读侧预算 10 s）
     ——`S5r2-02-e2e-timing.png` / `S5r2-02-e2e-timing.txt`；`config.json` mtime 10:16:38.302（`S5r2-06`）；
  5. 投放 `sample.jpg`（3,648 B）：10:17:03 被清洗为 626 B，日志页 6 条（`ExifTool started` +
     `Cleaned` ×2 + `backup_sidecar_version…`）——`S5r2-03-logs-after-reload.png`；审计与
     `processed.ndjson` 原样输出——`S5r2-07-audit-records.txt`；
  6. 处理后总览：`ExifTool=13.60`、往返 164、仍服务模式、待机提示消失——`S5r2-04-overview-after.png`
     （顺带复核 #86「版本按需解析」在服务模式的表现：首帧 unknown → 首文件后 13.60）；
  7. **非提权**：标准用户 `ppverify`（`whoami /groups` 为 Medium Mandatory Level、不在 Administrators）
     经计划任务（`/rl LIMITED`）跑传输级探针：`GetStatus` 0.050 s、两次 `ReloadConfig` 0.908 / 0.495 s，
     全部 `ok:true`、`mode=service`、`watch=…\hot-s5`——`S5r2-05-nonadmin-probe.txt`。
     这同时是 #70「普通用户对服务管道往返」的实机证据（票面流转归 #70）。
- **收尾状态**：UI 退出、服务卸载（SC 1060）、`ServicePipeInstallerSid` 随卸载清除、任务表 0/0；
  临时标准用户 `ppverify` 与其配置目录已删除，取证借用的 `SeBatchLogonRight` 已按导出备份回滚，
  计划任务与 `C:\ProgramData\pp85`（探针脚本）已删除；`pkg` 内只剩 hot-s5 的 626 B 已清洗样本与备份，
  全部落在取证目录内。
- **边界**：只覆盖 Windows 命名管道；Linux/macOS 服务端点仍归 #31/#32（**勘误 2026-11-04 #31 窗口**：
  Linux 侧已补齐，见下文「Linux systemd 实机」节；macOS 仍归 #32）。UI 帧本身跑在管理员会话——
  本机 `EnableLUA=0`，管理员账户不存在过滤令牌，非提权面由「独立标准账户 + 传输级往返」覆盖，
  它不是「非提权 UI 点到点」的等价物。**该缺口已由三轮补齐**（见下节）。
- **旧一轮的去向**：`fb388507` 那 12 个文件的哈希不再逐条收录，可从
  `git show 8a56a7e:docs/agents/ui-smoke-evidence.sha256` 取回（图仍在 `photo-smoke-85\evidence\`）。

### S5 复测产物记录（2026-11-03 三轮，#85 窗口：非提权 UI）

> **为什么再来一轮**：二轮的 UI 帧跑在管理员会话（本机 `EnableLUA=0`，管理员账户没有过滤令牌），
> 「非提权 UI 点到点」仍只是传输级等价物。经用户授权用标准账户把这一条补成真·非提权。

- **产物**：仍是二轮包（run 41563514193 的 `release-win-x64`），复制到 `C:\pp85pkg` 并把
  `config` 复位为出厂模板——`C:\Users\Administrator\…` 的目录标准用户不可访问，故换到公共可写位置。
- **身份**：标准用户 `ppverify`（SID `…-1004`，只属 Users；`net localgroup administrators` 实测不含它）；
  服务用 `install-service.ps1 -InstallerSid <该 SID>` 安装；UI 经 `CreateProcessWithLogonW`
  （`Start-Process -Credential`）在该用户下启动，落在交互会话 2——`Get-Process -IncludeUserName` 实测
  `ppverify`，Worker 仍是 `NT AUTHORITY\SYSTEM`（会话 0，只有服务那一个）。
- **步骤与结果**：
  1. 出厂配置（`config.sample.json` 覆盖 `config.json`、`hot_folder` 空）启动：UI 连服务端点、
     提示「请前往配置页设置监控目录」、`ExifTool=unknown`——`S5r3-00-overview-standby.png`；
  2. 配置页：ExifTool 提示「已自动检测到 `C:\pp85pkg\ExifTool\exiftool.exe`」——`S5r3-01-config-page.png`；
  3. 填 `C:\pp85pkg\hot-s5` → 绿字「已保存 · Worker 已重载」，**0.80 s 内配置落盘且
     `GetStatus.watchDirectory` 已切到新值**（采样 100 ms，未拆出「先写后重载」的次序）
     ——`S5r3-02-e2e-timing.png` / `.txt`；`config.json` mtime 10:56:14.123（`S5r3-05`）；
  4. 投放样本 → 服务 Worker 清洗 3,648→626 B；日志页 4 条（`ExifTool started` + `Cleaned`×2 +
     `backup_sidecar_version…`）——`S5r3-03-logs-after-reload.png`，审计原样——`S5r3-06-audit-records.txt`；
  5. 处理后总览：`ExifTool=13.60`、往返 140、仍服务模式、待机提示消失——`S5r3-04-overview-after.png`；
  6. 进程/服务面：UI=`ppverify`（会话 2）、Worker=`SYSTEM`（会话 0）、服务 STATE 4 RUNNING、
     登记 SID=ppverify——`S5r3-05-identity-process.txt`；服务日志 10:54:16 起无 FTL——`S5r3-07-service-log.txt`。
- **收尾状态**：UI 退出、服务卸载（SC 1060）、`C:\pp85pkg` 与 `ppverify` 账户删除、无残留进程。
- **这一轮补上的缺口**：二轮「边界」里写的「非提权面只是传输级等价物」不再成立——非提权 UI 自己完成了
  改配置保存、热重载、日志与总览呈现，服务管道 DACL 对标准用户生效（#70 的最终形态）。
- **方法备注（供后续窗口复用）**：本机 `EnableLUA=0`，`Start-Process -Credential` 会因进程环境里同时
  存在 `Path`/`PATH`/`path` 三个大小写变体而抛「字典中已添加项」的重复键错误；把这三项循环清空后
  重设单一 `PATH` 即可启动。清空只影响当前进程环境，不落盘、不改系统。

### Linux systemd 实机（#31 定形，2026-11-04：WSL2 Ubuntu 安装/卸载/日志全链路）

> **口径变更的授权**：本票标签为 `ready-for-human`（锐化口径是 Ubuntu/Debian 桌面实机）；用户在 #31
> 实现窗口明确改派「使用 WSL 的 Linux 环境测试、只走远端 CI/CD、本地不编译」（2026-11-03 深夜至
> 2026-11-04 凌晨，同 #88/#125 的 agent 自查取证先例）。实机 = WSL2（Ubuntu 24.04.3、systemd 255、
> WSLg 1.0.79），非物理桌面——凡与桌面环境相关的差异按「环境适配」显式登记，不冒充实机桌面。
> 本节是 Windows S 组同判据在 Linux 服务面的对应实机（S5 配置热重载不在本票 AC，仍归 #85 系）；
> 「无证据不虚构」不变。**结论只对本节记录的取证基线成立**（K 组同款口径）。

| # | 操作 | 结果 | 证据 |
|---|---|---|---|
| L1 | 未安装态：服务页显示未安装，启动/停止/卸载置灰、安装可点 | 通过 | 2026-11-04 桌面截图目检：服务页「服务状态：未安装」+「每 3 秒刷新」；启动/停止/卸载置灰、安装钮蓝可点，与 `button_state` 未安装态一致（`ui/crates/photoprivacy-service/src/state.rs`）——帧 `L1-service-not-installed.png`；同时刻 `systemctl is-active` inactive、`is-enabled` not-found、unit 文件不存在（`L1-systemctl.txt`）。同帧总览的后台回落形态见「新观察」第 2 条——`L1-overview-background-fail.png` |
| L2 | 安装服务（脚本通道） | 通过 | `bash scripts/install-systemd-service.sh --gui-user ppverify`：「Added GUI user 'ppverify' to group 'photoprivacy' (IPC socket 0660 group boundary)」+「Installed and started photoprivacy.service」（`L2-install-script.txt`）；同刻 `systemctl status` active (running)、enabled，unit `ExecStart=…PhotoPrivacyWorker --mode service --config … --hot-folder /var/lib/photoprivacy/hot --audit-folder /var/log/photoprivacy`，`/run/photoprivacy/worker.sock` = `srw-rw----` photoprivacy:photoprivacy（目录 0750）（`L2-status.txt`） |
| L2b | 安装服务（UI 安装按钮通道，加测） | 通过 | 服务页点「安装」（pkexec→bash 包内脚本，脚本路径解析 `<exe>/scripts/`）：绿字「成功：systemd service installed」+ 状态「运行中」——帧 `L2b-install-via-ui.png`；同刻 `is-active` active、`is-enabled` enabled（`L2b-systemctl-after-ui-install.txt`）。S2 的提权交互流（pkexec 认证对话框）仍未回填——取证机用 polkit 规则免交互（见产物记录「环境适配」） |
| L3 | 运行态呈现 + UI 启动 | 通过 | 安装后服务页「运行中」、启动/卸载置灰、停止可点（`button_state` Running 分支 `uninstall=false`——运行态不放卸载，L6 实测点「卸载」无动作即此判据）——帧 `L3-service-running.png`；L2b 轮卸载前先经 UI「停止」再装回后，点 UI「启动」→「运行中」，`is-active` active、新 PID 671、`/run/photoprivacy/worker.sock` 重建为 0660——帧 `L3b-service-started-via-ui.png` + `L3b-systemctl-after-start.txt` |
| L4 | 服务运行中 UI 经服务端点连接（服务页/日志页数据来自服务 Worker） | 通过 | 重启 UI 后总览连接态「已连接 · /run/photoprivacy/worker.sock · 服务模式」、上次往返 4 ms；Worker 卡：Worker 模式=服务模式、上报模式=service、服务 IPC=可达、协议=v1、ExifTool=13.60、往返次数=40（单帧读数）——帧 `L4-overview-service-mode.png`；进程面仅服务 Worker（photoprivacy 用户）+ UI（ppverify），UI 未拉起后台 Worker |
| L4b | UDS 0660 组边界（服务端点认证面） | 通过 | `stat`：目录 `drwxr-x---` photoprivacy、socket `srw-rw----` photoprivacy:photoprivacy；组成员 `ppverify`（安装脚本 `--gui-user` 入组）connect+`Ping` 全程通：`{"ok":true,"data":{…,"mode":"service"},"id":"l31","v":1}`（exifToolVersion 13.60、watchDirectory=/var/lib/photoprivacy/hot）；非成员 `outsider` connect 被拒 **EACCES (errno 13)**——`L4b-group-boundary.txt`。这是 S5 记录里「Linux 服务端点（Unix socket 0660 + 组边界）未实机」欠账的实机补位 |
| L5 | UI 停止 → 状态回落 | 通过 | 点 UI「停止」→ 绿字「成功：systemd service stopped」、状态「已停止」、启动/卸载转可点——帧 `L5-service-stopped.png`；同刻 `is-active` inactive、unit inactive (dead)、exit status=0/SUCCESS、`/run/photoprivacy` 随 RuntimeDirectory 回收（`L5-systemctl-after-stop.txt`） |
| L6 | UI 卸载 → 回到未安装态 | 通过（两轮） | 运行态点「卸载」按设计无动作（`button_state` Running 分支 uninstall=false——判据行为，非缺陷）；先停止再点「卸载」→ 绿字「成功：systemd service uninstalled」、状态「未安装」——帧 `L6-after-uninstall.png`（第一轮）与 `L6b-final-uninstalled.png`（L2b 装回后第二轮收尾）；两轮 `is-enabled` not-found、unit 文件与 `/run/photoprivacy` 均删除（`L6-systemctl-after-uninstall.txt`、`L6b-final-state.txt`） |
| L7 | 服务模式日志通道在日志页可见（与后台模式区分） | 通过 | 服务模式会话日志页「来自每日 JSONL 的审计尾巴——本地尾随 + IPC 回填」，15 条含 `Service started mode=live`、`inotify_limits`、`ExifTool started`、投放样本的 `Cleaned attempt=1;wipe_result=Cleaned_Modified`×2 + `backup_sidecar_version… content_diverged_sidecar_written`——帧 `L7-logs-page-service.png`；条目与 `/var/log/photoprivacy/audit-*.jsonl` 原样输出同刻（`L7-audit-records.txt`）：样本 302→142 B、exiftool 复查 Artist/Copyright 已空、`bak/l31-sample.jpg.20261103-162309.bak` 旁路版本写出。与后台模式的区分锚在连接态（L4：服务模式 · 服务 IPC=可达） |

#### L 组产物记录（2026-11-04，#31 窗口：WSL2 Ubuntu 实机，安装/卸载/日志全链路）

- **产物**：Release run [41579559557](https://github.com/Xxx91n/photo/actions/runs/41579559557)
  （ref `main`，head `1880b670`，version `0.1.0-verify162`，`create_release=false`）的 `release-linux-x64`。
  **本票起始基线即 `1880b6705e91…`（开工时的 origin/main），产物与基线同 head**——本地未编译，
  包体全部来自远端 CI。二进制 sha256 实测：`PhotoPrivacy 041ed9f2…`、`PhotoPrivacyWorker f7852f35…`、
  `ExifTool/exiftool 65e17a63…`（`L00-binary-sha256.txt`）。取证期间 origin/main 前进 8 个提交到
  `41c8bdf`（含 #32 窗口的 `--service-probe-report`/`--open-page` 取证链与 #169 连接失败三态分类）；
  经 `git diff 1880b67 41c8bdf` 复核，本节「新观察」第 1/2 条在 `41c8bdf` 上仍成立（locales 仅增
  连接拒绝文案、`endpoints.rs` 改动全在 Windows 通告名面、Linux socket 路径未动）。
- **环境**：Windows Server 2022（OpenStack VM）上的 WSL2；Ubuntu 24.04.3 rootfs（`ubuntu-base`
  6bc2cde3…，`wsl --import`）+ systemd 255、kernel `6.18.54.1-microsoft-standard-WSL2`；
  WSLg 1.0.79。取证用户 `ppverify`（标准用户，只属自己组+服务组）；非成员对照用户 `outsider`。
  `systemctl is-system-running` = degraded，唯一失败单元 `kmod-static-nodes.service`（WSL 无
  kmod 静态节点，噪声）；除此 systemd 面无异常。
- **取证通道**：整屏 PIL `ImageGrab` + 合成点击（`SetCursorPos` + `mouse_event`，#88 同款；
  工具 `photo-smoke-31\tools\winshot.py`，`SetWindowPos` 按 #88 教训显式声明 argtypes）；
  截图与文本取证存 `C:\Users\Administrator\photo-smoke-31\evidence\`（26 个文件，不入库），
  哈希锚 `git show 389f6bf:docs/agents/ui-smoke-evidence.sha256`（#154 轮起清单只装当前一轮，
  #31 的 26 条锚已随轮换转入该 git 历史版）。WSLg 窗口以「(Ubuntu)」后缀出现在 Windows 桌面，
  同一通道可截可点；导航/按钮坐标按 #88 的暗像素聚类标定（导航：总览 661/配置 709/服务管理 769/
  日志 829/规则 877，y≈235；服务页按钮行 y≈380/447/507）。
- **环境适配（如实登记，不虚构）**：
  1. **pkexec 免交互**：polkit 规则 `/etc/polkit-1/rules.d/10-photoprivacy-verify.rules` 允许
     `ppverify` 经 `org.freedesktop.policykit.exec` 免认证（仅限本取证机）。S2 提权交互流因此
     仍按 Windows 先例记未回填；UI 的安装/启动/停止/卸载命令通道本身是实打实走 pkexec 的。
  2. **gpui 需 X11 后端**：WSLg 下 gpui 的 Wayland 后端不出窗（进程活着、无 surface），以
     `WAYLAND_DISPLAY=`（清空）强制 X11 后端后窗口正常映射。见「新观察」第 5 条。
  3. **config 对齐**：取证前把发布模板 `config.json` 的 `watch.hot_folder`/`audit.log_directory`
     置为 `/var/lib/photoprivacy/hot`/`/var/log/photoprivacy`（与 install 脚本默认 CLI 覆盖一致），
     使 UI 日志页读到服务审计目录——`L2-config-used.json` 记录实际取值。
  4. **WSL 空闲回收 VM**：无前台进程约 8 s 后 WSL VM 终止（journal 出现 `-- Boot` 重置）；服务
     `enabled` 因此在每次 WSL 启动自启（`L9-journal-complete.txt` 的 Boot 段）——顺带构成
     「重启后服务自启」的旁证。取证期间以常驻 `sleep infinity` 保活，避免会话中途丢状态。
- **结果**：**L1/L2/L2b/L3/L4/L4b/L5/L6/L7 全部通过**（逐项证据见上文 L1-L7 行）。
  全生命周期 journal（6 次 Started/6 次 Stopped，含脚本安装、UI 启停、UI 安装卸载各轮）原样留存
  `L9-journal-complete.txt`（80 行）。
- **本轮新观察（不自行开票，是否成票/是否修待用户裁决）**：
  1. **服务页「安装服务」描述文案在 Linux 仍写「注册为 Windows 系统服务」**：
     `service.desc.install` 在 10 个 locale 全部硬编码 Windows 字样（如 zh-CN「将 PhotoPrivacyWorker
     注册为 Windows 系统服务」、en「Register PhotoPrivacyWorker as a Windows system service」），
     Linux/macOS 实机均失实。可选修法：文案平台中性化（「注册为系统服务」，10 locale 一词改动），
     或占位符按平台替换（systemd/launchd/Windows 服务，需动 i18n 取用点）。两轮截图均在帧内
     （`L1-service-not-installed.png` 等）。
  2. **Linux 后台模式 Worker 的 socket 目录对普通用户不可建**：契约双端锁定背景端点
     `/run/photoprivacy/worker-background.sock`（`WorkerIpcEndpointNames.cs`/`endpoints.rs`），
     但 `/run/photoprivacy` 只由 systemd `RuntimeDirectory` 在服务模式创建（0750 服务用户）；
     普通用户拉起的后台 Worker `Directory.CreateDirectory("/run/photoprivacy")` 吃 EACCES，
     UI 连接编排只见 ENOENT——未安装态的总览回落不到「后台模式」（帧
     `L1-overview-background-fail.png`：「重连中 · …worker-background.sock · 后台模式」+
     「连接 Worker 失败: No such file or directory (os error 2)」）。对照 macOS 背景端点在用户家目录
     （`endpoints.rs` 测试 `macos_socket_paths_live_under_user_home` 已钉）。候选方向：Linux 背景
     端点改 `$XDG_RUNTIME_DIR`（或 tmpfiles.d 预建目录）——属 IPC 契约变更，需动
     `docs/agents/ipc-contract.md` 与双端测试，不在本票 AC 内。
  3. **root-owned 安装目录下后台 Worker 的 #162 门首拍噪声**：`InstanceConflictAudit.LoadConfigFromArgs`
     对空 `audit.log_directory` 解析到 `<exe_dir>/hot/_audit` 再 `CreateDirectory`，/opt 安装时
     ppverify 无写权限 → 抛异常被 Program.cs:121 捕获记 ERR「config could not be resolved for the
     single-writer gate」。进程不退出（门按 fallback 身份获锁），但每次拉起都留一条误导性 ERR。
  4. **服务停机时 ExifTool 桥的「Pipe is broken」噪声**：`ExifToolBridge.StopAsync`→
     `ProcessExifToolProcess.StopAsync` 在 stdin `StreamWriter.Dispose` 抛 `IOException: Pipe is broken`
     并落 journal（`L9-journal-complete.txt` 00:35:32 段），unit 仍 `Deactivated successfully`
     （exit 0）——停机路径的 best-effort 清理没吞掉这个 IO 异常。
  5. **WSLg 下 gpui Wayland 后端不出窗**：`WAYLAND_DISPLAY` 存在时 gpui（`gpui_linux::current_platform`）
     进程常驻但窗口不映射；清空 `WAYLAND_DISPLAY` 强制 X11 后端后正常。真机 Wayland 桌面是否复现
     未测（本节环境是 WSLg），Linux 桌面实机面仍归 #2/#32 族。
  6. **单实例守卫跨用户全局**：ppverify 持有 UI 实例守卫时，root 启动第二个实例即「已有实例在运行」
     退出（`--service-probe-report` 在本基线二进制中尚不存在——该入口是 #32 窗口随后补进 main 的，
     本节探测态改用 systemctl+截图满足验收）。
- **收尾状态**：服务已卸载（两轮 UI 卸载，`is-enabled` not-found、unit 与 `/run/photoprivacy` 删除）、
  UI 已退出、无 Worker 残留进程；`/opt/photoprivacy` 包与 `ppverify`/`outsider` 测试用户保留在取证机
  （WSL 实例本身为一次性取证环境）；样本 `l31-sample.jpg` 与其 `bak/` 留在
  `/var/lib/photoprivacy/hot/`，全部在取证环境内，仓库工作区不受影响。
- **边界**：Linux systemd 服务面（安装/卸载/启停/日志/UDS 组边界）已实机；macOS launchd 对应面
  归 #32（同族取证链）。S5 配置热重载、跨用户授权集合（#1）、CLI/daemon 模式不在本票 AC。

### AC2/E4 组产物记录（2026-11-05，#57 + #102 窗口：600+ 条实机滚动/浏览/过滤 + E4 全链路复测）

- **构建**：Release run [41854940207](https://github.com/Xxx91n/photo/actions/runs/41854940207) /
  ref main / head `b72d489` / version 0.1.0-verify154 / create_release=false 的 release-win-x64
  （当前 main，本地零编译）。二进制指纹：`PhotoPrivacy.exe` 07e8b25b…（33107115 B）、
  `PhotoPrivacyWorker.exe` b4f0da15…（90489284 B）、包内 ExifTool 44b336d7…（13.60）
  ——全量见证据目录 `M00-binary-sha256.txt`。
- **实机与环境**：Windows 桌面（本机，用户 Administrator）；取证通道 = agent 桌面截图 +
  `SetCursorPos`/`mouse_event`（同 #125/#88/#113 先例，用户已批准）。产物解包于本机私有临时目录
  `C:\pp-ac2-verify\app\`；`config.json` 由 `config.sample.json` 生成（无 BOM，
  `audit.log_directory=C:\pp-ac2-verify\run\audit`、`hot_folder=C:\pp-ac2-verify\run\hot`、
  `ui.locale=zh-CN`，指纹 74370475…）。
- **灌入**：`audit-2026-11-05.jsonl` 三批共 **622 条**（250 INFO + 250 INFO +
  40 DEBUG/40 WARN/40 ERROR），UI 运行中由本地 tail（1 s 轮询）实时拾取，非启动前预置——
  tail 启动不回放历史（#57 双通道设计），预置历史只会经 Worker 末 50 行 backfill 部分呈现。
- **#57 AC2 帧**（帧文件与哈希见 `docs/agents/ui-smoke-evidence.sha256` M 组）：
  - `c01-batch1-250.png`：灌 250 条后计数 **252 条**（含 Worker 自身 2 条），列表实时刷新、最新项在顶；
  - `c02-batch2-502-cap500.png`：累计 502 条后计数 **500 条**——`LogBuffer` 500 条硬上限 GUI 实测生效；
  - `c03-batch3-mixed-620.png`：累计 622 条后计数 **460 条**（信息档下 40 条 DEBUG 被隐藏，460=500−40，
    与缓冲内容精确吻合）；错误行红色「Cleaning failed」徽章、警告行琥珀「Duplicate launch rejected」可区分；
  - `d01/d02/d03-scroll-*.png`：滚轮下滚 10 格、35 格、上滚 30 格，视口逐帧正确移动
    （error 20-30 段 → warn 2-12 段 → 回 error 21-30 段），行内容随滚动位置变化、无花屏/空白；
  - `e01-e04-filter-*.png`：切换 警告（**80 条** = WARN+ERROR）/错误（**40 条**）/调试（**500 条** = 全部）/
    全部（**500 条**），计数即时刷新、高亮随档位迁移，阈值语义与 `LevelFilter::allows` 一致；
  - 每步后 `PhotoPrivacy` 进程 `Responding=True`；判定「不卡顿」不新设帧率阈值。
- **#102 E4 帧**（严格口径：完整 UI 路径）：
  - `f01-after-clear.png`：点「清空」→ 0 条 + 空态占位「暂无审计条目」，`config` 同目录落盘
    `logs-clear-watermark.json`（00:10:57，51 B）；
  - `f02-restart-no-refill.png`：`Stop-Process`（TerminateProcess）完全退出 UI+Worker（进程数归 0）→ 重启 →
    仅新会话自身 2 条（ExifTool started / Service started），622 条已清历史不回灌；
  - `f03-new-line-appears.png`：重启后追加 1 条新日志 4 s 内出现在列表顶部
    （「Fresh line after restart (E4)」），计数 3 条。
- **顺带观察（记录供分诊，未立票、不属本票 AC）**：
  1. **带 BOM 的 config.json 跨栈容错不一致**：PowerShell 5.1 `Set-Content -Encoding utf8` 产出带 BOM
     的 `config.json` 时，Worker（.NET）容错读取，UI 侧 serde_json 解析失败 → 日志页静默回落
     `<base>/hot/_audit` 并创建该目录，用户配置的 `audit.log_directory` 被忽略（本轮首启实测复现，
     改无 BOM 写入后通过）。是否立票修复归用户裁定。
  2. **跨午夜轮转**：23:57 启动、00:00 后灌数，tail 自动轮转到 `audit-2026-11-05.jsonl`
     （#129 跨日轮转实机符合预期）；首轮灌进昨日文件的 250 条因此未进视图，与设计一致。
  3. **同秒批量行展示顺序**：同一秒内批量行的展示顺序与文件写入顺序存在小幅交错
     （同时间戳排序稳定性的观感问题，不影响判据）。
- **边界**：本轮回填只覆盖 #57 AC2 与 #102 E4 严格口径；AC3 的口径裁定（探针级证据是否满足）
  与整票关闭仍归 #57/#90 核销流程，不在本窗口代勾。

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
    - `#57` AC2（大批量日志滚动/浏览不卡顿）：**通过（容量截断探针级 + 2026-11-05 实机 GUI 滚动/浏览/过滤探活）**（2026-11-04，随 PR #178 / PR #183 落地远端 CI 虚拟环境流水线 `.github/workflows/logs-parity-verify.yml` 与 `large_volume_test.rs`：灌入 600+ 条审计记录，验证 `LogBuffer` 500 条上限硬截断及过滤索引计数；该通道只证容量与数据结构行为。**实机探活（2026-11-05，构建 head `b72d489` / Release run 41854940207 / 0.1.0-verify154，帧与读数见下文「AC2/E4 组产物记录」）**：UI 运行中经本地 tail 实时灌入 622 条审计记录（250+250 INFO、40 DEBUG/40 WARN/40 ERROR），计数 252 → 500（触发 500 条硬上限）→ 460（信息档下 40 条 DEBUG 被隐藏，与缓冲内容精确吻合）；滚轮下滚 10/35 格、上滚 30 格视口逐帧正确移动（error 段 → warn 段 → 回退），`uniform_list` 仅渲染可见行；切换 警告（80 条 = WARN+ERROR）/错误（40 条）/调试（500 条 = 全部）/全部（500 条）计数即时刷新、高亮随档位迁移，阈值语义与 `LevelFilter::allows` 一致。判据 = 交互各步视口正确移动、计数即时、`PhotoPrivacy` 进程每步 `Responding=True`，不新设帧率阈值）。
- 伴随发现与勘误记录：
  1. **清单文案勘误**：原清单中关于主题、语言、日志级别的“下拉菜单”表述为推测失真；实机均为平铺选项组（Segmented/Radio-like buttons），无原生下拉框，本轮已在清单中更正。
  2. **终端黑框弹出**：`photoprivacy-ui` 的入口 `main.rs` 未声明 `#![windows_subsystem = "windows"]`，双击启动时系统默认分配控制台黑窗口，随后弹出 GUI。（勘误，2026-10-30：该形态已随 #99/PR #106 修复，release 构建现在带 `windows_subsystem = "windows"`；本条只作历史记录。）
  3. **初始配置待机行为（#100）**：打包附带的 `config/config.json` 默认 `"watch.hot_folder": ""` 为空时，Worker 按 #100 起保持 IPC 连接并待机，UI 引导前往配置页选择监控目录，保存有效目录后经热重载启用监控。#113 已把这条路径定形为上文的 K1-K4 四项，并把「真实传输级待机连通」补成进 CI 门禁的自动化用例；**四项已于 2026-11-03 在 head `fb388507` 上实机取证通过**（见上文 2026-11-03 产物记录），本条不再是「只有代码与集成测试口径」。
     （勘误，2026-10-31 #125：K1-K4 已按桌面截图通道实机取证并全部通过，见上文「首次配置待机 4 项」表；本条的「未探活」只描述写下它时的状态。）
  4. **GUI 文本不可划选复制**：gpui 纯文本展示未支持鼠标划选高亮与系统剪贴板复制，报错无法直接复制排障。
  5. **主题设置入口冗余**：右上角快捷切换与设置页平铺切换并存，交互体验重叠。

> AI生成
