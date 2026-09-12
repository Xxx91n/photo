# ADR 0062: token 消费纪律 gate + 按钮反馈标准定稿 + 反馈补缺（架构恢复第六轮 · 票 26）

**Date**: 2026-09-04
**Status**: Accepted
**Branch**: arc-recovery/26-token-button-gate（GitButler 虚拟分支，叠于票 24 之上）
**来源**: issue 26 + handoff 26 + spec 架构恢复第六轮 + atomcode-report-gui-mental-models 维度 2/5/6

---

## 背景

票 24（Shell+Pages 拆分，ADR 0061）落地后，design tokens 生产端完备（Space 8 档 / Radius 5 / Typography 6 档 / Elevation 4 级 / Duration 3），但消费端缺 gate：Views 内残留内联 FontSize（9 处）、主题色板内联 hex（5 处）、按钮 variant 缺态（nav/nav-action/caption-btn 无 :disabled；ghost/danger/icon 缺 :pressed/:disabled）。atomcode 调研（27 条来源）确认：按钮反馈两派皆工业级（官方 Fluent = 每态 brush + scale(0.98)；VS Code/Win11 = 纯色 150ms），关键在全 variant 覆盖一致 + source-lint 锁定；本项目 ADR 0054 已定向纯色派。

## 决策

### D1: token 消费纪律 gate（source-lint 三断言，DesignSystemTests）

1. **Views 内 FontSize= 清零**：字号唯一权威是 AppTheme typography class（caption=11 / mono=12 / body=14 / title=16 / headline=18 / display=20，档位在 DesignTokens.axaml）。既有 All_Xaml_FontSize_Must_Be_On_Token_Ladder（全 src 档位校验）保持零回归，新增 Views 全禁断言收紧消费面。
2. **Views 内联 hex 清零**：语义色一律走 SemiColor* DynamicResource；**数据色与 chrome 色分层**——审计事件色（AuditLogEntry.ColorHex）与色板标识色（票 25 ThemeSwatchCatalog.ColorHex）是 C# 数据层常量，不在 XAML chrome 断言范围。3/6/8 位 hex 全禁。
3. **无未定义 Classes**：从 AppTheme.axaml 选择器提取 class 白名单（词法提取 .class），Views 的 Classes="a b" 逐词校验。h2 类违例（report-22 B 节）不得回潮。

### D2: 按钮反馈标准定稿（ADR 0054 定稿延续）

- **纯色/透明度过渡 150ms（SineEaseOut），无 scale**（WCAG 2.2 SC 2.3.3：scale 属 motion animation，前庭障碍触发源）。对齐 VS Code / Windows 11 Settings 派。
- **pressed 语义**：Semi 无 darker token（Themes 覆写文件未定义 PrimaryPressed 类 key），不引入新 token；pressed = 「原色 + Opacity 下探（0.85/0.8）」或 Background2 中性加深——与既有 caption-btn.danger:pressed 同一内部惯用法。
- **disabled 语义**（票 26 点名）：Opacity 0.5 + Foreground SemiColorText2（可达性下限的视觉衰减）。

### D3: 7 variant × 5 态矩阵补齐

primary / ghost / danger / icon / nav / nav-action / caption-btn × idle / pointerover / pressed / disabled / focus-visible。idle = 基线样式本体（无伪类）；:focus-visible 由全局 Button:focus-visible ring 覆盖全 variant（SemiColorPrimaryLight + BorderThickness 2）。断言 Ticket26_Button_State_Matrix_Must_Be_Complete 锁定 21 格 + 全局 ring。

### D4: Caption 11 下限豁免 + Display Bold 例外（书面化）

- **Caption 11**：低于 Fluent ramp 12 下限，仅用于时间戳/弱化文本/状态微文案——接受并书面豁免（atomcode 维度 6 建议）。
- **Display 用 Bold**：偏离 Fluent Semibold 惯例，小分歧，维持 ADR 0050 落地值不动，记录例外。

### D5: 反馈补缺 — Logs/Rules 空态文案（必做项落地）

- LogsPage：空审计流占位（ex:Localize log.empty），开关 = MainWindowViewModel.HasNoLogs，在 AppendLog/AppendLogBatch/ClearLogs **收口处联动刷新**（零事件订阅，无长活泄漏——ADR 0061 页面 code-behind 零订阅规约不受影响）。
- RulesPage：空规则占位（ex:Localize rules.empty），开关 = RulesPanelViewModel.HasNoVisibleRules，在 ApplyFilter 收口处联动。
- locale keys 补齐 10 语言（en/zh-CN 人工文案；ja/ko/de/fr/es/pt/ru/ar 按语言惯例翻译），All_Locales_Have_Identical_Flat_Key_Sets_As_English 保持绿。

### D6: Ursa Toast 接入 — 跳过（可选项，停手规则适用）

「配置已保存/失败」Toast 的自然接线点 = ConfigEditor 保存完成回调（票 27 在途领地：ConfigEditor.cs / MainWindow.axaml.cs）。WORKFLOW §4.3 共享文件停手规则：跨票共享文件一次只许一票碰。SaveStatus 文本反馈（ConfigPage 底部）继续承载被动感知。**移交大脑**：后续票在票 27 合入后接线。

### D7: 5 色板切换耗时实测 <200ms — PASS

- 观测插桩：App.ApplyCommunityThemeResources 包 Stopwatch，落 ThemeSwapMs={毫秒} theme={名称} applyDark={布尔} 到 UiDiagnosticLog（logs/ui-*.log），长期保留作诊断。
- 实测载体：真实 UI 进程 + 票 27 ConfigFileWatcher 回环（外部改 config.json theme_id → 500ms 防抖 → ApplyRuntimeConfigToUiState → ApplyCommunityThemeResources），无人值守可复现。
- 结果（Debug/win-x64，2026-09-04）：7 次应用事件；启动路径 65/27ms；外部切换 dracula=209ms（首切换含 JIT/XAML 首编译预热）、nord=59ms、tokyonight=44ms、onedarkpro=35ms、catppuccin=23ms。**spec 口径（首次含预热、取第 3 次起均值）：74.0ms < 200ms PASS**；剔除预热后稳态均值 40.3ms。

### D8: 与并行票 25 的协同（High-frequency components，同时段施工）

本票与票 25（NavButton/ThemeSwatch/PathPicker 抽取）共享工作区并行施工，形态裁决：

- **色板标识色权威移交**：票 25 ThemeSwatchCatalog（C# 数据层）承载 5 色板 hex；本票原拟的 DesignTokens Swatch* token 撤销（无消费即删，Ponytail），hex gate 只锁 XAML chrome。
- **Views 内联迁移载体**：MainWindow/ConfigPage 的 caption 化/ToolTip 由票 25 版本文件承载（其重构保留并吸收了本票行级改动）；LogsPage/RulesPage 迁移由本票承载。
- **AppTheme 共享**：本票 5 态矩阵（约 60 行）+ 票 25 PathPicker.inline-input 块（7 行）；后者缺 Ursa xmlns 导致 AVLN2000，本票补 xmlns:u 修复并把 selector 修正为 u|PathPicker（Avalonia 选择器中冒号是伪类分隔符，命名空间前缀用竖线）。
- **DesignSystemTests 混载**：本票 7 个 Ticket26* facts + 票 25 的 Browse/Icon 断言形态迁移（FolderOpen→PathPicker、icon ≥7→≥2）。

## 约束

- 不引新 NuGet 依赖；零新 Semi token 依赖（pressed/disabled 只用已验证 token + Opacity）。
- 单槽串行测试（test.runsettings MaxCpuCount=1）。
- 版本控制 WORKFLOW §4.2；动栈前 §4.4 快照（本票 2026-09-04T14:48 快照 ZERO-LOSS 58/58）。
- 不触碰票 27 在途文件（MainWindow.axaml.cs/ConfigEditor.cs/ConfigFileWatcher.cs/AppConfig*）与票 25 在途文件（Controls/*、MainWindow.axaml.cs、ConfigPage.axaml.cs、MainWindowShellSourceTests.cs）。

## 门禁证据（2026-09-04）

- build 0 错误、0 SCS 警告；semgrep（p/csharp + p/security-audit）0 发现。
- 单槽 Core 191/191 + Integration 321/321 全绿（DesignSystemTests 零回归，含新增 7 断言全绿）。
- 5 色板切换 mean(3rd+) = 74.0ms < 200ms。

## 调研引用

- NN/g Button States: Communicate Interaction（pressed = very slight color change 100-150ms）
- WCAG 2.2 SC 2.3.3 Animation from Interactions（scale = motion，颜色/透明度不算）
- Fluent 2 Design tokens（global/alias 双层模型）
- Windows 11 Fluent typography（Caption 12 下限 → 本项目 11 豁免书面化）
- atomcode-report-gui-mental-models.md 维度 2/5/6（Semi+Ursa 组合冻结、按钮五态对照、图标密度 1:4 健康）
