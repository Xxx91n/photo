# Plan: GUI 修复 — ADR 0055 实施路线图

4 决策（A1-A4），6 阶段 22 步，每步含验收标准和 test 闭环。

## 阶段 1: 侧栏导航分组统一 (A1)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 1.1 | AppTheme.axaml Button.nav: Height 44->40, Padding 16,0,0,0->12,0 | grep Height=40 in nav style | source-lint |
| 1.2 | MainWindow.axaml 侧栏: 3 主导航按钮 Height=40 Padding=12,0 | 三按钮 Height 一致 | grep Height=40 |
| 1.3 | Service Manager 按钮 + PauseResume 移至 DockPanel.Dock=Bottom 分组 | 主组无动态项 | 代码检查 |
| 1.4 | 底部分组加分隔线 Border Height=1 | 视觉分隔 | 编译 |
| 1.5 | 主导航组间距 SpaceXxs->4px fixed (或 Spacing=0) | 间距连续无割裂 | 视觉检查 |
| 1.6 | commit | git log 含此阶段 | — |

## 阶段 2: 滚动看门狗 (A2)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 2.1 | 新增 Stopwatch _clock 墙钟 + AutoScrollState.Watchdog/LastStepMs 字段 | 编译通过 | 编译 |
| 2.2 | Extract StepScroll(long nowMs) 从 ScrollFrame 主体 | dt 来源改为墙钟 | diff 确认 |
| 2.3 | ScrollFrame 改为薄包装：续期 RAF + 调 StepScroll | RAF 行为不变 | 代码检查 |
| 2.4 | Watchdog_Tick: Render 优先级, RAF 停摆 >32ms 时保底步进 | 近零开销 | 编译 |
| 2.5 | OnPointerPressed: TopLevel 非 null 分支加 Watchdog.Start() | 同时启动 | 代码检查 |
| 2.6 | Stop(): 加 Watchdog.Stop() + null 清理 | 无泄漏 | 代码检查 |
| 2.7 | commit | — | — |

## 阶段 3: 主题色板 i18n (A3)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 3.1 | LocalizationService.cs 加 settings.theme_preset + preset.{catppuccin,dracula,nord,onedarkpro,tokyonight} 6 key x 10 语言 | 60 行 i18n | grep key count |
| 3.2 | MainWindow.axaml 5 swatch TextBlock Text 改为 {ex:Localize preset.xxx} | 无硬编码主题名 | grep TextBlock.*preset |
| 3.3 | settings.theme_preset 标签也绑定 i18n | 标题同步 | 编译+视觉 |
| 3.4 | commit | — | — |

## 阶段 4: 热目录路径修正 (A4)

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 4.1 | DefaultPaths.ResolveDefaultHotFolder() 改为 Path.Combine(AppContext.BaseDirectory, "hot") | 三端兼容 | 单元 test |
| 4.2 | DefaultPaths.DefaultBackupDirectory 子目录名 .pp_backup->bak 对齐 BackupPathResolver | Watermark 准确 | 交叉验证 |
| 4.3 | AppConfig.Default.HotFolder 保持 string.Empty (ADR 0045 不变) | dynamic fallback 语义不变 | RuleEngineTests |
| 4.4 | config.sample.json hot_folder 保持空字符串不变 | JSON 无错 | schema test |
| 4.5 | commit | — | — |

## 阶段 5: 编译 + 测试 + 打包 + 测活

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 5.1 | dotnet build PhotoPrivacy.sln | 0 error | build log |
| 5.2 | dotnet test 核心+集成 | 0 failed | test log |
| 5.3 | scripts/publish-app.ps1 -Zip false | release/win-x64/PhotoPrivacy.exe 存在 | publish log |
| 5.4 | smoke test: Start-Process + 13s + Responding=True | 进程存活 | smoke log |
| 5.5 | commit 修复 + push | origin/main 更新 | git log |

## 阶段 6: 文档更新

| 步骤 | 内容 | 验收标准 | test 闭环 |
|------|------|----------|-----------|
| 6.1 | CONTEXT.md 追加 4 术语 | 术语格式正确 | grep CONTEXT |
| 6.2 | git diff --check LF clean | 无 CRLF | diff --check |

## 验收总标准

- 编译 0 error
- 测试 0 failed
- publish 成功
- smoke test 进程存活 Responding=True
- git diff --check LF clean
