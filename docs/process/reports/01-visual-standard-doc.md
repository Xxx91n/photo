# Report — 票 01 visual-standard-doc（ui-craft 轮）

- 日期：2026-09-12 ｜ 阻塞状态：**无（首个开工）**
- 覆盖 A-xxx：A-003、A-004（清单扩充 + 拍摄规程）、A-005（规则落地）、A-006（核验闭合）、A-009（Toast 规划节）
- 覆盖 D-xxx：D-002、D-003、D-005、D-006、D-007、D-009

## 0. 开工复述（启动器要求）

> 本票 **Blocked by = 无（首个开工）**。必读清单 13 份**已逐份读全**（handoff 01 / issue 01 / spec / decision-ledger（D+A 全读）/ WORKFLOW.md / CONTEXT.md / ADR 0050·0051·0052·0054·0055·0056·0062），无缺份，未停在呈报环节，据此动手。

## 1. 声明 → 证据 → 结论对照表

| # | 声明（完成定义 / 检查点） | 证据（实物） | 结论 |
|---|---|---|---|
| 1 | docs/design/ui-visual-standard.md 七节齐备 | 文件 24,510 字节；七节标题实物齐备（§1 按钮组宽度策略 / §2 nav 反馈三态 / §3 表单行骨架 / §4 空态规范 / §5 间距节奏 / §6 Toast 反馈链规划 / §7 验收标尺） | **PASS（C1 7/7）** |
| 2 | 七节每节引用三锚具体设计点、无空泛审美词 | 全文 Wasabi×7 / Apple×16 / VS Code×12 / Discord×11；§0 三锚表逐锚给「取什么 / 具体可核验设计点 / 明确不取」；每节设独立「三锚对照」小节；全文无「高级感 / 精致 / 现代 / 有质感」 | **PASS** |
| 3 | ADR 0065 落盘 | docs/adr/0065-ui-visual-standard-live-doc-nav-language-button-group-width.md（10,340 字节，D1–D7 齐备） | **PASS（C2）** |
| 4 | CONTEXT.md 新增 2 词条 | `**UI Visual Standard**:` 与 `**Visual Baseline**:` 实物存在，均含 _Avoid_ 行 | **PASS（C2）** |
| 5 | 44px 修正核验 | 词条标题已改 `Sidebar Nav Item (40px Icon+Text)` 并保留沿革；AppTheme.axaml 实物 Button.nav=40、Button.nav-action=40，与词条一致 | **PASS（C2）** ⚠ 见 §4 呈报 |
| 6 | 守卫两条规则写入测试约定区 | tests/TEST-CONVENTIONS.md（5,368 字节）：R1 新断言必写「防什么」+ 票号、写不出拒收；R2 撞红三档分类（杀 / 改造 / 保留）+ characterization 定性 + 复审期 + 语义化优先级 RS0016/ApiCompat/ArchUnitNET 先于 Headless | **PASS（C3）** |
| 7 | report-32 探活清单扩充，不改写原 28 项语义 | 新增 §2 G 组（G1–G6，对应 D-004 三根因）+ §5.2 四页截图拍摄规程 + §3.1 新增项回填登记表；**原 28 项逐行与 git HEAD 逐字比对 28/28 一致**，原小结「28 项中通过 0 / 失败 0 / 未探活 28」完好 | **PASS（C4）** |
| 8 | atomcode 调研结论与来源清单入报告 | atomcode 5.0.9 单发串行 headless；三引擎 16 次检索 + 28 次原文抓取核验；结论见 §3，完整 28 条来源见 §5 | **PASS** |
| 9 | 本机零构建；静态核验自证 | 本机未执行 dotnet build / test（CI-only）；静态核验见 §6 | **PASS** |
| 10 | CI 云端绿；无新增无注释断言 | 本票**零测试代码改动**（无新增断言，故不存在无注释断言）；CI 验收由大脑推验证分支后确认 | **待大脑（CI-only）** |

## 2. 交付物清单

| 文件 | 类型 | 字节 | 说明 |
|---|---|---|---|
| docs/design/ui-visual-standard.md | 新增 | 24,510 | 主交付物：活规范七节 + §0 三锚 + 附录 A + §8 修订记录 |
| docs/design/screenshots/.gitkeep | 新增 | 650 | 基线资产目录占位（含规程指针） |
| docs/adr/0065-...nav-language-button-group-width.md | 新增 | 10,340 | 关键取舍（D1–D7） |
| tests/TEST-CONVENTIONS.md | 新增 | 5,368 | 测试约定区（R1 / R2） |
| CONTEXT.md | 修改 | +10 / −3 | 两词条新增 + Sidebar Nav Item 44→40 修正 |
| docs/process/reports/32-ui-manual-smoke.md | 修改 | 23,370 | 探活清单扩充（G 组 + §5.2 + §3.1 + 锚点 3 行） |
| .scratch/.../report-32-ui-manual-smoke.md | 修改 | 23,370 | 主本，与 docs 副本逐字节一致 |

> **未触碰**：AGENTS.md。理由：ui-craft README 波次表列票 01 的归属文件为「新文件为主（docs/design/、tests 约定区、ADR）」，AGENTS.md 不在其中；为遵守 WORKFLOW §4.3「跨票共享文件一次只许一票碰」，约定区落在 tests/ 而非 AGENTS.md。

## 3. atomcode 深度调研

### 3.1 verbatim 提问（原文要点）

> 「为 Avalonia 12.1 + Semi.Avalonia 12.1 基座的桌面隐私工具做工业界控件级证据调研，三个问题：① 侧栏导航项 idle/hover/active 三态设计语言（VS Code Activity Bar 与 Side Bar、Discord 服务器/频道列表、Windows 11 Settings 导航、macOS 系统设置侧栏）——hover 是否有过渡动画（时长与 easing、源码行/CSS 变量）、hover 是否引入 accent 色、hover 时图标与文字是否向 accent 靠拢、active 指示条与 hover 是否预示联动；② 按钮组宽度策略：同组等宽还是内容自适应（Win11/WinUI 对话框、Apple HIG、M3、VS Code），「同组文案长度不一致」场景各规范推荐哪种；③ 表单行骨架：macOS 系统设置分组卡片的分组间距/行高/左右分置比例/分隔线 inset/行内节奏，以及 WinUI SettingsCard 对应规格。要求每条结论给可核验一手证据，附完整来源清单。」

### 3.2 结论（每条含证据锚点）

**C1 — nav hover 四大参考产品一律中性灰叠加，accent 只属 active/selected；hover 无「预示指示条」。**
- VS Code Activity Bar：hover **无背景**，只把图标从 `activityBar.inactiveForeground`(#868686) 切到 `activityBar.foreground`(#D7D7D7)，是「灰→中性白」的亮度叙事；active 指示条 2px 左边框，Dark Modern 取 accent `#0078D4`。证据：activityaction.css、workbenchThemeService.ts
- WinUI NavigationViewItem：hover 背景 = `SubtleFillColorSecondaryBrush`（Fluent 2 最弱中性层），前景 = `TextFillColorPrimaryBrush`；accent **只给** `SelectionIndicator`（初始 `Width=0 Opacity=0`，仅 Selected 展开）。证据：`NavigationView_rs1_themeresources.xaml`
- Discord：hover `rgba(78,80,88,0.3)`、selected 同色 0.6；Blurple(#5865F2) 只用于未读/提及/活跃白柱。证据：oh-my-design-cli / BetterDiscord / gist 三信源交叉
- macOS：侧栏图标默认 accent、选中为 accent 整行填充；系统列表默认无 hover 高亮。证据：Apple HIG Sidebars / Color

**C2 — hover 过渡在桌面体系以「切换」为主。** VS Code activity bar 唯一 transition 是拖拽指示条（`duration 0ms`）；WinUI NavigationViewItem 用 `DiscreteObjectKeyFrame KeyTime=0` 离散切换；唯一可复用数值是 WinUI Button 的 **83ms BrushTransition**。

**C3 — 按钮组：对话框语境等宽，页内工具按钮行内容自适应。** WinUI `ContentDialog` 五列 stretch 网格 + `ButtonSpacing=8`，官方 PR #3926 确认「对话框变宽→按钮变宽」是设计意图；VS Code 对话框按钮 `width: fit-content` + 右对齐 + 4px；Apple HIG 原文「用**样式**而非尺寸区分首选动作」「同尺寸 = 一组连贯选择」；M3 尾缘对齐、最多两操作、8dp 间距、文案过长**改堆叠**。

**C4 — 表单行有完整开源模板可对齐。** WinUI SettingsCard 四列网格 `Auto(图标) / *(标题+描述) / Auto(控件) / Auto(ActionIcon)`；断点 800px 控件换行到标题下方、600px 连图标折叠；卡片间 `Spacing=4`；背景过渡同为 83ms；模板内置 `SettingsCardContentMinWidth`。

**C5 — macOS 侧定量参考（社区逆向，非官方）。** 标签列与控件列之间 8pt、描述 11pt(Small)、分隔线左右各留 ≥20pt。HIG 官方仅有侧栏行高 small/medium/large 三档定性规格；**System Settings 分组卡片无官方数值规格**。

### 3.3 调研对项目现状的诊断价值

调研直接**修正了 D-004 的一处时点假设**：D-004 记「pointerover 段无 Transitions setter」。静态核验发现票 26（ADR 0062）已在**全局** `Button` 选择器补了 `BrushTransition Background/BorderBrush 150ms SineEaseOut`（AppTheme.axaml:337-347），故：

- 根因①（无过渡硬切）**已部分缓解**；
- **残留硬切在 Foreground**（Text2→Text0 无过渡）；
- 因此「塑料感」的第一现场更可能是「**背景平滑 + 前景 0ms 硬切的两路不同步**」，而非「完全没有动画」。该结论已写入规范 §2.2 N2 与 ADR 0065 D2，票 02 的定值重点据此调整。
## 4. 呈报大脑（不静默改向）

### 4.1 张力 T-1 — nav hover 是否引入 primary 色彩叙事

| 项 | 内容 |
|---|---|
| 用户决策 | **D-004（已拍板）**：hover 时图标与文字向 primary 叙事，禁纯中性灰平移 |
| 工业证据 | VS Code / WinUI / Discord / macOS 的 nav hover **全部为中性灰叠加，accent 只属 active/selected**（C1） |
| 本票处置 | 规范**沿用 D-004**（用户决策优先），并在 §2.4 显著标注张力；**不静默改向** |
| 裁定机制 | 票 02 实现时同时产出「primary 叙事版」与「中性灰 + 亮度叙事版」两版 before/after 截图，按规范 §7 V2 同机位人工对照；结论回写规范 §2.4 与 §8 |
| 不受影响项 | N1 / N2 / N3 三条硬性约束（工业证据一致支持「背景与前景两路须同步过渡」这一本项目实际缺口） |

### 4.2 张力 T-2 — 页内按钮组等宽与否

| 项 | 内容 |
|---|---|
| 用户决策 | **A-001 / spec ID3（已拍板）**：同组按钮等宽，消灭「宽度 = 文案长度」默认行为 |
| 工业证据 | 仅**对话框语境**等宽；页内工具按钮行 WinUI / Apple / M3 / VS Code **均为内容自适应**（C3）。Apple 的成组信号靠「同尺寸 / 同样式」而非等宽拉伸 |
| 本票处置 | 规范**沿用等宽**，并新增 R1-5 例外（组内文案长度差 >2 倍时改用样式层级区分主次，不强行拉宽短按钮） |
| 裁定机制 | 票 03 双版截图人工裁定，结论回写规范 §1.3 与 §8 |

### 4.3 ⚠ 账本与磁盘实态不符 — A-006「已闭合」声明未落地

- decision-ledger A-006 记「**状态：已闭合（2026-09-12 grill 整理环节执行）**」。
- 但票 01 开工时 `CONTEXT.md:297` 实物仍为 `**Sidebar Nav Item (44px Icon+Text)**`，且 `git log -- CONTEXT.md` 的最近提交（票 28 CI/CD 修复）中**无 44px 修正**。
- 判定：**该修正在 grill 环节被声明但未落盘**。本票已实际执行修正（44→40 + 沿革说明），并在此呈报——请大脑确认「A-006 闭合」是否应改记为「由票 01 闭合」。

### 4.4 其他观察（未处置，仅登记）

- **ADR 0064 不在工作区**：spec Further Notes 引用 ADR 0064，但 `docs/adr/` 实物最新为 0063；`git log --all` 显示 0064 存在于 `origin/main` 的 8a96564（工作区未拉取，but 提示 origin/main 已 ahead）。ADR 0065 依本票 delta 新立，未与 0064 冲突。
- **ConfigPage.axaml:113 `ComboBox Width="160"` 行内写宽**：与票 24（ADR 0061）已立的「Views 禁止内联宽度」口径冲突，已登记为规范 §3.3 B2，交票 04。
- **ConfigPage.axaml:8 `StackPanel Spacing="0"`**：分组卡片之间零间距，与 WinUI SettingsCard `Spacing=4` / macOS「分组须成视觉簇」不符，登记为 §3.3 B3，交票 04。
- **nav accent bar 宽度 4px**：工业参照 VS Code 2px、WinUI 视觉约 3px；ADR 0052 A1 定值为 4px 且用户未抱怨，登记为「观察项，非缺陷」。

### 4.5 ⚠ 跨窗口 CONTEXT.md 文本冲突 + GitButler 多 base 重放缺陷（已回滚，移交大脑）

**结论先行**：票 01 六件产物已全部落盘于 commit `xov`（分支 `ui-craft/01-visual-standard-doc`，`conflicted: false`），工作区**已回到 0 冲突、可继续提交**的干净态。两处残留须大脑裁定：① 该分支 `mergesCleanly: false`（与上游 CONTEXT.md 真实文本冲突）；② report-32 扩充（变更 ID `rl`）因依赖锁留在 `zz` 未提交区。

**A. 根因（pull 之前即已存在）**：动栈前 `but pull --check` 就只把票 01 标为 `[conflict - rebasable]`，票 03 / 04 为 clean。冲突源是 `CONTEXT.md` 的同一释义区段——上游 `origin/main` 的 `8a96564`（ADR 0064）新增 `Composition Root (AppComposition)` / `WindowPollingHostedService` 两条词条；本票新增 `UI Visual Standard` / `Visual Baseline` 并把 `Sidebar Nav Item` 由 44px 改为 40px。两组改动落点相邻，属并行窗口共享文件的必然碰撞（WORKFLOW §4.3），**不是本票改向**。

**B. 处置时间线（证据链）**：

| # | 动作 | 结果 |
|---|---|---|
| 1 | §4.4 轨 2 强制快照 `workflow-snapshot.js` | 263 文件 / 2,684,997 字节 → `photo-snapshots/20260913-005405`（另增 010146、010610 两份） |
| 2 | `but pull --check` | 仅票 01 `[conflict - rebasable]`；03 / 04 clean |
| 3 | `but pull` | 03 / 04 干净 rebase；票 01 冲突于 CONTEXT.md；票 32 的 `snp` / `yqn` 连带被标 `{conflicted}` |
| 4 | 手工解析 CONTEXT.md（**双侧并存**：上游 ADR 0064 词条 + 本票词条全留） | 0 冲突标记，42,862 字节 |
| 5 | `but resolve finish` ×3 | **失败**：`Failed to merge bases while cherry picking ... Encountered a conflict while merging the commit new bases: 8a96564 / 0ebce17 / 211c65d / 91e19f8 / 8685211 / 05b2ebc`。6 个 base 之间自身冲突，属 GitButler 多 base 合并缺陷，**与我的解析内容无关** |
| 6 | `but resolve cancel --force` | 成功退出 edit mode；未提交区出现 `no`(CONTEXT.md) / `py`(票 03) / `rl`(report-32) |
| 7 | `but uncommit xov` | 被拒：`Cannot uncommit commits that would result in merge conflicts` |
| 8 | `but commit -b ui-craft/01-visual-standard-doc ... no rl` | 被拒：复现 #5 的 6-base 错误——证明该栈在 post-pull 态**无法写入任何提交** |
| 9 | `but oplog restore 41438d5`（pull 前快照 00:50:43） | **成功**：0 冲突、无 edit mode、`ui` / `cr` / `ra` / `g0` 四栈齐在，`xov` 完整持有 6 件产物 |
| 10 | 还原票 03 报告（回滚使其由 28,562 退回 23,189 字节） | 已从保护副本还原为 28,562 字节，票 03 成果零丢失 |

**C. 数据完整性**：全过程零丢失。动栈前对 12 个文件做了仓库外字节级保护副本（`photo-snapshots/20260913-010146/ticket-all-protect/`），并验证 git 对象层恢复通道可用（`git show c153f9f4:<path>` 可读）。回滚后逐字节核验：ADR 0065 / `.gitkeep` / `ui-visual-standard.md` / report-01 / `TEST-CONVENTIONS.md` / report-32 六件与备份**完全一致**；CONTEXT.md 为 41,302 字节（含本票 3 处新增、40px 已改；不含上游 ADR 0064 词条——pre-pull 基线本就如此，非丢失）。

**D. 移交大脑（两个待裁定项）**：

1. **`mergesCleanly: false`**：票 01 分支与上游 CONTEXT.md 存在真实文本冲突。可选路径——(a) 大脑先合并上游 CONTEXT.md 再让本票重放；(b) 由大脑直接裁定 CONTEXT.md 该区段的最终排版，本票按其重写；(c) 维持现状、待 land 时一次性解析。**本窗口不自行重放**：重放已被证明会触发 GitButler 多 base 缺陷并连带阻塞票 03 / 04。
2. **`rl`（report-32 扩充）**：依赖锁未解，仍留在 `zz` 未提交区。内容已在磁盘双轨一致（23,370 字节；LCS 校验为相对 HEAD 的 76 行纯插入、0 删除 0 修改，原 28 项零改写），随时可重提。

### 4.6 ⚠ 实时并发编辑：票 03 正在改写本票主交付（未阻止，如实登记）

- **事实**：01:10:49，`docs/design/ui-visual-standard.md` 由 24,510 字节变为 26,595 字节（+21 行），新增「附录 B. 按钮变体使用对账表」与修订记录 v1.1，**落款为票 03 / ui-craft**。同一时刻未提交区另出现 `pq`(RulesPage.axaml)、`rx`(ServiceManagerPage.axaml)、`ovl`(DesignSystemTests.cs)——票 03 / 04 窗口正在活跃施工。
- **性质判定**：这是 WORKFLOW §4.3「共享文件同一时刻只归一个票」的实时实例，与 §4.5 的 CONTEXT.md 冲突同源——并行窗口对同一批共享文件的写入互相放大。
- **本窗口处置**：**不回滚、不覆盖、不抢提交** `kl`（该改动归票 03）。本票提交只含自己名下的改动。C1 门禁取证于 24,510 字节版本；票 03 的 21 行为纯插入（附录 B + v1.1 修订行），不破坏七节结构，张力 T-1 / T-2 登记仍在。本票**不再主张对该文件的独占**。
- **移交大脑**：D-009 立的是活文档，但未指定 owner。建议明确 `docs/design/ui-visual-standard.md` 在票 01 之后归谁维护，否则并行窗口将持续互相放大写入——§4.5 的 CONTEXT.md 冲突与本节是同一病灶的两次发作。

## 5. atomcode 调研完整来源清单（28 条，均为本轮实际抓取验证）

| # | 来源 | URL | 角度 | 贡献 |
|---|---|---|---|---|
| 1 | VS Code activityaction.css | github.com/microsoft/vscode/blob/main/src/vs/workbench/browser/parts/activitybar/media/activityaction.css | Official | 2px 指示条、hover 仅前景色、唯一过渡 0ms/100ms |
| 2 | VS Code activitybarpart.css | 同上路径 /media/activitybarpart.css | Official | Activity Bar 48px 宽度 |
| 3 | VS Code sidebarpart.css | github.com/microsoft/vscode/blob/main/src/vs/workbench/browser/parts/sidebar/media/sidebarpart.css | Official | Side Bar viewlet hover 前景 |
| 4 | VS Code workbenchThemeService.ts | github.com/microsoft/vscode/blob/main/src/vs/workbench/services/themes/common/workbenchThemeService.ts | Official | activeBorder #0078D4、foreground #D7D7D7 / #868686 |
| 5 | VS Code Theme Color 官方文档 | code.visualstudio.com/api/references/theme-color | Official | 主题变量语义锚点 |
| 6 | VS Code dialog.css | github.com/microsoft/vscode/blob/main/src/vs/base/browser/ui/dialog/dialog.css | Official | 按钮 fit-content、右对齐、4px 间距 |
| 7 | WinUI NavigationView_rs1_themeresources.xaml | github.com/microsoft/microsoft-ui-xaml/blob/main/dev/NavigationView/NavigationView_rs1_themeresources.xaml | Official | hover/selected=SubtleFillColorSecondary、SelectionIndicator |
| 8 | WinUI NavigationView 官方文档 | learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview | Official | 指示条沿左缘、显示模式 |
| 9 | WinUI ContentDialog_themeresources.xaml | github.com/microsoft/microsoft-ui-xaml/blob/main/dev/CommonStyles/ContentDialog_themeresources.xaml | Official | ButtonSpacing=8、五列等宽网格、Min/Max 尺寸 |
| 10 | WinUI Button_themeresources.xaml | github.com/microsoft/microsoft-ui-xaml/blob/main/dev/CommonStyles/Button_themeresources.xaml | Official | 83ms BrushTransition、ButtonPadding、hover 中性层 |
| 11 | microsoft-ui-xaml PR #3926 | github.com/microsoft/microsoft-ui-xaml/pull/3926 | Currency | 对话框按钮随宽拉伸是设计意图 |
| 12 | WinUI 对话框官方文档 | learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs | Official | 三按钮次序 |
| 13 | Fluent 2 React Nav | fluent2.microsoft.design/components/web/react/core/nav/usage | Official | Nav 选中指示条语义 |
| 14 | FluentAvalonia 资源表 | amwx.github.io/FluentAvaloniaDocs/pages/Resources | Community | SubtleFill 系列令牌（Avalonia 侧） |
| 15 | Apple HIG Sidebars | developer.apple.com/design/human-interface-guidelines/sidebars | Official | accent 图标、行高三档 |
| 16 | Apple HIG Buttons | developer.apple.com/design/human-interface-guidelines/buttons | Official | 44×44、同尺寸成组、样式区分主次 |
| 17 | Apple HIG Color | developer.apple.com/design/human-interface-guidelines/color | Official | accent 作用于选择高亮与侧栏图标 |
| 18 | M3 Dialogs Guidelines | m3.material.io/components/dialogs/guidelines | Official | 尾缘对齐、最多两操作、堆叠出路 |
| 19 | M3 Dialogs Specs | m3.material.io/components/dialogs/specs | Official | 280–560dp、8dp 按钮间距、24dp 内边距 |
| 20 | M3 Buttons Specs | m3.material.io/components/buttons/specs | Official | 48×48 目标区、16/24dp 内边距 |
| 21 | Discord DESIGN.md（反拆） | unpkg.com/oh-my-design-cli@1.9.0/web/references/discord/DESIGN.md | Community | hover/selected 灰阶、白色 pill |
| 22 | BetterDiscord 变量文档 | docs.betterdiscord.app/discord/variables | Community | --background-modifier-hover/-selected 交叉验证 |
| 23 | Discord 主题 gist | gist.github.com/caminashell/ef7bf9f74f1122d56f58c20769bcb356 | Community | rgba 值第三信源 |
| 24 | zenn macOS 设置窗口指南 | zenn.dev/usagimaru/articles/b2a328775124ef | Community | 8pt 列距、11pt 描述、20pt 分隔线缩进 |
| 25 | SettingsCard 官方文档 | learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingscard | Official | 属性集、Wrap 阈值、Spacing=4 |
| 26 | SettingsCard.xaml 源码 | github.com/CommunityToolkit/Windows/blob/main/components/SettingsControls/src/SettingsCard/SettingsCard.xaml | Official | 四列网格、83ms、SettingsCardContentMinWidth |
| 27 | uno discussion #11283 | github.com/unoplatform/uno/discussions/11283 | Community | WinUI 资源映射第三方交叉验证 |
| 28 | StackOverflow NavigationView 指示条 | stackoverflow.com/questions/52974979/ | Community | SelectionIndicator 模板佐证 |

> **信息缺口（调研方自陈）**：① macOS System Settings 分组卡片无官方数值规格（8/11/20pt 为社区逆向值，且针对旧式 toolbar 偏好窗口）；② Discord 与 macOS 的 hover 过渡时长无官方数值；③ WinUI 指示条展开动画时长未数值化发布；④ VS Code 经典 Dark+ 与 Dark Modern 的 `activityBar.activeBorder` 不同（引用需注明主题版本）。

## 6. 静态自证门禁（本机 CI-only，未跑 dotnet）

| 项 | 结果 |
|---|---|
| 新增 / 修改文件编码 | 全部 UTF-8 无 BOM（`BOM false`），行尾 LF（`CRLF 0`） |
| git diff --check | 空（无空白错误） |
| report-32 主本 vs docs 副本 | 逐字节一致（`identical true`，23,370 字节） |
| 原 28 项探活语义 | 与 git HEAD 逐行比对 **28/28 逐字一致** |
| 测试代码改动 | **零**（无新增断言 → 不存在无注释断言） |
| Core / Worker 源 | 未触碰（D-008 约束遵守） |
| dotnet build / test | 本机未执行（CI-only），云端验收待大脑推验证分支 |

## 7. 遗留与移交

1. **T-1 / T-2 裁定**（§4.1 / §4.2）——需大脑与用户裁决；裁定前票 02 / 03 须按双版截图执行。
2. **A-006 闭合记法**（§4.3）——建议由 grill 账本改为「由票 01 闭合」。
3. **视觉基线仍为空**：`docs/design/screenshots/` 只有 .gitkeep；report-32 原 28 项 + 本票 G 组 / S1 / S2 全部为「未探活」初始态。**无用户执行探活则本轮验收机制不成立**——与票 32 终态同一风险，移交大脑决定是否催办。
4. **票 02 / 03 / 04 的规范依据**：分别引用规范 §2（nav 反馈三态）、§1（按钮组宽度）、§3 / §4 / §5（表单行 / 空态 / 间距节奏）。

## 8. 收尾动作

- [x] 报告写入 .scratch/ui-craft/reports/01-visual-standard-doc.md（本文件）
- [x] 轨 1 沉淀：docs/process/reports/01-visual-standard-doc.md 已随票提交（commit xov）
- [ ] 推验证分支由大脑审核云端 CI（本机零构建 / 测试，CI-only）
- [x] **已处置**：edit mode 退出 + `but oplog restore 41438d5` 回滚，工作区 0 冲突；票 03 / 04 分支 `mergesCleanly: true`，不再被本票阻塞
- [ ] **遗留 1**：票 01 分支 `mergesCleanly: false`（与上游 CONTEXT.md 同区段文本冲突），待大脑裁定合并路径（见 §4.5-D-1）
- [ ] **遗留 2**：report-32 扩充（变更 ID `rl`）因 GitButler 依赖锁留 zz 未提交区，磁盘内容不丢失，待解锁后重提（见 §4.5-D-2）
