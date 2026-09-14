# 票 03 收口报告 — nav-capsule-tri-state（胶囊三态重写 + 槽位退役 + AA 对比度）

> 覆盖：D-002, D-003；A-002, A-004；新裁定 D-008。分支 `ui-craft2/03-nav-capsule-tri-state`（锚定 ui-craft2/02-rounded-window-shell-tokens 之上）。
> 日期：2026-09-14。本机 CI-only：零构建零测试；验证全部为静态门禁 + 断言级预演（§6）。

---

## 0. 结论

4px 左缘 accent 槽位体系整体退役，nav/nav-action 双段落地胶囊三态：idle 透明 / hover `SemiColorNavItemHover` 中性半透明胶囊（RadiusLg 圆角）/ active `SemiColorPrimary` 实心胶囊 + `SemiColorNavActiveForeground` on-primary 深字 + SemiBold。五主题 + 亮/暗回退 active 前景对比度 5.00–7.79 全过 WCAG AA，nord/dracula 上轮观察项（3.81/4.15）就此解决。NavFeedbackSourceTests 撞红三档处置完毕（杀 0 / 改造 2 / 保留 2 / 新增 1），规范 §2 已转落地态、T-3 登记改已裁定。报告双轨逐字一致落盘。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| # | issue 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | `4,0,0,0` 槽位在 nav/nav-action 双段 absent（守卫） | ✅ | AppTheme.axaml nav 区段零 `BorderThickness`/`BorderBrush`；守卫 = `Nav_Must_Not_Have_Retired_4px_Accent_Slot`（§4 F3 改造） |
| 2 | 三态值与 ui-visual-standard.md §2.1 一一对应 | ✅ | §5 对照表行号级对应 |
| 3 | 五主题对比度表 + nord/dracula 处置 | ✅ | §6 表：五主题 5.90–7.79 全过 AA；观察项不再豁免而是实解 |
| 4 | 撞红三档表 + 新断言 R1 注释 | ✅ | §4：改造 2 / 保留 2 / 杀 0 / 新增 1；每条新断言带「防什么回潮 + 票号」 |
| 5 | 报告双轨逐字一致落盘 | ✅ | §8 SHA256 比对 |

D-001 承接注：本票承接「侧栏 4 按钮观感丑陋」主诉的视觉语言层；尺寸/分组/路由不动。

## 2. 改动清单（先列后改；T-3 已裁定 = C 折中 + hover 中性，§9）

| 文件 | 动作 | 说明 | 共享面 |
|---|---|---|---|
| `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | 修订 | nav/nav-action 双段重写：① `BorderThickness 4,0,0,0` + `BorderBrush` 槽位体系整体退役（base/pointerover/active 三处）；② `CornerRadius` RadiusSm→RadiusLg；③ hover 底 → `SemiColorNavItemHover`、前景 setter 删除（不变色）；④ active → `SemiColorPrimary` 实心 + `SemiColorNavActiveForeground` + `FontWeight=SemiBold`；⑤ 新增 `Button.nav.active:pointerover` 保持实心不褪回 hover 底；⑥ Transitions 收窄 Background+Foreground 两路（BorderBrush 路随槽位退役，对齐规范 §2.1 过渡列） | **共享文件**——仅 nav 段，他段不动 |
| `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml` | 修订 | ThemeDictionaries 亮/暗回退档新增两键：`SemiColorNavItemHover`（亮 #1B1C21@0.08 / 暗 #FFFFFF@0.12）、`SemiColorNavActiveForeground`（亮 #FFFFFF / 暗 #1B1C21） | **共享文件**——只增不改 |
| `src/PhotoPrivacy.Ui/Themes/{Catppuccin,Dracula,NordDark,OneDarkPro,TokyoNight}.axaml` ×5 | 修订 | 各增两刷：`SemiColorNavItemHover` = SemiBackground2Color @ Opacity 0.7；`SemiColorNavActiveForeground` = SemiBackground0Color | **共享文件**——只增不改，位置紧随各文件 SemiColorNavBackground |
| `src/PhotoPrivacy.Ui/Views/Controls/NavButton.axaml` | 修订 | 头部注释 4px 槽位描述订正为胶囊语言 + 职责边界声明；控件结构零改动 | 本票专属 |
| `tests/.../Ui/NavFeedbackSourceTests.cs` | 修订 | 5 Fact（§4 明细），断言剥 XAML 注释走 Regex 不走 ReadStripped（既定纪律沿用） | 本票专属 |
| `docs/design/ui-visual-standard.md` | 修订 | §2.1 hover/active 行定值落地、§2.3 全项转「已落地」、§2.4 目标列与 T-3 注记改已裁定、§8 + v2.2 行 | **共享文件**——活文档随票修订（既定机制） |
| `CONTEXT.md` | 修订 | Sidebar Nav Item 词条订正 + 沿革记；Nav Capsule Language 词条「反白」nuance 订正；顺手治愈措辞钉（§9-b） | **共享文件**——最小订正 |
| `.scratch/ui-craft2/decision-ledger.md` | 修订 | 追加 D-008（T-3 裁定记录，含用户原回答） | .scratch 流程文件 |
| 本报告 ×2 | 新建 | .scratch 主本 + docs/process/reports 副本 | 本票专属 |

### 2-B T-3 裁定（用户拍板，2026-09-14）

- **T-3a = C 折中**：active = 主色实心胶囊 + on-primary 深字 + SemiBold，无左缘 pill。B 案（全 MangoDisk 公式）被否决的工程理由已核实并呈报：nav-action 两枚为裸 `Button.nav-action`（MainWindow:89/94），不经 NavButton 控件模板，左缘 pill 须 Button 模板级手术或 nav-action 控件化——改动面显著且 atomcode 注意点明示「实心 accent 底与 accent pill 不可叠加（蓝上蓝不可见）」。
- **T-3b = 按 MangoDisk 改中性**：hover 前景不变色（D-004 primary 叙事 revised）。

### 2-C 职责边界核验（A-002 要求）

- `NavButton.axaml`（控件）= 结构：Button.nav 实例 + Grid(Auto,*) + MaterialIcon 20 + TextBlock.body；`IsActive` → code-behind 映射 `.active` class（NavButton.axaml.cs:38-43）。
- `AppTheme.axaml`（类样式）= 三态视觉全部落点：idle/pointerover/active/disabled 画笔与过渡。本票视觉改动全在此。
- `MainWindow.axaml` 零改动；nav-action 裸 Button 形态保持。

## 3. 调研（动工前置要求，通用纪律第 1 条）

### 3.1 atomcode 深度调研（串行一次，2026-09-14）

- 执行：`atomcode -p`（Avalonia 生态 nav item 胶囊高亮做法 / pastel accent 实心底前景取色 AA 惯例 / 3×24 左缘 pill 实现手段）。三引擎 11 检索（Exa 6 + Tavily 4 + AnySearch 1）、五角度覆盖、8 篇原文核验（FluentAvalonia NavigationViewItem*Styles 三份 / Avalonia Fluent ListBoxItem.xaml / Ursa.Themes.Semi NavMenu.axaml / Semi ListBoxItem.axaml / MS Learn NavigationViewItem / Uno 主题资源页）。
- **核心结论一（生态分两派）**：Fluent/FluentAvalonia 派 = 左缘 SelectionIndicator pill（W3×H16(rs1)/24、Radius 2、Opacity 淡入）+ 微弱选中底；Semi/Ursa 派 = 无独立指示器、selected 整块圆角底（Radius 4 / MinHeight 32）。MangoDisk「accent 胶囊 + 3×24 pill + 字重 600」= 两派融合，Avalonia 生态无现成件需自建。
- **核心结论二（AA 取色）**：pastel accent 实心底白字是硬伤（#89B4FA 上 2.11:1、#88C0D0 上 2.00:1，连非文本 3:1 都不过）；工业惯例 = M3 on-primary 角色 → 深字 #1E1E2E on #89B4FA = 7.79:1 AAA。
- **核心结论三（pill 手段）**：模板内 `Border HAlign=Left VAlign=Center W3 H24 Radius1.5 Opacity 0→1` + DoubleTransition；禁复刻 FA 跨项滑动 Composition 动画（issue #602/#643/#679 三连 bug）。本票裁定不取 pill（§2-B），手段记录留档。
- **注意点**：①实心 accent 底与 accent pill 二选一；②hover/active 圆角必须同半径（本票同 RadiusLg）；③item 级 150ms Cubic/Sine 为生态主流（既有定值沿用）；④Semi 选中/悬停刷系 primary 低透明度（5–15%），与 MangoDisk 中性路线不同族。
- **账本回顾**：动工前全读 decision-ledger D-001~D-007 + A-001~A-005、spec §0/§2、handoff 通用纪律、WORKFLOW §4.2/4.3/4.4、ui-visual-standard.md v2.0、CONTEXT.md 五词条、ADR 0065。工业对标 = MangoDisk 附录 D.3 + 本轮五家生态源码。
- **信息缺口如实登记**：Semi.Avalonia 默认亮/暗档 primary 值本窗已从上游 Tokens/Palette 源码补齐核验（Light=SemiBlue5 #0064FA、Dark=SemiBlue5 #54A9FF）；Files App 源码未直读；FA SelectionIndicator 数值由 WinUI rs1 + Uno 文档双源交叉。

### 3.2 本窗 WCAG 实测（WCAG 2.x 相对亮度公式）

见 §6 终态表（含回退档）。

## 4. 撞红预演与三档处置（A-004 / D-006）

预演清单 → 实际处置（全部落 `NavFeedbackSourceTests.cs`，断言剥 XAML 注释后作用于选择器块）：

| 原 Fact | 预判 | 处置 | 落点 |
|---|---|---|---|
| F1 `Nav_Transitions_Must_Cover_Foreground_Not_Only_Background` | 不红（两路保留） | **保留**（注释补票号） | 同名 Fact |
| F2 `Nav_Hover_Foreground_Must_Narrate_Primary_Not_Neutral_White` | **红**（hover 前景改中性，primary 断言目标消失） | **改造** → `Nav_Hover_Must_Paint_Neutral_Capsule_And_Not_Retint_Foreground`：pointerover 必须含 `SemiColorNavItemHover`、不得含任何 Foreground setter、不得含 `SemiColorPrimary`（D-008 锁定中性 + D-002 实体底色块） | 新 F2 |
| F3 `Nav_Hover_Must_Preview_Active_Accent_Bar` | **红**（4px 槽位体系整体退役，断言目标不存在） | **改造** → `Nav_Must_Not_Have_Retired_4px_Accent_Slot`：nav/nav-action base + pointerover + active 段不得含 `BorderThickness`/`BorderBrush`，且 base 必须 `CornerRadius`=RadiusLg（胶囊正向结构）——验收项 1 的守卫承载体 | 新 F3 |
| F4 `Nav_Feedback_Must_Not_Use_Scale_Or_Elastic_Easing` | 不红 | **保留**（注释补票号） | 同名 Fact |
| — | — | **新增** `Nav_Active_Must_Be_Solid_Primary_Capsule_With_OnPrimary_Foreground`：active 必须 `SemiColorPrimary` + `SemiColorNavActiveForeground` + SemiBold，不得退回 PrimaryLight/Text0（AA 回潮防线） | 新 F5 |

三档计数：**杀 0 / 改造 2 / 保留 2 / 新增 1**；新断言全部带 R1 注释（防什么回潮 + 票号）。关联守卫核查：`DesignSystemTests` nav 变体/高度断言（选择器名与 Height=40 不变 → 不红）；`MainWindowShellSourceTests` NavButton 组件化断言（结构不动 → 不红）；`PagesVisualAlignmentSourceTests` 对 NavButton.axaml 的字面量扫描（注释级改动 → 不红）。

**失效即红预演证据**：将 5 条新断言对 git HEAD 版 AppTheme 复演——F2/F3/F5 全红（断言目标在旧版不存在/反向），F1/F4 按设计保持绿（保留型守卫非撞红型）。对新版复演全绿（§6-C）。

## 5. 三态定值对照表（规范节号一一对应，issue 验收项 2）

| 规范 §2.1 行 | 规范定值 | 本票落地（AppTheme.axaml） |
|---|---|---|
| idle | Transparent / SemiColorText2 | base `Background=Transparent` + `Foreground={DynamicResource SemiColorText2}`（L83-84） |
| hover 底 | 半透明中性胶囊（sidebar-accent~52%，RadiusLg） | `Button.nav:pointerover` / `nav-action:pointerover` → `SemiColorNavItemHover`（Bg2@0.7 各主题 + 亮/暗回退）（L100-102/134-136） |
| hover 前景 | 不变色（D-008） | pointerover 段无 Foreground setter（守卫 F2 锁定） |
| pressed | 同族加深（~72%） | 既有 `:pressed` → `SemiColorBackground2` 实底（L230/239 原值沿用，Bg2 实底 vs hover Bg2@0.7 = 同族加深） |
| active | 主色实心胶囊 + on-primary 深字 + SemiBold（D-003/D-008） | `Button.nav.active` → `SemiColorPrimary` + `SemiColorNavActiveForeground` + `FontWeight=SemiBold`；`active:pointerover` 保持实心（L107-116） |
| 圆角 | RadiusLg(8) | base 段 `CornerRadius={DynamicResource RadiusLg}`（L85/122） |
| 过渡 | BrushTransition Background+Foreground 两路 ~150ms | 两路 150ms SineEaseOut（L90-93/127-130），BorderBrush 路随槽位退役 |
| 尺寸 | Height=40 / Padding=12,0 / Stretch | 原值保留（L86-88/123-125） |
| disabled | Text2 + Opacity 0.5 | 既有段不动（L234/243） |
| 硬性约束 | N1 无 scale/位移/弹性；N2 过渡白名单；N3 层次分明 | 代码内零 `scale`/`TransformOperationsTransition`/弹性缓动（F4 守卫）；idle透明→hover半透→active实心三层递进 |

## 6. 五主题对比度表（issue 验收项 3；WCAG 2.x 相对亮度实算）

### 6-A active 终态（主色实心底 + on-primary 深字，AA 门槛 ≥4.5）

| 主题 | 主色（底） | on-primary 前景（Bg0） | 对比度 | 判定 |
|---|---|---|---|---|
| Catppuccin | #89B4FA | #1E1E2E | 7.79 | ✅ AAA |
| Dracula | #BD93F9 | #282A36 | 5.90 | ✅ AA |
| NordDark | #88C0D0 | #2E3440 | 6.24 | ✅ AA |
| OneDarkPro | #61AFEF | #282C34 | 5.92 | ✅ AA |
| TokyoNight | #7AA2F7 | #1A1B26 | 6.79 | ✅ AA |
| 回退·暗（Semi 默认） | #54A9FF | #1B1C21 | 6.88 | ✅ AA |
| 回退·亮（Semi 默认） | #0064FA | #FFFFFF | 5.00 | ✅ AA |

**nord/dracula 观察项处置结论**：上轮观察项（nord 3.81 / dracula 4.15 不达 AA）源于「白字/亮字 on pastel 主色」路线；本票 D-008 改取 on-primary 深字（各主题 Bg0），nord 6.24 / dracula 5.90 实解，无需豁免登记。对照旧路线实测：白字 2.00–2.52 全灭、Text0 1.11–2.26 全灭——深字是五主题唯一过 AA 的取色，与 atomcode §2.3 调研结论一致。

### 6-B hover 终态（中性半透明胶囊，信息项——胶囊存在感非 AA 文本对）

| 主题 | hover 底（Bg2@0.7 叠 navbg） | vs navbg 底差 | pressed（Bg2 实底）vs hover |
|---|---|---|---|
| Catppuccin | #2A2A3B | 1.25 | 1.12 |
| Dracula | #3A3C4C | 1.45 | 1.19 |
| NordDark | #3A4251 | 1.44 | 1.17 |
| OneDarkPro | #292D37 | 1.12 | 1.06 |
| TokyoNight | #202332 | 1.15 | 1.07 |
| 回退·暗 | #36373C（#FFF@0.12 叠 #1B1C21） | 1.43 | — |
| 回退·亮 | #E3E2E0（#1B1C21@0.08 叠 #F4F3F1） | 1.17 | — |

底差 1.06–1.45 为「可感知的中性抬升」档（MangoDisk hover 52% 档位同族等效）；pressed 对 hover 的再加深步进与基准 52%→72% 同向。

### 6-C 静态门禁结果（本机零构建零测试）

| 门禁 | 方法 | 结果 |
|---|---|---|
| XAML 标签平衡 | Node 栈式解析（剥注释后），8 份触碰文件 | 8/8 PASS |
| BOM / CRLF | 逐文件字节检查 | 全部无 BOM、纯 LF |
| 守卫预演（新版） | JS 复演 5 Fact 断言于新 AppTheme（剥注释 + 选择器块切取，与测试同法） | 5/5 预期绿 |
| 守卫预演（旧版） | 同法作用于 git HEAD 版 AppTheme | F2/F3/F5 红 ✓（断言确实咬住改动面），F1/F4 绿 ✓（保留型守卫） |
| C# 词法 | 花括号配平 13/13、Fact 计数 5、断言串字面量逐条复核 | PASS |
| 撞红波及面 | 全 tests/ 扫描 `4,0,0,0`/`BorderBrush`/`SemiColorPrimaryLight` 断言残留 | 仅本文件（已处置），零漏网 |
| 措辞钉 | `4 按钮（Config/Logs/Rules/ServiceManager）` 在 CONTEXT.md | 治愈（原为既有红，见 §9-b） |
| 构建/测试执行 | — | **未运行**（CI-only 纪律；云端验证由大脑推分支后发生） |

## 7. 双轨一致性核验

主本 `.scratch/ui-craft2/reports/03-report.md` ↔ 副本 `docs/process/reports/03-nav-capsule-tri-state.md`：副本由主本字节级复制产生，写盘后 SHA256 比对一致、无 BOM、纯 LF（核验命令与输出见 §6-C 末行口径）。

## 8. 不动项核验

- Height=40 / Padding=12,0 / HorizontalContentAlignment=Stretch：原值保留（DesignSystemTests nav 高度断言不红）。
- 无 scale / 位移 / 弹性缓动 / TransformOperationsTransition（F4 守卫锁定）。
- 无新 NuGet 依赖；Core/Worker 零改动；MainWindow.axaml 零改动；路由与分组零改动。
- `SemiColorPrimaryLight` 等既有 token 仍被他处使用，未删键。
- nav-action 裸 Button 形态保持；`IsActive` → `.active` class 映射机制不动。

## 9. 张力与呈报

- **T-3（已裁定）**：D-008 = C 折中（§2-B）。规范 §2.4 注记已改「已裁定」并登记理由。D-003「反白」措辞保留、取色语义修正为 on-primary 深字；D-004 hover primary 叙事 revised 为中性不变色。
- **观察项 a（顺手治愈，呈报知悉）**：`ContextMdTerminologyTests.SidebarNav_Should_Have_Four_Buttons` 断言 `4 按钮（Config/Logs/Rules/ServiceManager）`，该短语在 git HEAD 版 CONTEXT.md 中为 0 命中——**系既有红，非本票撞红**。本票订正 Sidebar Nav Item 词条时以自然句式补回该事实句，措辞钉由红转绿。
- **观察项 b（如实登记，不属本票验收）**：idle/hover 前景 `SemiColorText2`（Opacity 0.62 族）对 navbg 的文本对比约 1.7–2.5:1，低于 AA 文本门槛——此为上轮既定设计 token 选择（nav 弱化叙事），非本票引入、不在本票处置范围；若未来收进无障碍专项，候选 = idle 提亮至 Text1 族或去 Opacity。本次仅登记不静默改。
- **观察项 c**：`Button.nav.active:pointerover` 段为防选择器序依赖的显式锁（否则 active 项 hover 时会褪回中性底）；MangoDisk 同态（selected+hover 不变）。

## 10. 完成定义自证

- [x] 4px 槽位双段 absent + 守卫（验收 1）
- [x] 三态定值与规范 §2.1 一一对应（验收 2，§5 表）
- [x] 五主题 + 回退对比度表，nord/dracula 实解（验收 3，§6-A）
- [x] 撞红三档表 + 新断言 R1 注释（验收 4，§4）
- [x] 报告双轨逐字一致（验收 5，§8/文末核验行）
- [x] 必读清单全读；调研先落 §3；改动清单先于代码改动落盘（本文件骨架先行）
- [x] 通用纪律：本机零构建零测试、静态门禁全记、信息缺口如实登记、不动项核验
- [x] WORKFLOW §4.2：单分支两提交（uur 主体 + lwm 冲突解析续件）；§4.3 共享文件触碰面已声明；§4.4 pull 触发快照——photo-snapshots/20260914-094037（100 文件 / 463821 B，workflow-verify ZERO-LOSS 100/100）
- [x] T-3 裁定入台账 D-008 + 规范 §2.4 + 本报告三处一致

- **观察项 d（pull 事件留痕）**：提交时 h0 车道 `mzl`（ui-craft/02 nav-hover，已 merged upstream）阻塞依赖——按 §4.4 先快照再 `but pull`：上游 7 提交进入基准（含 f1e4f13 NavFeedbackSourceTests 转义修正与 3cfe884 等三笔 ui-craft 轮 merge），h0 车道消解、`ui-craft2-grill-artifacts` 随合并移除；本票两文件与上游冲突（CONTEXT.md 词条内容 / 测试文件旧断言），解析取本票版本并采上游 verbatim `@` 转义修正（本窗原写 `\s` 非原文串属 C# 非法转义——上游 f1e4f13 修的正是此缺陷，如实采信）；`ui-craft2/01` 车道 pull 后呈 3 提交 {conflicted}（b77a970/cbcc852/kzx）——**属票 01 领地，本票不代解析**，移交该票窗口/大脑处置。

> **停点**：报告落盘后停住等复核；不自动续票 04。
