# UI 视觉规范（UI Visual Standard）

> **状态**：现役活文档（living document） ｜ **建立**：2026-09-12（ui-craft 轮票 01） ｜ **版本**：v2.0
> **权威**：本文件是 PhotoPrivacy **页面级**视觉规范的唯一现役标准，永远代表当前标准。
> **分工**：token / 变体级规范在 ADR 0048 / 0050 / 0051 / 0052 / 0054 / 0055 / 0056 / 0062；本文件与它们**互补不冲突**——ADR 记「为什么」，本文件记「是什么」。取舍记录见 ADR 0065 / ADR 0067。
> **引用方式**：后续 UI 票在验收条件中直接引用节号（例：「须符合 ui-visual-standard.md §2」）。
> **演化规则**：随票修订并版本化，每次修订在 §8 留一行。禁止把本文件内容整体搬进单篇 ADR（D-009）。

---

## 0. 审美基准（D-005 / ui-craft2 —— 全文唯一的「像不像」标尺）

> **v2.0 变更**：主基准 = **MangoDisk 观感级全面对标**（ui-craft2 决策 D-005，取舍记录 ADR 0067）。上轮三锚（Wasabi / Apple / VS Code·Discord）**降级为气质参考**——保留其「克制 / 分组 / 密度」的心智约束，不再作为逐条对标的定值来源。

### 0.1 主基准：MangoDisk 观感级对标（harry0703/MangoDisk）

复刻方式 =「看观感、自实现」（同 Files.App Middle-Click 移植先例）：设计风格不受版权保护可复刻；**GPL-3.0 源码禁止拷贝 / 移植 / 翻译进本仓库**，实现全部 Avalonia XAML 自绘（D-005）。施工触及的页面必须整页达新基准，禁止「半新半旧」混排。

| 基准族 | 取什么 | MangoDisk 具体可核验设计点（取证出处见附录 D） | 明确不取 |
|---|---|---|---|
| **设计 tokens** | 圆角阶梯 / 暖中性色板 / 内容字号阶梯 / 低投影 | radius 基准 8（阶梯 sm4 / md6 / lg8 / xl12，`--radius:0.5rem` + 派生式）；暖白 / 暖棕黑双层色板（oklch 值离线转 sRGB，Avalonia 无 oklch）；内容字号 6 档 = 20/15/13/12/11/10 + 页头 h1=22；阴影仅两档 subtle 7% 与 dialog 18% | 高饱和品牌色大面积铺陈；多重阴影层级堆叠；任意字号字面量 |
| **布局骨架** | 可收合侧栏 + 页壳 + 分组卡片 | 侧栏展开 240 / 收合 68（仅图标 + tooltip）；nav 项高 40、圆角 8、icon 24 盒 + label 14；组标签 11px / 字重 600 / 字距 0.04em；页头高 58（h1 22 + 右侧操作区 min-height 36 / gap 8）；内容宽度档 readable 1160 / wide 1280 居中；卡片 = bg + border + radius + padding 24 | IDE 式高密度双列排布；无宽度档的通栏内容；侧栏不可收合的固定形态 |
| **交互范式** | toast / 确认流 / 空态 / 按钮无 transform | toast = sonner 形态：应用级右下堆叠、gap 10、同屏 4 条、hover 展开、rich-colors、close-button、约 364px 宽、默认 4s；确认对话框 = cancel(outline) + confirm(语义变体) + busy spinner 双禁用；空态 = icon 盒 52 / 字形 36 主色 + 标题 20 + 说明 12（max-w 520 居中）；**按钮全局禁 transform**（位移/缩放被 `!important` 清零），过渡白名单 = color / background / border / shadow / opacity | hover 无实体底色块的「纯变色」；每个操作都弹 toast；按钮带位移 / 缩放 / 弹性缓动 |

### 0.2 气质参考（上轮三锚，降级保留）

| 锚 | 取什么（气质层面） | 明确不取 |
|---|---|---|
| **Wasabi 气场**（全局） | 隐私工具的克制感：大留白、暗色基调、点缀色极少、无装饰性元素；状态用状态点 + 文案表达 | 装饰性图形、渐变背景、品牌色大面积铺陈 |
| **Apple 系统设置骨架**（配置页 / 表单） | 分组卡片 + 行内左标签右控件 + 行间分隔的心智模型 | 通栏分隔线、等距均匀网格、IDE 式双列密度 |
| **VS Code · Discord 密度与 icon 法**（侧栏 / 列表） | icon 密排与列表行密度法 | 把 IDE 的高信息密度带进默认界面（D-002：默认界面属大众隐私用户） |

> **硬约束**：全文禁止空泛审美词（「高级感」「精致」「现代」「有质感」）。凡提「像不像」，必须落到 §0.1 基准表或附录 D 取证值的具体设计点。

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
| `ServiceManagerPage.axaml:43-45` | 启动 / 停止 / 刷新（三枚 `ghost`） | 无等宽约束，宽度 = 文案长度 | **不符 R1-1** | 票 03（ui-craft）已落地 SharedSizeGroup，见附录 B |
| `RulesPage.axaml:29-30` | 保存 / 重置（两枚 `ghost`） | 同上 | **不符 R1-1** | 同上 |
| `MainWindow.axaml:82-94` | 暂停·恢复 / 打开配置目录（两枚 `nav-action`） | `HorizontalAlignment=Stretch` 已全宽 | 符合 | — |
| `ServiceManagerPage.axaml:30` / `:58` | 安装（`primary`）/ 卸载（`danger`） | 各占独立行、右对齐 | 符合 R1-2 | — |
| `LogsPage.axaml:15` | 清空日志（`ghost`） | 单枚 | 符合 R1-2 | — |
| `MainWindow.axaml:85` / `:90` | 原裸按钮两枚 | 已归队 `nav-action`（A-001 已闭合） | 符合 | — |

### 1.3 基准对照

- **MangoDisk**：按钮全局被 `!important` 锁定 `translate/scale = 0/1`，过渡白名单 = `color, background-color, border-color, box-shadow, opacity`（附录 D.6）——「按钮无位移无缩放」是其刚性不变量，与本项目 N1 同构；页头操作区按钮取 `border-border/70` + 无阴影无 transform；确认流 footer = cancel(outline) + confirm(语义变体)。
- **WinUI / Windows 11（atomcode 实测）**：**对话框按钮行等宽**——`ContentDialog` 的 CommandSpace 为五列网格（Primary `*` / 8px spacer / Close `*`），按钮 `HorizontalAlignment=Stretch`，`ContentDialogButtonSpacing=8`；官方 PR #3926 确认「对话框变宽→按钮变宽」是设计意图。但**页内工具按钮行** WinUI 用 `ButtonPadding 11,5,11,6` + 最小高 32，即**内容自适应**。
- **Apple HIG（atomcode 实测）**：原文是「用**样式**（accent 填充 vs 无填充）而非尺寸区分首选动作」；「当你用**同尺寸**按钮提供两个以上选项时，你在发出这是一组连贯选择的信号」——Apple 的成组信号靠**同尺寸 / 同样式**，不靠等宽拉伸。命中区 ≥44×44pt（本项目 32 高已由 ADR 0056 票 01 书面裁定取 Win11 标准，此处不取 44）。
- **Material 3（atomcode 实测）**：对话框按钮**尾缘对齐**、最多两个操作、**按钮间距 8dp**；文案过长时**改为上下堆叠**而非拉伸等宽——M3 与 WinUI 在对话框语境存在**方向性分歧**。

### 1.4 反例（禁止）

- 同组按钮宽度 = 文案长度（当前服务页三枚的实际形态，即用户痛点的字面来源）。
- 用 `Padding` / `Margin` 微调单枚按钮去「凑」等宽（违反 R1-3）。
- 在 Views 内行内写 `MinWidth` / `Height` 覆盖（违反 R1-3，会被既有 guard 拦下）。

---

## 2. nav 胶囊三态（v2.0：胶囊语言取代左缘指示条）

> 基准定位：本节视觉语言 = ui-craft2 D-002 / D-003 拍板的**胶囊体系**，观感基准 = MangoDisk 侧栏 `nav-item` 实测公式（附录 D.3）。上轮 4px 左缘 accent 槽位体系（v1.2 §2.1）随 D-002 **退役**——当前代码仍是旧语言，迁移施工在票 03（nav-capsule-tri-state，落点双处：`Views/Controls/NavButton.axaml` + `AppTheme.axaml`，A-002）。

### 2.1 三态定义（目标标准）

| 态 | 背景 | 前景（文字 + 图标） | 过渡 |
|---|---|---|---|
| idle | Transparent | `SemiColorText2` | — |
| hover | **半透明中性圆角胶囊底色块**（基准值：`sidebar-accent` 约 52% 透明叠加，圆角 8 = RadiusLg（v2.0 阶梯，票 02 落地）；Avalonia 落点 = Semi 语义刷等效，票 03 定值） | 沿用 D-004 定值 = 向 `SemiColorPrimary` 叙事（基准偏差见张力 T-3） | BrushTransition Background + Foreground 两路 ~150ms |
| pressed | 同族加深（基准值：`sidebar-accent` 约 72%） | 继承 | 同 hover |
| active | **D-003 拍板：主色实心胶囊**（`SemiColorPrimary` 或等效主色），圆角 8 | **反白前景**（对主色底 ≥4.5:1 达 WCAG AA；五主题色板逐一验证，nord 3.81 / dracula 4.15 观察项必须解决或显式豁免登记） | 同 hover |
| disabled | 继承 | `SemiColorText2` + `Opacity 0.5` | — |
| focus-visible | 继承 | 继承 | 全局 2px 焦点环（基准：nav 用 inset box-shadow 2px `primary` 约 32% 透明，与 ADR 0051 A1 描边兼容） |

### 2.2 三条硬性约束

- **N1 纯色 / 透明度叙事**：无 `scale`、无位移、无弹性缓动（ADR 0054 定稿 + ADR 0051 A2 延续；禁 `BackEaseInOut` / `ElasticEaseInOut`）。MangoDisk 佐证：按钮 transform 被全局 `!important` 清零（附录 D.6）。
- **N2 过渡走 BrushTransition 白名单**：Background / Foreground 两路必备（上轮已补 Foreground 150ms SineEaseOut，保留复用）。基准：MangoDisk nav-item 过渡 = `background-color / color / box-shadow 0.16s ease`；禁 `Transition` 泛类全属性过渡（等价 `transition: all`，白名单纪律要防的）。
- **N3 三态层次分明、active 与 hover 视觉联动**：hover 半透明胶囊 → active 实心胶囊的「由浅入实」叙事；禁止同为浅色导致「看不出选中」（D-003）。

### 2.3 现状对账（v2.0 时点）

| 项 | 现状 | 目标 | 处置 |
|---|---|---|---|
| 视觉语言 | 4px 左缘 accent 槽位体系（v1.2，票 02/ui-craft 交付） | 胶囊三态（D-002 / D-003） | 票 03 |
| 过渡机制 | BrushTransition 三路（Background / BorderBrush / Foreground 150ms SineEaseOut）已就位 | 保留复用 | — |
| `NavFeedbackSourceTests` 既有断言（4px 槽位等） | 撞红预演对象 | 三档处置（杀 / 改造 / 保留+characterization） | 票 03（A-004） |
| 尺寸 | nav / nav-action Height=40、Padding=12,0 | 保留（基准 nav 项高同 40；icon 盒 24、label 14 见附录 D.3，票 03 对齐细节） | — |

### 2.4 基准对照（MangoDisk `nav-item` 实测公式）

| 态 | MangoDisk 实测（md-sidebar.vue） | 本项目目标标准（§2.1） |
|---|---|---|
| idle | `background: transparent`，fg 继承 sidebar-foreground | Transparent / Text2 |
| hover | `sidebar-accent` 52% 透明圆角胶囊（radius 8），**前景不变色** | 半透明中性胶囊 +（现行决策）前景向 primary 叙事 ⚠ T-3 |
| pressed | `sidebar-accent` 72% | 同族加深 |
| active | `sidebar-accent` 实心 + `sidebar-accent-foreground` + **字重 600** + **3px×24px 圆头主色左缘条**（`::before`，radius 999） | 主色实心胶囊 + 反白（D-003）⚠ T-3 |
| focus-visible | inset box-shadow 2px，primary 约 32% | 全局 2px 焦点环 |
| 过渡 | `background-color / color / box-shadow 0.16s ease` + 结构变化 240ms | BrushTransition ~150ms 白名单 |

> **张力 T-3（登记待裁定，本规范不静默改向）**：D-003 拍板「active = 主色实心胶囊 + 反白」在 MangoDisk 参照物进入决策（D-005）**之前**；MangoDisk 实物 active 并非主色实心，而是「accent 实心胶囊 + 3px×24px 主色左缘 pill + 字重 600」。hover 前景亦不同（基准不变色 vs 现行向 primary 叙事）。票 03 施工前须由用户 / 大脑裁定：维持 D-003 实色胶囊，或按基准实物 revised。台账处置建议随票 03 报告呈报。

### 2.5 气质参考对照（上轮证据保留）

- **VS Code Activity Bar（atomcode 实测）**：hover 无背景，图标 `activityBar.inactiveForeground`(#868686) → `activityBar.foreground`(#D7D7D7)——「灰→中性白」亮度叙事；active 指示条 2px 左边框，现代暗色主题取 accent `#0078D4`。
- **WinUI NavigationViewItem（atomcode 实测）**：hover 背景 = `SubtleFillColorSecondaryBrush`，前景中性；accent 只给 `SelectionIndicator`（初始 `Width=0 Opacity=0`，仅 Selected 展开）。Button hover 过渡官方值 = 83ms BrushTransition。
- **Apple 系统设置 / Discord / Fluent NavigationView**：胶囊高亮语言一族（D-002 呈报参照系，用户选择即接受）。

### 2.6 反例（禁止）

- hover 无实体底色块的「纯变色」（用户判塑料感的直接原因，D-002）。
- hover 引入 `scale` / 图标放大 / 位移 / 弹性回弹。
- active 与 hover 同为浅色导致「看不出选中」。
- 4px 左缘 accent 槽位体系（`BorderThickness 4,0,0,0` + Transparent 占位）——已退役语言，票 03 移除。

---

## 3. 表单行骨架（分组卡片参照）

### 3.1 骨架定义

```
页 = 1..N 张 settings-card（分组卡片）
卡 = section-header + N × ( settings-row + row-divider )
行 = 左：row-label(.body) [+ row-desc(.caption)]  ｜  右：控件（Dock=Right / HorizontalAlignment=Right）
```

### 3.2 现状（ConfigPage.axaml / ServiceManagerPage.axaml 实物）

| 元素 | 实态 | 位置 |
|---|---|---|
| `Border.settings-card` | `SemiColorBackground1` + `SemiColorBorder` + `RadiusLg`(8) + `Elevation2` + `Padding 16` | `AppTheme.axaml:250-257` |
| `Border.settings-row` | `Padding 16` | `AppTheme.axaml:259-261` |
| `Border.row-divider` | `BorderThickness 0,0,0,1` + `Margin 16,0,16,0`（inset，票 04/ui-craft 落地） | `AppTheme.axaml:263-266` |
| `TextBlock.section-header` / `.row-label` / `.row-desc` | 三级文本角色 | `AppTheme.axaml:306-320` |
| 控件列宽度（路径类） | `u|PathPicker.inline-input Width=280` 共享 class 收敛（ADR 0061 票 24 / 票 25） | `AppTheme.axaml:325-331` |

### 3.3 表单行偏差（票 04 已落地）

- **B1 分隔线通栏** —— 已落地（票 04）：AppTheme 的 Border.row-divider 增 Margin="16,0,16,0"，使分隔线与行文本左缘对齐（settings-card Padding 16 + settings-row Padding 16 = 文本自卡边起 32；分隔线加 16 后同为 32）。守卫：Row_Divider_Must_Be_Inset_To_Label_Column_Not_Full_Bleed
- **B2 控件列宽度散落** —— 已落地（票 04）：ConfigPage 三枚 ComboBox 行内 Width="160" 收敛为共享 class ComboBox.inline-control（AppTheme，取值 160 沿用实态、仅迁移权威位置），与既有 u|PathPicker.inline-input Width=280 同族。守卫：Views_Must_Not_Inline_ComboBox_Width
- **B3 分组卡片之间零间距** —— 已落地（票 04）：ConfigPage 与 ServiceManagerPage 的卡片容器均设 Spacing="{DynamicResource SpaceLg}"（16）。取 16 而非 WinUI 的 4：§5.3 Wasabi 锚要求卡片外间距 >=16，且 §5.2 P1 为规范定值（4 属 WinUI 官方示例，非本项目口径）。守卫：Settings_Cards_Must_Be_Separated_By_SpaceLg

### 3.4 基准对照

- **MangoDisk**：设置页 = Card 组合——每组一张 `bg-card` + `border` + `rounded` + `p-6`(24) 卡，卡内标题 + 行控件右对齐，行间 16、组间约 24；对话框内容区 padding 20 inline（附录 D.5）。本项目 `settings-card` 现 Padding 16 为既有定值，与基准 24 的差异交页面票按 P 族规则复核（不做行内字面量回潮）。
- **Apple 系统设置**（气质参考）：分组卡片 + 左标签右控件 + 行间 inset 分隔线；本项目已具雏形。
- **WinUI SettingsCard**（同族对照）：Header + Description 左、控件右、卡片内行分隔 → 佐证「左标签右控件」是桌面设置页的共识骨架，而非 Apple 独有。
- **VS Code / Discord**（气质参考）：只取列表 / icon 密度法，**不把 IDE 密度带进表单**（D-002）：表单行保持呼吸感，不做 8px 高密度行。

### 3.5 反例（禁止）

- 通栏分隔线 + 等距 Padding 组成的「均匀网格」（AI 感第一根因，ADR 0051 A1）。
- 行内写 `Margin` / `Padding` 字面量绕过 `settings-row`（A-008 清理对象）。
- 把表单做成 IDE 式双列高密度。

---

## 4. 空态规范

### 4.1 规则

- **E1 必有占位**：每个可空列表 / 表格必须有空态占位——非空着、非只显示表头。
- **E2 骨架（v2.0 按 MangoDisk `md-empty-state` 重校）**：容器纵向居中偏上（下侧留白 > 上侧，基准 padding-bottom = clamp(52px, 9vh, 88px) 的视觉中心补偿）+ **图标盒 52×52 / 字形 36 主色**（compact 变体 44 / 28、muted 色）+ 标题 `content-empty-title`(20) 卡片前景 + 一句说明 `content-body`(12) muted、max-w 520 居中 + 可选操作位；**不加插画、不加渐变、不加装饰**（克制气场延续）。
- **E3 本地化**：文案必须走 locale key，10 语言 key 集合与 en.json 对齐（`All_Locales_Have_Identical_Flat_Key_Sets_As_English` 保持绿）。
- **E4 开关联动**：由 ViewModel 在**收口处**联动刷新（Append / Clear / Filter 等），不得用事件订阅长活（ADR 0061 页面 code-behind 零订阅规约）。
- **E5 区分空因**：「真空」与「过滤无结果」用不同文案；过滤无结果须提示可清除筛选。

### 4.2 现状

| 面 | 落点 | 状态 |
|---|---|---|
| 日志页 | `LogsPage.axaml:57-62`（`log.empty`，`HasNoLogs`，`MainWindowViewModel:444/509` 收口联动） | 已落地（ADR 0062 D5）；E2 新骨架对齐在各页面票 |
| 规则页 | `RulesPage.axaml:63`（`rules.empty`，`RulesPanel.HasNoVisibleRules`，`ApplyFilter` 收口） | 已落地（ADR 0062 D5）；E2 新骨架对齐在各页面票 |
| 服务管理器页 | 「未安装服务」态 | 已评估（票 04）：**不构成 E1 缺口** —— 该页无可空列表 / 表格，E1 适用对象不存在；「未安装服务」由 ServiceStatus 文案 + ServiceStatusDotColor 状态点承载（ServiceManagerPage 状态卡），属状态展示而非空态，不新增占位 |
| 配置页 | 路径行空值由 `Watermark` 承载，不属空态 | 不适用 |

### 4.3 基准对照

- **MangoDisk**：`md-empty-state` 即上述 E2 公式实物（附录 D.5）；其语义色 = 图标 `text-primary`（主色小面积点缀）+ 标题 card-foreground + 说明 muted-foreground。
- **气质参考**：Wasabi（空态 = 一句克制文案，无插画无徽章）；Apple（弱化文案居中，不用图形占位）；VS Code（说明 + 可选操作按钮；本项目取轻量版，不强制操作按钮）。

---

## 5. 间距节奏

> 本节是**页面级消费规则**（新增层）。token 本身的定义与数值不变，见 `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml`；v2.0 token 定值刷新（圆角阶梯 / 布局 token / 阴影两档）票 02（rounded-window-shell-tokens）已落地。

### 5.1 token 表（实物）与 MangoDisk 布局 token 基准

| 族 | 本项目档位（实物） | MangoDisk 基准（附录 D.2） |
|---|---|---|
| Space | Xxs 2 / Xs 4 / Sm 8 / Md 12 / Lg 16 / Xl 24 / Xxl 32 / Xxxl 48 | Tailwind 4px 基数，DIP 同值直用 |
| Radius | Xs 2 / Sm 4 / Md 6 / Lg 8 / Xl 12（票 02 已按基准列落地） | base 8 → **sm 4 / md 6 / lg 8 / xl 12**（`--radius:0.5rem`，派生 sm=base-4 / md=base-2 / xl=base+4） |
| Duration | Fast 75 / Normal 150 / Slow 250 | 色彩反馈 160ms ease；结构变化 240ms ease |
| Elevation | 0 / 1,2=subtle 7% / 4=dialog 18%（票 02 已落地：`0 1 2 0 #12000000` / `0 1 3 0 #12000000` / `0 25 50 -12 #2E000000`） | 阴影仅两档：subtle = 黑 7%、dialog = 黑 18%（`0 25px 50px -12px`） |
| 布局（新增族，票 02 立 `Layout*` 17 枚） | LayoutSidebarWidth 240 / LayoutSidebarCollapsedWidth 68 / LayoutNavItemHeight 40 / LayoutPageHeaderHeight 58 / LayoutPagePaddingInline 20 / LayoutPagePaddingTop 14 / LayoutContentWidthReadable 1160 / LayoutContentWidthWide 1280 / LayoutToolbarHeight 36 / LayoutResultRowHeight 44 / LayoutDialogWidth{Sm,Md,Lg,Xl} 440/520/620/720 / LayoutDialogHeaderHeight 68 / LayoutDialogFooterHeight 56 / LayoutDialogBodyPaddingInline 20 | 侧栏 240/68、nav 项高 40、页头 58、页 padding 20/14、readable 1160 / wide 1280、toolbar 36、结果行 44、dialog 宽 440/520/620/720（header 68 / footer 56 / body 20） |

### 5.2 页面级消费规则

| 编号 | 场景 | 档位 | 落点 |
|---|---|---|---|
| P1 | 同级分组卡片之间 | `SpaceLg`(16) | 已修正（票 04）：ConfigPage / ServiceManagerPage 卡片容器均设 `Spacing="{DynamicResource SpaceLg}"`，见 §3.3 B3；MangoDisk 基准组间约 24，页面票可复核 |
| P2 | 卡片内边距 / section-header 与首行 | `16` / `SpaceMd`(12) | `settings-card` 与 `settings-row` 的 `Padding 16`；基准卡片 padding 24，页面票复核 |
| P3 | 行内标签列与控件列之间 | ≥ `SpaceLg`(16) | 控件右对齐贴卡内边距 |
| P4 | 同组内元素（如一组按钮） | `SpaceSm`(8) | 服务页按钮组；基准页头操作区 gap 8 同值 |
| P5 | 侧栏导航项之间 | `SpaceXxs`(2)，连续无 gap（票 02 按基准组内项距 2 落） | `MainWindow.axaml` 两组导航 StackPanel；基准组间 10-12 由上下 Dock 分区布局承担，非相邻组距不适用 |
| P6 | 标题栏按钮组 | `SpaceXxs`(2) | `MainWindow.axaml:27` |
| P7 | **禁止等距均匀** | — | 不同层级必须用不同档位；等距均匀是 AI 感第一根因（ADR 0051 A1） |

### 5.3 基准对照

- **MangoDisk**：页面 padding 20 inline / 14 top；侧栏 padding-inline 8（收合）→10（展开）；组内项距 2、组间距 10-12；卡片 p-6=24；dialog body 20、footer padding 7/20。**层级靠档位差表达**（附录 D.2）。
- **气质参考**：Apple（分组间距明显大于行内间距，层级靠间距差表达）；VS Code / Discord（列表密排 4-8、区块间 16-24）；Wasabi（大留白是气场来源——卡片外间距不得小于 16）。

### 5.4 间距字面量（A-008）—— 票 04 已清零

- **历史登记**：6 个 axaml 共 14 处 `Margin` / `Spacing` 字面量残留（旧口径）。
- **票 04 复算**：22 处间距字面量，其中**离轨 11 处**（值 `20` ×7 / `10` ×3 / `6` ×1）；`0` 为合法零间距，**不计离轨**。
- **终态（票 04）**：**离轨 0**；`Spacing` 字面量 **0**（全部 `{DynamicResource SpaceXxx}`）；余 25 处 `Margin` / `Padding` 字面量**全部落在 ramp 上**。
- **技术硬约束（不可绕过）**：`Margin` / `Padding` 属 **Thickness**，受 CONTEXT「Padding Literal Quantization」约束必须保持**字面量字符串**——`DynamicResource` Double 赋 Thickness 会跳过 `ThicknessTypeConverter`，导致布局测量期 `InvalidCastException`。故 Thickness 的「token 化」= **量化到 ramp 字面量**，**不等于**改成 `DynamicResource`。`Spacing` 属 Double，**可且应**改 `{DynamicResource SpaceXxx}`。
- **离轨值映射（票 04 定值，规范 P1–P7 未覆盖故由本票定值）**：`20` → `24`（`SpaceXl`）；`10` → `8`（`SpaceSm`，对齐同侧栏兄弟元素）／→ `12`（`SpaceMd`，卡内垂直）；`6` → `8`（`SpaceSm`，事件徽章）。**取上档 24 而非下档 16 的决定性理由 = P7 禁等距均匀**：取 16 会使页头 `24,16,24,16` 与内容区 `24,16,24,24` 两个层级取同一档位。
- **守卫**：`PagesVisualAlignmentSourceTests` —— `Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp` + `Views_Spacing_Must_Use_DynamicResource_Not_Literal`。

---

## 6. Toast 反馈链规划（规划面，非现役标准）

> 本节是**规划**。实现票 = 票 08（toast，ui-craft2 轮）。形态基准 = MangoDisk 的 vue-sonner 配置实物（附录 D.4）；组件选型（Ursa Toast / Notification 现成 vs 自绘 ItemsControl）由票 08 内调研定，禁新 NuGet 依赖除非票内调研结论 + 用户确认（A-003）。

| 编号 | 维度 | 规划内容（v2.0 按 sonner 基准重校） |
|---|---|---|
| T1 | 触发点 | 配置防抖自动保存成功 / 失败（升级现有 `SaveStatus` 文本反馈）、规则保存、服务安装 / 卸载完成、清理异常 |
| T2 | 位置与时长 | **应用级右下堆叠**（viewport 右下，跨页面 / 对话框恒定；基准 `position="bottom-right"`），默认 **4s**（sonner 默认；文档隐藏时暂停计时），可手动关闭（close-button） |
| T3 | 视觉 | **popover 同族容器**（基准 `--normal-bg=popover` + `--normal-border=border` + radius 8 + `shadow-lg` 级投影 + `ring-1 black/5`）；约 **364px** 最大宽；**禁用 emoji / Unicode 符号**（ADR 0050 A2 已清零），状态用 Material.Icons 16px（基准 lucide size-4：CircleCheck / Info / TriangleAlert / OctagonX / 加载 spin / X 关闭） |
| T4 | 语义色 | rich-colors 模式：成功 `primary`（或 success 绿）/ 警告 `warning` / 失败 `danger`（destructive）/ 信息 `info`，各 10 语言文案 |
| T5 | 无障碍 | 可被屏幕阅读器播报（LiveRegion 语义）且不抢焦点（WCAG） |
| T6 | 去重与排队 | 同 key 连续触发合并刷新（覆盖上一条）；**同屏堆叠上限 4 条**（基准 `visible-toasts=4`）、堆叠间距 10（`gap=10`）；hover 时展开（`expand`）露出全部 |
| T7 | 动效 | 堆叠位移 **14px/层** + 缩放 **0.05/层**；过渡 **400ms ease 可中断**（Avalonia `Transitions` / `TransformOperationsTransition` 天然可中断重定向，行为对齐 sonner 从 keyframes 改 transition 的决策） |
| T8 | 反例 | 每个操作都弹（噪声）；用 Unicode 符号；自动消失但无手动关闭；抢焦点 / 模态化 |

---

## 7. 验收标尺（MangoDisk 基准 + 截图基线）

- **V1 每票验收 = 同机位 before/after 人工对照 + MangoDisk 基准过目**。拍摄规程见 `docs/process/reports/32-ui-manual-smoke.md §5.2`，基线资产落 `docs/design/screenshots/`。
- **V2 机位**：窗口 920×600 非最大化、同坐标、同 DPI、侧栏 200、预设 catppuccin + 明暗档位写明、语言 zh-CN。
- **V3 MangoDisk 对照清单**（逐条打勾，禁止「看起来不错」一类结论）：
  - **胶囊 nav**：idle 透明 / hover 半透明胶囊 / active 实心层次分明；无 4px 左缘槽位残留；过渡平滑无双路硬切。
  - **卡片骨架**：配置类页为分组卡片 + 左标签右控件；行分隔线 inset；卡片间距与卡内 padding 层级有别。
  - **密度与节奏**：nav 项高 40、icon 20-24 + body 14、组内紧凑；间距档位不塌缩为等距。
  - **按钮纪律**：全局无 transform（无位移 / 缩放 / 弹性）；过渡只覆盖画笔与阴影。
  - **空态构图**：居中偏上 + 主色图标盒 + 标题 + 弱化说明，无插画无渐变。
  - **toast（票 08 起）**：右下堆叠、popover 同族卡片、语义色 + 图标 + 可关闭。
  - **克制气场（气质参考）**：无装饰性图形 / 渐变 / 插画 / 徽章堆砌；点缀色仅用于 active / 状态点。
- **V4 探活**：report-32 §2 的 28 项 + 票 01（ui-craft）新增 G 组由用户执行，结果回填 §3 / §3.1。
- **V5 视觉一致性不交给纯文本断言单独背锅**：source-lint 只能锁结构不变量（如「不得行内覆盖按钮尺寸」），色彩 / 节奏 / 观感走人工对照。新断言须满足 `tests/TEST-CONVENTIONS.md` R1；撞红按 R2 三档分类（杀 / 改造 / 保留）并留痕。
- **V6 门禁**：CI 云端绿（`.github/workflows/ci.yml`）；本机零构建（CI-only）；无新增无注释断言。

---

## 附录 A. atomcode 控件级调研（2026-09-12，票 01 / ui-craft）

> 执行方式：`atomcode 5.0.9` 单发串行 headless 调研（遵守串行护栏：同一时刻仅一个在途）；Exa / Tavily / AnySearch 三引擎共 16 次检索，28 次原文抓取核验（含 Patchright 渲染 Apple HIG 页面），覆盖 ≥10 个域名。
> **完整 28 条来源清单见 `.scratch/ui-craft/reports/01-visual-standard-doc.md` §5**；本附录只列与规范条文直接绑定的关键来源。

### A.1 核心结论

| # | 结论 | 关键证据 | 规范落点 |
|---|---|---|---|
| C1 | **四大参考产品的 nav hover 一律是中性灰叠加，accent 只属于 active/selected**；hover 无「预示指示条」，指示条是 active 专属 | VS Code Activity Bar hover 无背景、图标 #868686→#D7D7D7；WinUI hover=`SubtleFillColorSecondaryBrush`、指示条 `SelectionIndicator` 初始 `Width=0 Opacity=0`；Discord hover=`rgba(78,80,88,0.3)`、品牌色只用于未读/提及 | §2.4 / §2.5（**张力 T-1** 史录；v2.0 胶囊语言下该张力形态见 T-3） |
| C2 | **hover 过渡在桌面体系里以「切换」为主**：VS Code 无过渡（activity bar 唯一 transition 是拖拽指示条，`duration 0ms`），WinUI NavigationViewItem 用 `DiscreteObjectKeyFrame KeyTime=0` 离散切换；唯一可复用数值是 WinUI Button 的 **83ms BrushTransition** | `activityaction.css`、`NavigationView_rs1_themeresources.xaml`、`Button_themeresources.xaml` | §2.2 N2（本项目 150ms 为既有 ADR 定值，不因本条改） |
| C3 | **按钮组：对话框语境等宽，页内工具按钮行内容自适应**；Apple 的成组信号靠「同尺寸/同样式」而非等宽拉伸；M3 文案过长时改堆叠 | WinUI `ContentDialog` 五列 stretch 网格 + `ButtonSpacing=8` + PR #3926；VS Code `width: fit-content`；Apple HIG Buttons 原文；M3 Dialogs Specs | §1.1 R1-1（**张力 T-2**） |
| C4 | **表单行骨架有完整开源模板可对齐**：WinUI SettingsCard 为四列网格 `Auto(图标) / *(标题+描述) / Auto(控件) / Auto(ActionIcon)`，断点 800px 控件换行到标题下方、600px 连图标折叠，卡片间 `Spacing=4`，背景过渡同为 83ms，模板内置 `SettingsCardContentMinWidth` | `SettingsCard.xaml`（CommunityToolkit/Windows）+ 官方文档 | §3.1 / §3.3 B2 |
| C5 | **macOS 侧：分隔线需 inset（两侧各 ≥20pt）、描述文字 11pt（Small）、标签列与控件列之间 8pt**；System Settings 分组卡片**无官方数值规格**（仅有社区逆向值与 HIG 侧栏 small/medium/large 三档定性规格） | zenn《macOS Settings Window Guidelines》（社区）；Apple HIG Sidebars（官方，行高三档 + 侧栏图标默认 accent） | §3.3 B1 / §5 |

### A.2 已登记张力（须大脑裁定，本票不静默改向）

| 编号 | 张力 | 用户决策 | 工业证据 | 处置 |
|---|---|---|---|---|
| **T-1** | nav hover 是否引入 primary 色彩叙事 | 上轮 D-004（用户拍板）：向 primary 叙事，禁纯中性灰平移 | VS Code / WinUI / Discord / macOS 的 nav hover **均不用 accent**；MangoDisk 实物 hover 前景亦不变色 | v2.0 并入 T-3 一并登记，票 03 施工前裁定 |
| **T-2** | 页内按钮组等宽与否 | A-001 / spec ID3（用户拍板）：同组等宽 | 页内工具按钮行四大平台**均为内容自适应**；仅对话框语境等宽 | 沿用等宽；票 03（ui-craft）已落地 SharedSizeGroup，观感复核随页面票 |
| **T-3** | nav active/hover 定值 vs MangoDisk 实物公式 | D-003（用户拍板）：主色实心胶囊 + 反白；hover 前景向 primary 叙事 | MangoDisk 实物：active = accent 实心胶囊 + 3px×24px 主色左缘 pill + 字重 600；hover 前景不变色 | **新增登记（v2.0）**：票 03 施工前须裁定维持或 revised（§2.4） |

### A.3 信息缺口（诚实声明）

- macOS System Settings 分组卡片**无官方数值规格**，8pt / 11pt / 20pt 属社区逆向值（zenn），且其针对旧式 toolbar 偏好窗口。
- Discord 与 macOS 的 hover 过渡时长**无官方数值**（Discord 闭源 Electron，仅有社区 CSS 反拆）。
- WinUI NavigationViewItem 指示条展开动画时长**未数值化发布**（仅 Button 的 83ms 可同类参考）。
- VS Code 经典 Dark+ 与 Dark Modern 的 `activityBar.activeBorder` 不同（accent #0078D4 vs 白色），引用时须注明主题版本。
- Ursa Toast / Notification 的具体样式细节未逐行读源码（atomcode 调研已标注），票 08 选型时补核验。

### A.4 关键来源（完整 28 条见上轮报告 §5）

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

## 附录 B. 按钮变体使用对账表（票 03 / ui-craft，2026-09-13）

> 本节随票演化。数据由 `DesignSystemTests.Button_Elements_Must_Carry_A_Variant_Class`（零裸按钮）与 `Button_Groups_Must_Use_Equal_Width_Mechanism`（等宽机制不回退）两条 source-lint 断言锁定。

| 变体 | 实例数 | 落点 | 五态 | 备注 |
|---|---|---|---|---|
| `primary` | 1 | ServiceManagerPage:30 安装 | ADR 0062 D3 全矩阵 | 单枚 hug（R1-2） |
| `ghost` | 6 | LogsPage:15；RulesPage:35-36；ServiceManagerPage:51-53 | 同上 | 后两组已按 R1-1 等宽 |
| `danger` | 1 | ServiceManagerPage:58 卸载 | 同上 | 单枚 hug（R1-2） |
| `icon` | 2 | ConfigPage:205-206 | 同上 | `Button.icon` 32×32，天然等宽 |
| `nav` | 1（组件内） | Views/Controls/NavButton.axaml:10 | 同上 | 侧栏主导航组，全宽 Stretch |
| `nav-action` | 2 | MainWindow:85 / :90 | 同上 | 自 2026-04-27 首版即归队；票面「裸按钮」为单行 grep 假阳性 |
| `caption-btn` | 3（含 1 枚 `danger`） | MainWindow:28 / :31 / :34 | 同上 | 标题栏组，`Padding 16,6` 由既有断言锁定 |

- **合计 16 枚：零裸按钮、零行内尺寸覆盖**（ADR 0056 票 01 的清零成果保持）。
- **等宽落点仅两处**：`ServiceManagerPage`（`ServiceActions` 组，3 枚）与 `RulesPage`（`RuleActions` 组，2 枚）。机制 = `Grid` + `ColumnDefinition SharedSizeGroup`（组内最长文案动态定宽）+ `ColumnSpacing="{DynamicResource SpaceSm}"`（§5.2 P4）。
- **不取固定 `MinWidth` 的理由**：本项目 10 语言，组内最长文案随语言变化，定值在长文案语言（德 / 俄）下失效；`SharedSizeGroup` 按运行时测量结果定宽，任何语言下都成立。

---

## 附录 C. A-007 侧栏定宽处置（票 04 / ui-craft，2026-09-13）

| 项 | 内容 |
|---|---|
| 现象 | 侧栏内部 Border 固定 `Width="200"`，`GridSplitter` 拖动改变列宽但面板视觉宽度不跟随（report-32 D3 观察项） |
| 实态 | `MainWindow.axaml` 列宽由 `ColumnDefinitions="200,4,*"` + `MainWindow.axaml.cs` 的 `RestoreSidebarWidth` / `OnSidebarSplitterDragCompleted` 控制在 **170–400**；内部面板硬钉 200，两者冲突 |
| 判定 | **缺陷，非设计意图** —— 列宽既已开放可调（ADR 0052 A5 显式交付「可拖拽侧栏」），内部定宽与之直接冲突，且无任何注释 / ADR 声明该定宽为设计 |
| 后果 | 列宽 > 200 → 面板右侧留空、露出 `SemiColorBackground0`（侧栏视觉宽度与内容区断裂）；列宽 < 200 → 面板溢出被裁 |
| 处置 | 删除 `Width="200"`；Border 位于 Grid cell 内默认 `HorizontalAlignment=Stretch`，移除后自动跟随 Column[0] 实际宽度（改动 1 行，零副作用） |
| 与 §0 的关系 | §0 Wasabi 锚记「窄侧栏（默认 200，拖宽 170–400）」——「拖宽」语义要求面板跟随列宽，本处置与之**一致**，不触发 §0 修订 |
| 守卫 | `PagesVisualAlignmentSourceTests.Sidebar_Panel_Must_Follow_Column_Width_Not_Fixed_200` |

---

## 附录 D. MangoDisk 观感取证表（票 01 / ui-craft2，2026-09-14）

> 取证方式：`gh api` 只读 harry0703/MangoDisk 仓库（GPL-3.0——**只读观感与数值，源码零拷贝 / 零移植**），叠加 atomcode 深度调研（10 检索 / 13 原文核验，报告 §调研）。数值为其实测定值，本项目落地时按 Semi.Avalonia 语义刷映射（票 02-08 施工）。

### D.1 主题色板（`src/assets/themes/mangodisk.css`）

- 皮肤机制：`html[data-skin='mangodisk']`（应用默认皮）× `[data-theme='dark']` 明暗双轴；oklch 色值，Avalonia 侧需离线转 sRGB（Avalonia #8450 无原生 oklch）。
- 暗色：`background oklch(0.18)` 暖棕黑、`card oklch(0.225)`、**sidebar oklch(0.14) 比 background 更深**、`workspace transparent`（大工作区透出页面画布而非第三层板）；`border oklch(0.3)` 低对比描边。
- 亮色：`background oklch(0.984)` 暖白、`card` 纯白、`sidebar oklch(0.968)`、`primary oklch(0.58 0.165 52)` 深芒果橙（亮模式压暗保对比）/ 暗模式 `primary = brand-mango oklch(0.7323 0.1825 54)`。
- 语义色族齐备：secondary / muted / accent / destructive / success / warning / border / input / ring + sidebar 独立组（sidebar / sidebar-foreground / sidebar-accent / sidebar-accent-foreground / sidebar-border）——侧栏**不复用**主内容 accent，单开一组。

### D.2 布局 token（`src/assets/main.css :root`）

| token | 值 | token | 值 |
|---|---|---|---|
| sidebar 收合 / 展开宽 | 68 / **240** | 页 padding inline / top | 20 / 14 |
| nav 项高 | **40** | 页头高 | **58** |
| 内容宽度 readable / wide | 1160 / 1280 居中 | workspace toolbar / control | 36 / 30 |
| 结果行 / 组 / 子行 | 44 / 48 / 40 | 操作条高 | 48 |
| dialog 宽档 | **440 / 520 / 620 / 720**（tall 600） | dialog header / footer / body | 68 / 56 / 20 inline |
| radius（mangodisk 皮） | base **8** → sm 4 / md 6 / lg 8 / xl 12 | 阴影 | subtle 黑 7% / dialog `0 25px 50px -12px` 黑 18% |
| 字号阶梯（content） | meta 10 / secondary 11 / body 12 / primary 13 / section-title 15 / empty-title 20 | 页头 h1 / 标签字重 | **22** / 500（结果行 primary 500） |
| scrollbar | 10px，thumb muted/45 + 3px 透明边 + 全圆 | focus ring | 2px solid + offset 2（nav 用 inset primary 32%） |

### D.3 侧栏 nav-item 公式（`md-sidebar.vue` 实测）

- 结构：`aside.sidebar`（256 默认 / 68 收合）→ `nav-list`（gap 10-12 组间）→ `nav-group`（label 11px / 600 / 0.04em 字距 / 58% 透明）→ `nav-item`（高 40、radius 8、padding 0/14→12、gap 12、icon 盒 24 + label 14）。
- 态：idle 透明 → hover `sidebar-accent` **52%** → pressed **72%** → active `sidebar-accent` 实心 + accent-foreground + **字重 600** + **3px×24px 圆头主色左缘条**（`::before`，radius 999）。
- 过渡：`background-color / color / box-shadow 0.16s ease`；结构（宽 / padding / gap / label 展开）240ms ease；收合态 item 仅图标 + tooltip（side-offset 10）。
- 副件：busy = icon 上 1.5px 描边转圈（primary 染色）或 accessory 位 11px 转圈；notice = 8px destructive 圆点 + ring-2。
- footer：gap 3、padding 8/14；toggle 钮高 32、字号 12。

### D.4 toast（vue-sonner 实物配置）

- `App.vue`：`<Toaster position="bottom-right" :gap="10" :visible-toasts="4" expand rich-colors close-button />`——应用级单点挂载，跨页面 / 对话框恒定。
- 容器：popover 同族（`--normal-bg=popover` / `--normal-border=border` / radius 8）。
- sonner 通用定值（作者文 + issue #630）：max-w ≈364px、默认 4s、堆叠位移 14px/层 + 缩放 0.05/层、变换过渡 400ms ease 可中断。
- 图标：lucide 16px——CircleCheck / Info / TriangleAlert / OctagonX / Loader2 spin / X。

### D.5 空态与确认流

- `md-empty-state`：flex 居中、`gap 10`、padding `24 24 clamp(52,9vh,88)`（下侧重补偿视觉中心）；icon 盒 52 / 字形 36 `text-primary`（compact：盒 44 / 28、muted）；标题 `content-empty-title` 20 card-fg；说明 `content-body` 12 muted、max-w 520 居中；可选 actions。
- `md-confirm-dialog`：Dialog + header（Title + Description）+ 可选 body（padding `0 20 12`）+ footer = cancel(`outline`) + confirm（语义 variant，busy 时 spinner + 双禁用）。
- `md-page-shell`：列 flex + `container-type: inline-size`；页头 min-h 58、grid `1fr / auto`、h1 = 22 font-normal tracking-tight + 副标题 14 muted；操作区 min-h 36 / gap 8 / 按钮 border-70 无阴影无 transform；内容宽度档 readable/wide 居中；Windows 端页头右侧预留 `--window-controls-width + 12px`。

### D.6 全局不变量（`main.css`）

- **按钮零 transform**：`:where(button,...)` 上 `translate:0 0 / scale:1` + `--tw-translate-*` 全 `!important` 清零；过渡白名单 `color, background-color, border-color, box-shadow, opacity`。
- focus-visible：`:not(...) outline-ring/70` 2px solid offset 2；`::selection = primary/20`。
- `prefers-reduced-motion`：全部动画/过渡 0.01ms，**仅运行中任务指示（`.md-operational-motion`）保留但放慢到 1.8s**——运营指示在 reduced-motion 下继续转（低刺激化）。
- dialog：overlay 走 `--modal-overlay-background`（无 backdrop blur）；content `box-shadow 0 25px 50px -12px shadow-dialog`。
- 侧栏结构过渡 240ms ease（宽 / padding / gap）。

### D.7 参考距离声明

- 本项目取「观感定值 + 构图骨架 + 交互范式」；MangoDisk 的 Web 技术能力（Tailwind / color-mix / container queries / backdrop-filter）**不得误当成 Avalonia 免费能力**——逐条按 Avalonia 语义刷 / Transitions / ControlTheme 机制映射（atomcode §2.1 三层 ResourceDictionary 映射法）。
- MangoDisk 侧栏 active 公式与 D-003 的偏差已在 §2.4 / 附录 A.2 T-3 登记，未静默改向。

---

## 8. 修订记录

| 版本 | 日期 | 票 | 修订摘要 |
|---|---|---|---|
| v1.0 | 2026-09-12 | 票 01 / ui-craft | 立文档：七节齐备（按钮组宽度策略 / nav 反馈三态 / 表单行骨架 / 空态规范 / 间距节奏 / Toast 反馈链规划 / 验收标尺）+ §0 三锚表 + 附录 A（atomcode 控件级调研）；登记张力 T-1（nav hover 色彩叙事）与 T-2（页内按钮组等宽）待大脑裁定 |
| v1.1 | 2026-09-13 | 票 03 / ui-craft | 新增附录 B（按钮变体使用对账表，16 枚全量）；§1.2 两处 R1-1 不符项落地——等宽机制取 `Grid` + `SharedSizeGroup`（不取固定 `MinWidth`，理由见附录 B）；订正「裸按钮」为单行 grep 假阳性，`nav-action` 两枚自首版即归队 |
| v1.2 | 2026-09-13 | 票 04 / ui-craft | §3.3 B1/B2/B3 三处表单行偏差全部落地（分隔线 inset `16,0,16,0` / `ComboBox.inline-control` 共享 class / 卡片间 `SpaceLg`，并记明取 16 而非 WinUI 4 的理由）；§4.2 服务管理器页空态评估结论（无可空列表 / 表格，不构成 E1 缺口）；§5.2 P1 现状偏差修正、P5 落点 token 化；§5.4 A-008 清零（离轨 11 → 0，`Spacing` 字面量 → 0）并书面化 Thickness 技术硬约束与离轨值映射定值理由；新增附录 C（A-007 侧栏定宽处置：删硬钉 `Width="200"` 跟随列宽） |
| v2.1 | 2026-09-14 | 票 02 / ui-craft2 | **tokens 刷新落地（D-004 / D-005）**：MainWindow 显式 `Win32Properties.WindowCornerPreference="Round"`；§5.1 Radius 实物列改 v2.0 阶梯（Sm4/Md6/Lg8/Xl12，Xs2 保留子档）+ Elevation 收两档制（1/2=subtle 7%、4=dialog 18% 原式）+ 新增 Layout 族 17 枚 `Layout*`；壳层侧栏深一档定值——Catppuccin/OneDarkPro/TokyoNight 本深于 Bg0，Dracula/NordDark 纠偏为深档惯例色，亮/暗回退档入 DesignTokens ThemeDictionaries；settings-card 角档改 RadiusLg、按钮/输入框改 RadiusMd（md=6）；§2.1/§3.2 半径引用订正 RadiusLg；§5.2 P5 落 SpaceXxs(2)；侧栏持久化默认宽 200 不动（Core 配置语义+§7 V2 机位同值） |
| v2.0 | 2026-09-14 | 票 01 / ui-craft2 | **主基准切换（D-005 / A-001 / ADR 0067）**：§0 改 MangoDisk 观感级对标主基准表 + 上轮三锚降级为气质参考；§2 重写为胶囊三态目标规范（4px 槽位体系随 D-002 退役，施工在票 03），新增 §2.4 MangoDisk `nav-item` 实测公式对照并**登记张力 T-3**（D-003 主色实心胶囊+反白 vs 基准实物 accent 胶囊+3px×24px 主色 pill+字重 600；hover 前景叙事偏差并入）；§4 E2 按 `md-empty-state` 重校；§5.1 增 MangoDisk 布局 token 基准列；§6 Toast 规划按 vue-sonner 实物配置重校（T2/T3/T6 改值 + 新增 T7 动效档）；§7 V3 验收清单改 MangoDisk 对照项；新增附录 D 观感取证表（gh api 只读取证，GPL-3.0 零拷贝）；附录 A.2 张力表补 T-3 行、T-1 改并入 T-3 口径 |
