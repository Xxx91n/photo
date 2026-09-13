# Spec — ui-craft2 轮（MangoDisk 观感级复刻：胶囊 nav + 圆角窗口 + 全页对齐 + Toast）

> 唯一决策数据源：`.scratch/ui-craft2/decision-ledger.md`（D-001~D-007 全 current；A-001~A-005 对账闸登记）。
> 本 spec 每节声明覆盖 D-xxx/A-xxx；覆盖声明见 §8。

---

## §0 基准与范围总纲 【覆盖 D-005, D-006; A-001】

- 复刻基准：harry0703/MangoDisk 观感级全面对标——设计 tokens（圆角/间距/色彩/阴影/字号）+ 布局骨架（侧栏+卡片内容区）+ 交互范式（toast/确认流/空态）。
- 复刻方式="看观感、自实现"（Files.App Middle-Click 先例）：**GPL-3.0 源码禁止拷贝/移植/翻译**，实现全部 Avalonia XAML 自绘。
- 范围=全部实物面：壳层（MainWindow 115 行：自绘标题栏+侧栏 nav+内容宿主，ADR 0061）+ 4 页（Config/Logs/Rules/ServiceManager）+ Toast 新建。**无"主页"独立文件、无"设置弹层"**（A-001 实物核查）——handoff 任务书清单与实物的差异按本节收敛，不立幽灵票。
- 上轮 D-005 三锚（Wasabi/Apple/VSCode·Discord）降级为气质参考；上轮视觉规范活文档 `docs/design/ui-visual-standard.md` v1.2 升 v2.0 后为唯一现役标准。

## §1 痛点与验收基准 【覆盖 D-001】

- 痛点：用户在现役构建（release/win-x64/PhotoPrivacy.exe）上判侧栏四枚 nav 按钮"鼠标停留、移开的反馈很丑陋，大小、风格整体都很丑"。
- 验收基准=用户目检认可；过程验收=每票 before/after 同机位截图对照 + 守卫测试失效即红。上轮票 02 成品（4px 槽位语言）不达预期，其定值不得默认延续。

## §2 nav 胶囊三态 【覆盖 D-002, D-003; A-002, A-004】

- 视觉语言：idle 透明 → hover 半透明中性圆角胶囊底色块 → active 主色实心胶囊+反白前景；三态层次分明（实心 vs 半透明禁同浅）。
- 4px 左缘 accent 槽位体系（BorderThickness 4,0,0,0+Transparent 占位）退役；BrushTransition 三路（Background/BorderBrush/Foreground ~150ms SineEaseOut）保留复用。
- 落点双处（A-002）：`Views/Controls/NavButton.axaml`（控件结构/默认态）+ `AppTheme.axaml`（Button.nav/nav-action 类样式三态）；改前核职责边界。
- 对比度硬验收：反白前景对主色底 ≥4.5:1，五主题色板逐一验证；nord（3.81）/dracula（4.15）上轮观察项必须解决或显式豁免登记（D-003）。
- 撞红预案（A-004）：NavFeedbackSourceTests 既有断言预演红清单+D-006 三档处置（杀/改造/保留+characterization），报告列三档计数；40px 定值保留。

## §3 圆角窗口 【覆盖 D-004】

- `MainWindow.axaml` 显式设 `Win32Properties.WindowCornerPreference="Round"`（Avalonia 12.1.1 原生附加属性；Win10 <Build 22000 官方 ignored=安全 no-op；**必须显式设**，禁依赖 Default——Avalonia #9660 回归先例）。
- 可选加固（Qt QTBUG-147453 先例）：若有全屏路径，全屏切 DoNotRound/退出恢复 Round；产品无全屏路径则省。
- 不动项：不透明背景、系统阴影、ADR 0050 自绘标题栏、GridSplitter。
- 验收：Win11 22H2/24H2/25H2 × {普通/最大化/贴边/125%DPI/跨屏} 目检 + Win10 冒烟直角 + macOS/Linux 无视觉变化；**禁止**"读回 preference 断言"。透明窗口自绘圆角路线（B/C）永久排除。
- 实施细节出处：docs/process/ui-craft2/atomcode-q4-rounded-window-verdict.md §4。

## §4 全页面逐页对齐 【覆盖 D-006, D-005; A-001】

- 4 页逐页对齐 v2.0 基准：ConfigPage / LogsPage / RulesPage / ServiceManagerPage（一票一页，"拆票细一点"）。
- 每页整页达新基准，禁"半新半旧"混排；页面触及的共享文件（ui-visual-standard.md/AppTheme/CONTEXT.md/测试）按 WORKFLOW §4.3 一次一票——**全部票面串行**（波次表见 README，从 Blocked by 推导）。
- 上轮 S2 收窄先例不延续（用户明选全范围，靠拆票细化控风险）。

## §5 Toast 反馈 【覆盖 D-007; A-003】

- Toast 交互链从零新建（实物零现状）：组件选型票内先调研（Ursa 现成反馈组件 vs 自绘），验收含规范 §6 从"规划"升"现役"。
- 形态对标 MangoDisk toast 范式（vue-sonner 观感：右下角堆叠/自动消失/hover 暂停等，以观感为准自实现）。
- 禁新 NuGet 依赖除非票内调研结论+用户确认。

## §6 纪律与流程 【覆盖 A-005】

- 全程串行波次；WORKFLOW §4.2（禁裸 git 写）/§4.3（共享文件一次一票）/§4.4（动栈前仓库外快照）。
- **票 01 首件=提交工作区 CONTEXT.md 词条**（A-005：引擎 koy 悬空依赖，词条内容以保险副本 docs/process/ui-craft2/context-entries-pending.md 为准，禁重写措辞）。
- CI-only：本机禁构建/测试（例外=用户明令硬验收）；静态门禁+报告双轨照旧。
- 上轮教训（ADR 0066）：CI 首跑不可被静态复演替代；报告零丢失结论须词条级复核。

## §7 票面清单

| 票 | slug | 覆盖 | Blocked by |
|---|---|---|---|
| 01 | visual-standard-v2 | D-005; A-001, A-005 | — |
| 02 | rounded-window-shell-tokens | D-004, D-005 | 01 |
| 03 | nav-capsule-tri-state | D-002, D-003; A-002, A-004 | 02 |
| 04 | page-config | D-005, D-006 | 03 |
| 05 | page-logs | D-005, D-006 | 04 |
| 06 | page-rules | D-005, D-006 | 05 |
| 07 | page-service-manager | D-005, D-006 | 06 |
| 08 | toast | D-007; A-003 | 07 |

锐评六条处置轮（D-007-①）=独立轮，不在本 spec 票面（handoffs/next-round.md 有任务书）；backlog 5 项维持。

## §8 覆盖声明（三段核验）

- 段1：spec 声明的 D 并集 {D-001,002,003,004,005,006,007} = 账本 current D 全集（7/7）✅
- 段2：票面 D 并集（§7 列）= spec 声明全集 ✅（D-001 在 §1 为验收基准；票面以验收段承接注统一声明——8 票逐份含 D-001 承接注，比对工具口径=覆盖行+承接注）
- 段3：A-001~A-005 全部有去向（A-001→§0/§4+票01/04；A-002→§2+票03；A-003→§5+票08；A-004→§2+票03；A-005→§6+票01）✅
- 无去向记录清单：**空**（7D+5A 全部有去向）。
