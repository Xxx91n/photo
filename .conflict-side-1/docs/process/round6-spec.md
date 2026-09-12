# Spec — 架构恢复第六轮（GUI 架构与设计系统心智模型落地）

来源（架构报告）：2026-09-04 GUI 心智模型调研（3 份仓库实物勘察 + atomcode 三引擎联网调研 12 项目对照，star 均 ≥500 经 GitHub API 核验）。评估判定 GUI 层「选型正确、消费端未拆分」，定位四处残留。

## Problem Statement

第五轮收口后，宏观评估发现 GUI 层四处明确残留：(1) 单一 MainWindow.axaml 636 行装 4 页 + chrome，0 个 UserControl，三页同形 Grid ≈110 行重复；(2) 129 处内联 Classes 中 h2 未定义、Width=280 ×4 散值、design tokens 消费端大量内联覆写；(3) 6 个按钮变体中 nav/nav-action/caption-btn 缺 :disabled 态，按钮反馈不统一；(4) 权威配置「生效但 3 处双 DTO 漂移 + 5 处 Service→View 直写」，缺手改 config.json → UI 自动同步的回环。

## Solution

四张垂直切片票：Shell + Pages/ 拆分 + 顺带修复 Service→View 直写（票24）；高频组件抽取（票25）→ token 消费纪律 gate + 按钮反馈标准定稿 + 反馈补缺（票26）；Config Editor Round-Trip 回环补全（票27，独立于 GUI 票）。

## User Stories

1. As a 用户, I want 每个页面独立成文件且切换不卡顿, so that 界面结构清晰、后续改动不牵一发动全身。
2. As a 维护者, I want MainWindow 只留 shell 骨架, so that 页面增删改不再改同一个 636 行文件。
3. As a 开发者, I want 高频控件（路径选择/导航按钮/主题色板）抽成可复用 UserControl, so that 改一处全局生效、不再复制粘贴。
4. As a 用户, I want 按钮在禁用态有明显视觉衰减, so that 不可用操作一眼可辨。
5. As a 用户, I want 手改 config.json 后界面自动同步, so that 权威配置真正生效、无「改了半天没反应」的疑虑。
6. As a 维护者, I want design tokens 消费由 source-lint 锁定, so that 内联硬编码不再回潮。
7. As a 新人/Agent, I want GUI 结构符合行业 Shell+Page 范式, so that 心智模型清晰可信、可导航。

## Implementation Decisions

- 票24 为地基：MainWindow 降纯 shell（≤180 行），4 页抽 Views/Pages/*.axaml（各 ≤220 行），保留 IsVisible 页切换语义（本轮不迁导航形态）；顺带修复 5 处 Service→View 直写，改走 VM 中转。
- 票25 先查库再自制：PathPicker 用 Ursa v1.8.0 原生（本项目 Ursa 2.2.0 已含），NavButton/ThemeSwatch 自制 UserControl。
- 票26 定稿按钮反馈标准：纯色 150ms、无 scale（WCAG 2.2 SC 2.3.3），补全 variant×state 矩阵 + 反馈补缺（Toast/空态），source-lint 锁消费纪律。
- 票27 补范式①回环：新增 ConfigFileWatcher，手改 config.json → UI 自动同步 + 收敛 3 处双 DTO 漂移点；不切全量 Observable（无防抖致 IPC 风暴）、不切纯 IOptionsMonitor（无法解决 Worker round-trip）。
- 版本控制遵循 WORKFLOW §4.2；动栈前遵循 WORKFLOW §4.4 快照。

## 研究输入（atomcode 完整提示词）

子代理在做设计裁决（尤其票25 组件抽取、票26 按钮反馈派系、票27 配置范式）时，用 atomcode-research 跑下面这条完整提示词（一次一条、串行，见 atomcode-research skill 的串行护栏）：

全景调研并给出架构决定：回顾本项目 docs/adr 全部架构决策记录与 CONTEXT.md 领域术语表里已确立的心智模型，结合 Avalonia 12 工业界成熟落地的桌面应用 GUI 架构心智模型（重点对照 StabilityMatrix/ClassIsland/PicView/Files 的 Shell+Page 拆分、Semi/Ursa/Fluent 设计系统、按钮五态、图标字体密度），判定：(1) 工业界成熟心智模型里哪个最适合本项目 GUI 层继续收敛；(2) 本项目 GUI 心智模型里还有哪个关键缺口最需要补充；(3) 给出优先级与落地建议，重点标注工业级成熟模板而非重复造轮子。

（此提示词已由大脑用 atomcode 跑过一次，结论见 Further Notes 与 atomcode-report-gui-mental-models.md。）

## Testing Decisions

- 单槽串行门禁不变（test.runsettings MaxCpuCount=1）。
- 票24：source-lint 断言 MainWindow 行数上限 + Views/Pages/*.axaml 存在 + Page code-behind 无长活订阅；行为零回归靠既有 smoke/UI 探活清单。
- 票25：PathPicker 弹窗/回填行为与现一致（TestStorageProvider 注入）；NavButton active 态样式断言；ThemeSwatch 切换后 ThemeId 持久化 roundtrip。
- 票26：source-lint 断言 Views 内无 FontSize/硬编码 hex/未定义 Classes；6 variant × 5 态矩阵补齐；5 色板切换耗时 <200ms。
- 票27：ConfigFileWatcher 手改文件 → UI 同步测试；漂移点收敛回归；round-trip 全字段。

## Out of Scope

- 不迁 Avalonia 12 官方 NavigationPage/DrawerPage（远期评估，本轮只拆文件）。
- 不引入 DI 容器 / CommunityToolkit.Mvvm / R3 / ReactiveUI（保持手写 INPC + SetField）。
- 不换设计系统（Semi + Ursa.Themes.Semi 组合方向正确，冻结）。
- 不做 double-DTO 架构重构（单 DTO/源生成器，中远期 backlog）。
- 不迁 Material.Icons → PathIcon 静态化（发布体积敏感时再评估）。

## Further Notes

- 波次由 issue 的 Blocked by 字段唯一推导，见 README.md 波次表。
- 时效纠错：csproj 实为 Avalonia 12.1.1（非 11），AGENTS.md「Avalonia 11」口径已过期，随票24 顺带修正。
- 大脑已跑的 atomcode 调研结论（GUI 心智模型）：工业共识 = Shell（只留 chrome）+ 按页拆分 UserControl + VM-first；现役 Semi+Ursa.Themes.Semi 与官方 README 逐字同构，方向正确，差距全在消费端未拆分。12 项目对照 + 27 条来源见 atomcode-report-gui-mental-models.md。
- 三份勘察报告（report-22 XAML / report-23 VM / report-24 Config）是本轮 spec 的前置输入，均落 .scratch + docs/process/reports 双轨。
- 票 24/25/26/27 编号承接 atomcode §7 与 report-24 D 章推荐；报告 slug 与勘察报告 report-24-config-authority-audit 通过 slug 区分，互不混淆。
