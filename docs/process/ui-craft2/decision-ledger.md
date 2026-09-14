<<<<<<< New base: 票03(ui-craft2)续: CONTEXT.md 词条订正+NavFeedbackSourceTests 重写补入——pull 后上游新版与本窗改写两文件
# Decision Ledger — ui-craft2 轮（nav 按钮风格 + 圆角窗口 + 锐评处置）

> 防丢规则（最高优先）：每个被用户确认的实质性结论当场追加一条；压缩/compact/handoff 前确认本文件已落盘最新。
> 上一轮账本：`.scratch/ui-craft/decision-ledger.md`（D-001~D-009 + A-001~A-010，已结算归档，见 `.scratch/_archive/ui-craft-2026-09-13/`）。

---

## D-001 痛点基准：新构建上 nav 按钮反馈与风格仍被判丑（非长度问题）

- **日期**：2026-09-13
- **原问题**：Q1 — 按钮痛点的事实核对：你看的是哪个构建版本？（A 旧 exe / B 新构建仍不统一 / C 不确定）
- **用户原回答原文**：
  > "D:\Aworker\photo\release\win-x64\PhotoPrivacy.exe，也不是长度不统一吧，就是这个"配置"、"日志"、"规则"、"服务管理器"的按钮鼠标停留、移开的反馈效果感觉很丑陋，不好看、这几个按钮的大小、风格整体都很丑，或许应该参考别的库的效果"
- **规范化需求**：
  1. 痛点对象收敛为侧栏四枚 nav 按钮（Config/Logs/Rules/ServiceManager，即 `Button.nav` 四实例）+ 相关 nav-action；
  2. 两个维度都成立：动态反馈（pointerover 进入 + 移开 leave 的过渡效果）与静态风格（大小、整体风格）；
  3. 上一轮票 02 交付的 nav 三态（150ms 三路过渡 + SemiColorPrimary 前景 + 4px accent 槽位，ADR 0065/规范 §2）**未达用户审美预期**——本轮不得默认延续其定值为终态；
  4. 用户倾向"参考别的库的效果"——参照系问题开放，待后续问题裁决。
- **显式约束/负向需求**：
  - 用户明确否定"长度不统一"作为本轮痛点表述（上轮 SharedSizeGroup 等宽交付不在被否定范围，但 nav 四枚本身的观感仍不达预期）；
  - 与上轮账本关系：不直接推翻 D-004（三根因诊断的技术事实仍有效）与 D-005（三锚），但"按三锚执行后的成品仍丑"是新事实——若后续裁决指向参照系问题，D-005 须标 revised；
- **状态**：current

---

## D-002 nav 视觉语言 = A（圆角胶囊高亮），否决左缘指示条路线

- **日期**：2026-09-13
- **原问题**：Q2 — nav 按钮的视觉语言要哪种"好看"？（A 圆角胶囊高亮 / B 左缘指示条（现状）/ C 贴某个库现成效果 / D 用户给参照）
- **用户原回答原文**：
  > "A"
- **规范化需求**：
  1. 侧栏 nav（及同构 nav-action）hover 反馈改为**圆角胶囊底色块**：pointerover 浮出半透明中性色圆角底色；
  2. 选中（active）态同样走胶囊语言（具体胶囊配色为后续问题）；
  3. 现有 `BorderThickness 4,0,0,0` 左缘指示条槽位体系随本决策**退役**（B 路线否决）——上轮票 02 的 Transitions 三路过渡机制本身可保留复用（动画机制≠视觉语言）；
  4. 参照对象：Apple 系统设置 / Discord / Fluent NavigationView 一族的胶囊高亮语言（选项 A 呈报时明示的参照系，用户选择即接受）。
- **显式约束/负向需求**：
  - 禁止 hover 无实体反馈的"纯变色"语言（用户判其为丑的直接原因）；
  - 与上轮账本关系：D-004 的三根因诊断（无过渡/无色彩叙事/无预示联动）中，"无过渡"与"无色彩叙事"的修复仍有效并保留；**根因③的 4px accent 槽位预示方案被本决策否决**（B 语言退役）；
  - 承接 D-001：胶囊底色块的观感质感是本轮 nav 成败标准；
- **状态**：current

---

## D-003 nav 选中态胶囊 = A（主色实心胶囊，文字/图标反白）

- **日期**：2026-09-13
- **原问题**：Q3 — 选中（active）态的胶囊配色？（A 主色实心胶囊反白 / B 主色淡胶囊 / C 中性深胶囊+主色文字）
- **用户原回答原文**：
  > "A"
- **规范化需求**：
  1. active 态 = 整枚按钮填充 SemiColorPrimary（或等效主色），文字/图标反白（前景用反色/白）；
  2. 三态层次定形：idle 透明 → hover 半透明中性胶囊 → active 主色实心胶囊；
  3. 对比度验收：反白前景对主色底的对比度须达 WCAG AA（≥4.5:1），五主题色板（含 nord/dracula 等预设）逐一验证——上轮 backlog#6 已发现 nord 3.81/dracula 4.15 低于 AA 的观察项，本轮胶囊配色落地时必须一并解决或显式豁免登记。
- **显式约束/负向需求**：
  - active 与 hover 的胶囊观感须层次分明（实心 vs 半透明），禁止同为浅色导致"看不出选中"；
  - 承接 D-002：4px accent 槽位在 active 态的替代即实心胶囊本身；
- **状态**：current

---

## D-004 圆角窗口 = 方案 A（Win11 DWM 原生圆角一行 XAML，Win10 安全降级直角）

- **日期**：2026-09-13
- **原问题**：Q4 — 圆角窗口方案裁决（A 仅 Win11 DWM / B Win11+Win10 模拟 / C 全平台透明自绘），经 atomcode 深度调研后呈报（续跑锚 6c24e960，三次续跑完成，22KB 裁决报告存 `C:/Users/Administrator/AppData/Local/Temp/atomcode-q4.out`）。
- **用户原回答原文**：
  > "采纳，而且GUI相关的内容我发现一个极好的复刻参考 https://github.com/harry0703/MangoDisk"
- **规范化需求**（调研裁决全文并入，实施清单定稿后进票面）：
  1. `MainWindow.axaml` 显式加 `Win32Properties.WindowCornerPreference="Round"`（Avalonia 12.1.1 原生附加属性，底层 DwmSetWindowAttribute(33)；Win10 <Build 22000 官方 Remarks 明文 ignored = 安全 no-op，无需版本分支）；
  2. **必须显式设 Round**（默认值 Default 曾致 Avalonia #9660 无边框窗硬边回归，PR #9695 修复路径即显式 DWM 调用）；
  3. 可选加固（Qt 先例 QTBUG-147453，3-5 行）：若产品有全屏路径，全屏切 DoNotRound、退出恢复 Round；无全屏路径可省；
  4. 不动项：不透明背景、系统阴影、ADR 0050 自绘标题栏、GridSplitter（渲染路径零改动）；
  5. 验收：Win11 22H2/24H2/25H2 × {普通/最大化/贴边/125%DPI/跨屏拖拽} 目检 + Win10 冒烟直角 + macOS/Linux 无视觉变化；**禁止**"读回 preference 断言"（官方明示读回值≠实际状态）；
  6. 已知小注：若实机首帧直角（handle 建立前设置时机问题），回退 code-behind `Opened` 事件设一行，不构成方案风险。
- **显式约束/负向需求**：
  - 否决透明窗口路线（B/C）：MS 官方三分类中 per-pixel alpha 自绘阴影窗属 "cannot ever be rounded" 类；透明窗不可系统菜单最大化、resize 不可靠、透明区不可点击穿透（Electron 官方 Limitations）；C 在 Avalonia 上 macOS/Linux 支持矩阵先天不成立；
  - Win10 功能兼容保留、视觉圆角不做（EOL 2025-10-14，ESU 至 2027-10-12，份额 30-45% 区间；Win10 参照系系统应用全直角，直角不构成"丑"）；
  - 工业先例锚定：Qt 官方测试 `Win11_21H2 以下 → QSKIP`（零 Win10 回退）；WinUI3/Files App 同路线；
- **状态**：current（附带说明：调研上报的"代码 4px 槽位 vs D-002"为时序差异非账本矛盾——D-002 系 grill 中拍板，槽位退役待施工票执行）

---

## D-005 复刻基准 = A（MangoDisk 观感级全面对标，设计 tokens + 布局骨架 + 交互范式）

- **日期**：2026-09-13
- **原问题**：Q5 — "复刻参考 MangoDisk"的深度边界？（A 观感级全面对标 / B 组件级复刻 / C 仅情绪板参照）
- **用户原回答原文**：
  > "A"
- **参照物核实记录**（实物，2026-09-13）：harry0703/MangoDisk，2552★，GPL-3.0，2026-08-01 创建、2026-09-10 仍活跃；技术栈 Tauri 2 + Rust + Vue 3 + Tailwind + shadcn-vue 风格组件（reka-ui/cva/vue-sonner）；定位磁盘清理+隐私清理，与本项目同赛道同气质。
- **规范化需求**：
  1. **观感级全面对标**：设计 tokens（圆角半径/间距/色彩/阴影/字号）+ 布局骨架（侧栏+卡片内容区）+ 交互范式（toast 反馈、确认流、空态）全部以 MangoDisk 为基准重校；
  2. 上一轮 `docs/design/ui-visual-standard.md` 视觉规范活文档按此升级（承接上轮 D-009 活文档+ADR 分工架构）；
  3. 本轮施工页逐页对齐新基准；
  4. 复刻方式="看观感、自实现"（同 Files.App Middle-Click 移植先例，CONTEXT.md 有词条）——设计风格不受版权保护可复刻，**禁止拷贝 GPL-3.0 代码**；实现全部为 Avalonia XAML 自绘。
- **显式约束/负向需求**：
  - GPL-3.0：MangoDisk 任何源码（TS/Vue/CSS/Rust）不得复制、移植、翻译进本仓库——只许读它的渲染观感与交互范式；
  - 与已拍板决策的关系：D-002（胶囊 hover）/ D-003（主色实心胶囊）/ D-004（圆角窗口）与 MangoDisk 语言一致，被本决策收编为其具体实例；上轮 D-005 三锚（Wasabi×Apple×VSCode/Discord）**降级为气质参考**，对标基准以 MangoDisk 优先——上轮账本 D-005 按防丢规则在本轮账本标注 revised（原记录保留在上轮归档）；
  - 范围内禁止"半新半旧"：施工触及的页面必须整页达新基准，不留旧组件混排；
- **状态**：current（对上轮 D-005 形成 revised 关系，原记录见 `.scratch/_archive/ui-craft-2026-09-13/decision-ledger.md`）

---

## D-006 施工范围 = A（全部页面一轮全对齐），拆票粒度细化补偿

- **日期**：2026-09-13
- **原问题**：Q6 — 本轮施工范围：哪些页面入本轮"逐页对齐"？（A 全部页面 / B 高频先行 / C 只做壳）
- **用户原回答原文**：
  > "A，后面拆票细一点就行"
- **规范化需求**：
  1. 全部 UI 页面（Files 主页/Config/Logs/Rules/ServiceManager + 设置弹层/壳层）一轮内全部对齐 MangoDisk 基准，不留"半新半旧"（与 D-005 负向约束一致）；
  2. **拆票粒度细化**：to-spec/to-tickets 阶段按页面/组件级拆票（对齐上轮"一票一页面"粒度并更细），单票体量小、验收面窄、可串行推进；
  3. 工程组织含义：票数多 → 波次编排（README 波次表）须显式处理共享文件串行（上轮教训：CONTEXT.md/AppTheme 并行冲突税，WORKFLOW §4.3）；
  4. 壳层全局项（设计 tokens 刷新、圆角窗口 D-004、侧栏 nav D-002/D-003）作为先行票，页面票依赖壳票。
- **显式约束/负向需求**：
  - C 选项（只做壳）被明确排除；
  - 上轮 S2 收窄先例（上轮 D-003）不延续——本轮用户明选全范围，靠拆票细化而非范围收缩控制风险；
- **状态**：current

---

## D-007 锐评六条去向 = B（ui-craft2 收口后紧随的独立处置轮）+ 上轮 backlog 两项并入本轮

- **日期**：2026-09-13
- **原问题**：Q7 — ① 锐评六条 P0/P1 并入本轮 / 紧随独立轮 / 再排；② 上轮 backlog 其余 7 项处置（推荐 Toast+对比度并入，其余 5 项维持 backlog）
- **用户原回答原文**：
  > "B"
- **规范化需求**：
  1. **锐评六条**（75 格式黑洞挂死 / 空配置径落 CWD / excluded_patterns fail-open / IPC 无认证 / 性能三件 / 备份互覆盖）= 紧随 ui-craft2 收口后的**独立处置轮**：本轮收口完成后立即开"锐评处置轮"，以 atomcode 原文六条 + 事实核验闸为起点直接 to-spec（锐评原文 `.codex-tmp/锐评.txt`，上轮 D-008 账本基础承接）；
  2. **Toast 反馈实现**并入本轮（backlog#4 转正）：MangoDisk 基准含 vue-sonner toast 范式，属 D-005 交互范式对标范围；
  3. **active 态对比度 AA**并入本轮（backlog#6 转正）：D-003 已把五主题 AA 验证列为胶囊配色验收硬条件（nord 3.81/dracula 4.15 观察项必须解决或显式豁免）；
  4. 其余 5 项维持 backlog 独立处置：04-fix 登记修正、探活 28+8 项（用户侧）、CI 视觉回归评估、stale 词条清理、g0 分支处置。
- **显式约束/负向需求**：
  - 本轮（UI 复刻）票面禁止夹带锐评六条的功能/安全修复——"审美工程"与"正确性工程"两线隔离，复核协议不混用（UI 票=源码守卫+目检；锐评票=真测试+编译验证）；
  - 锐评六条在其独立轮处置时，第一道闸应为"六条实物事实核验"（锐评自述 §0：无编译器验证、纯静态读码）；
- **状态**：current

---

# A 系列问题簿（to-spec 第 0 步对账闸登记，2026-09-13）

## A-001 handoff 任务书页面清单与实物差异（无"主页/设置弹层"）

- **问题描述原文**：handoffs/next-round.md T3："Files 主页 / Config / Logs / Rules / ServiceManager + 设置弹层，按页面拆票"。
- **实物核查**（ctx_execute node，2026-09-13）：`src/PhotoPrivacy.Ui/Views/Pages/` 仅 4 页（ConfigPage/LogsPage/RulesPage/ServiceManagerPage）；MainWindow.axaml 仅 115 行（ADR 0061 Shell/Pages 拆分，壳即主工作区）；Views/Controls 仅 NavButton/ThemeSwatch，无 Dialog/Flyout/Popup/Overlay 引用。
- **规范化需求**：票面按实物拆分——壳层（MainWindow+侧栏 nav）+ 4 页；"Files 主页"由壳层票覆盖、"设置弹层"由 ConfigPage 覆盖，不立幽灵票。
- **显式约束**：票面禁止引用不存在的页面/弹层工件。
- **状态**：current

## A-002 nav 胶囊落点是双处（NavButton 控件 + AppTheme），handoff 只写了 AppTheme

- **问题描述原文**：handoffs/next-round.md T2："AppTheme.axaml `Button.nav`/`Button.nav-action` 重写"。
- **实物核查**：侧栏导航按钮经 `Views/Controls/NavButton.axaml` 控件承载（MainWindow 无 `Classes="nav"` 直用，nav-action 2 处直用）；AppTheme.axaml 408 行含 Button.nav/nav-action 样式段。
- **规范化需求**：nav 票落点声明双处——NavButton.axaml（控件默认态/结构）+ AppTheme.axaml（类样式/三态/过渡）；改前先核控件模板与类样式的职责边界，禁止只改一处。
- **显式约束**：涉及 NavigationButton 措辞或 Sidebar Nav Item 词条的改动会撞 ContextMdTerminologyTests 措辞钉（D-006 三档预案预置）。
- **状态**：current

## A-003 Toast 零现状（T4 为从零新建，规范 §6 仅为规划）

- **问题描述原文**：handoffs/next-round.md T4："Toast 交互链落地（上轮规范 §6 规划已备）"。
- **实物核查**：AppTheme.axaml 无 toast 样式；全仓无 Ursa NotificationCard/Toast 用例。
- **规范化需求**：T8（Toast）按"新建组件"施工——组件选型（Ursa 现成 vs 自绘）在票内先调研后动工，验收含规范 §6 从"规划"升"现役"的条款改写。
- **显式约束**：不得引入新 NuGet 依赖除非票内调研结论+用户确认（AGENTS.md 依赖纪律）。
- **状态**：current

## A-004 上轮守卫与措辞钉预撞清单（nav 退役槽位必撞 NavFeedbackSourceTests）

- **问题描述原文**：上轮收口交付 NavFeedbackSourceTests 4 Fact（断言 Transitions 三路/4px 槽位/hover 前景）+ ContextMdTerminologyTests 措辞钉。
- **实物核查**：D-002 退役 4px 槽位 → NavFeedbackSourceTests 既有断言必红；Sidebar Nav Item 词条（40px）与胶囊语言并存不冲突但改动周边措辞可能击穿。
- **规范化需求**：票 03 施工前置"撞红预演"（预期哪些 Fact 红），逐条按 D-006 三档处置（杀/改造/保留+characterization），报告列三档计数；40px 定值保留（D-002 未推翻尺寸，只换语言）。
- **显式约束**：新断言必注释防什么 bug（TEST-CONVENTIONS R1）；XAML 断言不可走 ReadStripped（xmlns 截断）。
- **状态**：current

## A-005 GitButler 引擎悬空依赖（koy）——CONTEXT.md 词条在工作区未提交

- **问题描述原文**：整理文档环节呈报："but commit 持久报 line 411 depends on commit koy……T0 票开工第一件事把工作区词条作为票面首件提交"。
- **实物核查**：工作区 CONTEXT.md 含 3 新词条+Visual Baseline 修订（uncommitted）；逐字保险副本已提交 docs/process/ui-craft2/context-entries-pending.md（zpp）。
- **规范化需求**：票 01 首件=提交工作区词条（届时 koy 引用的内容已在 base，依赖自然消解）；若引擎仍报错，按保险副本文件头预案处置并呈报。
- **显式约束**：词条内容以保险副本为准，禁止重写措辞（已过措辞钉风险评估）。
- **状态**：current
|||||||
=======
# Decision Ledger — ui-craft2 轮（nav 按钮风格 + 圆角窗口 + 锐评处置）

> 防丢规则（最高优先）：每个被用户确认的实质性结论当场追加一条；压缩/compact/handoff 前确认本文件已落盘最新。
> 上一轮账本：`.scratch/ui-craft/decision-ledger.md`（D-001~D-009 + A-001~A-010，已结算归档，见 `.scratch/_archive/ui-craft-2026-09-13/`）。

---

## D-001 痛点基准：新构建上 nav 按钮反馈与风格仍被判丑（非长度问题）

- **日期**：2026-09-13
- **原问题**：Q1 — 按钮痛点的事实核对：你看的是哪个构建版本？（A 旧 exe / B 新构建仍不统一 / C 不确定）
- **用户原回答原文**：
  > "D:\Aworker\photo\release\win-x64\PhotoPrivacy.exe，也不是长度不统一吧，就是这个"配置"、"日志"、"规则"、"服务管理器"的按钮鼠标停留、移开的反馈效果感觉很丑陋，不好看、这几个按钮的大小、风格整体都很丑，或许应该参考别的库的效果"
- **规范化需求**：
  1. 痛点对象收敛为侧栏四枚 nav 按钮（Config/Logs/Rules/ServiceManager，即 `Button.nav` 四实例）+ 相关 nav-action；
  2. 两个维度都成立：动态反馈（pointerover 进入 + 移开 leave 的过渡效果）与静态风格（大小、整体风格）；
  3. 上一轮票 02 交付的 nav 三态（150ms 三路过渡 + SemiColorPrimary 前景 + 4px accent 槽位，ADR 0065/规范 §2）**未达用户审美预期**——本轮不得默认延续其定值为终态；
  4. 用户倾向"参考别的库的效果"——参照系问题开放，待后续问题裁决。
- **显式约束/负向需求**：
  - 用户明确否定"长度不统一"作为本轮痛点表述（上轮 SharedSizeGroup 等宽交付不在被否定范围，但 nav 四枚本身的观感仍不达预期）；
  - 与上轮账本关系：不直接推翻 D-004（三根因诊断的技术事实仍有效）与 D-005（三锚），但"按三锚执行后的成品仍丑"是新事实——若后续裁决指向参照系问题，D-005 须标 revised；
- **状态**：current

---

## D-002 nav 视觉语言 = A（圆角胶囊高亮），否决左缘指示条路线

- **日期**：2026-09-13
- **原问题**：Q2 — nav 按钮的视觉语言要哪种"好看"？（A 圆角胶囊高亮 / B 左缘指示条（现状）/ C 贴某个库现成效果 / D 用户给参照）
- **用户原回答原文**：
  > "A"
- **规范化需求**：
  1. 侧栏 nav（及同构 nav-action）hover 反馈改为**圆角胶囊底色块**：pointerover 浮出半透明中性色圆角底色；
  2. 选中（active）态同样走胶囊语言（具体胶囊配色为后续问题）；
  3. 现有 `BorderThickness 4,0,0,0` 左缘指示条槽位体系随本决策**退役**（B 路线否决）——上轮票 02 的 Transitions 三路过渡机制本身可保留复用（动画机制≠视觉语言）；
  4. 参照对象：Apple 系统设置 / Discord / Fluent NavigationView 一族的胶囊高亮语言（选项 A 呈报时明示的参照系，用户选择即接受）。
- **显式约束/负向需求**：
  - 禁止 hover 无实体反馈的"纯变色"语言（用户判其为丑的直接原因）；
  - 与上轮账本关系：D-004 的三根因诊断（无过渡/无色彩叙事/无预示联动）中，"无过渡"与"无色彩叙事"的修复仍有效并保留；**根因③的 4px accent 槽位预示方案被本决策否决**（B 语言退役）；
  - 承接 D-001：胶囊底色块的观感质感是本轮 nav 成败标准；
- **状态**：current

---

## D-003 nav 选中态胶囊 = A（主色实心胶囊，文字/图标反白）

- **日期**：2026-09-13
- **原问题**：Q3 — 选中（active）态的胶囊配色？（A 主色实心胶囊反白 / B 主色淡胶囊 / C 中性深胶囊+主色文字）
- **用户原回答原文**：
  > "A"
- **规范化需求**：
  1. active 态 = 整枚按钮填充 SemiColorPrimary（或等效主色），文字/图标反白（前景用反色/白）；
  2. 三态层次定形：idle 透明 → hover 半透明中性胶囊 → active 主色实心胶囊；
  3. 对比度验收：反白前景对主色底的对比度须达 WCAG AA（≥4.5:1），五主题色板（含 nord/dracula 等预设）逐一验证——上轮 backlog#6 已发现 nord 3.81/dracula 4.15 低于 AA 的观察项，本轮胶囊配色落地时必须一并解决或显式豁免登记。
- **显式约束/负向需求**：
  - active 与 hover 的胶囊观感须层次分明（实心 vs 半透明），禁止同为浅色导致"看不出选中"；
  - 承接 D-002：4px accent 槽位在 active 态的替代即实心胶囊本身；
- **状态**：current

---

## D-004 圆角窗口 = 方案 A（Win11 DWM 原生圆角一行 XAML，Win10 安全降级直角）

- **日期**：2026-09-13
- **原问题**：Q4 — 圆角窗口方案裁决（A 仅 Win11 DWM / B Win11+Win10 模拟 / C 全平台透明自绘），经 atomcode 深度调研后呈报（续跑锚 6c24e960，三次续跑完成，22KB 裁决报告存 `C:/Users/Administrator/AppData/Local/Temp/atomcode-q4.out`）。
- **用户原回答原文**：
  > "采纳，而且GUI相关的内容我发现一个极好的复刻参考 https://github.com/harry0703/MangoDisk"
- **规范化需求**（调研裁决全文并入，实施清单定稿后进票面）：
  1. `MainWindow.axaml` 显式加 `Win32Properties.WindowCornerPreference="Round"`（Avalonia 12.1.1 原生附加属性，底层 DwmSetWindowAttribute(33)；Win10 <Build 22000 官方 Remarks 明文 ignored = 安全 no-op，无需版本分支）；
  2. **必须显式设 Round**（默认值 Default 曾致 Avalonia #9660 无边框窗硬边回归，PR #9695 修复路径即显式 DWM 调用）；
  3. 可选加固（Qt 先例 QTBUG-147453，3-5 行）：若产品有全屏路径，全屏切 DoNotRound、退出恢复 Round；无全屏路径可省；
  4. 不动项：不透明背景、系统阴影、ADR 0050 自绘标题栏、GridSplitter（渲染路径零改动）；
  5. 验收：Win11 22H2/24H2/25H2 × {普通/最大化/贴边/125%DPI/跨屏拖拽} 目检 + Win10 冒烟直角 + macOS/Linux 无视觉变化；**禁止**"读回 preference 断言"（官方明示读回值≠实际状态）；
  6. 已知小注：若实机首帧直角（handle 建立前设置时机问题），回退 code-behind `Opened` 事件设一行，不构成方案风险。
- **显式约束/负向需求**：
  - 否决透明窗口路线（B/C）：MS 官方三分类中 per-pixel alpha 自绘阴影窗属 "cannot ever be rounded" 类；透明窗不可系统菜单最大化、resize 不可靠、透明区不可点击穿透（Electron 官方 Limitations）；C 在 Avalonia 上 macOS/Linux 支持矩阵先天不成立；
  - Win10 功能兼容保留、视觉圆角不做（EOL 2025-10-14，ESU 至 2027-10-12，份额 30-45% 区间；Win10 参照系系统应用全直角，直角不构成"丑"）；
  - 工业先例锚定：Qt 官方测试 `Win11_21H2 以下 → QSKIP`（零 Win10 回退）；WinUI3/Files App 同路线；
- **状态**：current（附带说明：调研上报的"代码 4px 槽位 vs D-002"为时序差异非账本矛盾——D-002 系 grill 中拍板，槽位退役待施工票执行）

---

## D-005 复刻基准 = A（MangoDisk 观感级全面对标，设计 tokens + 布局骨架 + 交互范式）

- **日期**：2026-09-13
- **原问题**：Q5 — "复刻参考 MangoDisk"的深度边界？（A 观感级全面对标 / B 组件级复刻 / C 仅情绪板参照）
- **用户原回答原文**：
  > "A"
- **参照物核实记录**（实物，2026-09-13）：harry0703/MangoDisk，2552★，GPL-3.0，2026-08-01 创建、2026-09-10 仍活跃；技术栈 Tauri 2 + Rust + Vue 3 + Tailwind + shadcn-vue 风格组件（reka-ui/cva/vue-sonner）；定位磁盘清理+隐私清理，与本项目同赛道同气质。
- **规范化需求**：
  1. **观感级全面对标**：设计 tokens（圆角半径/间距/色彩/阴影/字号）+ 布局骨架（侧栏+卡片内容区）+ 交互范式（toast 反馈、确认流、空态）全部以 MangoDisk 为基准重校；
  2. 上一轮 `docs/design/ui-visual-standard.md` 视觉规范活文档按此升级（承接上轮 D-009 活文档+ADR 分工架构）；
  3. 本轮施工页逐页对齐新基准；
  4. 复刻方式="看观感、自实现"（同 Files.App Middle-Click 移植先例，CONTEXT.md 有词条）——设计风格不受版权保护可复刻，**禁止拷贝 GPL-3.0 代码**；实现全部为 Avalonia XAML 自绘。
- **显式约束/负向需求**：
  - GPL-3.0：MangoDisk 任何源码（TS/Vue/CSS/Rust）不得复制、移植、翻译进本仓库——只许读它的渲染观感与交互范式；
  - 与已拍板决策的关系：D-002（胶囊 hover）/ D-003（主色实心胶囊）/ D-004（圆角窗口）与 MangoDisk 语言一致，被本决策收编为其具体实例；上轮 D-005 三锚（Wasabi×Apple×VSCode/Discord）**降级为气质参考**，对标基准以 MangoDisk 优先——上轮账本 D-005 按防丢规则在本轮账本标注 revised（原记录保留在上轮归档）；
  - 范围内禁止"半新半旧"：施工触及的页面必须整页达新基准，不留旧组件混排；
- **状态**：current（对上轮 D-005 形成 revised 关系，原记录见 `.scratch/_archive/ui-craft-2026-09-13/decision-ledger.md`）

---

## D-006 施工范围 = A（全部页面一轮全对齐），拆票粒度细化补偿

- **日期**：2026-09-13
- **原问题**：Q6 — 本轮施工范围：哪些页面入本轮"逐页对齐"？（A 全部页面 / B 高频先行 / C 只做壳）
- **用户原回答原文**：
  > "A，后面拆票细一点就行"
- **规范化需求**：
  1. 全部 UI 页面（Files 主页/Config/Logs/Rules/ServiceManager + 设置弹层/壳层）一轮内全部对齐 MangoDisk 基准，不留"半新半旧"（与 D-005 负向约束一致）；
  2. **拆票粒度细化**：to-spec/to-tickets 阶段按页面/组件级拆票（对齐上轮"一票一页面"粒度并更细），单票体量小、验收面窄、可串行推进；
  3. 工程组织含义：票数多 → 波次编排（README 波次表）须显式处理共享文件串行（上轮教训：CONTEXT.md/AppTheme 并行冲突税，WORKFLOW §4.3）；
  4. 壳层全局项（设计 tokens 刷新、圆角窗口 D-004、侧栏 nav D-002/D-003）作为先行票，页面票依赖壳票。
- **显式约束/负向需求**：
  - C 选项（只做壳）被明确排除；
  - 上轮 S2 收窄先例（上轮 D-003）不延续——本轮用户明选全范围，靠拆票细化而非范围收缩控制风险；
- **状态**：current

---

## D-007 锐评六条去向 = B（ui-craft2 收口后紧随的独立处置轮）+ 上轮 backlog 两项并入本轮

- **日期**：2026-09-13
- **原问题**：Q7 — ① 锐评六条 P0/P1 并入本轮 / 紧随独立轮 / 再排；② 上轮 backlog 其余 7 项处置（推荐 Toast+对比度并入，其余 5 项维持 backlog）
- **用户原回答原文**：
  > "B"
- **规范化需求**：
  1. **锐评六条**（75 格式黑洞挂死 / 空配置径落 CWD / excluded_patterns fail-open / IPC 无认证 / 性能三件 / 备份互覆盖）= 紧随 ui-craft2 收口后的**独立处置轮**：本轮收口完成后立即开"锐评处置轮"，以 atomcode 原文六条 + 事实核验闸为起点直接 to-spec（锐评原文 `.codex-tmp/锐评.txt`，上轮 D-008 账本基础承接）；
  2. **Toast 反馈实现**并入本轮（backlog#4 转正）：MangoDisk 基准含 vue-sonner toast 范式，属 D-005 交互范式对标范围；
  3. **active 态对比度 AA**并入本轮（backlog#6 转正）：D-003 已把五主题 AA 验证列为胶囊配色验收硬条件（nord 3.81/dracula 4.15 观察项必须解决或显式豁免）；
  4. 其余 5 项维持 backlog 独立处置：04-fix 登记修正、探活 28+8 项（用户侧）、CI 视觉回归评估、stale 词条清理、g0 分支处置。
- **显式约束/负向需求**：
  - 本轮（UI 复刻）票面禁止夹带锐评六条的功能/安全修复——"审美工程"与"正确性工程"两线隔离，复核协议不混用（UI 票=源码守卫+目检；锐评票=真测试+编译验证）；
  - 锐评六条在其独立轮处置时，第一道闸应为"六条实物事实核验"（锐评自述 §0：无编译器验证、纯静态读码）；
- **状态**：current

---

## D-008 T-3 裁定 = C 折中（active 主色实心胶囊 + on-primary 深字 + 字重 600，无左缘 pill）+ hover 前景改中性

- **日期**：2026-09-14
- **原问题**：票 03 开工前置——规范 §2.4 登记的 T-3 张力（D-003 主色实心胶囊+反白 vs MangoDisk 实物 accent 胶囊+3×24 主色左缘 pill+字重 600；hover 前景 D-004 向 primary 叙事 vs 基准不变色）
- **用户原回答原文**：T-3a 选「C 折中（推荐）」；T-3b 选「按 MangoDisk 改中性」
- **规范化需求**：
  1. active = `SemiColorPrimary` 实心胶囊 + on-primary 深字（非白字——五主题 pastel 主色上白字 2.0–2.5 全灭、Text0 1.1–2.3 全灭、深字 5.9–7.8 全过 AA，atomcode 票 03 调研 §2.3 + 本窗实测表）+ `FontWeight=SemiBold`(600，取 MangoDisk 字重信号)；**无左缘 pill**（atomcode 注意点：实心 accent 底与 accent pill 不可叠加——蓝上蓝不可见）；
  2. hover = 中性半透明胶囊（新增 `SemiColorNavHover`，各主题 SemiBackground2Color @ Opacity 0.55）+ **前景不变色**（MangoDisk/VS Code/WinUI/Discord 实物 hover 前景均中性；上轮 D-004「向 primary 叙事」就此 revised）；
  3. 4px 槽位体系按 D-002 退役；BrushTransition 收窄 Background+Foreground 两路 150ms SineEaseOut；圆角 RadiusLg(8)；40px 定值保留。
- **显式约束/负向需求**：
  - 否决 B 案（全 MangoDisk 公式）的工程理由已核实并呈报：nav-action 两枚为裸 Button（MainWindow:89/94）不经 NavButton 模板，pill 须 Button 模板级手术——用户知悉后仍选 C；
  - 与上轮账本关系：D-003「反白」按 M3 on-primary 语义落地为深字（措辞保留、取色语义修正）；D-004 hover 前景叙事 revised 为中性；
  - 对既有守卫：NavFeedbackSourceTests F2（hover primary 断言）与 F3（4px 槽位断言）预期撞红，按 D-006 三档处置（见票 03 报告 §4）。
  - **落地注记（2026-09-14 票 03 首脑复核时大脑补记）**：hover 刷最终落地名 `SemiColorNavItemHover`、各主题 Opacity **0.7**（非本条规范化需求草拟时的 `SemiColorNavHover`@0.55——实现期调优：0.55 叠 navbg 底差偏弱，0.7 实测五主题底差 1.06–1.45 达「可感知中性抬升」档）；语义不变（中性半透明胶囊+前景不变色），裁定方向无改向。
- **状态**：current
# A 系列问题簿（to-spec 第 0 步对账闸登记，2026-09-13）

## A-001 handoff 任务书页面清单与实物差异（无"主页/设置弹层"）

- **问题描述原文**：handoffs/next-round.md T3："Files 主页 / Config / Logs / Rules / ServiceManager + 设置弹层，按页面拆票"。
- **实物核查**（ctx_execute node，2026-09-13）：`src/PhotoPrivacy.Ui/Views/Pages/` 仅 4 页（ConfigPage/LogsPage/RulesPage/ServiceManagerPage）；MainWindow.axaml 仅 115 行（ADR 0061 Shell/Pages 拆分，壳即主工作区）；Views/Controls 仅 NavButton/ThemeSwatch，无 Dialog/Flyout/Popup/Overlay 引用。
- **规范化需求**：票面按实物拆分——壳层（MainWindow+侧栏 nav）+ 4 页；"Files 主页"由壳层票覆盖、"设置弹层"由 ConfigPage 覆盖，不立幽灵票。
- **显式约束**：票面禁止引用不存在的页面/弹层工件。
- **状态**：current

## A-002 nav 胶囊落点是双处（NavButton 控件 + AppTheme），handoff 只写了 AppTheme

- **问题描述原文**：handoffs/next-round.md T2："AppTheme.axaml `Button.nav`/`Button.nav-action` 重写"。
- **实物核查**：侧栏导航按钮经 `Views/Controls/NavButton.axaml` 控件承载（MainWindow 无 `Classes="nav"` 直用，nav-action 2 处直用）；AppTheme.axaml 408 行含 Button.nav/nav-action 样式段。
- **规范化需求**：nav 票落点声明双处——NavButton.axaml（控件默认态/结构）+ AppTheme.axaml（类样式/三态/过渡）；改前先核控件模板与类样式的职责边界，禁止只改一处。
- **显式约束**：涉及 NavigationButton 措辞或 Sidebar Nav Item 词条的改动会撞 ContextMdTerminologyTests 措辞钉（D-006 三档预案预置）。
- **状态**：current

## A-003 Toast 零现状（T4 为从零新建，规范 §6 仅为规划）

- **问题描述原文**：handoffs/next-round.md T4："Toast 交互链落地（上轮规范 §6 规划已备）"。
- **实物核查**：AppTheme.axaml 无 toast 样式；全仓无 Ursa NotificationCard/Toast 用例。
- **规范化需求**：T8（Toast）按"新建组件"施工——组件选型（Ursa 现成 vs 自绘）在票内先调研后动工，验收含规范 §6 从"规划"升"现役"的条款改写。
- **显式约束**：不得引入新 NuGet 依赖除非票内调研结论+用户确认（AGENTS.md 依赖纪律）。
- **状态**：current

## A-004 上轮守卫与措辞钉预撞清单（nav 退役槽位必撞 NavFeedbackSourceTests）

- **问题描述原文**：上轮收口交付 NavFeedbackSourceTests 4 Fact（断言 Transitions 三路/4px 槽位/hover 前景）+ ContextMdTerminologyTests 措辞钉。
- **实物核查**：D-002 退役 4px 槽位 → NavFeedbackSourceTests 既有断言必红；Sidebar Nav Item 词条（40px）与胶囊语言并存不冲突但改动周边措辞可能击穿。
- **规范化需求**：票 03 施工前置"撞红预演"（预期哪些 Fact 红），逐条按 D-006 三档处置（杀/改造/保留+characterization），报告列三档计数；40px 定值保留（D-002 未推翻尺寸，只换语言）。
- **显式约束**：新断言必注释防什么 bug（TEST-CONVENTIONS R1）；XAML 断言不可走 ReadStripped（xmlns 截断）。
- **状态**：current

## A-005 GitButler 引擎悬空依赖（koy）——CONTEXT.md 词条在工作区未提交

- **问题描述原文**：整理文档环节呈报："but commit 持久报 line 411 depends on commit koy……T0 票开工第一件事把工作区词条作为票面首件提交"。
- **实物核查**：工作区 CONTEXT.md 含 3 新词条+Visual Baseline 修订（uncommitted）；逐字保险副本已提交 docs/process/ui-craft2/context-entries-pending.md（zpp）。
- **规范化需求**：票 01 首件=提交工作区词条（届时 koy 引用的内容已在 base，依赖自然消解）；若引擎仍报错，按保险副本文件头预案处置并呈报。
- **显式约束**：词条内容以保险副本为准，禁止重写措辞（已过措辞钉风险评估）。
- **状态**：current
>>>>>>> Current commit: docs(ui-craft2): 大脑收尾件 — 票03首脑复核ACCEPT登记（W3状态表+复核登记+frontier重算W4+01分支conflicted移
