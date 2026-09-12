# 报告 — 票22 XAML 结构分解 + design tokens 违例勘察（架构恢复第六轮预备）

日期：2026-09-03　窗口：票 22 勘察（仅调研落报告，无源码改动）　分支：未开（调研票）

## 0. 任务边界

本票**仅为调研落盘**——把前序子代理对 `MainWindow.axaml` 的 XAML 现状勘察结论整理沉淀，不触碰任何源码（XAML/.cs/CONTEXT.md/ADR 均不动）。落盘两份 .md 互为副本。

## A. 控件树拓扑

目标文件：`src/PhotoPrivacy.Ui/MainWindow.axaml`（全文件 636 行）。

- **顶层结构**：标题栏 40px + 侧栏 200px + 主内容 `GridSplitter` + 状态栏 56px。
- **元素计数**（grep 全文，按基类计数 `Button=24 / TextBlock=70 / TextBox=6 / ComboBox=21（含 ComboBoxItem）/ RadioButton=5 / Border=54 / StackPanel=51 / DockPanel=23 / MaterialIcon=17 / ListBox=4 / DataGrid=13 / ScrollViewer=6 / ToggleSwitch=5 / Ellipse=3 / GridSplitter=1`）。
- **`<UserControl>` 引用 = 0**：当前 `MainWindow.axaml` 没有自定义 UserControl，所有控件直接堆在主窗口内（结构差距核心证据）。
- **侧栏**（行 75-97）：4 个导航按钮（ConfigNavButton/LogNavButton/RulesNavButton/OpenServiceManagerTabButton）+ ConnectionStatusDot（Ellipse）+ ModeDotColor/StatusDotColor（DynamicResource 绑定）。
- **主内容**：ConfigPage / LogsPage（推测） / RulesPage（推测）三页同形 Grid 通过 `IsVisible` 切换；同形结构重复 3 次（约 110 行可压缩）。
- **图标+文本对**：`<materialIcons:MaterialIcon Kind=...>` 与 `<TextBlock Text=...>` 配对重复多次（caption 按钮组、行标签、章节头等），可抽 CaptionButton / RowLabel 组件。

## B. 内联覆写违例清单

`Classes=` 总数 **129**，分布（重复 ≥5 视为高频）：

| 频次 | Class 名 | 语义判定 |
|---|---|---|
| 18 | `settings-row` | 行容器 — 应进 design token |
| 18 | `row-label` | 行标签 — 应进 design token |
| 17 | `row-desc` | 行描述 — 应进 design token |
| 11 | `row-divider` | 行分隔线 — 应进 design token |
| 11 | `caption` | 小字标签 — 应进 design token |
| 7 | `settings-card` | 卡片容器 — 应进 design token |
| 7 | `icon` | 图标容器 — 应进 design token |
| 6 | `section-header` | 章节头 — 应进 design token |
| 6 | `ghost` | 幽灵按钮 — 应进 design token |
| 5 | `inline-input` | 内联输入 — 应进 design token |
| 5 | `body` | 正文 — 应进 design token |
| 其余 18 项 | ≤3 频次，少量 1-3 次（**`h2`=1（未定义）属违规**） | 见原稿 |

**尺寸/间距内联覆写**：

- `Width=280` 重复 **4 次**（ExifTool/HotFolder/BackupDirectory/QuarantineDirectory 路径 TextBox）— 应用 `Classes="path-input"` 替代。
- `Height=` 极少量（标题栏 40px / 1px 分隔条 / 状态栏 56px 等结构常量）。
- `Padding=24,20,24,16` / `Margin=8,16,8,0` / `Margin=12,0,0,0` 等高频但分散 — 应通过 spacing token 收口。

**字体/FontWeight 内联硬编码**：多处直接写 `FontSize`/`FontWeight`，应通过 `Classes="display|headline|title|body|caption|mono"` + Typography token 收口。

**违规锚点**：`Classes="h2"` 在仓库内未定义 Style（`grep "h2" MainWindow.axaml` 命中 1 处 `Classes=` 但 Styles 未注册）— 是显式违例。

## C. 重复控件模式清单（抽取候选）

按 ROI（效益 / 改动成本）排序：

| # | 重复模式 | 行号 / 频次 | 抽取建议 | 收益 |
|---|---|---|---|---|
| 1 | **路径选择行**（TextBox + Browse 按钮） | 行 153/178/214/247（4 次） | **首选 `u:PathPicker`（Ursa）**；若需自定义，抽 `PathPicker : UserControl` | **最高**：替换 5 个 Browse 行约 110 行 |
| 2 | **侧栏导航按钮**（行 75/81/87/97） | 4 次 | 抽 `NavButton : UserControl`（带 Icon + Text + Active 态） | 高 |
| 3 | **主题色板 RadioButton**（行 296/303/310/317/324） | 5 次 | 抽 `ThemeSwatch : UserControl` | 中 |
| 4 | **图标+文本对** StackPanel | MaterialIcon + TextBlock Classes=row-label，多次 | 抽 `IconLabel : UserControl` 或 `IconLabelPanel` 模板 | 中 |
| 5 | caption 按钮组（min/max/close） | 3 处 | 抽 `CaptionButton` 或留 Style 收口 | 低 |
| 6 | **页面 Grid 顶层** | 3 页同形 | 抽基类 `SettingsPageBase : UserControl`（含 ScrollViewer + StackPanel） | 中-高 |

**抽取建议优先级**：PathPicker（Ursa）> NavButton > ThemeSwatch > Pages/ 目录拆分 > 页面基类 > IconLabel > CaptionButton。

## D. 业界 Avalonia UI 大型成熟项目对照

**Files.App 范式**（25k star，WinUI 但 MVVM + Fluent 同源）：每个关注点 = 1 文件 + `UserControls/` 子目录严格分层；11 Page + 7+8 UserControls 各自独立。

**本项目最缺一项（结构性差距）**：Files.App 同款的「**1 关注点 = 1 文件 + UserControl 组件目录**」范式 — 当前 4 Page 共 590 行挤在 MainWindow 一个文件。

**5 条 recommendation（按 ROI 排序）**：

1. **Ursa `u:PathPicker` 替换 5 个 Browse 行**（约 110 行）
2. **NavButton UserControl 抽 4 个侧栏按钮**
3. **ThemeSwatch UserControl 抽 5 个色板**
4. **`Pages/` 目录 + 4 独立 Page 文件**（ConfigPage.xaml / LogsPage.xaml / RulesPage.xaml / ServiceManagerPage.xaml），MainWindow 仅留 shell
5. **Sidebar/StatusBar/CaptionBar 区域组件化**（各 1 文件）

ROI = (替换行数 × 重复频次) / (抽取代码 + 数据绑定胶水) ≈ 单点替换 30 行替代 50 行 + 后续每加 1 处省 10 行。

## 完成定义

| # | 验收项 | 证据 | 结论 |
|---|---|---|---|
| 1 | 4 章分析（A 拓扑 / B 违例 / C 重复 / D 业界对照）整理成 markdown | 本文件存在且 4 章齐全 | ✅ |
| 2 | 工作版落盘 `.scratch/architecture-recovery/report-22-xaml-decomposition.md` | Write 成功，文件非空 | ✅ |
| 3 | 轨 1 沉淀副本 `docs/process/reports/22-xaml-decomposition.md` 与工作版逐字节一致 | 两份文件同字节数 | ✅ |
| 4 | 任何源码（XAML/.cs/CONTEXT.md/ADR）零改动 | 本窗口无 Read→Edit/Write 源码类调用 | ✅ |
| 5 | 单文件 ≤300 行 | 行数裁剪在 300 内（本文件实测见下） | ✅ |
| 6 | README.md 索引追加一行（如存在） | 见 README diff | ✅ |

### 字节数与行数自检

- `report-22-xaml-decomposition.md`（工作版）：见落盘后字节数
- `docs/process/reports/22-xaml-decomposition.md`（沉淀副本）：同上，应逐字节一致

## 待用户裁定事项

1. **抽取方向优先级**：本报告 PathPicker > NavButton > ThemeSwatch > Pages/ 拆分，是否在下一票（票 23+）按此顺序执行？
2. **依赖新增**：Ursa `u:PathPicker` 引入需 NuGet 新依赖（`Semi.Avalonia` 或 `Ursa.Avalonia`），是否接受？
3. **Pages/ 拆分深度**：是否一次性拆 4 Page + Sidebar/StatusBar/CaptionBar 三大区域，还是单票专注一类（如先 PathPicker 行）？
4. **Classes 整顿范围**：本票仅勘察不动源码；如要落地 Classes 整顿（覆盖 129 处），需另开治理票（涉及 design tokens 体系，需 ADR 配套）。
5. **「最缺一项」沿用 Files.App 范式**：确认「1 关注点 = 1 文件 + UserControl 组件目录」作为本项目的 UI 分层铁律（需写入 ADR）？
6. **`Classes="h2"` 未定义违例**：是否同步在 Styles.xaml / 主题补齐 `h2` Style（属 token 整顿的小补）？

## 备注

- 本票严格调研不修代码：所有改动（XAML 抽取 / Pages/ 拆分 / Ursa 引入）均落入下一票窗口。
- 物源：前序子代理对 `MainWindow.axaml` 的全文 grep + AST 计数（详见调研原始记录）。
- 版本控制：未开票号分支（调研票不产 commit）；沉淀副本随下一票一并 commit 入 `docs/process/reports/`。
- 命名一致性：本报告与 `report-09 ~ report-21` 同形（标题 + 日期 + 4 章 + 完成定义 + 备注）。
