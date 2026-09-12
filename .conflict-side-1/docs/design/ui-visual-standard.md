# UI 视觉规范（UI Visual Standard）

> **状态**：现役活文档（living document） ｜ **建立**：2026-09-12（ui-craft 轮票 01） ｜ **版本**：v1.0
> **权威**：本文件是 PhotoPrivacy **页面级**视觉规范的唯一现役标准，永远代表当前标准。
> **分工**：token / 变体级规范在 ADR 0048 / 0050 / 0051 / 0052 / 0054 / 0055 / 0056 / 0062；本文件与它们**互补不冲突**——ADR 记「为什么」，本文件记「是什么」。取舍记录见 ADR 0065。
> **引用方式**：后续 UI 票在验收条件中直接引用节号（例：「须符合 ui-visual-standard.md §2」）。
> **演化规则**：随票修订并版本化，每次修订在 §8 留一行。禁止把本文件内容整体搬进单篇 ADR（D-009）。

---

## 0. 审美三锚（D-005 —— 全文唯一的「像不像」标尺）

| 锚 | 取什么 | 具体可核验设计点（本项目落点） | 明确不取 |
|---|---|---|---|
| **Wasabi 气场**（全局） | 隐私工具的克制感 | 窄侧栏（默认 200，拖宽 170–400）、大留白、暗色基调、点缀色极少、无装饰性元素。状态用 8px 状态点 + caption 文案表达（`MainWindow.axaml:60-70`），不用徽章 / 渐变 / 插画 | 装饰性图形、渐变背景、品牌色大面积铺陈 |
| **Apple 系统设置骨架**（配置页 / 表单） | 分组卡片 + 行内左标签右控件 + 行间分隔 | `section-header` → `settings-row`（左 `row-label` + 可选 `row-desc`，右控件）→ `row-divider`；卡片 `Padding 16` + `RadiusMd` + `Elevation2` | 通栏分隔线、等距均匀网格、IDE 式双列密度 |
| **VS Code · Discord 密度与 icon 法**（侧栏 / 列表） | icon 密排与列表行密度法 | NavButton = 20px Material.Icons + `.body`(14) 文本，`Margin 12,0,0,0`；导航项高 40、组内 `Spacing=4`（连续无 gap，`MainWindow.axaml:79/83`） | 把 IDE 的高信息密度带进默认界面（D-002：默认界面属大众隐私用户） |

> **硬约束**：全文禁止空泛审美词（「高级感」「精致」「现代」「有质感」）。凡提「像不像」，必须落到上表某锚的**具体设计点**。

---

## 1. 按钮组宽度策略

### 1.1 规则

- **R1-1 同组等宽**（项目决策 A-001 / spec ID3，用户拍板）：同一容器内、语义同级的 ≥2 枚按钮必须等宽。实现二选一：(a) 以组内**最长文案**计算 `MinWidth`（写在共享 style class 或容器上，**不逐枚内联**）；(b) `Grid` 等分列（`ColumnDefinitions` 等宽 / `*`）+ 按钮 `HorizontalAlignment=Stretch`。
- **R1-2 单枚 hug**：独占一行 / 一区的按钮（如服务页「安装」「卸载」各占独立行）保持内容自适应 + 变体固定 `Padding`，**不强行拉宽到整行**。
- **R1-3 尺寸权威不变**：`Height` / `Padding` / `FontSize` 的唯一权威仍是 AppTheme 的 Button Size Ladder（内容区 32 / `12,6`；nav 与 nav-action 40 / `12,0`；icon 32×32 / `0`）。组内等宽**只调 `MinWidth` 或 Grid 列宽**，禁止行内覆盖 Height / Padding / MinWidth（ADR 0056 票 01 + 五个 source-lint guard）。
- **R1-4 变体清单**：7 变体 `primary / ghost / danger / icon / nav / nav-action / caption-btn` × 5 态（idle / pointerover / pressed / disabled / focus-visible）见 ADR 0062 D3。新增按钮必须先归队到既有变体，无对应变体才新增。

### 1.2 现状对账（2026-09-12 窗口侧静态核验）

| 位置 | 组 | 现状 | R1 判定 | 处置 |
|---|---|---|---|---|
| `ServiceManagerPage.axaml:43-45` | 启动 / 停止 / 刷新（三枚 `ghost`） | 无等宽约束，宽度 = 文案长度 | **不符 R1-1** | 票 03 |
| `RulesPage.axaml:29-30` | 保存 / 重置（两枚 `ghost`） | 同上 | **不符 R1-1** | 票 03 |
| `MainWindow.axaml:82-94` | 暂停·恢复 / 打开配置目录（两枚 `nav-action`） | `HorizontalAlignment=Stretch` 已全宽 | 符合 | — |
| `ServiceManagerPage.axaml:30` / `:58` | 安装（`primary`）/ 卸载（`danger`） | 各占独立行、右对齐 | 符合 R1-2 | — |
| `LogsPage.axaml:15` | 清空日志（`ghost`） | 单枚 | 符合 R1-2 | — |
| `MainWindow.axaml:85` / `:90` | 原裸按钮两枚 | 已归队 `nav-action`（A-001 已闭合） | 符合 | — |

### 1.3 三锚对照

- **WinUI / Windows 11（atomcode 实测）**：**对话框按钮行等宽**——`ContentDialog` 的 CommandSpace 为五列网格（Primary `*` / 8px spacer / Close `*`），按钮 `HorizontalAlignment=Stretch`，`ContentDialogButtonSpacing=8`；官方 PR #3926 确认「对话框变宽→按钮变宽」是设计意图。但**页内工具按钮行** WinUI 用 `ButtonPadding 11,5,11,6` + 最小高 32，即**内容自适应**。
- **Apple HIG（atomcode 实测）**：原文是「用**样式**（accent 填充 vs 无填充）而非尺寸区分首选动作」；「当你用**同尺寸**按钮提供两个以上选项时，你在发出这是一组连贯选择的信号」——Apple 的成组信号靠**同尺寸 / 同样式**，不靠等宽拉伸。命中区 ≥44×44pt（本项目 32 高已由 ADR 0056 票 01 书面裁定取 Win11 标准，此处不取 44）。
- **Material 3（atomcode 实测）**：对话框按钮**尾缘对齐**、最多两个操作、**按钮间距 8dp**；文案过长时**改为上下堆叠**而非拉伸等宽——M3 与 WinUI 在对话框语境存在**方向性分歧**。

### 1.4 反例（禁止）

- 同组按钮宽度 = 文案长度（当前服务页三枚的实际形态，即用户痛点的字面来源）。
- 用 `Padding` / `Margin` 微调单枚按钮去「凑」等宽（违反 R1-3）。
- 在 Views 内行内写 `MinWidth` / `Height` 覆盖（违反 R1-3，会被既有 guard 拦下）。

---

## 2. nav 反馈三态

> 三锚定位：本节的**设计语言**取 VS Code / Discord 的「过渡 + 色彩叙事」，**强度上限**受 Wasabi「点缀色极少」约束，active 的填充叙事参照 Apple 系统设置侧栏。

### 2.1 三态定义（现役标准）

| 态 | 背景 | 前景（文字 + 20px 图标） | 左 accent bar | 过渡 |
|---|---|---|---|---|
| idle | Transparent | `SemiColorText2` | 无 | — |
| hover | `SemiColorBackground1` | **向 `SemiColorPrimary` 叙事**（票 02 定值；禁止继续跳到近白 `SemiColorText0`）⚠ 张力 T-1 | **4px bar 低透明度预示（约 30%）** | Background 150ms + **Foreground 150ms**（SineEaseOut） |
| active | `SemiColorPrimaryLight` | `SemiColorPrimary` | `BorderThickness 4,0,0,0` + `SemiColorPrimary`（实态） | 同 hover |
| pressed | `SemiColorBackground2` | 继承 | 继承 | 同 hover |
| disabled | 继承 | `SemiColorText2` + `Opacity 0.5` | 无 | — |
| focus-visible | 继承 | 继承 | 全局 2px `SemiColorPrimaryLight` 描边（ADR 0051 A1） | — |

### 2.2 三条硬性约束

- **N1 纯色 / 透明度叙事**：无 `scale`、无位移、无弹性缓动（ADR 0054 定稿 + ADR 0051 A2 延续；禁 `BackEaseInOut` / `ElasticEaseInOut`）。
- **N2 过渡必须覆盖 Background 与 Foreground 两路**：现状全局 `Button` Transitions 只含 Background / BorderBrush（`AppTheme.axaml:337-347`），**Foreground 无过渡** → hover 时文字与 20px 图标 0ms 硬切。这是「塑料感」残留的第一现场，**是票 02 的定值重点**。
- **N3 active 与 hover 必须联动**：active 的 4px accent bar 在 hover 上有预示态，三态视觉连贯，禁止「三态各自割裂」。

### 2.3 现状对账（2026-09-12 静态核验 —— 修正 D-004 的时点假设）

| D-004 根因 | 现状 | 依据 |
|---|---|---|
| ① 无过渡硬切 | **已部分缓解**：票 26（ADR 0062，2026-09-04）已加全局 `Button` 的 `BrushTransition Background/BorderBrush 150ms SineEaseOut`；**残留硬切在 Foreground** | `AppTheme.axaml:337-347` |
| ② 纯中性灰平移、无色彩叙事 | **未修**：hover 背景 `SemiColorBackground1`（中性灰）、前景 Text2→Text0（近白），20px 图标随 Foreground 走，全程无 primary 参与 | `AppTheme.axaml:88-91` / `110-113` |
| ③ active 与 hover 零联动 | **未修**：`Button.nav.active` 的 4px accent bar 在 hover 上无任何预示 | `AppTheme.axaml:93-98` |
| 变体割裂（附加观察） | 主导航组走 `NavButton`（`Classes=nav`），底部两枚走 `nav-action`，两者 hover 段各自独立但内容一致；需探活核验一致性 | `NavButton.axaml:10` / `AppTheme.axaml:100-113` |

> 该对账修正了 D-004 写入时的假设（D-004 记「pointerover 段无 Transitions setter」）——变体段确实没有，但**全局段已补**，故票 02 不应重复加 Background 过渡，而应补 Foreground 过渡 + 色彩叙事 + accent 预示。

### 2.4 三锚对照

- **VS Code Activity Bar（atomcode 实测）**：hover **无背景**，只把图标从 `activityBar.inactiveForeground`(#868686) 切到 `activityBar.foreground`(#D7D7D7)——**是「灰→中性白」的亮度叙事，不是 accent 叙事**；active 指示条为 2px 左边框，现代暗色主题取 accent `#0078D4`。
- **WinUI NavigationViewItem（atomcode 实测）**：hover 背景 = `SubtleFillColorSecondaryBrush`（Fluent 2 最弱中性填充层），前景 = `TextFillColorPrimaryBrush`（中性）；accent **只给 selection indicator**（`SelectionIndicator` Rectangle 初始 `Width=0 Opacity=0`，仅 Selected 展开）。Button 的 hover 过渡官方值 = **83ms BrushTransition**。
- **Wasabi**：点缀色极少 ⇒ hover 的 primary 叙事必须**低强度**（用 primary 而非高亮白，且不叠加底色块），避免把克制气场做成「彩色按钮墙」。

### 2.5 反例（禁止）

- hover 把文字 / 图标拉到 100% 白（近白硬切）。
- hover 引入 `scale` / 图标放大 / 弹性回弹。
- active 与 hover 使用两套互不相关的视觉语言。

---

## 3. 表单行骨架（Apple 系统设置参照）

### 3.1 骨架定义

```
页 = 1..N 张 settings-card（分组卡片）
卡 = section-header + N × ( settings-row + row-divider )
行 = 左：row-label(.body) [+ row-desc(.caption)]  ｜  右：控件（Dock=Right / HorizontalAlignment=Right）
```

### 3.2 现状（ConfigPage.axaml / ServiceManagerPage.axaml 实物）

| 元素 | 实态 | 位置 |
|---|---|---|
| `Border.settings-card` | `SemiColorBackground1` + `SemiColorBorder` + `RadiusMd`(8) + `Elevation2` + `Padding 16` | `AppTheme.axaml:250-257` |
| `Border.settings-row` | `Padding 16` | `AppTheme.axaml:259-261` |
| `Border.row-divider` | `BorderThickness 0,0,0,1`（**通栏，无左侧 inset**） | `AppTheme.axaml:263-266` |
| `TextBlock.section-header` / `.row-label` / `.row-desc` | 三级文本角色 | `AppTheme.axaml:306-320` |
| 控件列宽度（路径类） | `u|PathPicker.inline-input Width=280` 共享 class 收敛（ADR 0061 票 24 / 票 25） | `AppTheme.axaml:325-331` |

### 3.3 待对齐的三处偏差（→ 票 04）

- **B1 分隔线通栏**：Apple 系统设置与 WinUI SettingsCard 的行分隔线均**自标签列起点 inset**，通栏线使行与行视觉粘连。
- **B2 控件列宽度散落**：仅路径行被共享 class 收敛（u|PathPicker.inline-input Width=280），其余控件无统一控件列宽度。实证：ConfigPage.axaml:113 的 ComboBox Width=160 属**行内写宽**，与票 24（ADR 0061）已立的「Views 禁止内联宽度」口径冲突。WinUI SettingsCard 的同位概念是模板内置 SettingsCardContentMinWidth。
- **B3 分组卡片之间零间距**：ConfigPage.axaml:8 的 StackPanel Spacing=0 使各 settings-card 直接相邻；WinUI SettingsCard 官方示例为 StackPanel Spacing=4，macOS 侧亦要求分组之间必须有间距以形成视觉簇。

### 3.4 三锚对照

- **Apple 系统设置**（骨架锚）：分组卡片 + 左标签右控件 + 行间 inset 分隔线；本项目已具雏形，差在 B1。
- **WinUI SettingsCard**（同族对照）：Header + Description 左、控件右、卡片内行分隔 → 佐证「左标签右控件」是桌面设置页的共识骨架，而非 Apple 独有。
- **VS Code / Discord**（密度锚）：只取列表 / icon 密度法，**不把 IDE 密度带进表单**（D-002）：表单行保持 16px Padding 的呼吸，不做 8px 高密度行。

### 3.5 反例（禁止）

- 通栏分隔线 + 等距 Padding 组成的「均匀网格」（AI 感第一根因，ADR 0051 A1）。
- 行内写 `Margin` / `Padding` 字面量绕过 `settings-row`（A-008 清理对象）。
- 把表单做成 IDE 式双列高密度。

---
## 4. 空态规范

### 4.1 规则

- **E1 必有占位**：每个可空列表 / 表格必须有空态占位——非空着、非只显示表头。
- **E2 骨架**：容器中央 + 一句说明文案（`.caption` 或 `.body`，弱化前景）+ 可选 Material.Icons 图标；**不加插画、不加渐变、不加装饰**（Wasabi 锚）。
- **E3 本地化**：文案必须走 locale key，10 语言 key 集合与 en.json 对齐（`All_Locales_Have_Identical_Flat_Key_Sets_As_English` 保持绿）。
- **E4 开关联动**：由 ViewModel 在**收口处**联动刷新（Append / Clear / Filter 等），不得用事件订阅长活（ADR 0061 页面 code-behind 零订阅规约）。
- **E5 区分空因**：「真空」与「过滤无结果」用不同文案；过滤无结果须提示可清除筛选。

### 4.2 现状

| 面 | 落点 | 状态 |
|---|---|---|
| 日志页 | `LogsPage.axaml:57-62`（`log.empty`，`HasNoLogs`，`MainWindowViewModel:444/509` 收口联动） | 已落地（ADR 0062 D5） |
| 规则页 | `RulesPage.axaml:63`（`rules.empty`，`RulesPanel.HasNoVisibleRules`，`ApplyFilter` 收口） | 已落地（ADR 0062 D5） |
| 服务管理器页 | 「未安装服务」态 | **缺口** → 票 04 评估补齐 |
| 配置页 | 路径行空值由 `Watermark` 承载，不属空态 | 不适用 |

### 4.3 三锚对照

- **Wasabi**：空态 = 一句克制文案，无插画、无徽章（与「点缀色极少」同源）。
- **Apple 系统设置**：空列表用弱化文案居中，不用图形占位。
- **VS Code**：空视图用说明 + 可选操作按钮；本项目取轻量版（只给提示，不强制操作按钮）。

---

## 5. 间距节奏

> 本节是**页面级消费规则**（新增层）。token 本身的定义与数值不变，见 `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml`。

### 5.1 token 表（实物）

| 族 | 档位 |
|---|---|
| Space | Xxs 2 / Xs 4 / Sm 8 / Md 12 / Lg 16 / Xl 24 / Xxl 32 / Xxxl 48 |
| Radius | Xs 2 / Sm 4 / Md 8 / Lg 12 / Xl 16 |
| Duration | Fast 75 / Normal 150 / Slow 250 |
| Elevation | 0 / 1 / 2 / 4 |

### 5.2 页面级消费规则

| 编号 | 场景 | 档位 | 落点 |
|---|---|---|---|
| P1 | 同级分组卡片之间 | `SpaceLg`(16) | **现状偏差**：ConfigPage.axaml:8 为 Spacing=0（卡片直接相邻），见 §3.3 B3 → 票 04 |
| P2 | 卡片内边距 / section-header 与首行 | `16` / `SpaceMd`(12) | `settings-card` 与 `settings-row` 的 `Padding 16` |
| P3 | 行内标签列与控件列之间 | ≥ `SpaceLg`(16) | 控件右对齐贴卡内边距 |
| P4 | 同组内元素（如一组按钮） | `SpaceSm`(8) | 服务页按钮组 |
| P5 | 侧栏导航项之间 | `SpaceXs`(4)，连续无 gap | `MainWindow.axaml:79/83` |
| P6 | 标题栏按钮组 | `SpaceXxs`(2) | `MainWindow.axaml:27` |
| P7 | **禁止等距均匀** | — | 不同层级必须用不同档位；等距均匀是 AI 感第一根因（ADR 0051 A1） |

### 5.3 三锚对照

- **Apple**：设置页的分组间距（约 20–24）明显大于行内间距（约 8–12），**层级靠间距差表达**。
- **VS Code / Discord**：列表行密排（4–8），区块之间 16–24。
- **Wasabi**：大留白是气场来源——卡片外间距**不得小于 16**。

### 5.4 待清理

- **A-008**：6 个 axaml 文件共 14 处 `Margin` / `Spacing` 字面量残留，随票清至 `SpaceXxx`（撞见才改，不做全仓扫荡票）。

---

## 6. Toast 反馈链规划（A-009 —— 规划面，非现役标准）

> 本节是**规划**。实现票 deferred（D-004：用户未选为主痛点；ADR 0062 D6 已把接线点移交至 ConfigEditor 保存完成回调，待票 27 合入后裁定）。

| 编号 | 维度 | 规划内容 |
|---|---|---|
| T1 | 触发点 | 配置防抖自动保存成功 / 失败（升级现有 `SaveStatus` 文本反馈）、规则保存、服务安装 / 卸载完成、清理异常 |
| T2 | 位置与时长 | 内容区右下（避开底部 utility 组），默认 3–4s，可手动关闭 |
| T3 | 视觉 | `settings-card` 同族容器（`Background1` + `Border` + `RadiusMd` + `Elevation2`）；**禁用 emoji / Unicode 符号**（ADR 0050 A2 已清零），状态用 Material.Icons |
| T4 | 语义色 | 成功 `primary` / 警告 `warning` / 失败 `danger`，各 10 语言文案 |
| T5 | 无障碍 | 可被屏幕阅读器播报（LiveRegion 语义）且不抢焦点（WCAG） |
| T6 | 去重与排队 | 同 key 连续触发合并刷新（覆盖上一条）；不同 key 最多同时 3 条堆叠 |
| T7 | 反例 | 每个操作都弹（噪声）；用 Unicode 符号；自动消失但无手动关闭 |

---

## 7. 验收标尺（D-005 三锚 + 截图基线）

- **V1 每票验收 = 同机位 before/after 人工对照 + 三锚过目**。拍摄规程见 `docs/process/reports/32-ui-manual-smoke.md §5.2`，基线资产落 `docs/design/screenshots/`。
- **V2 机位**：窗口 920×600 非最大化、同坐标、同 DPI、侧栏 200、预设 catppuccin + 明暗档位写明、语言 zh-CN。
- **V3 三锚过目清单**（逐条打勾，禁止「看起来不错」一类结论）：
  - **Wasabi 气场**：侧栏无装饰元素；点缀色仅用于 active / 状态点；卡片留白 ≥16；无渐变 / 插画 / 徽章堆砌。
  - **Apple 骨架**：配置页为分组卡片 + 左标签右控件；行分隔线 inset；行高与节奏一致。
  - **VS Code / Discord 密度**：侧栏 icon 20 + body 14 + 行高 40 + 组内 4px 连续；列表密排无空旷。
- **V4 探活**：report-32 §2 的 28 项 + 票 01 新增 G 组（nav hover 六项，对应 D-004 三根因）由用户执行，结果回填 §3 / §3.1。
- **V5 视觉一致性不交给纯文本断言单独背锅**：source-lint 只能锁结构不变量（如「不得行内覆盖按钮尺寸」），色彩 / 节奏 / 观感走人工对照。新断言须满足 `tests/TEST-CONVENTIONS.md` R1；撞红按 R2 三档分类（杀 / 改造 / 保留）并留痕。
- **V6 门禁**：CI 云端绿（`.github/workflows/ci.yml`）；本机零构建（CI-only）；无新增无注释断言。

---
## 附录 A. atomcode 控件级调研（2026-09-12，票 01）

> 执行方式：`atomcode 5.0.9` 单发串行 headless 调研（遵守串行护栏：同一时刻仅一个在途）；Exa / Tavily / AnySearch 三引擎共 16 次检索，28 次原文抓取核验（含 Patchright 渲染 Apple HIG 页面），覆盖 ≥10 个域名。
> **完整 28 条来源清单见 `.scratch/ui-craft/reports/01-visual-standard-doc.md` §5**；本附录只列与规范条文直接绑定的关键来源。

### A.1 核心结论

| # | 结论 | 关键证据 | 规范落点 |
|---|---|---|---|
| C1 | **四大参考产品的 nav hover 一律是中性灰叠加，accent 只属于 active/selected**；hover 无「预示指示条」，指示条是 active 专属 | VS Code Activity Bar hover 无背景、图标 #868686→#D7D7D7；WinUI hover=`SubtleFillColorSecondaryBrush`、指示条 `SelectionIndicator` 初始 `Width=0 Opacity=0`；Discord hover=`rgba(78,80,88,0.3)`、品牌色只用于未读/提及 | §2.4（**张力 T-1**） |
| C2 | **hover 过渡在桌面体系里以「切换」为主**：VS Code 无过渡（activity bar 唯一 transition 是拖拽指示条，`duration 0ms`），WinUI NavigationViewItem 用 `DiscreteObjectKeyFrame KeyTime=0` 离散切换；唯一可复用数值是 WinUI Button 的 **83ms BrushTransition** | `activityaction.css`、`NavigationView_rs1_themeresources.xaml`、`Button_themeresources.xaml` | §2.2 N2（本项目 150ms 为既有 ADR 定值，不因本条改） |
| C3 | **按钮组：对话框语境等宽，页内工具按钮行内容自适应**；Apple 的成组信号靠「同尺寸/同样式」而非等宽拉伸；M3 文案过长时改堆叠 | WinUI `ContentDialog` 五列 stretch 网格 + `ButtonSpacing=8` + PR #3926；VS Code `width: fit-content`；Apple HIG Buttons 原文；M3 Dialogs Specs | §1.1 R1-1 / R1-5（**张力 T-2**） |
| C4 | **表单行骨架有完整开源模板可对齐**：WinUI SettingsCard 为四列网格 `Auto(图标) / *(标题+描述) / Auto(控件) / Auto(ActionIcon)`，断点 800px 控件换行到标题下方、600px 连图标折叠，卡片间 `Spacing=4`，背景过渡同为 83ms，模板内置 `SettingsCardContentMinWidth` | `SettingsCard.xaml`（CommunityToolkit/Windows）+ 官方文档 | §3.1 / §3.3 B2 |
| C5 | **macOS 侧：分隔线需 inset（两侧各 ≥20pt）、描述文字 11pt（Small）、标签列与控件列之间 8pt**；System Settings 分组卡片**无官方数值规格**（仅有社区逆向值与 HIG 侧栏 small/medium/large 三档定性规格） | zenn《macOS Settings Window Guidelines》（社区）；Apple HIG Sidebars（官方，行高三档 + 侧栏图标默认 accent） | §3.3 B1 / §5 |

### A.2 已登记张力（须大脑裁定，本票不静默改向）

| 编号 | 张力 | 用户决策 | 工业证据 | 本票处置 |
|---|---|---|---|---|
| **T-1** | nav hover 是否引入 primary 色彩叙事 | D-004（用户拍板）：向 primary 叙事，禁纯中性灰平移 | VS Code / WinUI / Discord / macOS 的 nav hover **均不用 accent** | 沿用 D-004；票 02 双版截图人工裁定 |
| **T-2** | 页内按钮组等宽与否 | A-001 / spec ID3（用户拍板）：同组等宽 | 页内工具按钮行四大平台**均为内容自适应**；仅对话框语境等宽 | 沿用等宽；加 R1-5 例外；票 03 双版截图人工裁定 |

### A.3 信息缺口（诚实声明）

- macOS System Settings 分组卡片**无官方数值规格**，8pt / 11pt / 20pt 属社区逆向值（zenn），且其针对旧式 toolbar 偏好窗口。
- Discord 与 macOS 的 hover 过渡时长**无官方数值**（Discord 闭源 Electron，仅有社区 CSS 反拆）。
- WinUI NavigationViewItem 指示条展开动画时长**未数值化发布**（仅 Button 的 83ms 可同类参考）。
- VS Code 经典 Dark+ 与 Dark Modern 的 `activityBar.activeBorder` 不同（accent #0078D4 vs 白色），引用时须注明主题版本。

### A.4 关键来源（完整 28 条见报告 §5）

| 来源 | 锚点 | 支撑 |
|---|---|---|
| `activityaction.css`（microsoft/vscode） | 2px 指示条、hover 仅前景色、唯一 transition `0ms/100ms` | C1 / C2 |
| `workbenchThemeService.ts`（microsoft/vscode） | `activityBar.foreground #D7D7D7` / `inactiveForeground #868686` / `activeBorder #0078D4` | C1 |
| `NavigationView_rs1_themeresources.xaml`（microsoft-ui-xaml） | hover/selected=`SubtleFillColorSecondary`、SelectionIndicator Rectangle | C1 / C2 |
| `Button_themeresources.xaml`（microsoft-ui-xaml） | `BrushTransition Duration=0:0:0.083`、`ButtonPadding 11,5,11,6` | C2 / C3 |
| `ContentDialog_themeresources.xaml` + PR #3926 | 五列 stretch 网格、`ButtonSpacing=8`、等宽为设计意图 | C3 |
| `dialog.css`（microsoft/vscode） | 按钮 `width: fit-content`、右对齐、4px 间距 | C3 |
| Apple HIG Buttons / Sidebars / Color | 44×44 命中区、同尺寸=成组信号、侧栏图标默认 accent、行高三档 | C1 / C3 / C5 |
| M3 Dialogs Guidelines / Specs / Buttons Specs | 尾缘对齐、最多两操作、8dp 按钮间距、24dp 内边距、48×48 目标区 | C3 |
| `SettingsCard.xaml`（CommunityToolkit/Windows）+ 官方文档 | 四列网格、800/600 断点、83ms、`SettingsCardContentMinWidth`、`Spacing=4` | C4 |
| zenn《macOS Settings Window Guidelines》 | 8pt 列距、11pt 描述、分隔线左右 ≥20pt inset | C5 |
| Discord 设计令牌（oh-my-design-cli / BetterDiscord / gist 三信源交叉） | hover `rgba(78,80,88,0.3)`、selected 0.6、白色 pill | C1 |

---

## 8. 修订记录

| 版本 | 日期 | 票 | 修订摘要 |
|---|---|---|---|
| v1.0 | 2026-09-12 | 票 01 / ui-craft | 立文档：七节齐备（按钮组宽度策略 / nav 反馈三态 / 表单行骨架 / 空态规范 / 间距节奏 / Toast 反馈链规划 / 验收标尺）+ §0 三锚表 + 附录 A（atomcode 控件级调研）；登记张力 T-1（nav hover 色彩叙事）与 T-2（页内按钮组等宽）待大脑裁定 |
