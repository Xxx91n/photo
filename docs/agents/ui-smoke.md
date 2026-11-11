# 新 UI 手工冒烟

> 定位：新 UI 尚未进入发布链，也没有 headless UI 测试；本文件记录可重复的手工冒烟步骤和留证据的方法。
> 2026-10-28 至 2026-11-05 各轮实机执行记录（产物清单、逐项证据、勘误）已随 #254 清理出本文件，需要时从 git 历史取回（#254 之前的版本）。
> 视觉验收按 #49：观感参照点 + 冒烟步骤，不建 CI 像素基准。

## 怎么记结果

- 每项记：通过 / 失败 / 未测 / 不迁移（裁决）。没有证据不填通过。
- 证据写「日期 + 通道 + 看到的具体现象」；整屏截图可以当证据，哈希记进
  `docs/agents/ui-smoke-evidence.sha256`（一轮一清单，只装当前一轮；旧一轮从 git 历史取）。

## 已知限制（判结果前先读）

- **ar 文案字形不可读（#154）**：Windows 构建上，阿拉伯文呈孤立字形、整行词序反——
  上游 gpui 的 DirectWrite 转换层缺陷，应用层没有可行绕法。B3、K2 只判布局镜像和文案可见，
  不判字形质量，不据此记失败。升级 gpui 的 Windows 文本系统版本后先复验 ar 帧，再考虑撤销本条。
- **停服务时的 `Pipe is broken` 噪声（#37）**：ExifTool 已死时停机拆 stdin 会抛这个 IO 异常，
  属预期，代码已降 Debug；真实失败看其他异常或退出码非 0。
- **Linux 提权弹窗已实测（#206、#4）**：polkit-gnome（#206，Xvfb + 独立 logind 会话）和
  MATE（#4，WSL2 + X11 桌面）都走通了真实交互弹窗，四个操作的同意/取消分支各有当轮证据。
  取消语义跟认证代理有关：polkit-gnome 的 Escape 走 dismissed、pkexec 退出 126；lxpolkit
  的 Cancel/Escape 报 127（Not authorized），UI 按失败显示——这是已知失败，没修，别当成取消。
  #206 还记了两种环境坑：云镜像摘掉 polkit-agent-helper-1 的 setuid 位；lxpolkit 点 Cancel
  后弹窗不关、pkexec 挂到 60 秒超时。详见下方两节，别把不同代理的结果混着算。
- **macOS 提权按钮**：按 #32 裁定接受为残留（CLI 等价命令已验证）。
- **未知态的超时变体没有实机帧（#206）**：#206 轮诱发的是 spawn 失败分支；超时、无权限
  两变体与它共用同一处 Unknown 赋值和同一呈现（`windows.rs` 探针三分支）。无权限变体有 CI 测试
  `sc_query_access_denied_is_unknown_not_uninstalled`，按钮 fail-closed 有
  `unknown_is_fail_closed_for_buttons`；超时变体无专设单测，判第 9 项时别把本轮帧当成它的证据。
- **Windows 验证机的 UAC 同意框只能人点（#30）**：consent.exe 以 SYSTEM 完整性跑，UAC 框按设计
  拒绝程序输入（UIPI），agent 点不进去。`ConsentPromptBehaviorAdmin=2` 时框还落在安全桌面，
  普通会话截不到（截图报「句柄无效」）；改成 4 时框渲染在普通桌面，能截到整屏帧——但点击仍要人来做。
  留证据时把框帧和点完的结果帧都存上，谁点的写清楚。

## 留证据的方法（agent 桌面截图通道）

1. **取产物**：用 Release workflow（`create_release=false`）跑**当前 main** 的 `release-win-x64`，
   解压到私有目录；并行窗口共用发布链时不要把产物拷进 `ui/target` 这类共享构建目录。
   留证据前先核对 head——旧记录的 head 一旦落后 main，界面呈现就不是当前二进制。
2. **造待机态**：包内 `config/config.json` 保持出厂原样（`"watch.hot_folder": ""`）。
   这台机器之前验过别的票、目录已填过的话，先删 `config.json` 再从同目录 `config.sample.json` 拷一份。
3. **截屏**：整屏 `Graphics.CopyFromScreen`（BitBlt 已合成的桌面）能拿到 gpui 窗口真实像素
   （PIL `ImageGrab` 或 PowerShell/.NET 都行）；按窗口句柄取图的 `PrintWindow` 对 flip-model
   窗口只回黑帧，别用。
4. **点击与键入**：
   - 点击用 `SetCursorPos` + `mouse_event`，坐标由 `GetWindowRect` 换算成窗口内偏移；
     坐标别凭缩略图目测像素，先叠一层带数字标注的网格截图标定，或对导航条按暗像素聚类找文字中心。
   - 宿主自动化会抢前台：把「`SetWindowPos`(TOPMOST) 置顶 → 点击 → 等待 → 整屏截图」收进同一个进程
     原子完成；`SetForegroundWindow` 单独调用会被系统挡掉，要先 `AttachThreadInput` 到前台线程
     再补一发 ALT 的 `keybd_event`。
   - ctypes 调 `SetWindowPos` 必须先声明 `argtypes`，否则 HWND/-1 传参被静默弄错，
     看着像权限问题其实是参数编组问题。
   - 键入走「写剪贴板 + Ctrl+V」：`SendInput` 的 Unicode 字符（`KEYEVENTF_UNICODE`）在 gpui
     文本框上无反应，`INPUT` 结构体尺寸给错（只声明 `KEYBDINPUT`，40 → 28 字节）时还会静默失败；
     `SendInput` 的返回值必须检查，否则「敲了没反应」会被误记成 UI 缺陷。
   - **导航项坐标不跨语言复用**：导航条按文案宽度排布，切一次语言第二项就能漂出原坐标。
     逐语言验证要么每切一次重新标定，要么手改 `config.json` 的 `ui.locale` 靠热重载切语言、
     只点固定的第一项回概览。
5. **服务验证前置**：装/卸载服务用 `scripts/install-service.ps1`（安装时登记 `ServicePipeInstallerSid`）。
   服务页「安装」按钮也会在创建服务之前登记安装者 SID（#200 之后的行为，写不进就不装），
   但它只登记安装者自己，不像脚本能带 `-AuthorizedUsers` 授权多个账户。
   对比验证时两条通道别混用——授权集合不一样，混着验分不清是哪条链的结果。
6. **收尾**：服务卸载（`sc query` 回 1060）、`ServicePipeInstallerSid` 随卸载清除、
   无残留 UI/Worker 进程、`config.json` 逐字节还原出厂、临时测试账户删除。
7. **非提权启动的前置（这台验证机，#30）**：内置 Administrator 默认不在管理员批准模式，
   镜像启动还会把 `FilterAdministratorToken` 压回 0，只写注册表活不过重启。要验非提权启动，
   先把 UAC 的值经 `secedit /configure` 写进本地安全策略库再重启（要留同意框帧的把
   `ConsentPromptBehaviorAdmin` 设成 4，`PromptOnSecureDesktop` 设成 0；值 2 会去安全桌面）；重启后别只看注册表读数，
   用进程令牌核对被测 UI 是 `elevated=0 / elevationType=3 / integrityRID=8192` 才算数。
   收尾把策略库、注册表和窗口站/桌面 DACL 都恢复原值——`EnableLUA` 改回 0 同样要再重启才生效，
   什么时候重启用由账户持有人定。

## 冒烟步骤

每轮从全新的私有 `config` 目录开始（删掉 `rules.json` 或先点 `Reset`），否则上一次保存的值
会让开关起始状态相反。

### 规则页（#56）

1. 启动 UI，点导航 `Rules`。
2. 检查：五族 × 六开关全部可见；PDF/EPS 行显示 `Caution`；专家开关只切换提示条，六开关本身始终可点。
3. 点 JPEG 的 `Strip all`（默认 true → false）与 `EXIF`（默认 false → true），点 `Save`。
4. 期望 `<exe dir>/config/rules.json` 含 30 个 `{family}_{group}` 布尔键：
   `jpeg_strip_all=false`、`jpeg_strip_exif=true`，其余族保持编码默认值。
5. 关闭并重启 UI，再进 `Rules`：JPEG 两个开关保持第 3 步的值，且无加载告警、无「Unsaved changes」。
6. 旧版互读：CI 的 `FormatRulesStoreTests.Load_Reads_Rules_Written_By_The_New_Ui`
   用同一份 golden fixture 走冻结的 `FormatRulesStore.Load()`。

### 主题（5 项）

1. 配置页主题平铺选项组中 system/light/dark 三态可见。
2. light↔dark 切换即时生效，无需重启（右上角与设置页两处均可调，设计上有冗余）。
3. 切主题不改语言、切语言不改主题，两轴独立。
4. 切 dark → 重启后保持（`config.json` 的 `ui.theme_variant` 持久化）。
5. 运行中手改 `config.json` 的主题 → 约 1s 内热重载生效。

### 语言（4 项）

1. 语言平铺选项组逐一切换 10 个 locale，导航/标题/行标签/按钮与页面文案即时翻译
   （10 个全切才算覆盖）。
2. 选 العربية 布局翻转为 RTL（导航靠右、正文右对齐；只判布局镜像，字形质量见已知限制）。
3. 切 English → 重启后保持（`ui.locale` 持久化）。
4. 平铺选项文案（主题/日志级别）随当前语言刷新。

### 日志页（5 项）

1. 空日志时显示空态占位。
2. 向监控目录放照片，Worker 处理后日志页即时出新条目，无需手动刷新。
3. 不同级别/事件类型的呈现可区分（错误行、警告行样式不同）。
4. 点「清空」→ 计数归零出空态 → 完全退出并重启 UI → 已清历史不回灌 → 新日志几秒内出现在列表顶部。
5. 日志级别平铺选项切换生效：高档位下低级别事件不再出现。

### 按钮与窗口（5 项）

1. ghost 按钮 hover 有反馈（按 #49 的观感参照点目检）；primary/danger 实心按钮按设计不改 hover 底色。
2. 按下态有视觉反馈，松开恢复。
3. 不可用操作呈禁用态且点击无响应（如服务未安装时的启动/停止）。
4. 键盘 Tab 焦点有可见指示（2026-10-29 实测失败：Tab 无焦点移动也无轮廓；重验通过前别默认它好着）。
5. 标题栏拖动/双击最大化/右上角三键正常；关窗为驻守（见托盘组）。

### 托盘 / 驻守 / 单实例（6 项）

1. 托盘图标出现，菜单 Open Window 唤起主窗口。
2. 托盘 Exit 退出干净：进程结束，无 Worker 孤儿。
3. 关闭主窗口进程驻守（销毁+重建）：主题/连接状态/日志缓冲/服务快照保留，位置/尺寸回中
   （gpui 无窗口隐藏 API，取舍见 #59）。
4. `ui.hide_main_window_on_startup=true` 时启动只出托盘不弹窗（托盘辅助窗不算主窗）。
5. 二次启动不重开：第二实例退出（exit 0）并唤起既有窗口（`Global\PhotoPrivacyUi_Instance`）。
6. 待机态（空 `hot_folder`）下托盘 Exit 或停服务，进程在 `HostOptions.ShutdownTimeout`（10 s）内
   退出、无残留——实机证据还没补（自动化侧已由待机用例断言 `StopAsync` 在预算内完成），
   没补前不记通过。

### 服务模式（Windows，9 项）

注意：UI 启动会拉起后台 Worker；服务已装且未运行时点「启动」，服务 Worker 会触发单写者门
让位退出，UI 报「服务启动超时」（#162 形态）。验启动/停止前先结束后台 Worker、只留 UI。

1. 未安装态：服务页显示未安装，启动/停止禁用，安装可点。
2. 安装：点「安装」→ 绿字成功横幅、状态行 未安装→已停止、按钮组翻转；同刻 `sc query`
   STATE 1 STOPPED、`sc qc` 的 binPath/AUTO_START/LocalSystem 正确。失败分支：移走包内
   `PhotoPrivacyWorker.exe` 再点安装 → 红字报具体原因、状态行保持未安装。
   取消分支（#30 留下证据、#206 登记）：UAC 真生效且界面非提权的机器上点「安装」、同意框点「否」
   → 琥珀色「已取消」横幅（不是红字「失败」）、状态行保持未安装、10 秒采样无第二次弹框。
   框帧与结果帧在 #30 轮清单（`git show 0453da2:docs/agents/ui-smoke-evidence.sha256`
   的 b2-30/b2-34/b2-35 + b2-no-repeat-watch.txt）；#206 轮复核该清单 42/42 OK。
3. 启动：点「启动」→ 状态变运行中，`sc query` STATE 4 RUNNING，服务 Worker 进程在 Services 会话。
4. 服务运行中重启 UI → 总览连接态「已连接 · PhotoPrivacyCleaner.Service · 服务模式」，
   Worker 卡片服务 IPC=可达，UI 未拉起后台 Worker。
5. 服务模式下改配置保存 → 「已保存 · Worker 已重载」，配置秒级落盘、热生效
   （投放样本被清洗、日志页出新条目）。
6. 停止：点「停止」→ 状态回落已停止，服务 Worker 进程消失。
7. 卸载：已停止态点「卸载」→ 回到未安装态（运行态点卸载无动作是设计：`button_state`
   Running 分支）。
8. 服务日志通道：日志页实时出 `Service started`/`Cleaned` 等条目，与 `_audit` 审计文件、
   ProgramData 服务日志同刻对得上；与后台模式的区分看连接态显示。
9. 未知态：探针给不出结论（spawn 失败/超时/wait 失败）时状态行琥珀点「未知」，
   安装/启动/停止/卸载全禁用、只有刷新可点；不得谎报「未安装」或「已停止」。
   复现：把 UI 挂起放进 ActiveProcessLimit=1 的 Job 对象再放行（#206 轮
   launch-in-job.ps1，工具不入库、当轮在证据目录旁的 tools\，写后读回校验），子进程 spawn
   被配额挡掉（os error 1816）探针落 Unknown；
   同刻不受限 shell 的 `sc query` 作真值对拍，点禁用按钮应无响应；杀掉 Job 内 UI 正常拉起
   应回真实状态。证据与边界（超时变体未实机诱发）见 #206 轮清单 u1-round-notes.txt。

macOS 侧的服务页探测项（S1/S3/S6/S7 等）不以人工读屏为准，以 `scripts/verify-launchd-service.sh`
对拍为准：`ui_probe_report` 以 GUI 用户语境跑 `PhotoPrivacy --service-probe-report`，与 CLI
`probe_state`（`launchctl print system/<label>`）判定一致即通过；由
`.github/workflows/launchd-verify.yml` 执行。

### 服务模式（Linux systemd，9 项）

1. 未安装态：服务页显示未安装，启动/停止/卸载置灰、安装可点；同刻 `systemctl is-active`
   inactive、unit 不存在。
2. 脚本安装：`bash scripts/install-systemd-service.sh --gui-user <用户>` → 服务 active (running)、
   enabled；unit 的 `ExecStart` 与 socket（`/run/photoprivacy/worker.sock`，0660 服务组）符合预期。
3. UI 安装按钮（pkexec → 包内脚本）→ 绿字成功、状态运行中。
4. 运行态呈现 + UI 启动：运行中态按钮组正确；停止后点「启动」→ 回运行中、新 PID、socket 重建。
5. 服务运行中重启 UI → 总览「已连接 · /run/photoprivacy/worker.sock · 服务模式」。
6. UDS 0660 组边界：组成员 connect+`Ping` 全通；非成员 connect 被拒 EACCES。
7. UI 停止 → 状态回落，`is-active` inactive，`/run/photoprivacy` 随 RuntimeDirectory 回收。
8. UI 卸载 → 未安装态；先停止再卸载（运行态点卸载无动作是设计）。
9. 日志页可见服务审计条目（与 `/var/log/photoprivacy/audit-*.jsonl` 同刻对得上）。

WSLg 环境备注：gpui 的 Wayland 后端不出窗，清空 `WAYLAND_DISPLAY` 强制 X11 后端；
WSL VM 空闲约 8 s 会回收，验证期间用常驻进程保活。
#### Linux polkit 交互弹窗（#206，2026-11-11 实测）
环境：Ubuntu 26.04 CVM + Xvfb :99（1280x800x24）+ polkit-gnome 认证代理 + 独立 SSH logind
会话（session scope）+ 会话 dbus；polkit 免交互规则当轮停用，弹窗为真实交互认证。
包：Release run 42865294855（ref main，head 0453da2）出的 release-linux-x64，本机未编译。
1. 安装弹窗：点「安装」→ polkit 认证弹窗出现（身份下拉 + 密码框 + Cancel/Authenticate），
   截图 L3-01；输密码回车 → 服务 active+enabled、Worker 进程启动、UI 绿字
   systemd service installed（L3-03）。
2. 取消分支：弹窗出现后按 Escape → pkexec 退出码 126（Request dismissed）→ UI 反馈行
   warning 色 Elevation was cancelled（L3-02），服务保持未安装（is-active inactive、
   unit 不存在），不重复弹窗。
3. 停止/启动/卸载弹窗：三个操作各自弹窗认证后生效——停止 → inactive（L3-04/L3-05）；
   启动 → active、新 MainPID（L3-08/L3-09）；卸载 → unit 删除、not-found（L3-06/L3-07）。
4. 取消语义边界：pkexec 手册定义 126=用户关掉认证框、127=未授权/出错。实测（pkexec true
   直接调用，lxpolkit 代理）：Cancel 点击与 Escape 都退出 127（Not authorized）；polkit-gnome
   的 Escape 退出 126（Request dismissed）。UI 只把 126 分型为取消，127 按失败显示，与代码
   注释（unix.rs：只有 126 是用户取消）一致。注意区分：UI 流程里有一次点 Cancel 后弹窗不关、
   pkexec 挂到 60 秒超时（UI 显示超时失败）——那次是超时分支，不是 127 分支的验证。
证据清单与哈希：当轮记在 docs/agents/ui-smoke-evidence.sha256（该清单现只装 #5 轮，当轮哈希从
git 历史取：`git show f715c53de409f3e66e8d06004d6964868cda6826:docs/agents/ui-smoke-evidence.sha256`；
实物在验证机 /home/ubuntu/photo-verify/evidence-206/）。

#### #4 本轮提权弹窗验证（2026-11-11，WSL2 GUI）

用户指定由 agent 在 WSL2 GUI 操作、查看截图，不由用户代点。首次验证，非 PR 返修；
起始基准固定为 `0453da224a0145789b80bb8a29d812bd39cb1473`，独立分支 `verify/4-wsl2-polkit`。
前置服务页代码和 Linux 生命周期证据已分别由 #74、#272 合入此基线；本轮不确认 #206 的 Windows 项。

- **产物**：Release run `42767568814` 的 `release-linux-x64`，head
  `d1e30d8a52566609330eb32e70b4348d0ffcb2af`，版本 `0.1.0-preview`。此 head 到起始基准的
  `ui/`、`src/`、`config/`、发布脚本、Linux 安装/验证脚本无差异。只下载远端产物，本机未编译。
- **桌面**：Ubuntu WSL2 + WSLg，Xephyr 展示独立的 1360×900 X11 桌面，Openbox 管窗口；
  MATE polkit `1.26.1-4build3`，`GDK_BACKEND=x11`。这是实际显示的桌面，不是 Xvfb 或伪造认证框。
  点击由 `xdotool` 发给真实按钮，`scrot` 保存整屏；agent 查看全部结果帧和八张弹窗的汇总图。
- **身份与权限**：临时账号 `pp4verify` 需要密码认证；旧账号的免认证规则未修改，也不适用于它。
  UI 的实际 UID/EUID 为 1002，`CapEff=0`；`polkit-journal.log` 记录了真实认证及一次性授权。
- **运行库**：发布 UI 需要 GLIBC 2.43，WSL 原库为 2.39。仅在测试进程的私有挂载空间中使用
  已校验的 Ubuntu `libc6 2.43-2ubuntu2`；UI、Worker、安装脚本均未改动，系统库仍为 2.39。
  认证代理使用系统原库。生命周期探针也在同样的私有运行库环境运行。

| 操作 | 同意后的结果 | 取消后的结果 | 证据前缀（同意 / 取消） |
|---|---|---|---|
| 安装 | 通过：`Success`，服务 active/enabled，PID 4077 | 通过：`Cancelled`，仍未安装 | `59-install-approve` / `58-install-cancel` |
| 启动 | 通过：`Success`，服务 active，新 PID 3641 | 通过：`Cancelled`，仍已停止 | `54-start-approve` / `53-start-cancel` |
| 停止 | 通过：`Success`，服务 inactive，PID 0 | 通过：`Cancelled`，仍运行，PID 2680 不变 | `52-stop-approve` / `51-stop-cancel` |
| 卸载 | 通过：`Success`，unit 删除、未安装 | 通过：`Cancelled`，unit 保留、仍已停止 | `57-uninstall-approve` / `56-uninstall-cancel` |

八个分支执行于 01:42–01:44（UTC+8），编号是执行顺序所以不连续；这八个前缀各自都配有
`-before`、`-prompt`、`-result` 的原始整屏 PNG 和同刻 JSON，JSON 记录 UTC 时间、服务状态、
进程及窗口。取消前后状态字段逐项相同，结果横幅是 `Cancelled / Elevation was cancelled`，
不是 `Failed`。原始图不裁剪、不改字。准备卸载时另做了一次正常停止；安装复测结束后再从界面
停止、卸载。这两步以及早期失败尝试的帧、诊断日志也一并留在清单里，不和上面八个分支混着算。

**命令通道**：01:45，真实 GUI 账号运行
`wsl.exe -d Ubuntu -u pp4verify --exec python3 /mnt/c/photo-verify/issue4-harness.py`；
辅助脚本经该账号的 `sudo` 调用未修改的 `scripts/verify-systemd-service.sh`，不是 root 伪填 `SUDO_USER`。
结果 `PASS=52 FAIL=0`、`HARNESS_EXIT=0`，含四态 UI/CLI 对照、IPC、权限、重启和卸载清理。
`getfacl` 未安装的诊断行仅说明未采集 ACL 文本；权限断言及真实 GUI 用户读写检查均通过。
所跑两个脚本取自本分支工作区，哈希见证据里的 `lifecycle-scripts.json`；安装脚本与发布包内
那份相同，验证脚本是仓库工具、起始基准到本提交没有改动。

**LXDE 取消分支：失败（未修复）**：`30-install-cancel-*` 记录的是 lxpolkit 点取消后显示
`Failed / Not authorized`，跟 #206 记的 127 行为一致。按 ui-smoke 的档位记「失败」，不能
算「未测」，也不能并进上表说取消都已通过。127 不能一律当取消，怎么区分要另定，本轮没改
产品代码。GTK3 自动选后端时还出现过代理崩溃或弹窗不出，最终固定 X11；`50-stop-cancel`
那张弹窗在查看时超时，停止重跑成 `51/52`。同一操作两轮弹窗内容一样，整屏 PNG 可能逐字节
相同，截图时间看各自 JSON。macOS 按 #32 裁定不复测。

**收尾**：01:49，临时账号、密码及私有运行库已删除；原有三个服务目录的 inode/权限、权限组和
polkit 规则校验恢复，服务未安装、socket 和 Worker 无残留。新装的图形工具保留，未升级系统库。
证据实物位于 `C:\photo-verify\issue4-wsl2\evidence\`，#4 这 157 个文件的哈希和 #206 的
11 个 L3 文件哈希当轮记在 `docs/agents/ui-smoke-evidence.sha256`（该清单现只装 #5 轮，从 git 历史取：
`git show f715c53de409f3e66e8d06004d6964868cda6826:docs/agents/ui-smoke-evidence.sha256`）。其中 `review-*` 是
目检裁图，对应的原始整屏也在这个目录里；这些实物都没有上传。
本轮证明上述 MATE/X11 环境可用；原票不能据此按字面整票关闭，整个跨平台需求也没有完成。

#### #5 后台 Worker（--mode background）实机验证（2026-11-11，Windows）

首次验证，非 PR 返修；起始基准 `f715c53de409f3e66e8d06004d6964868cda6826`，独立分支
`verify/5-background-worker-smoke`。包：Release run `43008869529`（ref main，head 同基准，
0.1.0-preview，create_release=false）出的 `release-win-x64`，本机未编译。出厂 config 唯一差异
`watch.hot_folder`；Worker 手动以独立控制台拉起，停机走 CTRL_BREAK（IPC `Shutdown` 仅 CLI 可用）。

1. 拉起与连接：通过 —— Worker 独立进程盯文件夹、无 UI、服务未安装（`sc query` 1060、注册表无键）；
   UI 启动后状态栏绿点「已连接 · PhotoPrivacyCleaner.Background · 后台模式」，Worker 卡片
   服务 IPC=不可达（非服务模式）、ExifTool 13.61，无警告横幅、无分类文案；全轮只有一个 Worker
   进程（UI 未再拉第二个）。证据 `v5-worker-launch.txt` / `v5-01-ui-connected.png` /
   `v5-after-ui-start.txt`。
2. 清理链路：通过 —— 目录外备好带全套 EXIF（Artist/DateTimeOriginal/Make/Model/GPS）的 sample2
   拷入 → 同刻审计 `file_processing_succeeded attempt=1;wipe_result=Cleaned_Modified`、
   `processed.ndjson` 1→2 行、原件进 `hot\bak\`（五项元数据全保留）、清洗后主文件 exiftool
   读回为空；UI 日志页同刻出 Cleaned 条目（本地尾部 + IPC 回填）。ExifTool stay_open 长驻
   （启动后仅拉起一次，非每文件 spawn）。证据 `v5-drop2.txt` / `v5-02-ui-logpage.png`。
3. 退出干净：通过 —— CTRL_BREAK 后 1.2 s 进程退出（10 s 预算内）、无 Worker/exiftool 孤儿；
   UI 日志收到 `service_stopped / worker stopped`，总览转「重连中」+ 分类文案
   「连接 Worker 失败：…(os error 2)」，与 #169 断连语义一致。证据 `v5-stop.txt` /
   `v5-03-ui-after-worker-stop.png` / `v5-04-ui-overview-disconnected.png`。

边界与未测：sample1 首轮直接建在监控目录后补写元数据，补写被 `already_processed` 跳过——属预期
（已处理路径状态记忆），标准链路以 sample2 为准；待机态首次配置引导 4 项、连接失败三态第 1/2 项、
托盘组（含待机退出第 6 项）不在本票范围，未测；Worker 控制台窗口是手动启动通道，不是 UI 产物；
关 UI 主窗口 = 驻守（#59 设计），收尾用 taskkill 结束；连接失败三态第 3 项只验了断开方向
（Worker 停 → 报错），「放回 Worker 拉起 → 状态回已连接」的恢复方向未测。逐文件时刻与通道见
仓外实物目录的 `v5-round-notes.txt`，哈希与找回方式见 `ui-smoke-evidence.sha256`。

### 连接失败三态（#169，3 项）

三态文案（按平台分键）：服务端点拒绝 → `status.connect_denied`（Windows 指引加
`-AuthorizedUsers`）/ `status.connect_denied_unix`（Unix 指引加服务授权组）；
后台拉起未就绪 → `status.worker_startup_failed`；找不到 Worker → `status.worker_not_installed`。
心跳连上即清空，掉线不清。分类与文案映射由 CI 测试覆盖，本组以实机为准。

1. **授权拒绝**：装有服务（`-AuthorizedUsers` 只放行名单内用户）的机器上，名单外标准用户启动 UI，
   等回落链走完（约 5 s+）→ 状态卡 warning 色逐字提示把账户加入 `-AuthorizedUsers`，
   不出现 os error 2。对称验证：名单内用户启动 UI → 服务模式已连接。
2. **缺 Worker**：服务未装 + 包内移走 `PhotoPrivacyWorker.exe`，启动 UI → 状态卡红色错误行
   「未找到 Worker 程序」，不误报为未授权。
3. **断连恢复**：同一 UI 进程保持运行，放回 Worker.exe 并以 `--mode background` 拉起
   （或授权后重启 UI 连服务端点）→ 状态回已连接、分类文案消失。

### 首次配置待机（4 项）

出厂数据包 `config/config.json` 的 `watch.hot_folder` 为空，Worker 连上后待机，UI 引导首次配置。

1. 双击 `PhotoPrivacy.exe` → 状态栏连接态显示「已连接」（不应有控制台黑窗；有黑窗 = debug 产物
   或旧包）。Worker 卡片 ExifTool=unknown 属预期（待机不拉进程）。
2. 状态栏出现 warning 色提示「请前往配置页设置监控目录」；10 个 locale 逐一确认该文案可见、
   内容对应（只判可见，字形质量见已知限制）。
3. 点该提示文本 → 直接落配置页（不是只高亮、也不是无响应）。
4. 选有效目录保存 → 提示消失、监控启动；放一张带元数据的照片 → 日志页出条目、
   `_audit/processed.ndjson` 增长、原件进备份目录。出厂包自带 ExifTool（`<包根>/ExifTool/`，
   模板 `exiftool.path` 留空 = 运行时探测），没装 ExifTool 的裸机也能直接验这条。
   失败原因（如把 `ExifTool/exiftool.exe` 改名后保存）常驻显示到下次保存成功，恢复后消失。

### 不迁移项（裁决记录，别再加回）

- 中键滚动、侧栏拖宽、nav hover 三组：#49 D11 裁决不迁移。新 UI 长内容滚动走滚轮/滚动条，
  侧栏宽度固定，nav 反馈归按钮与窗口组第 1 项。
- 托盘 Pause/Resume：不迁移（#49 的托盘对等范围只含显示/退出）；需要时另立票。
