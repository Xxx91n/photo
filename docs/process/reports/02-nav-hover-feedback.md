# 票 02 nav-hover-feedback 报告（ui-craft 轮）

- 日期：2026-09-13 ｜ 窗口：票 02 执行窗口 ｜ 阻塞状态：票 01 已交付，阻塞解除
- **覆盖 A-xxx：** A-002 ｜ **覆盖 D-xxx：** D-004、D-005、D-006
- 定值依据：`docs/design/ui-visual-standard.md` §2「nav 反馈三态」（v1.1，26595 字节 / 308 行）
- 双轨：`.scratch/ui-craft/reports/` 主本 + `docs/process/reports/` 受控副本（WORKFLOW §4.4 轨 1）

---

## 0. 开工声明（启动器「开工第一句」）

**1. Blocked by 现状。** 票 02 阻塞于票 01（规范 nav 反馈节为定值依据）。开工前实物核验：

- `docs/design/ui-visual-standard.md` 已落盘（26595 B / 308 行），§2 nav 反馈三态齐备（2.1 三态定义 / 2.2 硬性约束 N1–N3 / 2.3 现状对账 / 2.4 三锚对照 / 2.5 反例），附录 A 为 atomcode 控件级调研，修订记录 v1.0（票 01）+ v1.1（票 03）；
- `.scratch/ui-craft/reports/01-visual-standard-doc.md` 已落盘，同名副本已沉淀至 `docs/process/reports/`；
- GitButler 分支 `ui-craft/01-visual-standard-doc` 已存在（票 03 分支叠于其上）。

→ **阻塞解除，本票开工。**

**2. 必读清单确认。** 13 项逐份读全。第 7 项在本窗口首次核验时缺失（票 01 在途，`docs/design/` 目录本身不存在），按启动器「缺一份即停下呈报」纪律停手并呈报大脑；票 01 交付后本窗口重新核验、补齐该项后方才动手施工。

## 1. 定值依据（规范 §2 → 实物落点）

| 规范条款 | 定值 | 落点 |
|---|---|---|
| §2.1 hover 前景 | 向 `SemiColorPrimary` 叙事，禁跳近白 `SemiColorText0` | `AppTheme.axaml:107` / `:140` |
| §2.1 hover accent | 4px bar 低透明度预示（约 30%） | `:108` / `:141` |
| §2.1 过渡 | Background 150ms + Foreground 150ms，SineEaseOut | `:92-98` / `:129-135` |
| §2.2 N1 | 无 scale / 无位移 / 无弹性缓动 | 全段 absent（实测见 §4） |
| §2.2 N2 | 过渡须覆盖 Background 与 Foreground 两路 | Foreground 一路为本票新补 |
| §2.2 N3 | active 与 hover 必须联动 | accent 槽位常驻 + hover 点亮 |

## 2. C1 — D-004 三根因逐条 diff 证据

| 根因 | 规范依据 | 改前（实物） | 改后 | 行号 |
|---|---|---|---|---|
| ① 无过渡硬切 | §2.2 N2 / §2.3 对账 | nav、nav-action 段**无 Transitions setter**，沿用全局 `Button` 段的 Background + BorderBrush 150ms；**Foreground 0ms 硬切**（文字与 20px 图标瞬跳） | 两变体段显式声明 Transitions **三路**（Background / BorderBrush / Foreground），均 `0:0:0.150` `SineEaseOut` | `:92-98`、`:129-135` |
| ② 纯中性灰平移、无色彩叙事 | §2.1 / §2.5 反例 | hover 背景 `SemiColorBackground1`（中性灰）、前景 `Text2→Text0`（近白），20px 图标随 Foreground 走，全程无 primary 参与 | hover 前景改 `SemiColorPrimary`，图标随 Foreground 继承一并染色 | `:107`、`:140` |
| ③ active 与 hover 零联动 | §2.2 N3 | `Button.nav:pointerover` 无任何 border setter；active 的 4px `SemiColorPrimary` bar 在 hover 上无预示 | base 段常驻 `BorderThickness="4,0,0,0"` + `BorderBrush=Transparent` 槽位，pointerover 以 `SemiColorPrimaryLight` 点亮 | `:86-87`、`:108`、`:123-124`、`:141` |

**实现要点（非直白处，登记以防后人误改）：** Avalonia 中变体段一旦声明 `Transitions` 即**整体覆盖**全局 `Button` 段的过渡集合，故 nav / nav-action 段必须一次性写全三路；只补 Foreground 会使 Background 与 BorderBrush 反而丢掉过渡。

**附带修复（非票面要求，如实登记）：** `BorderThickness` 由「active 时 0→4」改为常驻 4，消除两处既有缺陷 —— (a) hover/active 切换时内容的 4px 位移（违反 §2.2 N1「禁位移」）；(b) active 项与非 active 项之间的 4px 图标错位。代价：视觉左内边距由 12 变为 16（全体一致），需 before/after 截图对照确认。

## 3. C2 — 五主题 hover 可视性自查表

实测口径：WCAG 2.x 相对亮度对比度。hover 背景 `SemiColorBackground1`；侧栏底色 `SemiColorNavBackground`（五主题中与 `SemiColorBackground1` **同色**，见 §5 注）。

| 主题 | idle 前景 Text2 | **hover 前景 Primary** | 改前 hover 前景 Text0 | accent 预示条 15% | active 前景 on active 背景 |
|---|---|---|---|---|---|
| catppuccin | 2.63 | **8.34** | 12.14 | 1.33 | 6.28 |
| dracula | 1.90 | **5.41** | 12.25 | 1.30 | 4.15 |
| nord | 2.37 | **5.03** | 8.73 | 1.32 | 3.81 |
| onedarkpro | 1.76 | **6.51** | 7.22 | 1.32 | 4.93 |
| tokyonight | 1.86 | **7.14** | 11.14 | 1.28 | 5.57 |

**结论：**

1. **hover 前景 `SemiColorPrimary` 五主题对比度 5.03–8.34，全部 ≥ 4.5（WCAG AA 正文）** —— 主反馈通道达标，无主题劣化。
2. 改前 `Text0` 数值更高（7.22–12.25）但属「中性近白平移」，正是 D-004 定义的「塑料感」形态；本票以色彩叙事替换亮度堆叠，数值下降是**设计意图**而非退化。
3. accent 预示条 15% 对比度 1.28–1.33（装饰性，非唯一信息载体；主反馈由前景叙事承担）。与规范「约 30%」存在偏差，见 §7.1。
4. idle 前景 1.76–2.63 偏低，为**既有状态**（`Text2` @62%），规范 §2.1 idle 定值即 `Text2`，非本票引入，登记为观察项。

## 4. C3 — 撞红守卫处置记录

### 4.1 既有断言预演（改后求值）

自动解析 `tests/` 中全部读取 `AppTheme.axaml` 的测试方法，提取 `Assert.Contains` / `DoesNotContain` 并逐条求值：

```
PASS=55  FAIL=0
```

解析过程中出现 1 条判红，经核实为**脚本误报**：`ThemeVariantSourceTests.App_Source_Should_Include_AppTheme_ResourceDictionary` 断言的目标文件是 `App.axaml`（含 `Styling/AppTheme.axaml` 引用），非被本票修改的 `AppTheme.axaml` 本身；已单独验证 `App.axaml` 该引用存在 → 不撞红。

**三档分类计数（D-006 度量要求）：杀 0 / 改造 0 / 保留 0** —— 零撞红，无处置动作。

### 4.2 新增防回潮守卫（R1 合规）

新增独立文件 `tests/PhotoPrivacy.IntegrationTests/Ui/NavFeedbackSourceTests.cs`（4 条）。**采用独立文件而非追加 `DesignSystemTests.cs`**：后者已被票 03 改动，独立建文件可规避跨票共享文件冲突（WORKFLOW §4.3）。

| 断言 | 防的具体回潮形态 | 票号 |
|---|---|---|
| `Nav_Transitions_Must_Cover_Foreground_Not_Only_Background` | 删掉 nav 段 Transitions 的 Foreground 一路，或新增 nav 变体时只抄 Background → 根因①回潮 | 票 02 / ui-craft |
| `Nav_Hover_Foreground_Must_Narrate_Primary_Not_Neutral_White` | 为求「更亮」把 hover 前景改回 `SemiColorText0` → 根因②回潮 | 票 02 / ui-craft |
| `Nav_Hover_Must_Preview_Active_Accent_Bar` | 删掉 pointerover 的 BorderBrush 预示，或把 base 段 BorderThickness 改回 0 → 根因③回潮并带回 4px 位移与错位 | 票 02 / ui-craft |
| `Nav_Feedback_Must_Not_Use_Scale_Or_Elastic_Easing` | 给 nav 加 `TransformOperationsTransition` / `BackEaseInOut` / `ElasticEaseInOut` → 违反 §2.2 N1 | 票 02 / ui-craft |

### 4.3 「失效即红」自检（D-006 配套要求 3）

正反双对照（node 沙箱复刻断言逻辑）：

```
正向：当前实物                     -> ALL-PASS（绿）
反向 R1 删 Foreground 过渡          -> RED（A1 命中）
反向 R2 hover 前景退回 Text0        -> RED（A2 命中）
反向 R3 删 accent 预示             -> RED（A3 命中）
反向 R4 注入 scale(0.97)          -> RED（A4 命中）
```

守卫实现中另有一处关键设计：断言前先剥除 XAML 注释（`<!-- -->`）。本票在 nav 段写有较长设计依据注释，注释内含 `Foreground` / `BorderBrush` / `Transitions` 等字面量，若不剥注释，守卫在被回潮时仍会命中注释而恒绿失效。

**附带发现（口径警示）：** XAML 不可走 `SourceLint.ReadStripped` —— 它按 `//` 剥行注释，而 XAML 的 `xmlns` 声明形如 `https://github.com/avaloniaui`，会被拦腰截断。本文件改用 `SourceLint.Read` 后自行剥 XAML 注释，已在代码注释中写明理由。

## 5. 通用调研要求登记

### 5.1 atomcode 深度调研 —— 按大脑裁定跳过

2026-09-13 呈报阻塞时，大脑对本轮「通用调研要求 1」裁定为**跳过并登记未完成**（非「执行」）。故本窗口未发起 atomcode 调研，**据实登记为未完成项，零虚构**。

本票的工业实现证据改由**引用票 01 附录 A 的既有控件级调研结论**（三引擎 16 次检索 / 28 次原文核验，完整来源清单见 `reports/01-visual-standard-doc.md` §5），不重复调研：C1（四大参考产品 nav hover 一律中性灰、accent 只属 active）、C2（hover 过渡以「切换」为主，WinUI Button 官方 83ms）。

### 5.2 ADR / CONTEXT.md 心智模型对齐清单（逐条声明）

| 既有心智模型 | 与本票关系 | 判定 |
|---|---|---|
| ADR 0051 A2 按钮过渡 150ms `SineEaseOut`，禁 `BackEaseInOut`/`ElasticEaseInOut` | 本票沿用同一时长与缓动 | 对齐 |
| ADR 0054 去 scale、纯颜色过渡（NN/g + Fluent 2 + WCAG 2.2 SC 2.3.3） | 本票零 scale、零位移 | 对齐 |
| ADR 0055 A1 nav/nav-action Height=40、Padding=12,0、全宽命中 | Padding/Height 未动 | 对齐 |
| ADR 0062（票 26）5 态矩阵、无 darker token 时不引入新 token | accent 用现役 `SemiColorPrimaryLight`，未引入新 token | 对齐 |
| CONTEXT.md「Sidebar Nav Item」40px + 4px 左侧 accent bar | 本票保持 4px accent | 对齐 |
| CONTEXT.md「Button Transition Animation」词条 | 词条仍记 `TransformOperationsTransition 0.075s` 与 `scale(0.97)`，但 ADR 0054 已删除该实现 → **词条与实物不符** | **冲突（呈报，不静默改）** |

### 5.3 三锚对照（D-005，禁空泛审美词）

- **Wasabi 气场**：hover 背景保持 `SemiColorBackground1`，与侧栏底色 `SemiColorNavBackground` 同色 → 不产生底色块，符合 §2.4「不叠加底色块、避免彩色按钮墙」；点缀色仅出现在 4px accent 条与前景，克制。
- **Apple 系统设置**：active 的填充叙事（`SemiColorPrimaryLight` 背景 + primary 前景）参照 Apple 侧栏选中态，规范 §2 三锚定位已明确。
- **VS Code · Discord 密度法**：hover 表现为「过渡 + 前景色彩叙事」，与 VS Code Activity Bar「hover 无背景、仅前景色由 `inactiveForeground` → `foreground`」同构（附录 A C1 实测）；本项目额外给 hover 加 accent 预示，系 §2.2 N3 的显式要求，与四大参考产品「accent 只属 active」构成张力 T-1（见 §7.2）。

## 6. 完成定义逐项对账

| 完成定义项 | 状态 | 证据 |
|---|---|---|
| D-004 三根因逐条修复并有 diff 证据 | ✅ | §2 表 |
| 五主题 hover 可视性自查表入报告 | ✅ | §3 表 |
| 撞红处置记录 | ✅ | §4（零撞红 + 新增 4 守卫 + 失效即红自检） |
| CI 云端绿 | ⏳ **待大脑推验证分支** | 本机零构建 / 零测试（CI-only，TEST-CONVENTIONS §3） |
| 用户探活 nav 核验项回填 | ⬜ **未探活（如实登记）** | 规范 §7 V4 + report-32 G 组六项，属用户侧动作 |
| 报告写入 `reports/02-nav-hover-feedback.md` | ✅ | 本文件，双轨 |

**静态门禁自证（本机）**：XAML 标签平衡 PASS（`Style` 55/55、`Transitions` 5/5、depth=0）；N1 禁项全 absent（`scale(0.97)` / `TransformOperationsTransition` / `BackEaseInOut` / `ElasticEaseInOut` / `<Color x:Key=`）；文件 UTF-8 无 BOM、LF、394 行；新增 `.cs` 5499 字节 / 107 行，无 BOM、LF。

## 7. 偏差与呈报（请大脑裁定）

### 7.1 accent 预示取 15% 而非规范「约 30%」

规范 §2.1 定 hover accent 为「低透明度预示（约 30%）」。实物核查：五主题中 `SemiColorPrimary` 为 100%、`SemiColorPrimaryPointerover` 为 85%、`SemiColorPrimaryLight` 为 **15%**，**现役不存在 30% 档**。

处置：按现役最近档 `SemiColorPrimaryLight`（15%）施工。未补 30% token 的理由 —— ① 需新增 token 并在 5 个主题 `ResourceDictionary` 各覆写一次，超出本票「只动 AppTheme.axaml」范围（§4.3 共享文件纪律）；② Semi 内置默认主题下该新 key 不存在，会引入资源缺失分支。

请大脑裁定：接受 15%，或另立小票补 30% token 并五主题覆写。

### 7.2 张力 T-1（nav hover 是否引入 primary）

规范 §2.1 已按 D-004 用户拍板定值为 primary 叙事，附录 A.2 注明「票 02 双版截图人工裁定」。本票**按定值施工，不静默改向**；双版对照（中性灰版 / primary 版）待用户探活时执行。

### 7.3 观察项

1. **idle 前景对比度 1.76–2.63**（低于 AA 4.5）：既有状态，规范 §2.1 idle 定值即 `Text2`，非本票引入。建议下轮评估是否上调 idle 前景。
2. **active 项 hover 无额外变化**：规范 §2.1 未定义 active+hover 组合态（选择器特异性使 `.active` 覆盖 `:pointerover`）。本票不引入规范外行为，登记为探活核验项。
3. **CONTEXT.md「Button Transition Animation」词条陈旧**：仍记载 ADR 0054 已删除的 `scale(0.97)` 与 `TransformOperationsTransition`，与实物不符。本票不代改（涉票 03 共享面），建议下轮或由大脑指派修订。

## 8. 变更清单与移交

| 文件 | 变更 |
|---|---|
| `src/PhotoPrivacy.Ui/Styling/AppTheme.axaml` | nav / nav-action 三态重塑（365 → 394 行） |
| `tests/PhotoPrivacy.IntegrationTests/Ui/NavFeedbackSourceTests.cs` | 新增，4 条防回潮守卫 |

**未触碰**（纪律遵守）：`MainWindow.axaml`（票 04 领地）、`docs/design/ui-visual-standard.md`（票 01 / 03 领地）、`CONTEXT.md`、`tests/TEST-CONVENTIONS.md`、`DesignSystemTests.cs`（票 03 已改动，本票改用独立文件规避）。

**移交大脑：** ① 推验证分支取云端 CI 结果；② 裁定 §7.1 accent 30% token；③ 组织用户探活 G 组六项 + T-1 双版截图裁定；④ 指派 CONTEXT.md 词条修订。
