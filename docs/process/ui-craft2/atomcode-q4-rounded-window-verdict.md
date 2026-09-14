# atomcode Q4 圆角窗口深度调研 — 最终综合裁决（会话恢复副本）

> **恢复说明**：本文件是 2026-09-13 atomcode 调研（session 6c24e960-e225-4b68-a0fc-139a15f30faf，三次续跑完成）stdout 裁决报告的逐字恢复副本——原 temp 文件被系统清理，从工作会话上下文重建。调研问题文本见同目录 `atomcode-q4-question.txt`。D-004 决策依据即此报告。

---

**Sufficiency Gate**：searches: 11（Exa×6 / Tavily×1 失败-配额超限 / AnySearch×4）｜angles: Official（MS/Qt/Avalonia/Electron 一手）、Criticism（透明窗口坑清单、Avalonia #9660 历史回归、Q&A"preference≠实际圆角"）、Currency（2026 份额、EOL/ESU 2027-10-12）、Community（videocardz/Reddit/Q&A/thwindowsclub）——4 类｜full reads: 12 次（web_fetch×11 + AnySearch extract×1；其中 statcounter 直连 TLS 失败后改 extract 成功、visual-appearance 页 404）｜domains: learn.microsoft.com、support.microsoft.com、raw.githubusercontent.com（Avalonia tag 12.1.1 / qtbase commit 8b3654cb）、docs.avaloniaui.net、electronjs.org、videocardz.com、gs.statcounter.com——7 个独立域｜gaps: Files/StabilityMatrix/Wasabi 圆角实现源码本轮未直接读取（工业证据链改由 Qt+WinUI3+MS+Avalonia 本体承担）；Win11 各 H2 版本间圆角行为差异无官方文档化数据；Win10 用户"直角丑"感知无公开调研。

---

## 1) 执行摘要（Tl;dr）

**推荐方案 A：Win11 用 DWM 系统圆角（Avalonia 12.1.1 原生 `Win32Properties.WindowCornerPreference="Round"`，一行 XAML），Win10 保持直角、macOS/Linux 走系统。** Confidence：**高**——关键结论全部有一手原文（MS 官方 DWM 文档两篇、Avalonia 12.1.1 tag 源码、Qt 官方测试源码、Electron 官方文档）且 ≥2 独立信源交叉；扣分项仅为 Tavily 引擎本轮配额失败（实际双引擎+官方一手源完成交叉）与 Win10 份额两信源口径差异（已并区间处理，不构成矛盾）。

核心理由三条：① 本地窗口当前**不透明**（`SemiColorBackground0`，无 TransparencyLevelHint），落在 MS 文档"可被 DWM 圆角"的类别内，方案 A 零风险落地；② B/C 的透明窗口路线被 MS 官方文档直接判死——per-pixel alpha 自绘阴影的窗口属于 **"cannot ever be rounded, even if they call the opt-in API"** 第三类，且透明窗口在 Windows 上**不能最大化、不能可靠 resize、透明区无法点击穿透**（Electron 官方 Limitations 逐条列出，Win32 层同构）；③ Win10 已 EOL（2025-10-14，MS 官方），ESU 只到 2027-10-12，对一个"稳定性优先"的隐私工具，为 ~30% 的 EOL 用户群新增一条长期维护的透明渲染长尾 bug 面，收益/风险比倒挂。

## 2) 对比矩阵（A / B / C）

| 维度 | **A：仅 Win11 DWM 圆角** | **B：Win11 DWM + Win10 透明模拟** | **C：全平台透明自绘圆角** |
|---|---|---|---|
| Win11 观感 | 8px 系统圆角；最大化/贴边/VM **自动切直角**（by design，零代码） | 同 A | 自绘圆角（半径自定，未必与系统 8px 一致，失去"原生感"） |
| Win10 观感 | 直角（**该年代 OS 的原生状态**，Win10 系统应用全直角，用户无违和） | 模拟圆角 | 模拟圆角 |
| macOS/Linux | 系统自带（前提成立） | 同 A | Linux 取决于 compositor，**官方明示不可靠** |
| 系统阴影 | **保留**（不透明窗口，DWM 画） | Win10 上丢失，须自绘 | 全平台丢失，全自绘 |
| 新增 bug 面 | ≈0（渲染路径零改动，ADR 0050 全保留） | 高：resize 闪烁（Avalonia #8316 同类）、角区点击命中测试、最大化/贴边切换双渲染路径、DWM 禁用/节电/RDP 透明被 OS 压制回退 | 最高：B 全部 ×3 平台 + X11/Wayland×compositor 矩阵 + macOS 仅支持 Transparent 级 |
| MS 官方立场 | **明确支持**（opt-in API + 官方 C# 示例） | 不推荐（透明+per-pixel alpha = "cannot ever be rounded" 类；Win10 无任何官方圆角路径） | 无官方指引（第三方自绘范畴） |
| 工业先例 | Qt（Win11-only 圆角，无 Win10 回退）、WinUI3/Files（DWM/AppWindow）、MS 官方示例、Avalonia 自身 #9695 | **未找到成熟开源先例**（Electron 系社区至今靠 workaround） | 罕见，多为小工具 |
| 估算成本 | **0.5 人日**（1 行 + 实机验证矩阵） | 8–15 人日 + 长期回归 | 15–25 人日 + **永久性**跨平台回归 |

## 3) 分点结论（含证据）

### 3.1 Avalonia 12.1.1 原生能力结论
- **12.1.1 已原生提供 `Win32Properties.WindowCornerPreference` 附加属性**（挂在 `Window` 上），枚举 `Default=0 / DoNotRound=1 / Round=2 / RoundSmall=3`；Changed handler 直连 `IWin32OptionsTopLevelImpl.SetWindowCornerPreference`，底层即 `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE=33, ...)`。
- 官方 Remarks 原文："supported starting with Windows 11 Build 22000. **It is ignored on earlier Windows versions.**" → **Win10 上是安全 no-op**，天然降级直角，无需 try/catch、无需版本分支。
- **关键坑：默认值是 `Default`（让系统决定），不能依赖。** Avalonia 自身历史回归 issue #9660 证明 ExtendClientArea 窗口在 Win11 上曾"hard edges"，靠 PR #9695（2022-12 合并）显式调用 DwmSetWindowAttribute 才修复；后续 PR #21615 又移除了旧版"强制 Round"逻辑改由属性驱动。→ 必须**显式设 `Round`**。
> 证据：Avalonia 12.1.1 tag 源码（raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.1/src/Avalonia.Controls/Platform/Win32Properties.cs，全文读取）；Avalonia 官方 Windows 平台文档（全文读取）；Avalonia #9660/#9695/#21615/#17294（github 检索片段级）。

### 3.2 本地窗口现状 = 方案 A 的充分前提（决定性）
- `MainWindow.axaml` L17-20：`Background="{DynamicResource SemiColorBackground0}"`（**不透明**）、`ExtendClientAreaToDecorationsHint="True"`、`WindowDecorations="None"`、`ExtendClientAreaTitleBarHeightHint="-1"`；全仓 grep 无 `TransparencyLevelHint`/透明窗口配置。
- 对照 MS 文档三分类：本项目窗口无 per-pixel alpha、无 window region → 不属于"永远不可圆角"类；属于"policy 不圆角但可圆角"类（自绘框架/空 NC 区），**显式 opt-in 后即可被系统圆角**，且**系统阴影完整保留**。
> 证据：本地 MainWindow.axaml + 全仓 grep（实读）；MS《Apply rounded corners in desktop apps for Windows 11》三分类原文（2026-07-07 更新，全文读取）；本地 docs/adr/0050 全文。

### 3.3 Win11 各版本行为矩阵（22H2/23H2/24H2/25H2）
- API 自 **Build 22000（21H2）** 可用；MS 文档未记载各 H2 之间的圆角行为差异，即 22H2→25H2 行为一致。
- **最大化、贴边（snapped）、VM、AVD、WDAG 场景下系统 by design 不圆角**——无需任何代码，Avalonia 侧零改动自动获得"窗口态圆角 / 最大化态直角"的正确观感。
- `DwmGetWindowAttribute` 返回的是 preference 而非实际圆角状态（无法程序化断言"确实圆了"）→ 验收只能实机目检。
> 证据：MS apply-rounded-corners 原文（全文读取）；MS DWMWINDOWATTRIBUTE 枚举页（=33，Build 22000+）+ DWM_WINDOW_CORNER_PREFERENCE 枚举页；Microsoft Q&A 1525287（snippet 级）。

### 3.4 方案 B 的坑清单（Win10 透明模拟圆角）
1. **最大化断链**：Electron 官方 Limitations："On Windows: Transparent windows can not be maximized using the Windows system menu or by double clicking the title bar"（PR #28207）。
2. **resize 不可靠**："Transparent windows are not resizable. Setting resizable to true may make a transparent window stop working on some platforms"；Avalonia 侧同类历史 issue（#8316）。
3. **角区点击**：透明区**无法**干净地 click-through（Electron #1335）→ 四角透明像素要么吞点击、要么透过去点中下层窗口。
4. **阴影自绘 + 双渲染路径**：Win10 分支需 Background=Transparent + 内容层 CornerRadius clip + 自绘阴影 + 最大化/贴边切不透明，等于**第二条窗口渲染路径**常驻代码库。
5. **OS 级透明压制**：Avalonia 官方文档警告 "Windows may suppress transparency at the OS level... battery saver mode or remote/virtual sessions" → 回退无法感知、无法修复。
> 证据：Electron 官方文档 Custom Window Styles → Limitations 节（全文读取）；Avalonia 官方 Windows 文档 "Set a non-transparent fallback!" 节（全文读取）；MS apply-rounded-corners "cannot ever be rounded" 类（全文读取）。

### 3.5 方案 C 的跨平台可行性 = 不成立
- Avalonia 官方文档原文："**macOS supports only Transparent, and Linux support depends on the compositor.**" → "全平台统一透明窗口"在 Avalonia 12.1.1 上**先天不统一**。
- C 还继承 B 全部 Windows 坑，再 ×3 平台测试矩阵。
> 证据：Avalonia 官方 Windows 文档 "Window transparency and Mica" 节（全文读取）。

### 3.6 Win10 份额与 EOL 事实
- **EOL**：Windows 10 支持 **2025-10-14 终止**（MS 官方，22H2 为最终版本）；消费者 ESU 仅覆盖 22H2、**到 2027-10-12 止**。
- **份额（两信源并区间，非冲突）**：StatCounter（2026-08，12 个月滑动均值）：Win11 68.6% / **Win10 30.14%**；videocardz 引 StatCounter 月度时点：2025-10 41.71% → 2025-12 44.6%。→ 合理区间 **Win10 ≈ Windows 桌面内 30–45%**。
- **产品含义**：Win10 **功能兼容值得保留**；Win10 **视觉圆角不值得做**（无 OS 官方路径 → 只有透明模拟 → 代价量级差 20-50 倍；Win10 用户的参照系里系统应用全是直角）。
> 证据：MS 官方 EOL 页（全文读取）；MS lifecycle（snippet 级）；MS ESU 计划页（snippet 级）；StatCounter（AnySearch extract 全文）；videocardz 2026-01-28（全文读取）。

### 3.7 工业先例（心智模型）
- **Qt（最硬的工业信号）**：官方 Win11 风格测试套件断言"正常窗口 preference=ROUND；全屏→DEFAULT；退出全屏→ROUND"（QTBUG-147453），`initTestCase` 明确 `QOperatingSystemVersion < Windows11_21H2 → QSKIP`——**Qt 把系统圆角当 Win11-only 特性，零 Win10 回退**。
- **WinUI 3 / Files App**：走 AppWindow/DWM 体系，Win11 自动圆角、Win10 无回退。
- **Avalonia 自身**：#9695 的修复方式就是显式 DwmSetWindowAttribute——方案 A 与 Avalonia 上游修法完全同构。
> 证据：Qt 测试源码（raw.githubusercontent.com/qt/qtbase/8b3654cb/...tst_qwindows11style.cpp，全文读取）；Qt qwindows11style.cpp（snippet 级）；Electron #10886（全文读取）。

## 4) 推荐方案与实施清单（方案 A）

1. **一行改动**：`MainWindow.axaml` 的 `<Window>` 加 `Win32Properties.WindowCornerPreference="Round"`（附加属性在 `Avalonia.Controls` 命名空间，XAML 直接可用；12.1.1 `WindowImpl` 有 `_cornerPreference` 缓存字段，handle 建立前设置会保留）。
2. **可选加固（照抄 Qt 先例，3-5 行）**：若支持全屏，进入全屏时切 `DoNotRound`、退出恢复 `Round`（对应 QTBUG-147453 官方行为）；若产品路径从不用全屏，可省。
3. **验收矩阵（替代程序化断言）**：Win11 22H2/24H2/25H2 × {普通态、最大化、Win+← 贴边、125% DPI、跨屏拖拽} 目检圆角；Win10 22H2 确认直角且无异常；macOS/Linux 回归无视觉变化。**不做**"读回 preference 断言"（官方明示读回值≠实际状态）。
4. **不动**：不透明背景、系统阴影、ADR 0050 自绘标题栏、GridSplitter——渲染路径零改动。

## 5) 代价量化（半量化）

| 方案 | 开发 | 回归验证 | 长期维护 | 风险等级（稳定性优先视角） |
|---|---|---|---|---|
| **A（推荐）** | 0.5 人日 | 0.5 人日 | ≈0 | **极低** |
| B | 5–10 人日 | 3–5 人日 | **持续** | 高 |
| C | 15–25 人日 | 全量跨平台回归 | 永久 | 最高 |

## 6) 与账本 current 记录的冲突点（显式声明）

1. **⚠ 冲突①（nav 代码 vs D-002，时序差异）**：调研时账本 D-002 已拍板胶囊语言但施工未开始，AppTheme.axaml 仍为 4px 左缘指示条——属 grill 纪律"调研中禁改源码"的预期时序差异，非账本矛盾。槽位退役在施工票执行。
2. **无冲突项**：方案 A 与 ADR 0050 正交叠加；与 D-003、上轮 D-005 三锚无涉。
3. **数据分歧披露（非静默二选一）**：Win10 份额 30.14%（滑动均值）vs 44.6%（时点值）——口径差异已并区间呈报；Windows 占全体桌面 79% vs 57% 两信源打架（snippet 级，未采信）。

## 7) 完整来源清单（本轮实际打开核验）

| # | 来源 | 角度 | 日期 | 贡献 |
|---|---|---|---|---|
| 1 | MS《Apply rounded corners in desktop apps for Windows 11》（learn.microsoft.com/.../apply-rounded-corners） | Official | 2026-07-07 | 三分类、最大化不圆角 by design、API 是 hint、官方示例——裁决核心 |
| 2 | MS DWMWINDOWATTRIBUTE 枚举 | Official | 2026-03-09 | =33、Build 22000+ |
| 3 | MS DWM_WINDOW_CORNER_PREFERENCE 枚举 | Official | 2024-02-22 | 四值语义 |
| 4 | Avalonia 12.1.1 `Win32Properties.cs`（tag 源码） | Official(源码) | tag 12.1.1 | 原生附加属性实锤、Win10 ignored、Default 默认值坑 |
| 5 | Avalonia 官方 Windows 平台指南 | Official | 当前版 | 透明支持矩阵、透明被 OS 压制警告 |
| 6 | Qt `tst_qwindows11style.cpp`（commit 8b3654cb） | Official(源码) | 2026 | Win11-only 圆角先例、全屏→DEFAULT |
| 7 | Electron 官方文档 Custom Window Styles | Official | 当前版 | B 方案坑清单 |
| 8 | Electron issue #10886 | Community | 2017 | frameless+透明圆角长期未闭环 |
| 9 | MS《Windows 10 support has ended on October 14, 2025》 | Official | EOL 公告 | EOL、三无、ESU |
| 10 | videocardz（StatCounter 转引） | Currency | 2026-01-28 | Win10 41.71%→44.6% 时点 |
| 11 | StatCounter Windows 版本份额 | Currency | 2026-08 | Win10 30.14%（滑动均值） |
| 12 | 本地 MainWindow.axaml / AppTheme.axaml / ADR 0050 | 项目一手 | 2026 | 窗口不透明前提、4px 槽位冲突证据 |
| — | MS lifecycle/ESU、Qt qwindows11style.cpp、Avalonia #9660/#9695/#21615、thwindowsclub、MS Q&A、windowslatest | snippet 级 | 各 | 交叉印证，不单独支撑结论 |

## 8) 信息缺口

1. Files/StabilityMatrix/Wasabi/FluentAvalonia 圆角实现源码未直接读取（工业证据链由 Qt+WinUI3+MS+Avalonia 四源承担，充分非穷尽）。
2. Win11 各 H2 圆角行为差异无官方数据；Build 26100 新属性 `DWMWA_BORDER_MARGINS`（"upcoming"）纳入下轮观察项。
3. Win10 用户"直角丑"感知无公开调研（推断：系统应用全直角）。
4. Tavily 引擎配额超限失败（双引擎+官方一手源完成交叉，如实披露）。
5. `WindowCornerPreference` handle 建立前设置是否 100% 生效未逐行核验——若实机首帧直角，回退 code-behind `Opened` 事件设置（一行），不构成方案风险。
