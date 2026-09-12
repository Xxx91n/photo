# ADR 0055: GUI 侧栏导航分组 + 滚动看门狗 + 主题 i18n + 热目录路径修正

4 个 GUI 修复决策（A1-A4），每一项均经 atomcode 联网调研交叉验证行业心智模型后确认。

## A1: 侧栏导航分组统一（方案 A）

侧栏 4 个导航按钮 Height=44 在 XAML 层面已统一，但 Service Manager 动态 IsVisible 隐藏后主导航组留空位、底部 nav-action 按钮 Padding/Height 与导航项不一致、间距 SpaceXxs 过大。

**决策**：拆分主导航组（配置/日志/规则）+ 底部 utility 组（服务管理器+暂停/恢复）。所有项 Height=40 Padding=12,0 HorizontalContentAlignment=Stretch 全宽命中。间距降为 0-4px 连续。Service Manager 移至底部独立分组+分隔线，隐藏时整组消失，主导航位置稳定。

atomcode 调研：MD3 NavigationDrawer 规范 "命中区恒为全宽"、Fluent NavigationView FooterMenuItems、VS Code Activity Bar 连续无 gap。20 信源交叉验证。

## A2: 滚动看门狗修复最大化窗口卡顿（方案 A）

TopLevel.RequestAnimationFrame 在窗口最大化时被节流到实际渲染速率（Avalonia MediaContext.cs 源码证实：等待合成器提交期间 clock.Pulse 被跳过）。最大化→面积增大→每帧渲染贵→WM_SIZE 风暴→RAF tick 变稀→滚动"台阶式跳进"。

**决策**：RAF 主驱动 + Render 优先级看门狗定时器（RAF 停摆 >32ms 时保底推进）+ 墙钟 dt（时间基准，采样频率降低时指数曲线仍正确）。速度模型（指数平滑 k=15 dt 钳制 100ms）完全不动。看门狗与 RAF 同时启动：RAF 健康时 watchdog 空转跳过（近零开销），RAF 停摆时保底步进。

atomcode 调研：Avalonia MediaContext.cs 源码 + PR #18997 官方从 DispatcherTimer 迁到 RAF + Files.App 16ms DispatcherQueueTimer + Chromium 合成器线程。16 信源。纯 DispatcherTimer 在布局风暴中 Background 优先级饥饿，不可行。

## A3: 主题色板名称 i18n（方案 A）

LocalizationService.cs 中完全没有 theme.preset / preset.catppuccin 等 key。XAML 中 5 个 swatch TextBlock Text="Catppuccin" 等是硬编码字符串。

**决策**：加 6 个 i18n key（settings.theme_preset + preset.catppuccin/dracula/nord/onedarkpro/tokyonight），10 语言全覆盖。XAML 硬编码字符串改为 {ex:Localize preset.xxx}。

## A4: 热目录默认路径修正（方案 A）

两个问题：(1) ResolveDefaultHotFolder() 返回 MyPictures 系统图片目录，用户要求是软件运行目录下的 hot/（AppContext.BaseDirectory/hot），三端兼容——当前实现会直接选中用户图片导致损坏。(2) DefaultPaths.DefaultBackupDirectory 子目录名 .pp_backup 与 BackupPathResolver.DefaultBackupDirName "bak" 不一致——UI Watermark 误导用户（显示 .pp_backup 但实际备份到 bak/）。

**决策**：ResolveDefaultHotFolder() 改为 Path.Combine(AppContext.BaseDirectory, "hot")，三端兼容。DefaultPaths.DefaultBackupDirectory 子目录名对齐 BackupPathResolver.DefaultBackupDirName（bak）。AppConfig.Default.HotFolder 保持 string.Empty（ADR 0045 dynamic fallback 语义不变），只改 DefaultPaths 的 base 解析逻辑。config.sample.json 四目录保持空字符串不变。

## Consequences

- A1: 侧栏 XAML 重构约 25 行，AppTheme.axaml Button.nav 样式微调
- A2: MiddleClickScrollBehavior.cs 约 30 行改动，新增 Watchdog 字段 + StepScroll 提取
- A3: LocalizationService.cs 约 60 行 + MainWindow.axaml 10 行
- A4: DefaultPaths.cs 约 10 行，ADR 0045 dynamic fallback 不变
- 全部验收标准：编译通过 + dotnet test + publish + smoke test 进程存活
