# Plan: GUI 页面修复 — ADR 0052 实施路线图

> 基于 ADR 0052 的 7 个决策点（A1-A7），分 6 阶段 34 步实施。每步含验收标准和 test 闭环。

---

## 阶段 1: 侧栏导航统一 (A1) + 标题栏去重 (A4)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 1.1 | MainWindow.axaml 侧栏三 Button.nav 统一 Height=44, Padding="16,0,0,0" | 三按钮高度一致 44px | XAML lint: grep Height=44 in sidebar Buttons |
| 1.2 | 每按钮加 Material.Icons 20px 图标 (Settings/FileDocumentOutline/ServerNetwork) | icon 渲染可见 20px | 编译无 AVLN error |
| 1.3 | icon+text 加 StackPanel Orientation=Horizontal Spacing=12, text 用 .body class | 间距对齐 | 视觉检查 |
| 1.4 | AppTheme.axaml Button.nav.active 加 4px 左 accent bar (Border 或 BorderThickness) | active 态左侧蓝条 | style lint |
| 1.5 | Button.nav Padding 统一 (消除 12,8 vs 12,6 差异) | grep Padding 在 nav 样式唯一 | source-lint test |
| 1.6 | 标题栏删除 AppTitle + ModeLabel TextBlock (L24-28 区域) | 标题栏只留窗口按钮 | grep TitleBar 区域无 TextBlock binding ModeLabel |
| 1.7 | Window.Title 绑定 ModeLabel (OS 任务栏可见) | 标题栏文本=当前模式 | 编译+运行验证 |
| 1.8 | commit | git log 含此阶段 | — |

## 阶段 2: 中键滚动指数平滑 (A2)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 2.1 | MiddleClickScrollBehavior.cs 速度更新段: targetV 计算保留 (DeadZone/SpeedFactor/MaxSpeed) | 编译通过 | 单元测试 |
| 2.2 | 速度模型换指数逼近: v += (targetV-v)*(1-exp(-15*dt)), k=15 | 死区外无阶跃 | 模拟测试 |
| 2.3 | dt 钳制 ≤100ms (防窗口暂停大步) | dt>100ms 不跳 | 边界测试 |
| 2.4 | |v| < 1 停机 (指数衰减自然归零) | 松键后平滑减速 | 行为测试 |
| 2.5 | 死区加迟滞 (进入死区后阈值缩半) | 边界无抖动 | 模拟测试 |
| 2.6 | commit | — | — |

## 阶段 3: 主题色板网格 (A3)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 3.1 | AppConfig.UiOptions 加 string ThemeId = "catppuccin" | 编译通过 | record 字段测试 |
| 3.2 | ConfigEditCommand + ConfigEditor.UpdateConfig 映射 ui.theme_id | 持久化链路完整 | 配置读写测试 |
| 3.3 | config.sample.json 加 ui.theme_id 默认值 | JSON 解析无错 | schema test |
| 3.4 | 设置页加色板网格 (RadioButton+WrapPanel, 5 主题 swatch) | swatch 渲染可见 | 编译+视觉 |
| 3.5 | swatch RadioButton checked → ThemeId=tag + ApplyCommunityThemeResources(tag) 切主题文件(双轴独立，不覆盖 ThemeVariant) | 点击 swatch 主题立即变，明暗 ComboBox 选中态不变 | ThemeVariantSource 交互测试 |
| 3.6 | 5 主题文件各加 SemiColorSurfaceDim/Bright + ContainerLow/High + OnColor | MD3 角色完整 | grep token in 主题文件 |
| 3.7 | 深色基调 #121212 + Primary 去饱和到 70-80% | 视觉低饱和 | 视觉检查 |
| 3.8 | commit | — | — |

## 阶段 4: 侧栏拖拽调宽 (A5)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 4.1 | MainWindow.axaml ColumnDefinitions 200,* → 200,4,* | GridSplitter 列存在 | XAML lint |
| 4.2 | 加 <GridSplitter Grid.Column="1" ResizeDirection="Columns" /> | 拖拽生效 | 编译+交互 |
| 4.3 | Column[0] 加 MinWidth=170 MaxWidth=400 | 拖拽不超界 | 拖拽边界测试 |
| 4.4 | GridSplitter hover 高亮 (SemiColorBorder 120ms) | hover 变色 | style lint |
| 4.5 | UiOptions 加 double SidebarWidth = 200 | 编译通过 | record test |
| 4.6 | ConfigEditCommand + ConfigEditor 映射 ui.sidebar_width | 持久化链路 | 配置 test |
| 4.7 | DragCompleted → Math.Clamp(170,400) → 防抖 500ms 写盘 | 变更持久化 | 防抖测试 |
| 4.8 | 加载恢复 Math.Clamp(cfg.Ui.SidebarWidth, 170, 400) | 重启恢复宽度 | 加载 test |
| 4.9 | commit | — | — |

## 阶段 5: 目录默认路径 (A6) — UI 提示而非污染配置语义

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 5.1 | DefaultPaths.cs 加 DefaultBackupDirectory → <hotFolder>/.pp_backup | 路径三端兼容 | 单元 test |
| 5.2 | AppConfig.Default.Backup.Directory **保持 string.Empty**（ADR 0045 动态 fallback）；DefaultPaths.DefaultBackupDirectory 仅作 UI Watermark 源 | RuleEngineTests.Decide_Should_Create_Bak_Path 通过（基于 HotFolder 动态解析） | RuleEngineTests |
| 5.3 | config.sample.json 四目录**保持空字符串**（空=运行时动态解析，三端兼容） | JSON 解析无错 + 空 directory 不破坏 Validator | schema test |
| 5.4 | 配置页 TextBox 加 Watermark 绑定 XxxPathHint (显示当前默认路径) | 空时灰底提示 default | 编译+视觉 |
| 5.5 | 加 XxxPathHint 属性到 ViewModel (返回 DefaultPaths 值) | 绑定链路完整 | binding test |
| 5.6 | commit | — | — |

## 阶段 6: 托盘菜单验证 + 编译打包测活 (A7)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 6.1 | 验证 TrayHost.cs 保持 NativeMenu 不变 (零代码改动) | git diff 无 TrayHost 变更 | git diff check |
| 6.2 | 排查 Button:pointerover 是否缺少 :pressed 同款 TransformOperationsTransition | hover 无闪烁 | style lint |
| 6.3 | 如缺则补 TransformOperationsTransition 到 :pointerover | 闪烁消除 | 视觉验证 |
| 6.4 | dotnet build PhotoPrivacy.sln | 0 error | 编译 gate |
| 6.5 | dotnet vstest 全测试 | 全通过 | test gate |
| 6.6 | publish-app.ps1 win-x64 | release/win-x64/PhotoPrivacy.exe 存在 | publish gate |
| 6.7 | 启动 exe 测活 12s | 进程存活 PID 存在 Responding=True | 测活 gate |
| 6.8 | git diff --check (LF 规范) | 无 CRLF 违规 | LF gate |
| 6.9 | git add + commit | — | — |
| 6.10 | git push origin main | push 成功 | — |

---

## 验收总标准

- 编译: `dotnet build` 0 error
- 测试: `dotnet vstest` 全通过
- 打包: `release/win-x64/PhotoPrivacy.exe` 存在
- 测活: 启动后 12s 进程存活
- LF: `git diff --check` clean
- push: origin/main 更新
