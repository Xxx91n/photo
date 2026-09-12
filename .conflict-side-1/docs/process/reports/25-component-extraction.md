# 报告 — 票 25 高频组件抽取（架构恢复第六轮）

- 日期：2026-09-04　窗口：票 25 工作窗　分支：arc-recovery/25-component-extraction（GitButler 虚拟分支）
- 启动器：prompts/25-component-extraction.md；必读清单已逐份读全（handoff-25 / issue-25 / spec.md / WORKFLOW.md / atomcode-report-gui-mental-models / ADR 0052 / ADR 0054 / ADR 0050）。
- Blocked by 现状：票 24（Shell+Pages 拆分）已在栈（arc-recovery/24-shell-pages-split），依赖满足；票 27 在途与其并行未圈入。
- 动栈前快照：node scripts/workflow-snapshot.js → D:/Aworker/photo-snapshots/20260904-172536（59 文件 / 274335 字节 + manifest SHA256，workflow-verify ZERO-LOSS 59/59）。
- Ursa PathPicker 先查库后引入：Irihi.Ursa 2.2.0 nuget 实物（D:/Aworker/photo/.nuget/packages/irihi.ursa/2.2.0）+ GitHub v2.2.0 tag 源码双核验（PathPicker : TemplatedControl，SelectedPathsText TwoWay / UsePickerType OpenFile|OpenFolder / FileFilter [Name, pattern…] / Title / ButtonContent / Command / IsOmitCommandOnCancel；Semi ControlTheme 内置 TextBox+Button 模板）。

## 1. 声明 → 证据 → 结论

| # | 声明（完成定义） | 证据 | 结论 |
|---|---|---|---|
| 1 | 检查点 A：5 处 Browse 路径行 → u:PathPicker（Ursa 2.2.0 已含），零残留 inline | ConfigPage.axaml <u:PathPicker ×5（ExifTool=OpenFile+FileFilter "[ExifTool, exiftool.exe, exiftool]" 与原 FilePickerFileType Patterns 逐字等价；HotFolder/Backup/Quarantine/AuditLog=OpenFolder，Title 各走原 dialog.select_* locale key）；grep 全 Views："x:Name=\"Browse"=0、swatch inline RadioButton=0；source-lint 守卫 Views_Must_Not_Contain_Inline_Browse_Path_Rows 绿 | ✅ |
| 2 | 检查点 B：4 个 nav 按钮 → NavButton | MainWindow.axaml controls:NavButton ×4（Config/Logs/Rules/ServiceManager），IconKind+Label+PageTag 声明式；shell 内 inline Button Classes="nav active"=0；守卫 Shell_Nav_Buttons_Must_Use_NavButton_Component 绿；active 视觉零漂移：NavButton 内部 PART_Button 挂 Classes="nav"，IsActive 映射 active class → 直接复用 AppTheme Button.nav / Button.nav.active（SemiColorPrimaryLight 背景 + 4px 左边框）原样式，无样式复制 | ✅ |
| 3 | 检查点 B：5 个 swatch → ThemeSwatch（数据化，保留 GroupName/持久化语义） | ThemeSwatchCatalog.Presets 唯一权威（5 预设 ThemeId/ColorHex/LabelKey 与原 5 个 inline RadioButton 逐字段一致）；ItemsControl 数据化渲染；GroupName=ThemePreset + Tag=ThemeId 迁入 ThemeSwatch 控件本体；SyncThemeSwatchSelection 改走视觉树 PART_Radio.IsChecked（Tag 语义不变）；守卫 Theme_Swatches_Must_Be_DataDriven_From_Catalog 绿 | ✅ |
| 4 | MainWindow（或对应 Page）净减 ~150 行 | MainWindow 整体（axaml+cs）：1699 → 1548，净减 151 行（axaml 139→115，cs 1560→1433：5 个 OnBrowse*Click 处理器整块删除 + FindControl 块删除 + TestPickHotFolderAsync 删除）；ConfigPage 整体：239 → 225（-14）；新增可复用控件 4 文件 225 行（Views/Controls/NavButton ×2 + ThemeSwatch ×2） | ✅（MainWindow 侧净减 151 ≈ ~150 目标；新增行为可复用组件资产，重复结构 10 处 → 0） |
| 5 | grep 断言 Views 内无重复 Browse 行结构 | 新增 3 条 source-lint（MainWindowShellSourceTests 尾部）：Browse 行零残留 + NavButton×4 + ThemeSwatchCatalog 数据化断言；守卫随形态迁移 2 处（Browse_Buttons_Use_MaterialIcons 改断言 u:PathPicker 存在 + FolderOpen 清零；MainWindow_Icon_Buttons_Must_Use_Icon_Variant_Class ≥7→≥2，Add/Remove 排除目录按钮保留） | ✅ |
| 6 | PathPicker 弹窗/回填行为与现一致 | 弹窗：UsePickerType/FileFilter/Title 逐字映射原 OpenFilePickerAsync/OpenFolderPickerAsync 参数；回填：SelectedPathsText TwoWay 绑定 VM 路径属性（等效原 Text 绑定 + files[0].Path.LocalPath 回写，Ursa UpdateSelectedPaths 用 TryGetLocalPath()??Name 同语义）；IsChecked 回填经 ItemsControl.Loaded 一次性重放（InitializeRuntime 先于首布局的时序保持）；TestStorageProvider/LastPickerTask seam 保留（ResolveStorageProvider 存活，供 OnAddExcludedDirectoryClick 与未来测试）；唯一语义精化：Ursa 点击按钮禁用期防重入（_button IsEnabled false），无行为损失 | ✅ |
| 7 | NavButton active 态视觉逐像素一致 | 复用原 AppTheme 样式链（Button.nav Height=40/Padding 12,0 + Button.nav.active SemiColorPrimaryLight/4px 左边框），NavButton 仅组合不重定义；SetNavButtonActive 语义迁移为 NavButton.IsActive 赋值（SetCurrentPage 同步自 VM CurrentPage 不变） | ✅（样式零复制，同源渲染） |
| 8 | ThemeSwatch 切换后 ThemeId 持久化与 5 色板行为零回归 | Click 路由：MainWindow.AddHandler(Button.ClickEvent) 于 ItemsControl 一处收口（程序化 IsChecked 不触发 Click = 原 Click 语义）；OnThemePresetSwatchClick 读 e.Source(RadioButton).Tag → vm.ThemeId → ApplyCommunityThemeResources → ScheduleDebouncedConfigApply 原链保留；config roundtrip：AppConfigRoundTripTests ThemeId 断言与 Integration 全绿背书 | ✅ |
| 9 | build 0 错误 0 SCS | dotnet build PhotoPrivacy.sln：0 错误；SCS=0（全解警告为既有 CA 系列；新增文件无 SCS 类别警告）；semgrep p/csharp + p/security-audit 0 findings | ✅ |
| 10 | 测试单槽串行 | Core 191/191 + Integration 321/321（test.runsettings MaxCpuCount=1，禁并行 CollectionBehavior 既有） | ✅ |

## 2. 关键改动清单

- 新增：Views/Controls/NavButton.axaml(.cs)（PART_Button Classes=nav + Icon/Label/PageTag/IsActive StyledProperty）、Views/Controls/ThemeSwatch.axaml(.cs)（PART_Radio GroupName/Tag + ThemeSwatchCatalog 数据目录 + Label 经 LocalizationService 解析且 CultureChanged 自刷新）。
- ConfigPage.axaml：5 处 TextBox+Browse inline 行 → u:PathPicker（Width=280 收敛为 AppTheme 新共享 class PathPicker.inline-input，与票 24 TextBox.inline-input 同构）；5 swatch 块 → ThemeSwatch ItemsControl。
- MainWindow.axaml：4 nav inline 结构 → NavButton 声明；MainWindow.axaml.cs：删 5 OnBrowse*Click + TestPickHotFolderAsync，nav 接线经 NavButtonControl，swatch 经 ThemeSwatchListControl.AddHandler(Button.ClickEvent)，SyncThemeSwatchSelection 视觉树回填 + Loaded 重放。
- 样式：AppTheme.axaml +PathPicker.inline-input（Width=280 单一权威随控件迁移）。
- 测试：MainWindowShellSourceTests +3 守卫；DesignSystemTests 2 处随形态迁移。
- ConfigPage.axaml.cs：访问器收敛（删 10 个 Browse*/Swatch* 访问器，+ThemeSwatchListControl）。

## 3. 过程事故登记（单独呈报）

1. **并行窗口覆盖事故（工作树级，非 GitButler 栈手术）**：本窗 15:20-15:28 写入的 MainWindow.axaml / ConfigPage.axaml / MainWindow.axaml.cs 编辑被并行在途窗口（票 26 i18n ToolTip/caption 改动，zz 未提交区）整文件覆盖回滚；4 个新控件文件因未跟踪幸存。处置：在同一工作树上按行级脚本重施加全部票 25 改动（他人的 tooltip hunk 正交保留未动），此后立即进入门禁+提交以压缩竞态窗口。此为 WORKFLOW §7 教训 4（同文件多票需显式 Blocked by）的实爆样本：波次表允许 25/26 并行，但两票文件面在 MainWindow.axaml/ConfigPage.axaml/AppTheme.axaml/DesignSystemTests.cs 四文件重叠，建议大脑后续在 issue 文件面标注或将同文件票串行化。
2. **一次误用裸 git stash**（违反 WORKFLOW §4.2 禁裸 git 写命令）：归因 PublishApp smoke 失败时误操作 git stash push 圈入他人在途文件，随即 git stash pop 完整恢复（stash list 清空、git diff --stat 复核 23 文件与恢复前一致），无内容损失。已登记不再犯。
3. **PublishApp smoke 全量瞬时失败 ×2**：单跑 ×2 + 手动 publish（与测试同参数）×1 均 EXITCODE=0；失败发生仅在全量套件运行时，结合此前 MSB3027 testhost 锁 DLL 事件，判定为并行窗口并发 build/test 的资源竞争（违反 WORKFLOW §5 多窗口不并发跑 test 纪律）。第三次全量 321/321 全绿实证。

## 4. 遗留与移交

- PathPicker 空 Watermark：HotFolder/Backup/Quarantine 原 TextBox PlaceholderText（默认路径提示）随 Ursa 模板不可直达（PathPicker 无 Placeholder 属性），空态提示由 ButtonContent=btn.browse + 行 desc 文本承接；如需逐字节视觉等价，票 26 token 纪律窗可评估给 PathPicker 补 Watermark attached 方案。
- TestStorageProvider seam 保留但当前无测试消费者（tests 全目录 grep 0 命中，实证）；TestPickHotFolderAsync 已随 OnBrowseHotFolderClick 删除（其唯一消费点），如未来需要 picker 注入测试建议基于 Ursa PathPicker 的 TopLevel.StorageProvider 或 VM 级 seam 重建。
- UI 手工冒烟（弹窗/回填/色板切换/导航 active 切换）移交用户或大脑收口窗。
