# UI 换栈调研：gpui vs Tauri 2 vs Avalonia 换设计体系

> 票：[#50](https://github.com/Xxx91n/photo/issues/50)（父 [#49](https://github.com/Xxx91n/photo/issues/49)）  
> 日期：2026-10-26 ｜ 代码基线：`origin/main@1d7638c` ｜ 分支：`research/ui-stack-migration`  
> 性质：决策支持调研。最终裁决在 #53，且必须用户在场；本文只给事实、判据与建议。  
> 口径：会变化的事实均标注「截至 2026-10」，关键结论带一手来源；本地事实给 `路径:行号`。`【事实】` / `【推断】` 分标。

---

## 0. 结论摘要（给 #53）

**一句话**：三个候选都能做出好看的四页工具，但代价与硬约束差异很大。gpui 的组件与 GPU 观感上限最高，同时在三平台覆盖、无障碍、中文输入法与分发上出现了父票定义的「三平台硬伤」；Avalonia 换皮最便宜、最稳，审美上限约到 Linear 静态观感 80%；Tauri 2 在桌面壳、分发与共存上最完整，审美取决于 Web 前端与「Web 感」控制。

### 0.1 必须让 #53 看到的六条事实

1. **gpui 指认成立，但官方分发停更**：`gpui` crate 描述即 "Zed's GPU-accelerated UI framework"，维护方 Zed Industries，Apache-2.0；官方 crates.io 版本停在 **0.2.2（2025-10-22）**，仓库 main 已拆成 `gpui_platform` 等未发布 crate。实际可用路径是 git 依赖、社区快照 `gpui-pre`（Longbridge 重发，0.3.9 / 2026-10-12）或 fork。[来源](https://crates.io/crates/gpui) [来源](https://crates.io/crates/gpui-pre) [来源](https://github.com/zed-industries/zed/blob/main/crates/gpui/README.md)
2. **gpui 三平台有硬门槛**：Linux 要求 **Vulkan 1.3**；Windows 要求 **DX11 GPU 且排除 Microsoft Basic Display Adapter**；**无软件渲染兜底**，GPU device loss 无法优雅恢复。虚拟机、远程桌面、老显卡/无独显环境基本不可用。[来源](https://zed.dev/docs/installation) [来源](https://github.com/zed-industries/zed/discussions/57066) [来源](https://github.com/zed-industries/zed/issues/23288)
3. **gpui 的桌面壳几乎空白**：无托盘、无单实例、无开机自启、无官方打包/签名/公证/自动更新。社区 `gpui-kit`（18.6k star、75+ 组件、Longbridge Pro 商用）补的是 UI 组件，不补壳。[来源](https://gpui-kit.com/component) [来源](https://github.com/zed-industries/zed/pull/44047)
4. **gpui 的无障碍与中文输入法仍是风险区**：AccessKit 2026-05 才合入，Zed 官方仍称不支持屏幕阅读器；IME 有多条 open issue（候选窗偏移、Wayland preedit、X11 IME 损坏等）。对 10 locale、中文优先的产品必须真机验收。[来源](https://github.com/zed-industries/zed/pull/56065) [来源](https://github.com/zed-industries/zed/issues/41138) [来源](https://github.com/zed-industries/zed/issues/64850)
5. **Avalonia 换皮不是「再换一次色」**：现有 Semi/Ursa + 自建 token 已是第二轮视觉投入；要突破模板感必须逐控件重绘 `ControlTheme` + 重建 token/动效。UI C# 共 **8,138 行**，其中约 **6.3k** 为与框架弱耦合的逻辑可保留；样式/主题 **746 行** + 守卫测试约 **1,515 行**要重写；估算 **27–44 人日**（6–9 周单人）。Linear 静态观感可达约 **80%**，跨平台毛玻璃**不可得**。见 §5。
6. **两条既有跨平台缺陷会污染基线**：UI 客户端从不按 OS 解析端点（Linux/macOS 实际上连不上 Worker）；Windows 服务模式 `CurrentUserOnly` 与 LocalSystem 账户冲突（用户 UI 会 ACCESS_DENIED）。这两条与换栈无关，但会让人误以为「换框架后跨平台变好」。见 §2.6。

### 0.2 建议（供 #53，不是裁决）

- **不要在没有 spike 的情况下直接锁定 gpui。** 先做 3–5 人日竖切（配置页 + 托盘 + IPC 只读 + 三平台构建），候选优先 **Tauri 2** 与 **gpui**；Avalonia 换皮作为低风险对照。
- **若产品必须覆盖虚拟机/远程桌面/老显卡，或无障碍是硬要求** → gpui 触发一票否决，转 Tauri 2 或 Avalonia 换皮。
- **若审美目标明确是 Linear/Vercel 系**（而非 Apple 玻璃系），且接受 vendor/fork、周更 breaking、自建打包 → gpui 可行，但按 **3–6 个月**计，不是「换个 UI 库」。
- **若倾向 Tauri 2**：保留现有服务脚本为权威（bundler 不覆盖服务安装器）；**不要把关键 Worker 做成 sidecar**，用「服务 + Rust 客户端」；Linux 明确收窄到 Ubuntu 22.04+/Debian 12+；预留 Tauri 3 迁移预算；做 UI/服务协议版本兼容。
- **若倾向 Avalonia 换皮**：先定义可证伪的锚点验收（例如配置页/日志页对照 Linear 静态稿打分），设止损点；否则第三次投入仍可能停在「套皮后台」。
- **父票的扩围条件已触发**：gpui 在三平台/无障碍/输入法上已出现硬伤（§3.2、§3.3）。是否扩围到 iced/slint/egui/Dioxus，由 #53 决定；本票不擅自展开。
- **无论选哪个栈，先修 §2.6 的两条既有缺陷**，否则新 UI 会在 Linux/macOS 或服务模式上重复踩坑。

### 0.3 三条候选的一句话画像

| 候选 | 一句话 | 最大优势 | 最大风险 |
|---|---|---|---|
| **gpui** | GPU 原生、观感上限最高，但只有「UI 组件」，没有「桌面壳」 | 审美上限 + Rust 单栈 | 三平台门槛、无障碍/IME、依赖自担 |
| **Tauri 2** | Web 前端 + Rust 后端 + 官方壳插件，工程完整度最高 | 壳/分发/共存/生态 | Linux WebKitGTK、服务安装器不在 bundler、Web 感 |
| **Avalonia 换皮** | 最便宜、最稳，可保留全部 C# | 零共存成本、6–9 周 | 审美上限（跨平台材质）、第三轮投入 |

---

## 1. 方法与判据

### 1.1 问题边界

只换 UI 进程的 GUI 栈，保留 .NET Core/Ipc/Worker 与既有 IPC 协议（不改协议、不改端点语义）。候选固定为：gpui（主候选，需先确认指认）、Tauri 2、Avalonia 换设计体系。

**不在本票范围**：Worker/Core 的 Rust 化；IPC 协议改动；Rust 框架海选（仅当 gpui 暴露三平台/共存硬伤才建议扩围）；最终拍板。

### 1.2 判据与权重

来自 #49/#50 的既定顺序：**审美生态上限 > 三平台一致成本 > 与 C# Worker 共存形态 > 稳定性口碑**。

| 维度 | 权重 | 子项 |
|---|---:|---|
| 审美生态上限 | 40 | 组件/设计资源 15；渲染能力（字体/材质/动画）10；观感上限证据 10；设计体系可维护性 5 |
| 三平台一致成本 | 30 | 平台支持完整性 10；发布/签名/更新 10；实机与调试（驱动/字体/输入法）10 |
| 与 .NET Worker 共存 | 20 | IPC 客户端 8；服务探测/进程管理 8；托盘/单实例/对话框 4 |
| 稳定性口碑 | 10 | 版本节奏/breaking 4；生产采用/issue 3；长期治理 3 |

**硬门槛**：任一候选在「平台支持完整性」或「与 .NET Worker 共存」上出现 **1 分**（存在本次迁移内无法绕过的硬伤）→ 无论总分多少都不得作为主选。理由：目的地是让现有三平台用户无损升级，不是做实验品。

### 1.3 证据标准与局限

- 关键事实只用一手来源：官方文档、crates.io/docs.rs、GitHub 仓库/README/releases/issues、微软/systemd/Apple 官方文档、产品官方站点。
- 本地事实以 `origin/main@1d7638c` 为准。
- **局限**：本票不建原型、不上真机；平台支持结论依赖官方声明与公开 issue，强度低于亲手验证。「实现成本」是估算，不是实测；「够不够高级」最终仍需 #53 的人判断。

---

## 2. 现状基线（本仓实测）

### 2.1 规模

| 面 | 实测 | 证据 |
|---|---|---|
| UI 工程 | 71 个 `.cs`/`.axaml`；C# **8,138 行**，XAML **1,500 行** | `find src/PhotoPrivacy.Ui -type f` + `wc -l` |
| 其中 MainWindow | 122 行 XAML + 1,069 行 C# | `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`、`.axaml.cs` |
| Core/Ipc/Worker | **8,136 行** | `find src/PhotoPrivacy.Core src/PhotoPrivacy.Ipc src/PhotoPrivacy.Worker -name '*.cs'` |
| 测试 | 93 个文件、**12,598 行** | `find tests -name '*.cs'` |

UI 与后端体量相当，「只换 UI」在代码量上成立；但 UI 进程还承担了桌面壳与三平台服务编排。

### 2.2 现有 UI 栈与设计体系

- Avalonia **12.1.1** + `Semi.Avalonia 12.1.0.1` + `Irihi.Ursa 2.2.0` + UrsaSemiTheme + DataGrid + Material.Icons + Inter/Cascadia Code。见 `src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj:12-30`、`src/PhotoPrivacy.Ui/App.axaml:12-22`。
- 自建 token：间距 8 档、圆角 5 档、动效 3 档、字体 6+2 角色、阴影 4 档、布局 20+ token。见 `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml:4-103`。
- 504 行 `AppTheme.axaml`：7 个按钮变体 × 5 态、nav 胶囊三态、设置卡片/行、排版 6 角色、焦点环。见 `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml:6-503`。
- 5 套暗色色板 + 亮色，运行时替换 `MergedDictionaries[1]`。见 `src/PhotoPrivacy.Ui/App.axaml.cs:140-183`。
- 10 个 locale JSON（ar/de/en/es/fr/ja/ko/pt/ru/zh-CN），约 250 键。见 `src/PhotoPrivacy.Ui/Localization/Locales/`。
- 4 页：Config（4 组）、Logs、Rules、ServiceManager。见 `src/PhotoPrivacy.Ui/Views/Pages/`、`MainWindow.axaml:79-116`。
- 3 个自研控件：NavButton、ThemeSwatch、ToastHost。见 `src/PhotoPrivacy.Ui/Views/Controls/`。

### 2.3 此前的视觉投入（为什么「换皮」不是新提议）

- 2026-09-12：`ui-craft` 轮票 01 用 atomcode 做过控件级调研（28 次原文抓取），产出 v2.0 视觉规范：MangoDisk 主基准 + Wasabi/Apple/VS Code 气质参考 + 按钮/nav/表单/空态/token 五组定值。见 `docs/design/ui-visual-standard.md:285-330`。
- 规范已落到源码：`DesignTokens.axaml`、`AppTheme.axaml`、按钮 8 变体、nav 胶囊三态、ToastHost、ThemeSwatch。
- 用户仍感知「AI 味重、三平台都丑」，说明问题可能不在「有没有规范」，而在**规范的可执行上限**（框架表现力、平台字体/材质、组件默认模板渗透）或**单点复刻基准的局限**。

### 2.4 桌面壳能力（UI 进程实际承担）

| 能力 | 本地实现 | 换栈影响 |
|---|---|---|
| 托盘 + 原生菜单 | Avalonia `TrayIcon` + `NativeMenu`（暂停/恢复、打开、退出） | gpui 无内建；Tauri 有官方 tray |
| 关窗隐藏 | `TrayHost.OnWindowClosing` 取消关闭并隐藏 | 需重写生命周期 |
| 单实例 | 全局 Mutex/POSIX 锁 + 命名管道唤起 | 需重写；Rust 有文件锁/第三方方案 |
| 自绘标题栏 | `WindowDecorations=None` + 自绘三键 | gpui 需自绘；Tauri 可用系统装饰 |
| 服务管理 | Windows `sc.exe`；Linux `pkexec`+systemd；macOS `osascript`+launchd | UI 仅 Windows 暴露服务页，但探测/编排跨三平台 |
| 服务状态探测 | Windows ServiceController；Linux D-Bus；macOS `launchctl` | 见 `ServiceManager.cs:406-420` |
| 主题/语言切换 | 运行时换字典 + JSON locale | 机制需重做，键值可复用 |

证据：`TrayHost.cs:35-50`、`UiSingleInstance.cs:8-50`、`MainWindow.axaml:18-25`、`ServiceManager.cs:34-420`、`SystemdCommandExecutor.cs:54-98`、`LaunchdCommandExecutor.cs:49-88`。服务页仅在 Windows 显示：`MainWindow.axaml.cs:137`、`ServiceModeController.cs:251`。

### 2.5 IPC 协议与 Worker 生命周期

- 传输：Windows 命名管道；Linux/macOS UDS。见 `IpcTransportFactory.cs:23-55`、`WorkerIpcEndpointNames.cs:6-21`。
- 协议：UTF-8 JSON + `\n`，请求 `{method,id,v}`，响应 `{ok,data,logs,message,id,v}`。见 `WorkerIpcContracts.cs:17-38`。
- 8 方法：Ping/GetStatus/Pause/Resume/GetExifToolVersion/Shutdown/ReloadConfig/GetRecentLogs。见 `WorkerIpcContracts.cs:7-14`。
- 客户端约束：连接 700ms、读 3s、响应上限 64KB、一连接一请求一响应。见 `WorkerIpcClient.cs:14-59`。
- Worker 启动：`PhotoPrivacyWorker --mode background --config <path>`，无窗口。见 `WorkerProcessManager.cs:57-60,90-111`。
- 连接编排：服务端点优先 → 已安装未运行则服务壳 → 后台端点 → 启动 → 5s 轮询。见 `WorkerProcessManager.cs:19-84`。
- 认证：Windows `CurrentUserOnly`；Unix 0600/0660 + 对端 uid 白名单 fail-closed。见 `NamedPipeIpcTransport.cs:62-103`、`UnixDomainSocketIpcTransport.cs:56-137`。

**关键实现细节（Rust 客户端必须知道）**：

- **响应没有结尾换行**，服务端写完即关闭；必须循环读到 EOF 或 64KB，不能等 `\n`。见 `WorkerIpcServerLoop.cs:116-133`。
- **服务端同一时刻只预建 1 个管道实例**，并发会 `ERROR_PIPE_BUSY`，还有极短「无实例」窗口；必须单飞 + 重试 busy/not-found。见 `NamedPipeIpcTransport.cs:30-40`。
- `ReloadConfig` 可能超过 3s（停 bridge 4s），Rust 客户端应单独放宽到 ≥6s。见 `WorkerIpcRequestHandler.cs:10-16`。
- 现有 UI 的 `Shutdown` 在 service/background 模式会被拒绝（返回 `ok:false`），退出不要依赖它。见 `WorkerIpcRequestHandler.cs:65-74`。

### 2.6 两条既有跨平台缺陷（会污染「三平台基线」）

**6.1 UI 客户端从未按 OS 解析端点**

- Worker 服务端按 OS/模式解析：`IpcTransportFactory.ResolveEndpoint` → Linux/macOS 用 UDS 路径。见 `WorkerIpcServerHostedService.cs:39-44`。
- UI 却全程硬编码 Windows 管道名，从未调用 `ResolveEndpoint`：`WorkerProcessManager.cs:25-82`、`ServiceModeController.cs:177,270,390,413,430,457`、`AppComposition.cs:35,120`。
- 结果：Linux/macOS 上 UI 会拿 `PhotoPrivacyCleaner.Background` 当 UDS 路径去连，而服务端监听 `/run/photoprivacy/worker-background.sock`（macOS 为 `~/Library/...`）。两端不匹配，UI 连不上 Worker。
- 仓库内 `ResolveEndpoint` 调用点只有服务端一处（`grep -rn "ResolveEndpoint" src tests`）。

**6.2 Windows 服务模式管道认证自相矛盾**

- 安装脚本以默认账户（LocalSystem）注册服务，没有 `obj=`：`scripts/install-service.ps1:44-46`。
- 服务端管道统一 `PipeOptions.CurrentUserOnly`：`NamedPipeIpcTransport.cs:83-98`。该选项的 DACL 只授予创建者（服务进程 = SYSTEM）的 owner SID；普通用户 UI 连接会 ACCESS_DENIED（.NET 实现见 [NamedPipeServerStream.Windows.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs)）。
- UI 目前用「服务已安装即显示服务壳、状态留空」绕过：`WorkerProcessManager.cs:36-44`。服务模式的真实连通性没有被这条路径验证。

**含义**：任何 Rust UI 都修不了这两条；必须先修 .NET 侧（端点解析、DACL）或明确产品只用 background 模式。这是独立工单，不应算进「gpui/Tauri 共存成本」。

### 2.7 发布链成本

- 7 个 RID：win-x64/win-x86/win-arm64/linux-x64/linux-arm64/osx-x64/osx-arm64（`scripts/publish-app.ps1:32`、`publish.sh:42`）。
- 3 个服务安装脚本 + `release-readiness.ps1` + `smoke.ps1` + `benchmark.ps1`。
- 发布属性只经脚本 `/p:` 传入（`AGENTS.md` 工程红线）。

三平台成本的主体现在是「7 RID 构建 + 3 套服务安装 + 实机冒烟」，不是 UI 框架；只有「UI 与 Worker 同栈」才可能减少 RID（本方案不换 Worker，故不减）。macOS 目前**未签名、未公证**；全仓无 `codesign`/`notarytool`/`signtool`（`D` 线核对 `.github/workflows/release.yml`）。

---

## 3. 候选 A：gpui

### 3.1 指认与治理

| 项 | 值（截至 2026-10） |
|---|---|
| crate | `gpui`，描述 "Zed's GPU-accelerated UI framework" |
| 最新官方版 | **0.2.2（2025-10-22）**，此后一年未更新 |
| 维护方 | Zed Industries（`zed-industries/zed` monorepo） |
| license | **Apache-2.0**（crate 级；Zed 编辑器本体是 GPL-3.0） |
| 仓库规模 | 92,165 star / 483 贡献者 / 3,099 open issue |
| 发布节奏 | Zed 应用每周 4+ release；框架无 semver 承诺 |
| 实际依赖路径 | git rev、社区快照 `gpui-pre` 0.3.9（Longbridge 重发）、或 `gpui-ce`/`gpui-fast` fork |

- 【事实】官方 README 明确：「GPUI is still in active development… still pre-1.0. There will often be breaking changes between versions.」且要求配合 `gpui_platform`——该 crate **未发布到 crates.io**。[来源](https://github.com/zed-industries/zed/blob/main/crates/gpui/README.md)
- 【事实】main 已从单 crate + blade 迁移到 `gpui` + `gpui_platform` + `gpui_wgpu`（wgpu 以 git rev 固定），与 0.2.2 架构性分叉。[来源](https://github.com/zed-industries/zed/blob/main/Cargo.toml)
- 【事实】2026-02 维护者在社区表示 GPUI 开发「被大幅踩刹车」，只做与 Zed 自身用例直接相关的事，外部诉求导向社区 fork `gpui-ce`。[来源](https://news.ycombinator.com/item?id=47003569)（二手引述，可信度低于仓库数据）
- 【推断】换 gpui 的依赖治理形态不是「用一个稳定 SDK」，而是「维护一条自己的分支/锁 rev + 跟上游重构」。重度采用者（Sonora、muxy、frame）确实各自 fork/vendor。

### 3.2 三平台支持与硬伤

| 平台 | 官方要求 | 状态 |
|---|---|---|
| Windows | Win11 22H2+ / Win10 1903+；DX11 GPU（2012+）；驱动不能是 Microsoft Basic Display Adapter；DirectWrite | 稳定版 2025-10-15；IME/无障碍待打磨 |
| Linux | **Vulkan 1.3** 驱动；三个 desktop portal；glibc x86_64 ≥2.31 / aarch64 ≥2.35；X11 + Wayland | 官方支持；分数缩放/桌面环境仍有 issue |
| macOS | macOS 12+（11/10.15 部分支持）；Metal | 官方支持 |

- 【事实】**无软件渲染兜底**：`gpui_software` 提议未合入；虚拟化环境实测「每秒几帧」。[来源](https://github.com/zed-industries/zed/discussions/57066)
- 【事实】GPU device loss 优雅恢复 issue 自 2025-01 开放至今（#23288）。[来源](https://github.com/zed-industries/zed/issues/23288)
- 【事实】Windows 屏幕阅读器完全缺失（#41138）；Wayland fractional scaling 仍有 blurry 报告（#65032）；GNOME 50/Wayland 卡顿（#53522）；X11 下 Qt 源拖放失败（#65679，直接影响「拖照片进窗口」）；CJK 字形图集双线性采样导致额外模糊（#57777）；IME 组合选区错位并可 panic（#64389）；X11 窗口语义一批 issue（#64447/64448/64449/65493）。
- 【事实】AccessKit 2026-05 才合入，Zed 官方仍称不支持屏幕阅读器。[来源](https://github.com/zed-industries/zed/issues/41138)
- 【推断】对本项目，最相关的不是「编辑器级」bug，而是 **CJK 渲染、中文 IME、X11 拖放、无 GPU 环境**——它们正好落在「照片清理工具」的主交互与中文界面上。

### 3.3 组件生态与桌面壳

**组件层（强）**：

- 【事实】`gpui-kit`（原 gpui-component，Longbridge）v0.8.0（2026-10-19），75+ 组件、18.6k star、207 贡献者、每周发版；覆盖按钮/输入/表格/虚拟列表/表单/模态/日期/Markdown/代码编辑器/Toast/设置/命令面板/图表/Diff。[来源](https://gpui-kit.com/component)
- 【事实】token 五族（Color/Radius/Spacing/Typography/Shadow + Motion）、JSON 主题、38 套预设、System 模式、热加载；图标 Lucide 1,830 个；CJK 字体需自带/回退（缺字会方块）。[来源](https://gpui-kit.com/component/theme) [来源](https://gpui-kit.com/docs/i18n)
- 【事实】生产验证：Longbridge Pro 商业桌面端；Showcase 收录 86 个应用；OpenLogi（24k star）、Zedis、Sonora、Hummingbird 等。[来源](https://gpui-kit.com/apps)
- 【推断】四页所需的控件不需要自造；工作量在「拼产品 + 视觉语言」，不在「有没有控件」。

**桌面壳（弱）**：

| 能力 | 归属 | 证据 |
|---|---|---|
| 托盘 + 菜单 | **必须自建/第三方** | 上游 PR #44047 被关闭；gpui-kit 无 Tray；`gpui-tray` 0.3.0 仅 12 star、2 个月、单人维护 |
| 单实例 + 唤起 | **必须手写** | Zed 自写三套平台代码；`single-instance` 停更 2021 |
| 开机自启 | **必须自建** | `auto-launch` 可用 |
| macOS 应用菜单 | 内建 `set_menus` → NSMenu | Win/Linux 只存数据，需自绘 |
| 文件对话框 | 内建 `prompt_for_paths` | Win IFileOpenDialog / mac NSOpenPanel / Linux xdg-portal |
| 剪贴板/通知/多窗口/拖放 | 内建 | 三平台原生实现；通知需 AppUserModelID/bundle 身份 |
| 打包/签名/公证/更新 | **无官方方案** | 需自建三平台流水线 |

- 【事实】上游托盘 PR 关闭理由：「我们正把重心转向核心产品工作，没有 Zed 的使用场景我无法验证这段代码」。[来源](https://github.com/zed-industries/zed/pull/44047)
- 【事实】`gpui-tray` 的 README 仍写 `gpui-pre 0.3.4`，而当前 gpui-kit 0.8.0 pin 的是 0.3.9，版本咬合需自行核对。[来源](https://github.com/domenkozar/gpui-tray)
- 【推断】桌面壳自建约 **1.5–2.5 人周**（含三平台真机），打包/签名/更新 **2–4 人周**（首套最贵）。UI 组件不是主要成本。

### 3.4 审美上限

- 【事实】**有**：GPU 合成、阴影（drop/inset/spread）、四角独立圆角、cubic-bezier/spring/keyframes、窗口级透明/Blur/Mica、HiDPI、OpenType、三平台原生字体。
- 【事实】**没有**：元素级背景模糊（backdrop blur）、squircle 圆角平滑——上游 0 命中，只有 `gpui-ce` fork 有；可变字体按字重渲染有 bug（#64783）、连字跨高亮边界有 6+ open issue。[来源](https://github.com/gpui-ce/gpui-ce)
- 【推断】gpui 能稳定验证的风格区间是 **Linear/Vercel 系**（深色、硬边、精确排版、极轻阴影），不是 Apple 玻璃/大圆角/弹性动效系。若目标审美偏后者，缺的恰是辨识度最高的两块，**不是多写代码能补**。
- 【事实】最精致的非 Zed gpui 应用：Longbridge Pro、Sonora、Hummingbird、Zedis、OpenLogi；共同特征是深色、高密度、轻边框、克制动效。[来源](https://gpui-kit.com/apps)

### 3.5 分发与稳定性

- 【事实】Zed 官方产物：macOS DMG 87–95 MB、Windows EXE 57–64 MB、Linux tar.gz 85–90 MB；自带增量更新；Linux 依赖包管理器/脚本。[来源](https://github.com/zed-industries/zed/releases/tag/v1.25.3)
- 【事实】gpui 层无 bundler/notarize/update server 抽象；现有 `scripts/publish-app.ps1` 不可复用。
- 【推断】纯 gpui 应用基线会小于 Zed，但 gpui + 字体/着色器资源，**几十 MB 是合理预期**，「几 MB 安装包」不现实。
- 【事实】稳定性两面性：作为应用（Zed）每天被大量使用；作为框架无兼容承诺、无 LTS、无 deprecation 周期。API/依赖可靠性低（周更 breaking + 官方包停更）。
- 【推断】「稳定性口碑」要拆开：**运行时可靠性高，API/依赖可靠性低**。前者影响用户体验，后者影响维护工时。

### 3.6 与 .NET Worker 共存

- 【事实】协议小（8 方法、单行 JSON、一连接一请求），Rust 客户端约 **200 行**可覆盖；真正的坑在 Windows 管道权限、单实例管道、服务模式语义，不在 JSON。[证据见 §2.5/§2.6]
- 【事实】gpui 用 smol/async-task 执行器，**没有 tokio**；需单独起 tokio runtime 线程跑 IPC，再用 channel + `cx.spawn` 回灌 UI。[来源](https://crates.io/crates/gpui)
- 【事实】Windows 命名管道客户端 `open()` 是同步的；`poll_shutdown` 只 flush，无半关闭；DACL 只有 unsafe raw 入口。[来源](https://docs.rs/tokio/1.54.1/tokio/net/windows/named_pipe/index.html)

**Rust 客户端必须实现的契约**（全部来自本仓代码）：

| 约束 | 值 | 证据 |
|---|---|---|
| framing | 请求 `JSON + \n`；响应**无结尾换行**，以连接关闭定界 | `WorkerIpcClient.cs:28-42`、`WorkerIpcServerLoop.cs:116-133` |
| 读取 | 循环读到 EOF 或 64KB，不能单次 `read()` 后解析 | `IpcLineReader.cs:27-42` |
| 并发 | 单飞（全局一把异步锁）；服务端只预建 1 个管道实例 | `NamedPipeIpcTransport.cs:30-40` |
| 重试 | `ERROR_PIPE_BUSY` / `ERROR_FILE_NOT_FOUND`（无实例窗口） | 同上 |
| 超时 | 连接 700ms；普通读 3s；`ReloadConfig` 放宽到 ≥6s | `WorkerIpcClient.cs:14-15,26,36-38`、`WorkerIpcRequestHandler.cs:10-16` |
| 关闭 | Windows 无半关闭，写完保持句柄直到读完响应 | tokio 文档 |
| 端点 | Windows 管道名；Unix 绝对路径（**必须按 OS 解析**） | `IpcTransportFactory.cs:37-55` |

**服务探测/进程管理的两种处置**：

| 方案 | 做法 | 成本 | 风险 | 结论 |
|---|---|---|---|---|
| A. Rust 侧重写（只读） | `windows-service` 查 SCM；`systemctl is-active` 退出码；`launchctl print` | S–M（约 200–300 行 + 测试） | 低：只读、无提权、失败可降级 | ✅ 推荐 |
| A'. Rust 侧重写（含安装/启停） | 同上 + UAC/polkit/osascript 三条提权路径 + 脚本契约 | M–L | 中高：提权/取消/超时分支多，10 个 `sc.exe` 退出码要重测 | ⚠️ 不划算 |
| B. 下沉进 .NET Worker | 新增 IPC 方法，由 Worker 安装/启停自己 | H | 高：Worker 无法自我提权；服务运行于 Session 0；把特权操作塞进只有 OS 身份边界的 IPC，任何同用户进程都能触发 | ❌ 否决 |
| C. 独立 .NET 控制宿主 | 抽 `ServiceManager`+`ServiceModeController` 为无 UI 进程，Rust 经 stdio/socket 调 | M | 中：多一个进程/生命周期面 | 🟡 备选（第二阶段） |

- 【事实】Windows 服务运行在 Session 0，不能与用户交互；GUI 必须由用户会话进程承担，二者只能 IPC。[来源](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)
- 【事实】systemd 特权操作由 polkit 授权；launchd system domain 修改需 root；换成 Rust 一点也省不掉。[来源](https://www.freedesktop.org/software/systemd/man/latest/org.freedesktop.systemd1.html) [来源](https://keith.github.io/xcode-man-pages/launchctl.1.html)
- 【推断】**最稳的路径是 A + C**：Rust 只做 IPC 客户端 + 只读探测；安装/启停继续 shell out 到现有脚本；若坚持「下沉」，只能是独立控制宿主，不是 Worker。

**模块处置清单**：

| 模块 | 现文件 | 处置 |
|---|---|---|
| IPC 客户端（8 方法 + 超时 + 降级） | `WorkerIpcClient.cs` | Rust 重写 |
| Worker 启动/连接编排 | `WorkerProcessManager.cs` | Rust 重写（spawn + 5s 轮询；注意 Windows `CREATE_NO_WINDOW`） |
| 服务只读探测 | `ServiceManager` probe + `SystemdStateProbe`/`LaunchdStateProbe` | Rust 重写（或调用脚本） |
| 服务安装/启停 | `ServiceManager` 写路径 + 三个 executor | **保留在 .NET/脚本**，不要下沉 Worker |
| 模式状态机 | `ServiceModeController.cs` | Rust 重写 |
| 托盘/单实例/Toast | `TrayHost.cs`、`UiSingleInstance.cs`、`ToastService.cs` | Rust 重写（gpui 需第三方；Tauri 有插件） |
| 配置/日志/locale | `ConfigEditor`/`AuditTailService`/`LocalizationService` | Rust 重写；locale 键值可复用 |
| Core/Worker/IPC 服务端/脚本 | — | **零改动** |

**结论**：共存本身不是 gpui 的短板（Rust 侧能连），但 gpui 没有 tokio/托盘/单实例，壳层要一起重写；Tauri 的 tokio + sidecar + 官方插件使共存胶水最少；Avalonia 零共存成本。真正决定工作量的是「服务管理是否下沉」，与 UI 框架弱相关。

### 3.7 gpui 小结

**适合**：审美目标偏 Linear/Vercel 系、接受 GPU 门槛、愿意维护 fork/锁 rev、把桌面壳与打包当作独立工程项。
**不适合**：必须覆盖 VM/远程桌面/老显卡、无障碍是硬要求、希望「尽快跑通三平台壳」、团队没有 Rust 桌面壳经验。
**触发扩围**：三平台/无障碍/IME 已出现硬伤，父票 Out of Scope 的扩围条件成立。

---

## 4. 候选 B：Tauri 2

### 4.1 版本与平台矩阵

- 【事实】Tauri 2 稳定线当前 **2.12.5**（2026-10-21）；2.0 稳定版发布于 **2024-10-08**；crates.io 上已出现 **3.0.0-alpha.5**（2026-10-14），带来 GTK4/WebKitGTK 6.0 方向、可切换 CEF 运行时、Linux tray 改用 ksni 等破坏性变化，非稳定线，不应作为生产基线。[来源](https://crates.io/crates/tauri) [来源](https://v2.tauri.app/blog/tauri-20/) [来源](https://github.com/tauri-apps/tauri/releases/tag/tauri-v3.0.0-alpha.5)
- 【推断】若产品生命周期 2–3 年，要把「未来一次 Tauri 3 迁移」计入成本；2.12.5 不是永久基线。
- 【事实】官方插件与 `tray-icon` 要求 **Rust ≥ 1.90**（2025-08 稳定 `File::try_lock` 的同一版本线）。[来源](https://v2.tauri.app/plugin/single-instance/) [来源](https://docs.rs/tray-icon/0.26.1/tray_icon/)
- 【事实】平台前置：
  - Windows：WebView2 Runtime（Win10 1803+ 已预装）、MSVC Build Tools。
  - macOS：WKWebView（系统自带）、Xcode/CLT。
  - Linux：**WebKitGTK 4.1**（`libwebkit2gtk-4.1-dev`）+ `libappindicator` + `librsvg` + OpenSSL。
  [来源](https://v2.tauri.app/start/prerequisites/)
- 【事实】三个平台用的是**三个不同 WebView**（WebView2/Chromium、WKWebView、WebKitGTK），字体、滚动、表单控件、动画与输入法的行为并不完全一致；Linux 还依赖发行版提供的 WebKitGTK 版本。

### 4.2 打包与分发

- 【事实】官方 bundler 覆盖：Windows **MSI / NSIS / Microsoft Store**；macOS **App / DMG / App Store**；Linux **deb / rpm / AppImage / Flatpak / Snap / AUR**。签名与 macOS 公证是分发要求的一部分。[来源](https://v2.tauri.app/distribute/)
- 【事实】官方 **updater** 插件支持 Win/Linux/macOS，要求签名密钥（CLI 生成公私钥），通过静态 JSON 或动态服务器（`{{target}}/{{arch}}/{{current_version}}`）分发。[来源](https://v2.tauri.app/plugin/updater/)
- 【事实】Flatpak 不是 bundler 直接产出，需要自己写 manifest 并向 Flathub 提 PR；Snap 同理。[来源](https://v2.tauri.app/distribute/)
- 【事实】**bundler 不覆盖「系统服务安装器」**：Windows 需要自定义 NSIS/WiX hook 才能装服务；Linux 的 deb/rpm 可带 pre/post-install 脚本，但 **AppImage 是单文件、无安装脚本**，装不了 systemd；macOS **没有 pkg 目标**，DMG 只是拖拽安装，装不了 LaunchDaemon。[来源](https://v2.tauri.app/distribute/windows-installer/) [来源](https://v2.tauri.app/distribute/dmg/) [来源](https://v2.tauri.app/distribute/rpm/)
- 【事实】**updater 与安装格式错位**：Linux 官方 updater 只覆盖 **AppImage**，deb/rpm 不在更新产物内；Windows 是 MSI/NSIS 的 zip+sig，macOS 是 `.app.tar.gz`+sig。若主推 deb/rpm 便于装 systemd，就要自建 apt/dnf 仓库或调用包管理器。[来源](https://v2.tauri.app/plugin/updater/)
- 【推断】对本项目：Tauri 只负责 UI 包，现有 `install-service.ps1` / `install-systemd-service.sh` / `install-launchd-service.sh` 继续作为服务安装权威，缺口可控；若希望「装 UI 时一并装服务」，Tauri bundler 不能三平台一致完成，需要自建安装链。
- 【推断】对本项目，安装包体积的主项不是 Tauri 壳（通常几 MB），而是 **.NET Worker sidecar**（自包含发布可能几十 MB）。Tauri 的 bundler 能把 sidecar 一起打包，但 macOS 公证要对 sidecar 单独签名。
- 【推断】Tauri 的三平台发布链仍要付 Apple Developer ID/公证、Windows 代码签名、Linux 包维护；相比 Avalonia 的现状（无签名/公证/更新），Tauri 提供了现成机制，但不是零成本。

### 4.3 桌面壳：官方能力最完整

| 能力 | Tauri 2 | 证据 |
|---|---|---|
| 系统托盘 | **内建**（`tray-icon` feature） | [文档](https://v2.tauri.app/learn/system-tray/) |
| 原生菜单 | **内建**，Win/Linux/macOS 均支持（macOS 第一个子菜单为 App 菜单） | [文档](https://v2.tauri.app/learn/window-menu/) |
| 单实例 + 唤起 | 官方 `single-instance` 插件；第二实例退出，回调里手动 `set_focus()` 唤起 | [文档](https://v2.tauri.app/plugin/single-instance/) |
| 开机自启 | 官方 `autostart` 插件 | [插件目录](https://v2.tauri.app/plugin/) |
| 原生通知 | 官方 `notification` 插件 | 同上 |
| 文件对话框 | 官方 `dialog` 插件 | 同上 |
| 自动更新 | 官方 `updater` 插件（签名） | [文档](https://v2.tauri.app/plugin/updater/) |
| Shell / Sidecar | 官方 `shell` 插件 + `externalBin` | [文档](https://v2.tauri.app/develop/sidecar/) |
| 托盘/菜单在 Linux 的坑 | Linux 托盘事件有已知限制（文档注明部分事件不支持）；`show_menu_on_left_click` 在 Linux 不支持 | [tray 文档](https://docs.rs/tauri/latest/tauri/tray/struct.TrayIconBuilder.html) |

- 【事实】single-instance 必须是**第一个注册**的插件；Linux 用 D-Bus 实现，Snap/Flatpak 沙箱默认会阻断 D-Bus，需要在 manifest 显式授权。[来源](https://v2.tauri.app/plugin/single-instance/)
- 【事实】通知在 Windows **只对已安装应用生效**（开发态显示 PowerShell 名称/图标）；macOS 需用户授权；Linux 依赖桌面通知服务。[来源](https://v2.tauri.app/plugin/notification/)
- 【事实】sidecar **没有生命周期管理**：崩溃检测、重启退避、健康检查、优雅退出、孤儿进程清理都要自己写。[来源](https://github.com/tauri-apps/plugins-workspace/issues/3062)
- 【推断】这是 Tauri 相对 gpui 的最大工程优势：托盘、单实例、自启、通知、对话框、更新全部有官方实现，不需要自建三平台原生代码。
- 【推断】相对 Avalonia：Avalonia 的 TrayIcon/NativeMenu 已可用，但 Linux 托盘只支持 AppIndicator；Tauri 的托盘同样依赖桌面环境，不保证 GNOME 默认可用。

### 4.4 与 .NET Worker 共存

- 【事实】Tauri 的后端运行时就是 **tokio**（`tauri::async_runtime`），Rust 侧可直接 `await` 命名管道/UDS 客户端；不需要像 gpui 那样桥接 smol→tokio。[来源](https://docs.rs/tauri/2.12.5/tauri/async_runtime/index.html)
- 【事实】`.NET Worker` 可作为 **sidecar**：`tauri.conf.json` 的 `externalBin` 列路径，二进制带 `-TARGET_TRIPLE` 后缀，capability 加 `shell:allow-execute`/`shell:allow-spawn`；用 `app.shell().sidecar("name")` 启动，drop 子进程句柄即终止。[来源](https://v2.tauri.app/develop/sidecar/)
- 【推断】两种接法：
  1. **不打包 sidecar**：沿用现有布局，Rust 在 UI 同目录找 `PhotoPrivacyWorker` 并 `Command` 启动（与现 UI 的 `WorkerProcessManager` 等价）。改动最小，但安装器要保证 Worker 与 UI 同目录。
  2. **sidecar 打包**：由 Tauri 管理 Worker 生命周期；适合托盘常驻，但 macOS 公证要对 sidecar 单独签名，且 sidecar 的崩溃恢复要自己写。
- 【事实】Tauri 维护者明确：**Rust core 不能被替换**；接 C# 的可行路径是 sidecar（stdio/TCP/HTTP/WS/socket/pipe），没有 C# 专门文档，「a little on your own」。[来源](https://github.com/tauri-apps/tauri/issues/5174)
- 【事实】**生产先例**：Clash Verge Rev 是 Tauri 2 壳 + 独立特权服务，Windows 走 `\\.\pipe\clash-verge-service`，macOS/Linux 走 Unix socket，并带 `ProtocolVersion`、`OwnerSessionProof`/owner token 等鉴权与版本协商；服务端是 Rust，但「Tauri 壳 + 特权服务 + 管道/UDS」这套模式与本项目同构。[来源](https://github.com/clash-verge-rev/clash-verge-rev) [来源](https://github.com/clash-verge-rev/clash-verge-service-ipc/blob/main/src/lib.rs)
- 【推断】**推荐拓扑 B：服务客户端**。PhotoPrivacy 本来就是「服务 Worker + UI 客户端」，Rust UI 只替换原来的 C# IPC 客户端，C# Worker 仍是 Windows 服务/systemd/launchd，生命周期由 OS 管理；不需要 `externalBin`，UI 可独立更新。这比 sidecar 更贴合现有架构。
- 【推断】代价：UI 与服务独立更新 → 必须做**协议版本兼容**（UI 先更新、服务仍旧版时不能握手失败）；日志流不要每行一次 `invoke`，用 Tauri `Channel` + 50–100ms 合批；大文件用 `tauri::ipc::Response` 返回 `Vec<u8>`，不要 JSON 数组。
- 【事实】Windows 服务模式的 DACL 问题与 Tauri 无关，仍需 .NET 侧修（§2.6）。
- 【推断】共存胶水成本：Tauri 最低（tokio + 官方 shell/sidecar + 单实例插件 + 生产先例）；gpui 中等（tokio 桥接 + 自建壳）；Avalonia 为零。

### 4.5 审美上限与「Web 感」

- 【事实】Web 前端生态可直接复用：React/Vue/Svelte/Solid、Tailwind、shadcn/ui、Radix、Fluent UI 等；设计 token 用 CSS variables 表达，主题/暗色/密度控制成熟。
- 【事实】锚点里的 **Linear 与 Raycast 本身就是 Web/Electron 技术**；Linear 桌面端复用了同一套 React 前端，说明 Web 栈可以达到目标审美。[来源](https://linear.app/changelog/2019-04-25-linear-desktop-app)
- 【事实】Tauri 也支持原生窗口效果（macOS vibrancy、Windows Mica 等），不是只能做纯 Web 外观。[来源](https://v2.tauri.app/reference/config/)
- 【推断】风险不在「能不能画」，而在「像不像桌面应用」：
  - 字体：WebView 的系统字体栈与原生控件不同，中文/日文在三个 WebView 下观感不一致；需要自带字体子集。Tailwind 4 的官方浏览器基线（Safari 16.4 / Chrome 111）会把 macOS 实际下限抬到约 **13.3**，10.13/10.15 的 WKWebView 不适合现代设计系统。[来源](https://tailwindcss.com/docs/compatibility)
  - 滚动与选中：macOS WKWebView 的橡皮筋、Linux WebKitGTK 的平滑滚动与原生不同；长日志列表要虚拟化。
  - 原生菜单/托盘：由 Tauri 提供，但菜单项与 Web 内容的焦点/快捷键协同要设计；Linear 式自绘侧栏 + macOS 全局菜单等于两套菜单。
  - 拖放：WebView 的 HTML5 拖放与原生文件拖放不同；需要用 Tauri 的 drag-drop 事件，而不是浏览器 API。
  - 性能：WebView2/WKWebView 通常足够；Linux WebKitGTK 在低端机/高分屏上可能掉帧，且 WebGL 会静默降到软件光栅化。[来源](https://v2.tauri.app/develop/debug/linux-graphics/)
  - 「AI 味」：默认 shadcn/ui 外观本身就是新的模板感来源；设计上必须按锚点重做字体、密度、层级，否则会从「Avalonia 模板感」变成「Web 后台感」。
- 【推断】若目标是「Linear 静态观感 + 桌面级交互」，Tauri 的达成概率高于 gpui（生态成熟、迭代快）；若目标是「GPU 原生质感 + 完全自绘」，gpui 的上限更高。

### 4.6 稳定性与口碑

- 【事实】Tauri 2 自 2024-10 稳定，当前 2.12.5；官方插件在同一组织维护，版本节奏稳定；3.0 处于 alpha，带来 GTK4/WebKitGTK 6.0、CEF 运行时、ksni tray 等破坏性变化，升级噪音存在但可控。[来源](https://crates.io/crates/tauri) [来源](https://github.com/tauri-apps/tauri/releases/tag/tauri-v3.0.0-alpha.5)
- 【事实】已知硬伤集中在 Linux：WebKitGTK 4.1 的发行版覆盖、NVIDIA/DMA-BUF 黑屏/闪烁、Wayland DPI、tray 宿主、WebGL 静默降级；社区要求「打包 Chromium」的 issue 已积累 242 个 reaction，仍 open。[来源](https://v2.tauri.app/develop/debug/linux-graphics/) [来源](https://github.com/tauri-apps/tauri/issues/14963) [来源](https://github.com/tauri-apps/tauri/issues/5600)
- 【事实】Windows 依赖 WebView2（现代 Windows 已预装，但 LTSC/Server/精简版可能缺失）；macOS WKWebView 的能力跟随 OS；插件工作区约 476 个 open issue/PR，说明活跃但边界情况不少。[来源](https://github.com/tauri-apps/plugins-workspace)
- 【推断】稳定性口碑优于 gpui（框架/插件同组织、无周更 breaking），略优于 Avalonia 12（Tauri 2 已稳定两年，Avalonia 12 仅 6 个月）；但 Linux 的 WebView 差异是长期税，且 Tauri 3 迁移要预留预算。

### 4.6.1 生产采用案例（证明「Tauri 壳 + 服务/托盘/更新」可行）

| 应用 | 技术栈证据 | 与本项目的相关性 |
|---|---|---|
| **Clash Verge Rev** | Tauri 2.12.1 + React 19.3；托盘、autostart、updater、特权服务、named pipe/UDS、协议版本/owner token | **最接近**：托盘常驻 + 服务 + 三平台 + 自动更新 |
| **Yaak** | Tauri 2.11 API 客户端；普通 wry + CEF 变体 | 证明 CEF 可生产，但需 pin fork |
| **Readest** | Tauri 2.11 + Next 16 + Radix + Tailwind 4；Linux CEF 实验 | 证明 shadcn/Radix 栈可行 |
| **Spacedrive / Jan / Cap** | Tauri 2 + React/Solid；复杂列表、本地 AI、录屏 | 复杂前端 + 原生能力共存 |
| **Echo Grid** | Tauri 2 + SvelteKit 5 + ASP.NET Core 8 + SQLite | **C# 服务 + Tauri** 的真实案例（规模小） |
| **Screenpipe** | Tauri 2.11 + 后台进程 | sidecar 生命周期痛点 |

- 【事实】Clash Verge Rev 的 Windows setup 约 51MB、Linux deb 约 99MB，同时带 tray/autostart/updater/特权服务。[来源](https://github.com/clash-verge-rev/clash-verge-rev/releases)
- 【事实】没有找到「大型知名应用 + C# Worker + Tauri」的公开案例；Clash Verge 证明的是 Tauri 壳 + 服务 IPC 模式，C# 侧要自己验证。

### 4.6.2 性能与体积（不要把 Tauri 当「一定更轻」）

- 【事实】第三方基准：release 中位内存 Windows Tauri **307MB** vs Electron 279MB；macOS 96MB vs 364MB；Linux 94MB vs 589MB；空壳安装包 Windows/macOS/Linux 约 3–5MB；真实 Tauri 应用 Windows/macOS 39–67MB、Linux AppImage 145–202MB 常见。[来源](https://github.com/Elanis/web-to-desktop-framework-comparison) [来源](https://github.com/mountain-loop/yaak/releases)
- 【事实】同一基准构建时间：Windows 空壳 Tauri ~234s、macOS ~129s、Linux ~234s；Rust 冷构建明显慢于 Electron。[来源](https://github.com/Elanis/web-to-desktop-framework-comparison)
- 【推断】对本项目：
  - Windows 托盘常驻内存必须实机测，不能假设 Tauri 比 Avalonia 轻；WebView2 的进程模型可能让常驻成本偏高。
  - 安装包主项仍是 .NET Worker（自包含）；Tauri UI 包不是裁决关键。
  - 日志页现有上限 500 条，性能不构成区分度；若未来要 10 万行，WebKitGTK 必须压测 + 虚拟化 + 合批。

### 4.7 Tauri 2 小结

**适合**：希望三平台壳/分发/更新一次到位、接受 Web 前端、把「像桌面应用」当作设计验收项。
**不适合**：完全拒绝 WebView、要求 GPU 原生自绘、要求 Linux 所有发行版零依赖。
**关键前提**：设计上必须按锚点做「桌面化」处理（字体、滚动、菜单、拖放、密度），否则会从「Avalonia 模板感」变成「Web 后台感」。

---

## 5. 候选 C：Avalonia 换设计体系

### 5.1 版本与主题体系

- 【事实】Avalonia 稳定线 **12.1.4（2026-10-09）**；12.0（2026-04-07）、12.1（2026-07-09）；11.3 仍在维护。仓库锁 **12.1.1**，落后 3 个补丁版。[来源](https://www.nuget.org/packages/Avalonia)
- 【事实】主题机制完整：Styles + ControlTheme + ResourceDictionary + ThemeDictionaries；**没有一等公民 design token 机制**，token 只能是自建资源键 + 命名纪律。[来源](https://docs.avaloniaui.net/docs/styling/control-themes)
- 【事实】官方内置主题只有 FluentTheme（仍是 WinUI 2.5 / Win10 风格）与 SimpleTheme；Fluent v2（WinUI 3）自 2021 年立项至今 open（#6269）。
- 【推断】默认模板的「AI 味/年代感」根源在此：不重绘控件模板、只换颜色，必然像 2018 年的管理后台。本仓库从 Fluent 换到 Semi 也是同一原因。
- **结论**：能做出非模板感 UI，但前提是**逐控件重写 ControlTheme + 自建 token/动效**，不是换主题包或换色板。这是「工程量换观感」，不是「框架给不了」。

### 5.2 生态与渲染

| 库 | 最新版 | 判断 |
|---|---|---|
| Semi.Avalonia | 12.1.0.1（2026-07-31） | 企业后台审美，干净克制；本项目现用 |
| Ursa | 2.2.0（2026-07-31） | 补企业控件，Demo 偏「控件展台」 |
| FluentAvalonia | 3.1.0（2026-08-22） | WinUI 3 克隆，Windows 味最正，跨平台不中性 |
| Material.Avalonia | 3.21.0（2026-09-25） | 停在 Material 2 时代，明显过时 |
| Xaml.Behaviors | 11.3.0.6 | 无 12 版本，仓库归档；本项目自写 behavior 是正确决策 |
| ItemsRepeater | 12.0.0 | 官方 retired，不要选 |

- 【事实】**材质是最大短板**：`TransparencyLevelHint` 支持 Transparent/Blur/AcrylicBlur/Mica，但 **Windows 全支持、macOS 只支持 Transparent、Linux 取决于合成器**；没有 `backdrop-filter` 等价物；元素级 `BlurEffect` 只糊自己。[来源](https://docs.avaloniaui.net/docs/platform-specific-guides/windows)
- 【事实】动效并不缺：`SpringEasing`、`TransformOperationsTransition`、`TextBlock.LetterSpacing` 在 12.1.1 均存在；仓库规范里「12.1.1 不存在」的两处判断是误判。[来源](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Animation/Easings/SpringEasing.cs)
- 【事实】没有布局动画/FLIP/共享元素转场；列表重排要自己算。
- 【事实】字体：嵌入 Inter 保证拉丁一致；CJK 走系统 fallback，三平台中文观感不同；CJK fallback 污染 bug #22464（2026-10-14 报，open）。
- 【事实】性能：日志列表上限 **500 条**，ListBox 默认虚拟化；「大日志流畅」在 500 条上限下不构成区分度。

### 5.3 三平台一致性与打包

- 【事实】官方支持档位：Windows Tier 1 = 11 24H2（Win10 仅 Tier 2）；macOS Tier 1 = 26 Tahoe（14/15 Tier 2）；Linux Tier 1 = Ubuntu 25.x/Debian 13/Fedora 43。
- 【事实】Linux 托盘只实现 KDE AppIndicator（#13782，open）、图标会消失（#13130）；X11 分数缩放长期不准（#9390，2022 起 open）；Wayland 12.1 起 opt-in、实验性（Activate 无效 #21943、CSD 无 per-window #22039）。
- 【事实】macOS 自绘标题栏：12.0 移除 `ExtendClientAreaChromeHints.OSXThickTitleBar`，交通灯/厚标题栏无等价 API（#21119，open）。本项目用 `WindowDecorations=None` + 右侧 Windows 式三键，在 macOS 上「一致」但不「原生」。
- 【事实】打包：Windows zip 单文件、Linux tar.gz/deb、macOS 手工 `.app` 后 `ditto`；**全仓无签名/公证/自动更新**。Avalonia 官方 Parcel 是商业工具（Plus/Pro 授权）；自动更新社区事实标准是 Velopack。[来源](https://github.com/velopack/velopack)
- 【事实】服务管理页在 UI 层仅 Windows 暴露（`MainWindow.axaml.cs:137`），但后端三平台都可用；「四页」在 macOS/Linux 实际只有三页。

### 5.4 换皮工作量

| 维度 | 数值 |
|---|---|
| XAML 总量 | 1,500 行 / 20 文件 |
| 其中样式/主题 | **746 行**（AppTheme 504 + Tokens 105 + 5 主题 137） |
| 其中视图 | 754 行（MainWindow 122 + 4 页 522 + 控件 89 + App 21） |
| UI C# | **8,138 行**（其中约 6.3k 为 ViewModels/Services/Localization 等弱耦合逻辑，可保留；Views/Controls/样式相关需改） |
| 守卫测试 | **约 1,515 行**（DesignSystem 703、PagesVisualAlignment 326、MainWindowShell 174、NavFeedback 136、Toast 124、ThemeVariant 52） |
| 主题色键 | 16 键 × 5 套（没有完整语义层） |
| locale | 10 × 250 键 |

**改造面**：
- 零改动：ViewModels 1,045 行；Services 3,814 行（21 个文件中 16 个不含 `using Avalonia`）；Localization 716 行；IPC/单实例/服务编排/托盘逻辑；Behaviors 417 行。
- 逻辑复用、外观重写：3 个自研控件 235 行 C# + 89 行 XAML。
- 必须重写：`AppTheme.axaml` 504、`DesignTokens.axaml` 105、5 套主题 137。
- 必须重绘模板：ToggleSwitch、ComboBox、TextBox、ListBox/Item、ScrollBar、DataGrid、ToolTip、Toast（仍是 Semi 默认模板）。
- 必须同步重写：约 1,515 行源码守卫测试。

**估算**：27–44 人日 ≈ **6–9 周单人**（含 20% buffer）；仅换色板/字体/间距的「轻换皮」4–6 人日。对比换栈的 3–6 个月，换皮便宜一个数量级。

### 5.5 审美差距：框架限制 vs 设计投入

| 要素 | Avalonia 能力 | 结论 |
|---|---|---|
| 排版（字距/行高/层级/字体特性） | 有 | **可达** |
| 色彩/层级/渐变/多层阴影 | 有 | **可达** |
| 半透明面板 + 背景模糊 | 仅窗口级，Windows-only | **不可跨平台**（最大短板） |
| 弹簧/位移/布局过渡 | SpringEasing 有；无 FLIP | **基本可达**，列表重排手写 |
| 图标/插画 | 任意 Path/Geometry/位图 | **可达**，成本在设计 |
| 密度/键盘/命令面板 | 样式可控；命令面板自建 | **可达** |

- 【推断】可达度：MangoDisk/VS Code/Apple 设置 **90–95%**；Linear 静态观感 **80%**；Linear 动态 **60–70%**；Raycast **60%**。
- 只是设计投入不足（本仓自证）：基准选得低（MangoDisk 而非 Linear/Raycast）；规范明令禁 transform/弹性；两处 API 误判；色板只有 16 键；只重绘了 3 个控件。
- 真框架限制：跨平台毛玻璃、布局动画、macOS 原生 chrome、Linux 分数缩放/托盘、字体栅格化/CJK fallback。

### 5.6 Avalonia 小结

**适合**：以最低风险、最短工期改善观感；保留全部 C# 与 IPC；接受「不透明分层 + 细描边 + 低对比阴影」的 Linear 静态近似。
**不适合**：目标是 Apple 式玻璃/大圆角/弹性动效；要求 macOS 原生 chrome；要求跨平台完全一致的材质。
**关键前提**：第三次投入必须换设计基准（MangoDisk → Linear/Raycast 级）并重绘控件模板；否则仍是「套皮后台」。

---

## 6. 同维度矩阵

> 分数是**基于证据的判断**，不是测量值；每格的理由都能回到上文某节。总分只用于组织讨论，硬门槛优先于总分。

### 6.1 评分表

| 维度（权重） | gpui | Tauri 2 | Avalonia 换皮 |
|---|---:|---:|---:|
| 审美生态上限（40） | **35** | **37** | **28** |
| — 组件/设计资源（15） | 5（gpui-kit 75+，单点依赖） | 5（Web 生态） | 3（Semi/Ursa，企业后台级） |
| — 渲染能力（10） | 4（GPU/字体/动画强；缺元素模糊/squircle） | 4（WebView 强；平台差异） | 3（有 Spring/变换；缺跨平台材质） |
| — 观感上限证据（10） | 5（Zed/Longbridge/Sonora/Zedis） | 5（Linear/Raycast 本身是 Web） | 4（MangoDisk 级 90–95%，Linear 静态 80%） |
| — 设计体系可维护性（5） | 2（周更 breaking + fork） | 4（CSS token/组件库成熟） | 4（自建 token/ControlTheme） |
| 三平台一致成本（30） | **10** | **22** | **18** |
| — 平台支持完整性（10） | 2（Vulkan 1.3/DX11、无软件渲染、a11y/IME 硬伤） | 4（三平台官方；Linux WebKitGTK 依赖） | 3（三平台；Linux 托盘/HiDPI/Wayland 坑） |
| — 发布/签名/更新（10） | 1（无官方方案，从零建） | 4（官方 bundler + 签名/公证 + updater；但不覆盖系统服务安装器，Linux 更新仅 AppImage） | 3（有脚本但无签名/公证/更新；Parcel 商业授权） |
| — 实机与调试（10） | 2（CJK 模糊、X11 拖放、Wayland、GPU 门槛） | 3（WebKitGTK 版本/字体/滚动/NVIDIA/Wayland DPI） | 3（Linux 分数缩放、macOS chrome、CJK fallback） |
| 与 .NET Worker 共存（20） | **13** | **18** | **20** |
| — IPC 客户端（8） | 5（协议薄；需 smol→tokio 桥） | 5（tokio 原生） | 5（同栈，零桥接） |
| — 服务探测/进程管理（8） | 4（只读可写；写操作复用脚本） | 4（同左） | 5（现成 C#） |
| — 托盘/单实例/对话框（4） | 2（无内建，第三方年轻） | 5（官方插件/内建） | 5（现成） |
| 稳定性口碑（10） | **4.5** | **8** | **7** |
| — 版本节奏/breaking（4） | 1（官方包停更 + 周更 breaking） | 4（2.x 稳定；3 alpha 噪音） | 3.5（12.x 仅 6 个月；11.3 仍维护） |
| — 生产采用/issue（3） | 4（Zed/Longbridge/86 apps） | 4（大量生产应用） | 3（31.7k star；issue 多） |
| — 长期治理（3） | 2（维护者「刹车」、多 fork） | 4（官方插件同组织） | 3.5（MIT、社区活跃） |
| **加权总分** | **62.5 / 100** | **85 / 100** | **73 / 100** |
| **硬门槛判定** | ⚠️ **触发审查**：三平台完整性 2（GPU/无软件渲染/无障碍/IME） | 无 | 无 |
| 一句话 | 上限最高、门槛最硬 | 工程最完整、审美靠前端 | 最便宜最稳、上限有限 |

### 6.2 怎么读这张表

1. **Tauri 2 领先主要来自「三平台 + 共存 + 稳定」三项，不是审美碾压。** 审美分差只有 2 分；如果设计执行不到位，Tauri 也会做出「Web 后台感」。
2. **gpui 的分数被三平台与依赖治理拖累，而不是 UI 组件。** 它的组件层（gpui-kit）与观感证据是三家中最强的；如果 #53 认为「GPU 门槛 + vendor/fork + 无障碍缺口」可接受，gpui 的审美分可以上调到 38–40，但总分仍难超过 Tauri，除非把三平台权重降到很低。
3. **Avalonia 换皮的分数是「低风险」分数，不是「高上限」分数。** 它的共存满分、三平台中等、审美上限有限；适合作为止损/回退方案。
4. **硬门槛优先于总分。** gpui 的三平台完整性 2 分不是自动否决（1 分才是），但已触发父票的扩围审查条件；#53 必须显式回答「是否接受 GPU/无障碍/IME 的限制」。

### 6.3 敏感性

- 若把「三平台一致成本」权重升到 50、审美降到 20：gpui ≈ 51.7，Tauri ≈ 81.2，Avalonia ≈ 71 → 排序不变。
- 若把「审美」权重升到 60、三平台降到 10：gpui ≈ 73.3，Tauri ≈ 88.8，Avalonia ≈ 75 → 排序不变。
- **唯一能翻转排序的条件**：Tauri 的 WebView 被你判定为不可接受（例如 Linux WebKitGTK 在目标发行版不可用，或「Web 感」不可接受），或 gpui 的 GPU/依赖门槛被判定为可接受且审美权重极高。这两种都不是分数能决定的，必须由 #53 的产品判断拍板。

---

## 7. 审美锚点与截图证据

取证日期 2026-10-26。截图存 `docs/research/assets/`，来源与授权说明见 `assets/SOURCES.md`。锚点用于新栈「像不像」校准，只取设计原则，不复制品牌色/图标/版式。

| # | 产品 | 形态 | 可借鉴 | 不该照搬 | 截图 |
|---|---|---|---|---|---|
| 1 | **Linear** | 深色工作台 | 字号梯度克制、行高宽松、发丝边框、强调色只用于状态 | 品牌紫、营销渐变 | `assets/linear-diff-view.png`、`assets/linear-composer.png` |
| 2 | **Raycast** | 键盘优先启动器 | 分区标签、整行浅底选中、右侧元数据栏、基线对齐 | 高饱和红、玻璃+红光 | `assets/raycast-launcher.png` |
| 3 | **Things 3** | 单栏任务 | 大字号侧栏、留白、彩色图标只作识别锚点 | 手绘图标体系 | `assets/things-sidebar.jpg` |
| 4 | **Zed** | GPU 原生编辑器 | 标签页/分栏/选中态；证明 Rust+GPU 能达到的完成度 | 编辑器密度 | `assets/zed-agent-panel.webp` |
| 5 | **CleanShot X** | 菜单栏工具 | 浮动工具条分组/主次按钮、深色玻璃、图标网格 | 截图行业的强对比色 | `assets/cleanshot-toolbar.jpg` |
| 6 | **Transmit 5** | 双栏文件工具 | 表头/选中行/右侧表单层级、品牌色只落主操作 | 拟物图标 | `assets/transmit-two-pane.png` |

**共同原则**：排版先行；色彩克制（中性色 95%）；弱边框/弱阴影；密度靠行高与对齐；原生壳保真；动效 100–200ms；空态/加载态同 token；暗色是独立设计（3–4 层 surface，用描边替代阴影）。

**对候选的含义（先验）**：
- Avalonia：原则 1–4、7–8 可在现有 token 体系内做到；难点是 5（原生壳）与「为什么前两轮没达标」。
- Tauri：原则 1–4、7–8 由 Web 生态天然覆盖；难点是 5（WebView 内的字体/滚动/菜单）与「Web 感」。
- gpui：原则 1–4 需自建排版约束；5 需逐平台原生代码；4（密集表格/虚拟滚动）工程量最大；缺元素级模糊/squircle，做不了 Apple 玻璃系。

---

## 8. 交叉结论

1. **审美首先是设计问题，不是框架问题。** 三个候选都能画出锚点级界面；差异在上限与成本。当前「AI 味」的根因是默认模板渗透 + 基准选得低 + 色板语义层不足，而不是 Avalonia 画不出。换栈若不换设计基准与验收方式，仍会「AI 味」。
2. **三平台成本的主项不是 UI。** 发布/签名/公证/更新、服务安装与提权、托盘/单实例，四个候选都要付；Avalonia 已有一套（未签名），Tauri 有官方 bundler/插件（但 **bundler 不覆盖系统服务安装器**，Linux updater 仅 AppImage），gpui 要从零建。
3. **共存与 UI 框架弱相关，与服务管理是否下沉强相关。** 最稳的是「Rust 只做 IPC 客户端 + 只读服务探测 + 复用现有安装脚本」，不要把安装/启停下沉进 Worker（无法自我提权、扩大 IPC 攻击面）。
4. **两条既有缺陷必须先修。** UI 端点解析与 Windows 服务管道 DACL 与换栈无关；不修，任何新 UI 都会在 Linux/macOS 或服务模式上失败。
5. **gpui 的硬伤足以触发父票的扩围条款。** 但这不等于「必须扩围」；#53 要先确认产品是否必须覆盖 VM/远程/老显卡、无障碍是否硬性、审美是否 Apple 系。

---

## 9. 验证计划与风险

### 9.1 裁决前最小 spike（3–5 人日）

只做竖切：配置页静态版（侧栏 + 分组卡片 + 8 表单行 + 明暗切换）；托盘 + 关窗隐藏 + 单实例；IPC 只读三方法（Ping/GetStatus/GetRecentLogs 直连现有 Worker）；三平台各构建一次（Win11、Ubuntu 24.04 X11/Wayland、macOS 14+）。

验收观测项：冷启动 ≤1.5s；1 万行日志滚动无明显掉帧；125%/150% 缩放下中文不发虚；中文 IME 组合/候选框正常；三平台拖放照片可用；托盘四项菜单可用；VM/远程桌面能启动；记录安装包体积；服务探测能识别已装服务/后台 Worker。

### 9.2 风险登记

| # | 风险 | 影响 | 触发 | 缓解 |
|---|---|---|---|---|
| R1 | GPU 门槛（Vulkan 1.3 / DX11 排除基础适配器） | VM/远程/老显卡不可用 | 选 gpui | 明确支持矩阵；不承诺这些环境；或选有软件渲染的栈 |
| R2 | 无软件渲染兜底 | 同上，无降级 | 选 gpui | 等上游 `gpui_software`；或换栈 |
| R3 | 框架 API 周更/官方包停更 | 维护工时 | 选 gpui | vendor/fork + 锁 rev + 升级预算 |
| R4 | 托盘/单实例/自启缺口 | 壳层重写 | 选 gpui | `gpui-tray`/`tray-icon`+`muda`/`auto-launch`，逐平台验收 |
| R5 | 服务安装/提权重写 | 三平台高风险 | 选 Rust 栈且不下沉 | 只读探测 + 复用脚本；不把安装下沉 Worker |
| R6 | WebView 差异 | 三平台观感/性能不一致 | 选 Tauri | 锁 WebKitGTK 版本矩阵；Linux 实机验收 |
| R7 | 「Web 感」 | 审美目标未达成 | 选 Tauri | 锚点静态稿评审；字体/滚动/密度/拖放专项 |
| R8 | 第三次 Avalonia 视觉投入仍不达标 | 时间/信任损耗 | 选 Avalonia | 先定义可证伪的锚点验收；设止损点 |
| R9 | 三平台发布链重建 | 工期/签名成本 | 任一换栈 | spike 就打通三平台构建 |
| R10 | 迁移期双 UI 维护 | 工时翻倍 | 任一换栈 | 旧 UI 冻结只修致命 bug |
| R11 | Tauri bundler 不覆盖服务安装器；Linux updater 仅 AppImage | 「装 UI 一并装服务」做不到三平台一致；deb/rpm 更新需自建仓库 | 选 Tauri | 保留现有服务脚本为权威；UI 与服务分两条更新通道；协议版本兼容 |
| R12 | Tauri 3 迁移（GTK4/WebKitGTK 6.0/CEF/ksni） | 2–3 年内一次破坏性升级 | 选 Tauri | 锁 2.12.x；跟踪 v3 alpha；预留迁移预算 |
| R13 | Linux WebKitGTK 图形问题（NVIDIA/DMA-BUF/Wayland DPI/tray 宿主） | Linux 观感/可用性不一致 | 选 Tauri | 明确支持矩阵（Ubuntu 22.04+/Debian 12+）；提供 `WEBKIT_DISABLE_*` 降级指引 |
| R14 | Windows 托盘常驻内存/冷启动 | 常驻成本可能高于 Avalonia | 选 Tauri | 实机测内存/启动；必要时减少 WebView 常驻内容 |
| R15 | CEF 分支维护（若 Linux 必须统一渲染） | 需 pin fork + 跟上游 commit，+120MB 体积 | 选 Tauri 且 Linux 要求高一致 | 优先收窄 Linux 支持矩阵；CEF 只作备选 |
| R16 | 缺「大型 C# + Tauri」公开案例 | C# 侧 IPC/权限要自己验证 | 选 Tauri | spike 里用现有 Worker 真连；复用 Clash Verge 的协议版本/鉴权经验 |

### 9.3 需要在 #53 拍板的问题

1. 是否接受 gpui 的 GPU 支持矩阵（VM/远程/老显卡不支持）？
2. 是否接受 gpui 的依赖治理（vendor/fork + 周更 breaking）？
3. 若 gpui 硬伤不可接受，是否按父票 Out of Scope 扩围到 iced/slint/egui/Dioxus？
4. 服务管理是否下沉为独立 .NET 控制宿主（而非 Worker）？
5. 三平台「一致」的定义：像素级，还是同 token/同层级/同交互即可？
6. 无障碍是否硬性验收项？
7. 审美目标是 Linear/Vercel 系，还是 Apple 玻璃系？（直接决定 gpui 是否可行）

---

## 附录 A：主要来源

**gpui / Zed**
- crates.io `gpui`（0.2.2，2025-10-22，Apache-2.0）：https://crates.io/crates/gpui
- `gpui-pre`（0.3.9，2026-10-12）：https://crates.io/crates/gpui-pre
- gpui README（pre-1.0、breaking、文档不足）：https://github.com/zed-industries/zed/blob/main/crates/gpui/README.md
- Zed 安装要求（Vulkan 1.3 / DX11 / 排除 Basic Display Adapter）：https://zed.dev/docs/installation
- 无软件渲染讨论：https://github.com/zed-industries/zed/discussions/57066
- GPU device loss：#23288 https://github.com/zed-industries/zed/issues/23288
- Windows 屏幕阅读器：#41138 https://github.com/zed-industries/zed/issues/41138
- CJK 字形模糊：#57777 https://github.com/zed-industries/zed/issues/57777
- IME 组合选区：#64389 https://github.com/zed-industries/zed/issues/64389
- X11 拖放失败：#65679 https://github.com/zed-industries/zed/issues/65679
- 托盘 PR 被关闭：#44047 https://github.com/zed-industries/zed/pull/44047
- AccessKit 合入：#56065 https://github.com/zed-industries/zed/pull/56065
- 软件渲染 PR 未合：#63936 https://github.com/zed-industries/zed/pull/63936
- 维护者「踩刹车」引述：https://news.ycombinator.com/item?id=47003569
- gpui-ce：https://github.com/gpui-ce/gpui-ce
- gpui-kit：https://github.com/longbridge/gpui-kit ／ https://gpui-kit.com/component
- gpui-tray：https://crates.io/crates/gpui-tray
- Zed 1.25.3 产物体积：https://github.com/zed-industries/zed/releases/tag/v1.25.3

**Tauri 2**
- crates.io `tauri`（2.12.5 / 3.0.0-alpha.5）：https://crates.io/crates/tauri
- 平台前置（WebView2 / WebKitGTK 4.1 / Xcode）：https://v2.tauri.app/start/prerequisites/
- 分发目标（MSI/NSIS/DMG/deb/rpm/AppImage/Flatpak）：https://v2.tauri.app/distribute/
- Sidecar（externalBin + target triple + shell 权限）：https://v2.tauri.app/develop/sidecar/
- 官方插件目录：https://v2.tauri.app/plugin/
- 系统托盘（`tray-icon` feature）：https://v2.tauri.app/learn/system-tray/
- 原生菜单：https://v2.tauri.app/learn/window-menu/
- 单实例插件（Rust 1.90+）：https://v2.tauri.app/plugin/single-instance/
- Updater（签名）：https://v2.tauri.app/plugin/updater/
- `tauri::async_runtime`（tokio）：https://docs.rs/tauri/2.12.5/tauri/async_runtime/index.html
- Tauri 2.0 发布：https://v2.tauri.app/blog/tauri-20/
- Tauri 3 alpha（GTK4/WebKitGTK 6.0/CEF/ksni）：https://github.com/tauri-apps/tauri/releases/tag/tauri-v3.0.0-alpha.5
- Linux 图形问题（NVIDIA/WebGL/DMA-BUF）：https://v2.tauri.app/develop/debug/linux-graphics/
- 「打包 Chromium」issue（242 reactions）：https://github.com/tauri-apps/tauri/issues/14963
- Wayland DPI issue：https://github.com/tauri-apps/tauri/issues/5600
- Linux tray 左键限制：https://docs.rs/tauri/latest/tauri/tray/struct.TrayIconBuilder.html
- Tauri 接 C# 的官方立场（sidecar）：https://github.com/tauri-apps/tauri/issues/5174
- sidecar 生命周期 feature request：https://github.com/tauri-apps/plugins-workspace/issues/3062
- Clash Verge Rev（Tauri 壳 + 特权服务生产先例）：https://github.com/clash-verge-rev/clash-verge-rev
- Clash Verge service IPC（协议版本/owner token）：https://github.com/clash-verge-rev/clash-verge-service-ipc/blob/main/src/lib.rs
- macOS 分发/公证：https://v2.tauri.app/distribute/dmg/ ／ https://v2.tauri.app/distribute/sign/macos/
- Linux deb/rpm：https://v2.tauri.app/distribute/debian/ ／ https://v2.tauri.app/distribute/rpm/
- Windows 安装器：https://v2.tauri.app/distribute/windows-installer/
- Linear 桌面端复用 React：https://linear.app/changelog/2019-04-25-linear-desktop-app
- Tailwind 4 浏览器基线：https://tailwindcss.com/docs/compatibility

**Avalonia**
- NuGet：https://www.nuget.org/packages/Avalonia
- ControlTheme：https://docs.avaloniaui.net/docs/styling/control-themes
- 平台特性（材质矩阵）：https://docs.avaloniaui.net/docs/platform-specific-guides/windows
- 支持平台：https://docs.avaloniaui.net/docs/supported-platforms
- 12 发布：https://avaloniaui.net/blog/avalonia-12 ／ 12.1：https://avaloniaui.net/blog/release-12-1
- Linux 分数缩放 #9390：https://github.com/AvaloniaUI/Avalonia/issues/9390
- Linux 托盘 #13782：https://github.com/AvaloniaUI/Avalonia/issues/13782
- macOS 标题栏 #21119：https://github.com/AvaloniaUI/Avalonia/issues/21119
- CJK fallback #22464：https://github.com/AvaloniaUI/Avalonia/issues/22464
- Velopack：https://github.com/velopack/velopack

**共存 / 平台**
- tokio 命名管道：https://docs.rs/tokio/1.54.1/tokio/net/windows/named_pipe/index.html
- .NET CurrentUserOnly 实现：https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs
- Windows 命名管道安全：https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
- Windows 服务会话隔离：https://learn.microsoft.com/en-us/windows/win32/services/interactive-services
- systemd D-Bus/polkit：https://www.freedesktop.org/software/systemd/man/latest/org.freedesktop.systemd1.html
- launchctl：https://keith.github.io/xcode-man-pages/launchctl.1.html
- Apple 公证：https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
- Flatpak 沙箱：https://docs.flatpak.org/en/latest/sandbox-permissions.html
- `tray-icon`：https://docs.rs/tray-icon/0.26.1/tray_icon/
- `rfd`：https://docs.rs/rfd/0.17.2/rfd/
- `auto-launch`：https://docs.rs/auto-launch/0.6.0/auto_launch/
- Rust 1.89 `File::try_lock`：https://blog.rust-lang.org/2025/08/07/Rust-1.89.0/

---

## 附录 B：本地证据索引

- 规模/结构：`src/PhotoPrivacy.Ui/`（71 文件）、`tests/`（93 文件）
- 设计体系：`Styling/DesignTokens.axaml`、`Styling/AppTheme.axaml`、`Themes/*.axaml`、`App.axaml.cs:140-183`
- 视觉规范：`docs/design/ui-visual-standard.md`
- 桌面壳：`Services/TrayHost.cs`、`Services/UiSingleInstance.cs`、`Views/MainWindow.axaml:18-25`
- 服务管理：`Services/ServiceManager.cs`、`SystemdCommandExecutor.cs`、`LaunchdCommandExecutor.cs`、`ServiceModeController.cs`
- IPC：`src/PhotoPrivacy.Ipc/*`、`Services/WorkerIpcClient.cs`、`Services/WorkerProcessManager.cs`
- 发布：`scripts/publish-app.ps1`、`publish.sh`、`install-service.ps1`、`install-systemd-service.sh`、`install-launchd-service.sh`
- 既有缺陷：`WorkerIpcServerHostedService.cs:39-44`（服务端解析） vs `WorkerProcessManager.cs:25-82`（客户端硬编码）；`NamedPipeIpcTransport.cs:83-98` vs `install-service.ps1:44-46`（服务账户/DACL）
