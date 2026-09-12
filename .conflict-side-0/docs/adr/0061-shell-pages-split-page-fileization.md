# ADR 0061: 页面文件化 + shell 最小化 — MainWindow 拆分为 Shell + Views/Pages 四页

`Status: implemented` — 票 24（架构恢复第六轮）落地；build 0 错误 0 SCS，Integration 311/311 单槽绿，Core 191/191。

## 背景

- MainWindow.axaml 单文件 637 行，配置/日志/规则/服务管理四页内联堆叠，三页同形 Grid 重复；VM-View 心智模型退化（GUI 架构调研第 6 轮：选型正确、消费端未拆分）。
- `Classes="h2"`（report-22 B 节违例）与 5 处内联 `Width="280"` 待收敛；`MainWindowServiceAdapters.cs` 存在 Service→View 直写（SetPauseResumeAvailability 写按钮 IsEnabled/Content、SetServiceButtons 写 4 枚按钮 IsEnabled、InitializeRuntime 直写暂停按钮）。
- 工业共识（atomcode GUI 心智模型调研，票 24 ROI 最高）：Shell 只留 chrome（标题栏/侧栏/GridSplitter/页引用），1 页 1 View 独立文件。

## 决策

- **D1 页面文件化**：`Views/Pages/{ConfigPage,LogsPage,RulesPage,ServiceManagerPage}.axaml`（各 ≤220 行，ConfigPage 211）+ code-behind 仅 InitializeComponent 与 internal 控件访问器（ConfigPage 28 行等）。页面根 `x:DataType="vm:MainWindowViewModel"`（DataContext 自 Window 继承），`AvaloniaUseCompiledBindingsByDefault` 下编译绑定零 AVLN 警告。
- **D2 shell 最小化**：`MainWindow.axaml` ≤180 行（现 139），仅保留自绘标题栏 + 侧栏 + GridSplitter + 4 页引用；IsVisible 页切换语义保留（本轮不迁导航形态，票 25/26 再演进）。
- **D3 事件接线单点收口**：全部事件订阅（+=）仍集中在 `MainWindow.axaml.cs`，经页面 internal 控件访问器（`*Control` 后缀）接线；页面 code-behind 禁止订阅（source-lint 锁定），避免页面生命周期短于订阅目标造成长活订阅泄漏。
- **D4 Service→View 直写改 VM 中转**：`MainWindowViewModel` 新增 `ServiceButtons`（ServiceButtonState 单一真相源）与 `PauseResumeAvailable`/`PauseResumeContent`（暂停/恢复可用性与派生文案）；adapter 与 InitializeRuntime 全部改写 VM 属性，ServiceManagerPage 四按钮与 shell 暂停按钮改 XAML 绑定消费。5 处直写（adapters 28/29/75-78 + MainWindow.axaml.cs 190-191）清零。
- **D5 Width=280 收敛**：5 处内联宽度收敛为 `AppTheme.axaml` 的 `TextBox.inline-input` 共享 class 单一权威；Views 目录禁止内联 `Width="280"`（source-lint 锁定）。
- **D6 h2 违例清零**：RulesPage 标题 `Classes="h2"` → `Classes="title"`（AppTheme 从未定义 h2）。

## Pages/ 目录规约（对票 25/26 生效）

1. 一页一文件：`Views/Pages/{Name}Page.axaml(+.cs)`；页根 UserControl 声明 `x:Class` 与 `x:DataType`。
2. code-behind 只允许：构造函数 InitializeComponent + internal 控件访问器（`*Control` 后缀）；禁止事件订阅（+=）、禁止业务逻辑、禁止服务依赖。
3. 事件订阅与跨页协调一律在 `MainWindow.axaml.cs` 收口；页面间不互相引用。
4. 行数预算：shell ≤180 行；单页 ≤220 行（`MainWindowShellSourceTests` 锁定）。
5. 样式只允许消费 AppTheme/DesignTokens class，禁止内联宽度/尺寸覆写（`Button_*`/`Width_280` guard 延续语义）。

## 影响与测试

- `MainWindowShellSourceTests`（14 断言）锁定上述全部约束；`DesignSystemTests`/`MainWindowSourceDiagnosticTests` 5+1 处断言随文件形态迁移（扫描 Views 目录聚合，语义不变）。
- 行为零回归面：AuditTailService 日志流绑定、中键滚动 2 ScrollViewer 挂载点（Config/ServiceManager）、GridSplitter 拖拽持久化、5 色板切换、10 语言切换——全部保持原处理器与绑定不变，仅接线目标改为页面控件访问器。
- `TextBox.Watermark` → `PlaceholderText`（Avalonia 12 新名，消 AVLN5001；行为不变）。

## 时效纠错（随票）

- AGENTS.md 技术栈「Avalonia UI 11」→「Avalonia UI 12.1.1」（csproj PackageReference 实为 12.1.1；spec Further Notes 指定随票 24 修正）。§IPC 规则 6 中「Avalonia 11.1.3 死锁」为 ADR 0035 历史事实，保留不改。
