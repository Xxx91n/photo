# 票 02 收口报告 — rounded-window-shell-tokens（圆角窗口 + 全局 tokens 刷新）

> 覆盖：D-004, D-005。分支 `ui-craft2/02-rounded-window-shell-tokens`（锚定 ui-craft2/01-visual-standard-v2 之上）。
> 日期：2026-09-14。本机 CI-only：零构建零测试；静态门禁全绿。提交 `vpx` / git sha `ead97bb`。

---

## 0. 结论

票 02 两项主交付全部落地并提交：①`MainWindow.axaml` 显式 `Win32Properties.WindowCornerPreference="Round"`（D-004 / 裁决 §4-1）；②全局 tokens 按 v2.0 刷新——Radius 阶梯、Elevation 两档、Layout 新族 17 枚、侧栏独立 surface 深一档定值与亮/暗回退。验收 checkbox 全项满足（§1 对照表）；通用纪律无违规；全屏加固按裁决 §4-2 判省（§3）。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| 验收项 | 证据 | 判定 |
|---|---|---|
| Round 属性显式在 MainWindow.axaml（守卫测试若写必注释防什么 bug） | `Win32Properties.WindowCornerPreference="Round"` 落 Window 属性组（MainWindow.axaml:21）；新增守卫 Fact `MainWindow_Must_Set_Explicit_WindowCornerPreference_Round`（DesignSystemTests.cs:236），注释写明防 Avalonia #9660 硬边回归；静态断言非「读回 preference」（裁决 §4-3 禁项遵守） | PASS |
| 不动项清单逐项核验未触碰（背景/阴影/标题栏/GridSplitter） | 逐项核验表见 §4——不透明背景（无 TransparencyLevelHint 引入）、DWM 系统阴影（非透明窗路线）、ADR 0050 自绘标题栏结构与 click handler 零改动、GridSplitter 零改动；渲染路径仅 +1 附加属性 | PASS |
| tokens 刷新后壳层与 v2.0 定值一致（对照表入报告） | §5 对照表：Radius 阶梯 sm4/md6/lg8/xl12 全落；Elevation 两档制落；Layout 族 17 枚立；侧栏深一档五主题+回退档齐；按钮/输入框 md=6、卡片 lg=8 | PASS |
| 报告双轨落盘 | 本文件 + `docs/process/reports/02-rounded-window-shell-tokens.md`，SHA256 比对一致（§7） | PASS |
| D-001 承接注（过程验收代理） | 过程代理=守卫失效即红预演成立（§6）+ 壳层 v2.0 定值对照表（§5）；before/after 同机位截图与裁决 §4-3 目检矩阵**登记待用户目检**（CI-only 无明令，不虚构，§8-R2） | PASS（登记） |

## 2. 交付物清单

| 文件 | 动作 | 说明 |
|---|---|---|
| `src/PhotoPrivacy.Ui/Views/MainWindow.axaml` | 修订 | +`Win32Properties.WindowCornerPreference="Round"`；侧栏状态卡 `CornerRadius="8"`→`{DynamicResource RadiusLg}`；两组导航 StackPanel `SpaceXs`→`SpaceXxs`（基准组内项距 2，附录 D.3） |
| `src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml` | 修订 | Radius 阶梯重值；Elevation 两档制重值；新增 Layout 族 17 枚；新增 `SemiColorNavBackground` ThemeDictionaries 亮/暗回退 |
| `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | 修订 | 全局段本票独占：settings-card `RadiusMd`→`RadiusLg`；Button primary/ghost/danger/icon + TextBox.inline-input + PathPicker.inline-input `RadiusSm`→`RadiusMd`；**未碰** nav/nav-action/caption-btn（票 03 领地 / chrome 直角惯例） |
| `src/PhotoPrivacy.Ui/Themes/Dracula.axaml` | 修订 | `SemiColorNavBackground` #2E303E（浅于 Bg0，倒挂）→ #21222C 深档 |
| `src/PhotoPrivacy.Ui/Themes/NordDark.axaml` | 修订 | `SemiColorNavBackground` #3B4252（浅于 Bg0，倒挂）→ #242933 深档 |
| `docs/design/ui-visual-standard.md` | 修订 | §5 注记落地态；§5.1 Radius/Elevation 实物列+Layout 族登记；§2.1/§3.2 半径引用订正 RadiusLg；§5.2 P5 落 SpaceXxs(2)；§8 新增 v2.1 行 |
| `tests/PhotoPrivacy.IntegrationTests/Ui/DesignSystemTests.cs` | 修订 | +1 Fact 守卫（§6 预演记录） |
| `.scratch/ui-craft2/reports/02-report.md` | 新建 | 本文件（主本） |
| `docs/process/reports/02-rounded-window-shell-tokens.md` | 新建 | 双轨副本（§7 逐字一致） |

未触碰：NavButton.axaml、Catppuccin/OneDarkPro/TokyoNight 主题（本已深于 Bg0）、全部页面 axaml（票 04-07 领地）、CONTEXT.md、Core 配置默认链（侧栏 200 持久化语义不动）、`zz` 他票产物（无遗留）。

## 3. 调研（动工前置要求，通用纪律第 1 条）

### 3.1 atomcode 深度调研（串行一次，2026-09-14）

- 执行：`atomcode -p`（壳层 token 落 Avalonia 12/Semi.Avalonia 通行做法四面：侧栏独立 surface 组、BoxShadow 两档定值、圆角阶梯派生惯例、WindowCornerPreference 与自绘标题栏配合）。Sufficiency Gate：12+ 检索（Exa/Tavily/AnySearch）、5 角度覆盖（Official/Comparative/Criticism/Community/Currency）、6 次原文核验（Avalonia Docs×2 / MS Learn / shadcn Theming 全文 / Semi 资源自定义文档 / Avalonia #9660 原文）。
- 核心结论（与本票落地直接相关）：
  - **侧栏独立 token 组胜出于复用主 accent**（shadcn 8 枚 `--sidebar-*` vs M3 container 档位两流派）；落法=`ResourceDictionary.ThemeDictionaries` 语义层，控件不直接吃原始 token——本票 `SemiColorNavBackground` 即该语义层，补亮/暗回退档。
  - **阴影两档 BoxShadow 定值**：subtle 黑 7%（≈#12 alpha）、dialog 黑 18%（≈#2E，基准原式 `0 25px 50px -12px`）；资源类型须 `BoxShadows`（复数）非 x:String；暗色主题黑影不可见，分层由描边+surface 色差承担（Fluent/M3 同惯例）。
  - **圆角阶梯**：shadcn 派生式 `sm=base×0.6/md×0.8/lg×1/xl×1.4` 与本票 base8→4/6/8/12 同流派；Avalonia 无 calc()，派生注释维护；`CornerRadius` 资源可安全 `DynamicResource`（与 Thickness 字面量硬约束不同构）。
  - **不透明 ExtendClientArea 窗口 DWM 圆角不需内容区自绘裁切配合**；DWM 合成层直接裁圆含自绘标题栏；最大化/贴边 by design 自动直角；显式 Round 必需（#9660 实锤）；透明自绘路线永久排除（与本仓 Q4 裁决一致）。
- 决策账本回顾：动工前已全读 decision-ledger D-001~D-007 + A-001~A-005、ADR 0050/0051/0067、CONTEXT.md 相关词条心智模型（Surface Depth 4-Layer / Elevation ladder / Padding Literal Quantization / MangoDisk Benchmark / Rounded Window / Nav Capsule）；工业对标=MangoDisk 本体附录 D + shadcn/M3/Semi/FluentAvalonia 生态先例。**调研结论与账本零冲突，无 revised 项。**
- 信息缺口如实登记：Semi 亮色默认 `SemiColorBackground0` 具体色值未包外核验（回退档取基准暖白向近似值，用户目检复核）；Radix/Semi 色阶明度步长无公开数值（深档取各调色板社区惯例色）。

### 3.2 全屏加固判断（裁决 §4-2）

全仓 grep `FullScreen|WindowState|Fullscreen` 实物：仅 Normal/Minimized/Maximized 消费点（MainWindow.axaml.cs/App.axaml.cs/MainWindowServiceAdapters/TrayHost），**产品无 FullScreen 路径 → 可选加固省略**（Qt QTBUG-147453 先例不适用）。AppTheme `:fullscreen` 伪类规则为既有防御性样式，保留不删。

## 4. 不动项核验（渲染路径零改动逐条）

| 不动项 | 核验 | 判定 |
|---|---|---|
| 不透明背景 | `Background="{DynamicResource SemiColorBackground0}"` 原位；全仓无 TransparencyLevelHint/Transparent 窗口配置引入 | PASS |
| 系统阴影 | 非透明窗路线不变 → DWM 合成阴影完整保留（裁决 §2 矩阵） | PASS |
| ADR 0050 自绘标题栏 | titlebar-host Border/ElementRole/三 caption 按钮/click handler 零 diff | PASS |
| GridSplitter | SidebarSplitter 块零 diff | PASS |

注：本项核验对象是**圆角特性**的渲染路径；tokens 重值（Elevation alpha、Radius 档、侧栏色深）属 issue 需求要点 3 的显式要求，非「不动项」违约。

## 5. tokens 刷新对照表（壳层 × v2.0 定值）

| 项 | 刷新前 | 刷新后（v2.0 基准列） | 出处 |
|---|---|---|---|
| Radius 阶梯 | Xs2/Sm4/Md8/Lg12/Xl16 | Xs2/Sm4/**Md6/Lg8/Xl12**（base8 派生式） | 规范 §5.1 / 附录 D.2 |
| Elevation | 1=8%/2=12%/4=16% 三档投影 | 1,2=subtle 7% / 4=dialog 18%（`0 25 50 -12`）两档制 | 附录 D.2「阴影仅两档」 |
| Layout 族 | 无 | `Layout*` 17 枚定值（侧栏240/68、nav40、页头58、页padding20/14、readable1160/wide1280、toolbar36、结果行44、dialog 440/520/620/720+header68/footer56/body20） | §5.1「票 02 定值」 |
| 侧栏容器色深 | Catppuccin/OneDarkPro/TokyoNight 已深于 Bg0 ✓；**Dracula/Nord 倒挂**；亮色档 key 悬空 | 五主题全深一档 + ThemeDictionaries 亮 #F4F3F1 / 暗 #1B1C21 回退 | 附录 D.1 |
| 标题栏 | SemiColorBackground0（随画布） | 不变（基准=与画布融；DWM 裁圆直接作用） | — |
| 内容宿主 | 透明透出画布（Bg0） | 不变（基准 workspace transparent 同构） | 附录 D.1 |
| settings-card 角 | RadiusMd(8) | RadiusLg(8)——语义归基准 lg 档 | 附录 D.2 卡片 |
| 按钮/输入框角 | RadiusSm(4) | RadiusMd(6)——shadcn button/input=rounded-md | 附录 D.2 |
| 侧栏状态卡 | 字面量 8 | `{DynamicResource RadiusLg}` | token 化顺手 |
| nav 组内项距 | SpaceXs(4) | SpaceXxs(2)（基准组内 2；组间 10-12 由 Dock 分区承担非相邻组距） | 附录 D.3 / §5.2 P5 |
| 侧栏默认宽 | 200（XAML+Core 持久化默认） | **不动 200**——Core UiOptions/ConfigEditCommand 默认链属配置语义，且规范 §7 V2 机位钉 200；LayoutSidebarWidth=240 为基准登记档 | §7 V2 / §8-R3 |

## 6. 静态门禁（CI-only 口径）

| 门禁 | 结果 |
|---|---|
| XAML 标签平衡（MainWindow/DesignTokens/AppTheme/Dracula/NordDark） | 5/5 leftover 0 errs 0 |
| 无 BOM / 纯 LF（全部触碰文件含本报告） | 逐文件核验 BOM=false CRLF=0 |
| 既有断言面复核（DesignSystemTests/NavFeedbackSourceTests/PagesVisualAlignmentSourceTests 涉及 token key/Elevation 消费/nav RadiusSm/caption-btn/Spacing DynamicResource） | 26/26 静态核验通过；nav/nav-action RadiusSm 保留=票 03 领地未碰；无 Margin/Padding/Spacing 字面量新增 |
| 守卫失效即红预演 | 新 Fact 断言串 `Win32Properties.WindowCornerPreference="Round"`：在位→PASS；模拟删除→断言串缺失→必红（正反双对照成立） |
| 新增断言 | +1（`MainWindow_Must_Set_Explicit_WindowCornerPreference_Round`），注释写明防 Avalonia #9660 硬边回归（TEST-CONVENTIONS R1） |
| 撞红预演（D-006 三档） | 既有守卫零撞红：三档计数 杀0/改造0/保留0 |

## 7. 双轨一致性核验

- 主本：`.scratch/ui-craft2/reports/02-report.md`
- 副本：`docs/process/reports/02-rounded-window-shell-tokens.md`
- SHA256 逐字节比对：一致（字节数见 docs 侧 git diff）。

## 8. 张力与呈报

- **R1（登记非阻塞）**：T-3 张力（D-003 主色实心胶囊 vs MangoDisk 实物 accent 胶囊+3px pill+字重600）票 03 施工前须裁定——本票未碰 nav 样式段，仅维护性订正规范 §2.1 半径引用（RadiusMd→RadiusLg，值 8 不变），对 T-3 零立场。
- **R2（待用户目检）**：裁决 §4-3 目检矩阵（Win11 22H2/24H2/25H2 ×{普通/最大化/贴边/125%DPI/跨屏}+Win10 直角冒烟+macOS/Linux 无变化）与 §7 V3 MangoDisk 对照清单均**待用户实机目检**——本机 CI-only 无用户明令硬验收，不虚构执行记录。首帧直角回退预案（code-behind Opened 设一行）备而未用。
- **R3（呈报大脑）**：侧栏默认宽基准 240 vs 本仓持久化默认 200——改默认需动 Core `UiOptions`（200.0）/`ConfigEditCommand` 配置语义链，超本票「tokens 刷新」面；规范 §7 V2 机位亦钉 200。已立 `LayoutSidebarWidth=240` 基准登记档，是否改默认交由大脑/后续票裁定。
- **R4（观察项）**：settings-card 背景 `SemiColorBackground1` 在五主题中方向不一（3 深 2 浅）vs 基准 dark 卡片微升层（oklch0.225>0.18）；统一升层需 per-theme 色值精调，本票未动——建议页面票实机目检后裁定是否升 `SemiColorBackground2` 或精调。
- **R5（观察项）**：`SemiColorNavBackground` 亮色/无预设暗档原态为 key 悬空（回落窗口底色），本票补回退档属「从无到有」；亮色 #F4F3F1 为基准暖白向近似（Semi 亮色 Bg0 未包外核验），目检时复核。
- **R6（过程呈报）**：任务书必读清单第 3 项为字面 `undefined`（模板占位残留），按无文件处理已记录；建议大脑修模板。
- **R7（教训登记）**：AppTheme 编辑中一次全局 replace 误中 Button.primary（先被循环改成 RadiusMd 后成首个锚点），已按块定位修正并核验全 selector 绑定表——教训：同值 setter 场景 replace 首命中风险，须 selector 级定位。

## 9. 完成定义自证

- [x] issue 验收 checkbox 4/4 + D-001 承接注（§1）
- [x] 通用纪律 6 条：atomcode 调研先行且结论入 §3 ✓；CI-only 零构建零测试 ✓；版本控制 but + 一票一分支（`ui-craft2/02-rounded-window-shell-tokens` 锚票 01 之上）✓——无动栈类操作未触发 §4.4 快照前置；中文交互 ✓；报告双轨逐字一致 ✓（§7）；无静默改向（R1/R3/R4 登记呈报）✓
- [x] 改动清单先行声明（会话内呈报：文件/动作/共享面三列，施工与声明一致）
- [x] 共享文件触碰面：AppTheme 全局段本票独占期内完成；nav/nav-action/caption-btn 未碰（票 03）；页面 axaml/NavButton.axaml 未碰（票 03-07）
- [x] 新断言注释防什么 bug（R1）；XAML 断言未走 ReadStripped（A-004 注）
