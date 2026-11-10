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
- **Linux 提权弹窗未实测**：UI 安装/启停命令本身走 pkexec 实测过，但交互式 polkit 认证弹窗
  没覆盖过（验证机配置了免交互规则）。要补真弹窗得有带 polkit 认证代理的 Linux 桌面，
  是否算数由用户裁定。
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
   launch-in-job.ps1，写后读回校验），子进程 spawn 被配额挡掉（os error 1816）探针落 Unknown；
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
