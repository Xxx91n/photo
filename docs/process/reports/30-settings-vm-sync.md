# 报告 — 票 30 SettingsService 单一真相源 + code-behind 镜像收敛（架构恢复第七轮）

- 日期：2026-09-05　窗口：票 30 工作窗　分支：arc-recovery/30-settings-vm-sync（GitButler 虚拟分支，WORKFLOW §4.2）｜change ID rqo（跨历史编辑稳定的稳定引用）；实物 commit 以 git log 为准（教训 3）——报告落盘时点观察 9c1b210（含本报告最终稿；报告哈希自引用不可能逐字等于最终 commit，以 git log 实物为准），未 push 不 PR（§4.2）
- 启动器：prompts/30-settings-service-vm-sync.md；必读清单 11 份已逐份读全（启动器 / handoff / issue / spec / WORKFLOW / ADR 0037 / 0040 / 0044 / 0047 / 0052 / 0062）。
- Blocked by 现状：票 29 已在 GitButler 栈上（a564f4e，组合根 + DI 容器骨架，CI 云端背书由大脑推送验证分支），阻塞解除，issue 30 ready-for-agent。
- 动栈前快照：本票未执行 GitButler 历史改写/丢弃类操作（无 move/undo/squash/discard/uncommit/branch delete/pull），§4.4 轨 2 无强制触发；保险起见已做快照 D:/Aworker/photo-snapshots/20260905-185145（87 文件 / 378783 字节，manifest 逐文件 SHA256）。

## 1. 声明 → 证据 → 结论

| # | 声明（完成定义 / issue checkbox） | 证据 | 结论 |
|---|---|---|---|
| 1 | 三套手动镜像（主题/语言/日志级别）迁移为 VM 属性驱动绑定 | ConfigPage.axaml 三 ComboBox 加 `SelectedIndex="{Binding …, Mode=TwoWay}"`（ThemeVariantIndex/CurrentLocaleIndex/LogLevelIndex）；MainWindow.axaml.cs 删 3 个 SelectionChanged 订阅 + OnThemeVariantSelectionChanged/OnLogLevelSelectionChanged/OnLocaleSelectionChanged 三处理器 + SyncThemeVariantComboSelection/SyncLogLevelComboSelection/SyncLocaleComboSelection 三回填方法；UI→VM 与 VM→UI 两向镜像均由绑定承担（spec 研究输入 Q1：设置单一真相源在 VM） | ✅ |
| 2 | 12+ 处 Sync*/SelectionChanged 手动镜像清零 | 新守卫 SettingsVmSyncSourceTests（6 例）锁定：3 处理器名、3 Sync 方法名、_pendingThemeSwatchSelection/_pending+Loaded 重放族、`ComboBoxControl.SelectionChanged +=` 订阅形态全清零；色板第四套回填（SyncThemeSwatchSelection/_pending/OnThemeSwatchListLoaded/TryApplyThemeSwatchSelection）同步删除，由 ConfigPage MultiBinding（ThemeSwatchSelectionConverter：VM.ThemeId ↔ 色板 ThemeId 比较）驱动 | ✅ |
| 3 | 检查点 B：code-behind 行数下降（git blob 实物基准） | MainWindow.axaml.cs：基线 1437 行（HEAD a564f4e blob 实测）→ 本票 1158 行，**-279 行（-19.4%）**；`git diff --stat` 实物：7 文件 176 insertions / 330 deletions（净 -154 行，含 VM +91 行联动迁移与新增测试文件另计） | ✅ |
| 4 | 行为不变（守卫测试全绿） | 本机 CI-only 政策零 build/test 运行；静态门禁（与守卫同构口径）全绿：9 文件字符串感知括号平衡 0 偏差、LF 无 CRLF、BOM 仅 MainWindow.axaml.cs 既有基线（git blob 比对一致，无新增）、HardcodedChineseScanTests 规则复核（新增中文全在注释内，C# 仅扫注释外字符串字面量、AXAML 剥注释后扫属性值——均不触发）、被删成员全仓 grep 零残留引用、守卫锁定面（LocalizationServiceTests Force/CultureChanged-handler/ThemeSwatchCatalog/u:PathPicker×5/页行数等）逐条比对未触碰。**CI 云端背书由大脑推送验证分支确认** | ✅（静态口径；云端待大脑） |
| 5 | 报告双轨沉淀 | 本文件 + docs/process/reports/30-settings-vm-sync.md 副本随 commit | ✅ |

## 2. 关键改动清单（9 文件：7 改 + 2 新增）

**UI 侧（绑定化）**
- 修改 src/PhotoPrivacy.Ui/Views/Pages/ConfigPage.axaml（207→218 行）：三 ComboBox 加 SelectedIndex TwoWay 绑定；色板 ItemTemplate 加 MultiBinding（`$parent[ItemsControl].DataContext` + `ThemeId` → ThemeSwatchSelectionConverter）驱动 ThemeSwatch.IsChecked。
- 修改 src/PhotoPrivacy.Ui/Views/Controls/ThemeSwatch.axaml（17→18 行）：PART_Radio.IsChecked 单向绑定宿主 IsChecked（OneWay——写回走既有 Click 事件转发，票 25 语义保留）。
- 修改 src/PhotoPrivacy.Ui/Views/Controls/ThemeSwatch.axaml.cs（120→133 行）：新增 IsChecked StyledProperty（bool，绑定面）；删除 ThemePresetRadioControl 内部访问器（唯一调用方 TryApplyThemeSwatchSelection 已删，全仓 grep 零残留）。
- 修改 src/PhotoPrivacy.Ui/Views/Pages/ConfigPage.axaml.cs：删除无引用的 LocaleVariantComboBoxControl 访问器。

**VM 侧（单一真相源 + 联动收口）**
- 修改 src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs（439→528 行）：
  - 新增 ThemeVariantIndex（get 归一化映射 0/1/2，与原 Sync 回填的 OrdinalIgnoreCase 语义一致；set → ThemeVariant，RequestedThemeVariant 副作用既有）；
  - 新增 LogLevelIndex（get 归一化未知回落 1=info，与原 SyncLogLevelComboSelection 的 SelectedIndex=1 兜底一致；set 承载 LogEnabled/ShowDetailedEvents 联动——原 OnLogLevelSelectionChanged 行为迁入，且**仅在用户交互路径触发**，启动/热重载直接赋 LogLevel string 不联动，保住 DiagnosticMode 独立字段语义）；
  - 新增 CurrentLocaleIndex（10 语言静态顺序表 LocaleOrder 与 AXAML ComboBoxItem 逐位对应；未知回落 0=zh-CN，与原 SyncLocaleComboSelection 一致）；
  - CurrentLocale setter 加 `_localization.SwitchLocale(value)`（原 OnLocaleSelectionChanged 手动调用迁入 VM；ADR 0044 决策 8 / ADR 0047 A6 持久化链路「启动时读取 → Initialize(locale)」由此贯通，SwitchLocale 自带防重入幂等）。
- 新增 src/PhotoPrivacy.Ui/ViewModels/ThemeSwatchSelectionConverter.cs（34 行）：IMultiValueConverter，Convert 比较 vm.ThemeId 与色板 ThemeId（OrdinalIgnoreCase），VM 未就绪返回 false 不抛绑定错误；MultiBinding 为 OneWay，无 ConvertBack 业务。

**窗口侧（只剩事件转发）**
- 修改 src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs（1437→1158 行）：
  - 删 3 个 SelectionChanged 订阅与 3 处理器；删 3 个 Sync 回填 + 色板 pending 族（4 成员）；OnThemePresetSwatchClick 简化为纯转发（vm.ThemeId = tag）；
  - 主题应用迁 OnViewModelPropertyChanged 响应（ThemeVariant/ThemeId 任一变化 → App.ApplyCommunityThemeResources(ThemeId, applyDark)，幂等；启动 L171/172 与热重载 L1271/L1294 显式调用保留——双路径全覆盖）；
  - 死代码清理 5 处（全仓 grep 无引用实物）：OnApplyConfigClick（ADR 0037 按钮移除后遗留 67 行）、ApplyConfigForCurrentModeAsync、ReadComboItemString、ReadThemeVariantSelection、窗口私有 NormalizeExifToolStatus 副本（唯一权威在 ServiceModeController）；`using Avalonia.VisualTree` 随 GetVisualDescendants 使用清零移除。

**守卫侧（迁移 + 新增）**
- 修改 tests/…/MainWindowConfigHotReloadSourceTests.cs：OnApplyConfigClick 存在断言迁移为防抖链锁定（ScheduleDebouncedConfigApply → ApplyConfigImmediatelyAsync → ConfigEditor.UpdateConfig → ReloadConfigAsync → ApplyRuntimeConfigToUiState() → msg.auto_saved），并反向锁定死代码不回潮（DoesNotContain OnApplyConfigClick/ApplyConfigForCurrentModeAsync）。
- 新增 tests/…/SettingsVmSyncSourceTests.cs（6 例 source-lint，票 30 守卫）：镜像处理器/回填清零、ConfigPage 绑定落位、VM 索引属性 + 联动副作用、ThemeSwatch IsChecked 绑定面、检查点 B 行数阈值（≤1365 = 基线 1437 放宽 5% 防 CI 平台差异，实际 1158）。

## 3. 设计裁决与口径

- **SelectedIndex 而非 SelectedValue/选项集合**：三个绑定索引属性的 get 侧归一化（OrdinalIgnoreCase、未知回落默认项）与被删 Sync* 回填语义逐字一致；若走 SelectedValue 绑定 string Tag，config 中非规范大小写将匹配失败（下拉空选）——行为差异不可接受。VM 选项集合方案（I18n 文案刷新也绑定化）因 SelectionBoxItem 刷新为运行时行为、CI 无法验证而放弃（真实验证载体是票 32 桌面探活），本票守卫 ForceComboBoxSelectionBoxRefresh 原样保留（LocalizationServiceTests 既有断言零迁移）。
- **联动副作用分层**：日志级别联动放 LogLevelIndex（用户交互入口）而非 LogLevel string setter——启动路径 vm.LogEnabled = DiagnosticMode（独立 config 字段）先于 vm.LogLevel 赋值，若联动放 string setter 会覆盖 DiagnosticMode（行为变化）。语言 SwitchLocale 联动放 CurrentLocale setter：启动赋值贯通 ADR 0047 A6 链路（config locale ≠ 系统语言时 UI 文案随 config——文档化意图，报告披露此口径修正；SwitchLocale 幂等，多数用户零差异）。
- **色板 OneWay MultiBinding + Click 转发**：写回若走 ConvertBack 需在回调中还原色板 ID（ConvertBack 拿不到源值，参数不可绑定），复杂度不成比例；保留票 25 Click 路由转发（符合票面「code-behind 只剩事件转发」），读向回填绑定化消除 _pending+Loaded 重放 hack（绑定系统天然处理容器延迟生成）。
- **i18n 下拉文案刷新（RefreshI18nComboBoxItems/Force/两字典）保留**：它不是「选中状态镜像」（是文案本地化刷新），且有守卫锁定为工业模式；随 ComboBoxItem 静态 items 形态原样保留，行为零变化。
- **主题应用幂等双路径**：PropertyChanged 响应 + 启动/热重载显式调用并存，重复应用结果一致（ApplyCommunityThemeResources 幂等；UiDiagnosticLog ThemeSwapMs 诊断日志在启动与热重载时可能多一条，无用户可感知差异——披露）。

## 4. CI-only 边界与验证声明

- 本机零 build/test/lint 运行（CI-only 政策）；第 4 项声明的「全绿」为静态复核口径（括号平衡/LF/BOM/守卫锁定面逐条程序化比对），不替代 CI 云端证据。
- 需大脑推送验证分支触发 workflow：预期 Core + Integration 全绿。新增守卫 6 例 + 迁移守卫 1 例随行。
- 若 CI 红，预期红点集中在：(a) Avalonia 12.1.1 对 `$parent[ItemsControl].DataContext` 编译绑定路径的接受度（AVLN2000 系）——Avalonia 支持 $parent[type] 语法，静态预检通过；(b) 新守卫字符串与实际源的口径差（已在落盘后逐字核对）；(c) MultiBinding 运行时行为（色板回填）——静态不可证，票 32 桌面探活兜底。返修启动器交大脑按 .scratch 流程处理。

## 5. 与并行窗口的隔离（WORKFLOW §4.3）

- 本票触碰面：MainWindow.axaml.cs / MainWindowViewModel.cs / ConfigPage.axaml(.cs) / ThemeSwatch.axaml(.cs) / ThemeSwatchSelectionConverter.cs（新）/ 两个测试文件——均在票 30 票面定义的三套镜像消费链内。
- 未触碰：AppComposition.cs / Program.cs / App.axaml.cs（票 29 在途）、ConfigFileWatcher.cs / ConfigEditor.cs（票 27 收口）、ServiceModeController.cs / AppTheme.axaml / DesignTokens.axaml（票 24/25/26 产物）。
- MainWindow.axaml.cs 为跨票共享文件：本窗开工时栈上无他票在途提交（票 29 已收口、工作区 clean），§4.3 一次一票满足。

## 6. 遗留（逐票收敛 / 后续票）

- MainWindow 内其余 LocalizationService.Instance 直引（CultureChanged handler / RefreshI18nComboBoxItems / ApplyConfigImmediatelyAsync 等）——spec Out of Scope 静态单例逐票收敛，票 29 已收敛 VM 入口，本票不动窗口入口。
- 设置真相源若要彻底服务化（独立 ISettingsService 取代 VM 属性承载），需迁移 _configProperties 防抖面 + ConfigFileWatcher 回放链——超出本票「镜像收敛」票面，留待后续票。
- 三套下拉的 i18n 文案刷新 hack（SelectionBoxItem null→restore）保留原样；若后续票做 VM 选项集合绑定化可一并消除，验证载体为票 32 桌面探活。
