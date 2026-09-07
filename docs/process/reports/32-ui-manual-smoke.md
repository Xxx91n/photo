# Report — 票 32 UI 手工探活冒烟（架构恢复第七轮）

**Date**: 2026-09-07
**Status**: 终态-未探活交收口（检查点 A ✅；检查点 B 用户未执行，28 项如实登记未探活零虚构；检查点 C 报告终态化交大脑裁定；用户日后回填可重开）
**Branch**: arc-recovery/32-ui-manual-smoke（GitButler 虚拟分支，不动代码）
**来源**: prompts/32-ui-manual-smoke.md + handoffs/32 + issues/32

---

## 0. 开工复述（启动器要求）

- **Blocked by 现状**：票 28 ✅ done（run 33945684509 SUCCESS，Core 183/183 + Integration 273/273）；票 31 ✅ done（返修后 run 34071736168 SUCCESS，Core 191/191 + Integration 317/317）。两依赖均云端闭环，波 5 解锁。
- **必读清单（9 份全部读全）**：handoffs/32-ui-manual-smoke.md、issues/32-ui-manual-smoke.md、spec.md、WORKFLOW.md、docs/adr/0062、docs/adr/0050、docs/adr/0052、docs/adr/0055、docs/process/round6-README.md。另加读第七轮权威 README.md（.scratch/architecture-recovery/README.md）核对票 28–31 收口状态。

## 1. 声明 → 证据 → 结论对照（handoff 完成定义）

| # | 完成定义（handoff） | 声明 | 证据 | 结论 |
|---|---|---|---|---|
| 1 | 探活清单逐项含操作步骤 + 预期结果（对照 round6-README 冒烟清单），交用户执行 | 本报告 §2 清单 6 面 28 项，每项含操作步骤+预期结果+证据方式；六面对齐 round6-README「待用户裁定 3」维度（5 色板/10 语言/中键/拖宽/日志流）+ issue 32 增补的按钮反馈 | §2 各项锚点均标注源码实物行号（见 §4 锚点索引） | ✅ 检查点 A 达成 |
| 2 | 用户回填的每项 通过/失败 证据逐条登记，无证据项标注「未探活」不虚构 | §3 回填登记表初始状态全部「未探活」；用户在真实桌面执行后逐条回填 | §3 表格回填列（本窗口零虚构） | ⏳ 阻塞于用户执行 |
| 3 | 汇总报告写入 .scratch/architecture-recovery/report-32-ui-manual-smoke.md | 本文件即汇总报告；docs 受控副本 docs/process/reports/32-ui-manual-smoke.md 逐字一致（§4.4 轨 1 双轨） | 双轨文件落盘实物 | ✅ 骨架落盘，结论待回填 |

## 2. 探活清单（检查点 A）

> 执行环境与前置条件见 §5。每项回填口径：**通过 / 失败 / 未探活** + 证据（截图路径或一句话步骤记录）。

### A. 主题色板（5 预设，对照 ADR 0052 A3 / 0062 D7）

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| A1 | 启动应用 → 配置页 → 找「主题预设」行 | 5 个色板横排（WrapPanel）：色块+本地化名称；当前预设色板呈选中态（RadioButton 组互斥）。五色块分别约为蓝 #89B4FA / 紫 #BD93F9 / 青 #88C0D0 / 蓝 #61AFEF / 蓝紫 #7AA2F7 | 截图 | 未探活 |
| A2 | 依次点击 5 个色板各一次 | 每次点击后全窗配色即时切换（首切换可感知预热，稳态 <200ms，ADR 0062 D7 实测 74ms）；选中态随点击迁移；无需重启 | 截图×5 或一句话记录 | 未探活 |
| A3 | 双轴独立：任选色板后，切「主题」明暗下拉（系统/浅色/深色）；再反向操作 | 切明暗不改色板选中态；切色板不改明暗（双轴独立，ADR 0052 A3） | 一句话记录 | 未探活 |
| A4 | 切到 dracula → 关闭应用 → 重启 | 重启后仍 dracula；logs/ui-YYYYMMDD.log 内每次切换有一行 ThemeSwapMs={ms} theme={名} applyDark={bool}（诊断打点留存） | 日志行粘贴 + 截图 | 未探活 |
| A5 | 应用运行中，用文本编辑器改 config.json 的 ui.theme_id 为 "nord" 保存 | 约 1 秒内 UI 自动切换到 nord（ConfigFileWatcher 500ms 防抖热重载）+ 色板选中态同步迁移 | 一句话记录 | 未探活 |

### B. 语言（10 语言，对照 ADR 0044/0047、票 30）

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| B1 | 「语言」下拉依次切换：中文/English/日本語/한국어/Deutsch/Français/Español/Português/Русский/العربية | 每次选择后侧栏导航、页面标题、行标签、按钮、tooltip 全部即时翻译；语言下拉项自身保持母语标签（设计如此，不参与翻译） | 截图≥3 种语言 | 未探活 |
| B2 | 切语言后看「主题预设」行 5 个色板名称 | 色板 caption 即时跟随翻译（ThemeSwatch 监听 CultureChanged 自刷新） | 截图 | 未探活 |
| B3 | 选 العربية | 窗口布局翻转为从右向左（FlowDirection=RTL，侧栏移到右侧） | 截图 | 未探活 |
| B4 | 切 English → 重启应用 | 重启后仍英文（config.json ui.locale 持久化） | 一句话记录 | 未探活 |
| B5 | 切语言后展开「主题」明暗下拉与「日志级别」下拉 | 两个下拉的选项文案（系统/浅色/深色；all/info/debug/warn/error）跟随当前语言刷新 | 一句话记录 | 未探活 |

### C. 中键滚动（对照 ADR 0050 A3 / 0051 A3 / 0055 A2）

> 实态挂点：**配置页**与**服务管理器页**的 ScrollViewer 挂了 MiddleClickScrollBehavior（ConfigPage.axaml:7、ServiceManagerPage.axaml:8）；日志页与规则页**未挂**（见 C4）。

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| C1 | 配置页内容超出一屏（缩小窗口高度）→ 按鼠标中键 → 移动鼠标 | 光标变为滚动锚（随位置显示四向/上下/左右箭头）；内容随鼠标偏移平滑滚动（指数平滑，无台阶跳进） | 一句话记录 | 未探活 |
| C2 | 滚动锚定后，鼠标在锚点附近 ±12px 内小幅移动 | 不产生滚动（死区防误触；离开死区后阈值缩半防边界抖动） | 一句话记录 | 未探活 |
| C3 | 退出方式：按 Esc / 再次按中键 / 点其他键 | 中键平移立即停止，光标恢复常态 | 一句话记录 | 未探活 |
| C4 | 服务管理器页重复 C1 | 同 C1 平滑滚动 | 一句话记录 | 未探活 |
| C5 | （观察项，不判失败）日志页与规则页按中键 | 不触发中键平移——两页未挂 behavior 属当前代码实态（LogsPage 无 ScrollViewer 挂点、RulesPage ScrollViewer 无 attached 属性）；如实记录所见即可 | 一句话记录 | 未探活 |

### D. 侧栏拖宽持久化（对照 ADR 0052 A5）

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| D1 | 鼠标移到侧栏与内容区之间 4px 分隔条 → 按住拖动 | 分隔条 hover 高亮；拖动时分界线跟随；宽度被钳制在 170–400px（拖不出界） | 截图 | 未探活 |
| D2 | 拖到非默认宽度（如 300）→ 松手等 1 秒 → 重启应用 | 重启后侧栏列宽恢复为所拖宽度（config.json ui.sidebar_width；恢复时同样 Clamp 170–400） | 一句话记录 + 截图 | 未探活 |
| D3 | （观察项，不判失败）拖宽后看侧栏内部面板视觉宽度 | MainWindow.axaml:42 侧栏内部 Border 实物固定 Width=200——列宽变化与内部面板视觉宽度的实际所见如实记录（本项用于核验布局实态，通过/失败由大脑裁定口径） | 一句话记录 | 未探活 |

### E. 日志流（对照 ADR 0037/0046/0056、票 26 空态）

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| E1 | 清空审计日志后首次打开日志页 | 列表为空时中央显示空态占位文案（log.empty，HasNoLogs 驱动） | 截图 | 未探活 |
| E2 | 向监控目录放入一张含 EXIF 的照片，等 Worker 处理 | 日志页顶部即时插入新条目（时间/事件徽章/掩码路径/消息），无需手动刷新 | 截图 | 未探活 |
| E3 | 观察不同事件类型的徽章 | 不同事件类型徽章着色不同（AuditLogEntry.ColorHex） | 截图 | 未探活 |
| E4 | 点「清空日志」→ 重启应用 | 清空后列表立即空 + 空态文案重现；重启后旧日志不回灌（AuditTailService 读取偏移重置，ADR 0037） | 一句话记录 | 未探活 |
| E5 | 配置页「日志级别」下拉切换 all→error | 级别切换生效：error 档下常规 info 事件不再出现（LogLevelIndex setter 联动 LogEnabled/ShowDetailedEvents） | 一句话记录 | 未探活 |

### F. 按钮反馈（对照 ADR 0054/0062 七变体五态）

| # | 操作步骤 | 预期结果（锚点） | 证据方式 | 回填 |
|---|---|---|---|---|
| F1 | 悬停各变体按钮样本：侧栏导航（nav）/ 清空日志·保存·重置·刷新（ghost）/ 配置页 +−（icon）/ 服务页主操作（primary）/ 卸载（danger）/ 标题栏三键（caption-btn，关闭为红） | 悬停背景/透明度约 150ms 平滑过渡（纯色派，无 scale 缩放、无闪烁） | 截图≥2 | 未探活 |
| F2 | 任一按钮按下不放再松开 | 按下时颜色下探（Opacity 0.85/0.8 或中性加深），松开恢复 | 一句话记录 | 未探活 |
| F3 | 服务管理器页找当前不可用的按钮（如未安装服务时的「启动/停止」） | 呈禁用态：Opacity 0.5 + 弱化前景（SemiColorText2），点击无响应 | 截图 | 未探活 |
| F4 | 用 Tab 键在按钮间移动焦点 | 获焦按钮出现约 2px 主色描边 ring（focus-visible 全局样式） | 截图 | 未探活 |
| F5 | 标题栏：拖拽空白区 / 双击空白区 / 点最小化·最大化·关闭 | 空白区可拖动窗口、双击切换最大化；三键功能正确（关闭红色 hover/pressed 态）；窗口内不出现系统原生标题栏（自绘，ExtendClientArea） | 一句话记录 | 未探活 |

## 3. 回填登记表（检查点 B — 用户执行后逐条登记）

> 填写口径：结果 = 通过/失败/未探活；证据 = 截图文件路径或一句话步骤记录。无证据一律「未探活」，不虚构。

| 项 | 结果 | 证据（路径/记录） | 备注 |
|---|---|---|---|
| A1 | 未探活 | — | |
| A2 | 未探活 | — | |
| A3 | 未探活 | — | |
| A4 | 未探活 | — | |
| A5 | 未探活 | — | |
| B1 | 未探活 | — | |
| B2 | 未探活 | — | |
| B3 | 未探活 | — | |
| B4 | 未探活 | — | |
| B5 | 未探活 | — | |
| C1 | 未探活 | — | |
| C2 | 未探活 | — | |
| C3 | 未探活 | — | |
| C4 | 未探活 | — | |
| C5 | 未探活 | — | 观察项，不判失败 |
| D1 | 未探活 | — | |
| D2 | 未探活 | — | |
| D3 | 未探活 | — | 观察项，不判失败 |
| E1 | 未探活 | — | |
| E2 | 未探活 | — | |
| E3 | 未探活 | — | |
| E4 | 未探活 | — | |
| E5 | 未探活 | — | |
| F1 | 未探活 | — | |
| F2 | 未探活 | — | |
| F3 | 未探活 | — | |
| F4 | 未探活 | — | |
| F5 | 未探活 | — | |

**小结**：28 项中 通过 0 / 失败 0 / 未探活 28（终态，2026-09-07：用户未执行探活；按 handoff「无证据项标注未探活不虚构」如实登记，无任何项被虚构为通过/失败）。

## 4. 锚点索引（源码实物，复核用）

| 面 | 锚点 |
|---|---|
| 色板 | src/PhotoPrivacy.Ui/Views/Pages/ConfigPage.axaml:121-135；Views/Controls/ThemeSwatch.axaml.cs（ThemeSwatchCatalog 五预设 hex）；ViewModels/ThemeSwatchSelectionConverter.cs（选中态 MultiBinding）；Views/MainWindow.axaml.cs:589-599（Click 转发）、779-794（PropertyChanged→ApplyCommunityThemeResources） |
| 主题诊断打点 | App.axaml.cs:130-137（ThemeSwapMs 落 UiDiagnosticLog）；logs/ui-YYYYMMDD.log |
| 持久化 | src/PhotoPrivacy.Core/Configuration/AppConfig.cs:36（UiOptions 默认 catppuccin/200/zh-CN）；AppConfigJson.cs UiDto（ui.theme_id/ui.locale/ui.sidebar_width） |
| 语言 | ConfigPage.axaml:149-152（10 项下拉）；MainWindowViewModel.cs:252-278（CurrentLocale/CurrentLocaleIndex/SwitchLocale）；UiFlowDirection RTL（MainWindowViewModel.cs:17-33）；Localization/Locales/*.json 10 语言各 198 键 |
| 中键 | Behaviors/MiddleClickScrollBehavior.cs（DeadZone 12 / SpeedFactor 0.12 / MaxSpeed 32 / 指数平滑 k=15 / Watchdog 32ms / Esc 退出）；挂点 ConfigPage.axaml:7、ServiceManagerPage.axaml:8 |
| 拖宽 | MainWindow.axaml:40-42（ColumnDefinitions 200,4,*；Border Width=200 固定实态）；MainWindow.axaml.cs:798-830（RestoreSidebarWidth Clamp 170-400 / DragCompleted 防抖持久化） |
| 日志流 | Views/Pages/LogsPage.axaml（ClearLogsButton/LogsList/log.empty 空态）；MainWindowViewModel.cs:440-509（LogEntries 500 上限/HasNoLogs/ClearLogs） |
| 按钮态 | Styling/AppTheme.axaml:17-244（primary/ghost/danger/icon/nav/nav-action/caption-btn × idle/pointerover/pressed/disabled + 全局 focus-visible ring） |

## 5. 前置条件与环境（探活前必读）

1. **产物新鲜度（关键）**：本机现有产物 bin/Debug/net10.0/win-x64/PhotoPrivacy.Ui.exe 时间戳 2026-09-04 17:34，**早于票 29（9-05）/30（9-05）/31（9-07）的源码收口**。探活前须先产出含票 29–31 代码的新鲜产物（用户走 scripts/release-readiness.ps1，或用 CI 发布产物），否则探活结论只对旧版本负责。实际探活所用产物的版本/时间戳回填此处：________。
2. **CI-only 边界**：本窗口不本地构建、不本地运行应用；探活全部由用户在真实桌面执行（handoff 环境硬约束）。
3. **config.json 就绪**：ExifTool 路径与监控目录已配置（E2 需真实 Worker 处理链路）；若只探 UI 面（A/B/C/D/F），Worker 未就绪不影响。
4. **日志目录**：诊断打点与审计日志默认在 logs/（ui-*.log / worker-*.log / audit 子目录按配置）。

## 5.1 执行备忘（窗口侧补，2026-09-07 勘察）

- **产物路径（选其一）**：
  - 现货单文件：`release/win-x64/PhotoPrivacy.exe`（mtime 2026-09-04 09:20，早于票 29–31，仅当接受旧版口径时用）；
  - 新鲜产物：运行 `scripts/release-readiness.ps1`（含 test+smoke+publish 完整 gate）产出含票 29–31 代码的新 exe——本机 CI-only 政策禁 agent 跑构建，publish 由用户亲自执行即不违反（政策约束 agent，不约束用户本机操作）。
- **config.json 现状（已核验）**：无 ui 节 → 启动即全默认（catppuccin / system / zh-CN / 侧栏 200）+ ExifTool 路径已配（D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe）+ 监控目录 D:\hot 已配，E2 探活链路就绪；首次改设置后自动写入 ui 节（ADR 0037 防抖 500ms）。
- **A4/A5 提示**：诊断打点落在 **logs/ui-YYYYMMDD.log**（该目录当前无 ui-*.log，首次运行 ThemeSwap 即产生）。

## 6. 结论（检查点 C — 终态）

- **检查点 A（探活清单）**：✅ 达成——§2 六面 28 项，每项含操作步骤+预期结果+源码锚点（§4 索引）。
- **检查点 B（用户回填）**：❌ 未达成——用户未执行。窗口侧持续核实（报告未被改动 / 无 ui-*.log 运行证据 / bin 与 release 产物时间戳均停留 2026-09-04 早于票 29–31），并向用户发起执行征询（含推荐路径与替代口径）未获应答。按 handoff 规则全部项如实标注「未探活」，零虚构。
- **检查点 C（汇总报告闭环）**：✅ 本报告终态化并移交大脑收口。处置建议（由大脑/用户裁定，窗口不代裁）：
  - (a) 用户日后取得新鲜产物（release-readiness.ps1）在真实桌面执行 §2 后回填 §3/§5，本票随即重开闭环（报告结构已备好，回填即用）；
  - (b) 或大脑裁定将「运行侧验证」顺延为下一轮独立票项，本票以「清单交付 + 未探活终态」关闭。

## 7. 移交清单（交大脑收口）

- 分支：arc-recovery/32-ui-manual-smoke（GitButler 虚拟分支，commit yqn 清单双轨 + snp §5.1 执行备忘 + 本终态 commit），未 push（§4.2 合规）。
- 代码：零改动（issue 32 口径「不动代码」遵守）。
- 证据：报告双轨逐字一致（.scratch 主本 = docs/process/reports/32 副本，字节级核验）；无虚构证据；无未登记产物。
- 风险：运行侧证据持续缺位（第七轮 28–31 均有云端 CI 背书，唯 32 的桌面探活无实物），consequence = 第六轮 round6-README「待用户裁定 3」的运行侧验证悬置，属已知且已书面登记的状态，非新发现。
