# Report — 票 26 token 消费纪律 gate + 按钮反馈标准定稿 + 反馈补缺（架构恢复第六轮）

**窗口**: 票 26 工作窗口　**日期**: 2026-09-04　**分支**: arc-recovery/26-token-button-gate　**提交哈希（git log 实物）**: 3ec6837
**必读清单**: 9/9 逐份读全（启动器 / handoff / issue / spec / WORKFLOW / atomcode 报告 / ADR 0048/0050/0051/0054）
**Blocked by 现状复述（开工第一句）**: Blocked by 票 24（已落地，commit 8cdc769 / but 分支 arc-recovery/24-shell-pages-split）；票 27 同期在途（e0b6acb）。

## 一、声明 → 证据 → 结论对照

| # | 声明（issue/handoff 检查点） | 证据 | 结论 |
|---|---|---|---|
| 1 | source-lint：Views 内无 FontSize= | Ticket26_Views_Must_Not_Contain_Inline_FontSize 绿；迁移前 9 处（MainWindow×2 / ConfigPage×2 / LogsPage×4 / RulesPage DataGrid×1）全部迁移（caption/mono 类 + AppTheme DataGrid.data-grid token 样式） | ✅ |
| 2 | source-lint：Views 内无硬编码 hex（豁免白名单单例除外） | Ticket26_Views_Must_Not_Contain_Hardcoded_Hex_Colors 绿；5 处色板 hex 由票 25 ThemeSwatchCatalog 承载（数据层），XAML chrome 零 hex | ✅ |
| 3 | source-lint：无未定义 Classes | Ticket26_Views_Classes_Must_Be_Defined_In_AppTheme 绿（AppTheme 选择器白名单提取 + 逐词校验）；h2 清零（票 24）延续 | ✅ |
| 4 | source-lint（可选）：新图标伴随文字或 ToolTip | Ticket26_Icon_Only_Buttons_Must_Carry_ToolTip 绿：caption-btn×3（titlebar.minimize/maximize/close）+ icon 变体（Add/Remove，config.tooltip.*）ToolTip.Tip locale 化；5 处 Browse ToolTip 由票 25 u:PathPicker 自带承载 | ✅ |
| 5 | AppTheme 补 nav/nav-action/caption-btn:disabled（Opacity + SemiColorText2 语义） | AppTheme.axaml 新增 13 个状态样式；nav/nav-action/caption-btn :disabled = Opacity 0.5 + SemiColorText2 | ✅ |
| 6 | 6 variant × 5 态矩阵补齐 + :focus-visible 全 variant 可见 | Ticket26_Button_State_Matrix_Must_Be_Complete 绿：7 variant × pointerover/pressed/disabled 21 格 + 全局 Button:focus-visible ring | ✅ |
| 7 | 5 色板运行时切换 <200ms（首次含 JIT 预热，第 3 次起均值） | App.axaml.cs ThemeSwapMs 插桩 + 票 27 watcher 回环实测：7 事件，dracula=209ms（首切预热）/59/44/35/23ms → mean(3rd+)=74.0ms PASS（稳态均值 40.3ms） | ✅ |
| 8 | build 0 错 0 新增 SCS | dotnet build 0 错误；SCS 警告 0（全量 grep）；semgrep p/csharp+p/security-audit 0 发现 | ✅ |
| 9 | 11 个 DesignSystemTests 零回归 | 单槽 Core 191/191 + Integration 321/321 全绿（321 含票 25 期间新增断言）；DesignSystemTests 全 Fact 绿 | ✅ |
| 10 | 反馈补缺（可选）：Ursa Toast 接入「配置已保存/失败」 | **未实施**——接线点 ConfigEditor 保存回调属票 27 在途领地（WORKFLOW §4.3 停手）；SaveStatus 文本反馈保留；移交大脑（ADR 0062 D6） | ⏸ 移交 |
| 11 | 反馈补缺：Logs/Rules 空态文案 | LogsPage log.empty + RulesPage rules.empty；VM 开关 HasNoLogs（Append/AppendBatch/Clear 收口）/ HasNoVisibleRules（ApplyFilter 收口）；10 语言 keys 对齐（Flat_Key_Sets 绿） | ✅ |
| 12 | 交付 ADR：按钮反馈标准 + token 消费纪律 + Caption 11 豁免 + Display Bold 例外 | docs/adr/0062-token-discipline-button-feedback-standard-and-empty-states.md（D1-D8） | ✅ |
| 13 | 报告双轨 + 版本控制遵循 WORKFLOW | 本报告 .scratch 主本 + docs/process/reports/26 副本随票提交；快照 20260904-144813（ZERO-LOSS 58/58）先于动栈 | ✅ |

## 二、实施摘要

- **检查点 A**：Views 内联 FontSize 9 处 → typography class（caption=11 / mono=12；DataGrid 12px 经 AppTheme DataGrid.data-grid token 样式）；hex 5 处 → 票 25 catalog 承载；Classes 白名单 gate；icon-only ToolTip locale 化（titlebar×3 + config.tooltip×2）。
- **检查点 B**：AppTheme 新增 13 状态样式（primary/ghost:pressed、ghost/danger/icon:disabled、danger:pointerover+pressed、nav/nav-action:pressed+disabled、caption-btn:disabled、primary:pressed）。设计裁决：pressed = 原色+Opacity 下探或 Background2 中性加深（无 darker token，不引新 token）；disabled = Opacity 0.5 + Text2。
- **检查点 C**：空态 ×2（VM 收口联动，零订阅）；Toast 停手移交；色板切换实测 74.0ms PASS（ThemeSwapMs 插桩永久保留作诊断）。

## 三、并行协同与风险登记

1. **票 25 同时段施工**（NavButton/ThemeSwatch/PathPicker）：共享文件 MainWindow.axaml / ConfigPage.axaml / AppTheme.axaml / DesignSystemTests.cs 出现双向吸收。本票裁决：色板色权威移交 ThemeSwatchCatalog（本票 DesignTokens Swatch token 撤销）；AppTheme 中票 25 的 PathPicker.inline-input 块缺 xmlns:u 触发 AVLN2000，本票补 xmlns 并修正 selector 语法（u|PathPicker——Avalonia 选择器冒号是伪类分隔符）。其间一次文件交换（HEAD+本票重放→门禁）被票 25 回写覆盖，最终以联合形态收口。
2. **未圈入本票提交的物品**（他人在途）：Views/Controls/*（票 25）、MainWindow.axaml / ConfigPage.axaml / MainWindow.axaml.cs / ConfigPage.axaml.cs / MainWindowShellSourceTests.cs（票 25 为主，含本票被吸收的 caption/ToolTip 行——随票 25 提交）、docs/process/reports/22/23/24-config/27-fix（他人报告）。
3. **合并提示（交大脑）**：本票 DesignSystemTests/AppTheme 整文件提交；票 25 提交同名文件时按 GitButler change-id 正常叠加（内容兼容，联合形态 321/321 已验证）。Toast 接线在票 27 合入后开小票。
4. **提交范围终态（88cf3c1 → uncommit nrs:ro 后 3ec6837）**：MainWindow.axaml 整文件属票 25 形态（NavButton 替换与 caption 行同 hunk 不可分），已从本票提交移除——本票 caption/ToolTip 行级改动随票 25 提交（报告第三节已声明）；提交内 20 文件 519+/27-，零票 25/27/他人物品。

## 四、变更文件清单（本票提交范围）

- src/PhotoPrivacy.Ui/Styling/AppTheme.axaml（5 态矩阵 13 样式 + DataGrid.data-grid + xmlns:u 修复；票 25 的 PathPicker.inline-input 块以 working-tree 态存在未圈入）
- src/PhotoPrivacy.Ui/Styling/DesignTokens.axaml（swatch 权威移交注释）
- src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs（HasNoLogs）
- src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs（HasNoVisibleRules）
- src/PhotoPrivacy.Ui/App.axaml.cs（ThemeSwapMs 计时插桩）
- src/PhotoPrivacy.Ui/Views/Pages/LogsPage.axaml / RulesPage.axaml（空态 + FontSize 迁移；MainWindow.axaml caption/ToolTip 行因与票 25 NavButton 替换同 hunk 不可分，随票 25 提交）
- src/PhotoPrivacy.Ui/Localization/Locales/*.json ×10（log.empty/rules.empty/titlebar.*/config.tooltip.*）
- tests/PhotoPrivacy.IntegrationTests/Ui/DesignSystemTests.cs（+7 Ticket26 facts + 形态协同登记）
- docs/adr/0062-token-discipline-button-feedback-standard-and-empty-states.md
- docs/process/reports/26-token-button-gate.md（本报告灾备副本）

## 五、遗留与移交

- Toast（可选未做）→ 票 27 合入后小票。
- MainWindow/ConfigPage 的 caption/ToolTip 行随票 25 提交——若票 25 弃提交，需大脑裁定归属。
- 色板切换观测口径：ApplyCommunityThemeResources 墙钟（含 ResourceDictionary 换入+物化探测）；DynamicResource 传播到 4 页全量重绘的尾延迟未单独计量（视觉无卡顿，后续票可加 Render 侧打点）。
