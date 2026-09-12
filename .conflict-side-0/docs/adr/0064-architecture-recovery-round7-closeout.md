# ADR 0064 — 架构恢复第七轮收口（CI/CD 修复 + 组合根/DI + 窗口收敛 + 探活清单）

日期：2026-09-07 ｜ 状态：Accepted ｜ 来源：2026-09-04 宏观调查（gh run 全量取证 + actionlint v1.7.7 + atomcode 三引擎调研）

## 背景

第六轮收口后宏观调查判定两项：CI/CD 自 workflow 诞生起 28/28 全红 0 绿（根因 = release.yml 两处 GitHub Actions 表达式不支持的三元 `?:` → 整文件解析失败 → 每次 push 产生 0-job 无日志瞬时失败幽灵 run）；GUI 层 MainWindow code-behind 1433 行窗口级状态同步分散（缺组合根/DI 心智模型，atomcode Q1/Q2 结论：组合根 + SettingsService + VM 属性驱动三件套，工业模板 StabilityMatrix/Files.App/MarkSmith）。

## 决策

五张垂直切片票（28–32）串行波次（Blocked by 链 28→29→30→31，32 依赖 28+31）：

1. **票 28 CI/CD 修复**：三元改短路 `(cond) && '.exe' || ''`；workflow 按触发拆分 ci.yml（push/PR 测试门禁）+ release.yml（workflow_dispatch 手动发布，build/release job 加 `if: github.event_name == 'workflow_dispatch'` 守卫）；Verify structure 死检查转平铺校验。**对 ADR 0016 的部分修订**：测试门禁放开（push/PR 自动跑），发布仍纯手动——理由：幽灵 run + CI-only 政策使项目长期零云端测试证据；push 触发测试 + 手动发布为行业标准形态。返修三轮后云端绿（46 个 Windows 假设测试平台化 + Smoke 死过滤转活 [Trait] + 2 例历史测试竞态确定性修复）。
2. **票 29 组合根 + DI 容器**：新增 `Composition/AppComposition.cs` 唯一装配点（Microsoft.Extensions.DependencyInjection，9 服务单例注册 + BuildRuntimeOptions 收口）；App 经 `AppBuilder.Configure(() => new App(services))` 工厂持容器；MainWindow/MainWindowViewModel 构造注入，手写 new 依赖链清零；LocalizationService.Instance 收敛第一例（容器与静态同一实例，VM 恰留 1 处兜底 + CompositionRootSourceTests 锁定）。
3. **票 30 SettingsService + 镜像收敛**：三套 SelectionChanged/Sync*ComboSelection 手动镜像清零，ComboBox SelectedIndex TwoWay 绑定 VM 索引属性（ThemeVariantIndex/CurrentLocaleIndex/LogLevelIndex）；色板回填改 MultiBinding；MainWindow 1437→1158 行（-279）；LogLevelIndex 联动仅用户交互路径触发（保住 DiagnosticMode 独立字段语义）。
4. **票 31 轮询下沉**：版本轮询（1s）+ 服务状态轮询（3s）迁入 `WindowPollingHostedService : IHostedService`（ADR 0025 心跳同构）；装配破环——宿主服务只依赖 VM 单例 + 惰性版本源，服务模式轮询经 AttachServiceModePoll 委托由窗口挂载（Controller 构造需 MainWindow 视图适配器，直注成 MS DI 死环）；MainWindow 1158→1057 行。
5. **票 32 探活清单**：6 面 28 项手工探活清单（色板/语言/中键/拖宽/日志流/按钮反馈，逐项步骤+预期+源码锚点）；用户回填未发生，28 项如实「未探活」零虚构，运行侧验证悬置登记 backlog。

## 合并拓扑

main(53c669e) ← 406603e(28) ← f1c8ec1(29) ← f78a693(30) ← 0b51f86(31) ← 3d46d2b(32 docs)——四枝 --no-ff 按栈序零冲突；32 无代码改动，docs 副本随收口提交。

## 门禁证据

- CI 云端（CI-only 政策，唯一运行类证据）：28 = run 33945684509 SUCCESS（Core 183/183 + Integration 273/273）；29 = run 33959142272（191/191 + 305/305）；30 = run 33968573200（191/191 + 311/311）；31 = run 34071736168（191/191 + 317/317）。守卫增长链 273→305→311→317 与新增守卫（CompositionRoot 5 / SettingsVmSync 6 / WindowPolling 6）逐票吻合。
- 静态：git diff --check 空；快照 workflow-verify ZERO-LOSS 100/100（20260907-110731）；守卫文件实物在库（cat-file 实测）。
- 三层一致性：CONTEXT.md Manual Dispatch 条目已随票 28 修订；MainWindow SelectionChanged 6 处全为考古注释（代码 0 处）；AppComposition/WindowPollingHostedService 由本 ADR 首次记载。

## 返修与教训

- 28 返修 ×3：46 红灯 = Windows 假设测试跑 ubuntu + 死过滤（全仓 0 Trait，`Category!=Smoke&Category!=ExifTool` 过滤串自旧 release.yml 继承从未生效）+ 2 例 flaky；Smoke 5 类 + RealExifToolProbe 补 [Trait] 使过滤首次真实生效。
- 30 返修 ×1：新守卫 File.ReadAllText 命中「票 30：XXX 已删除」考古注释——source-lint 断言统一改 SourceLint.ReadStripped（剥 // 注释），拦截语义不放宽。
- 31 返修 ×1：CS0029 @ MainWindow:248——`Action<string?>` 委托 lambda 参数 `_` 绑定后吃掉 discard 槽位，体内 `_ = Task` 退化 Task→string 参数赋值；一行改 exePath 修复。
- 过程违规登记（历轮复核，不追认级）：28-fix 窗口自行推送验证分支 + 改测试文件未停手移交（V1/V2）；29「纯增分支非改写」失实 + 双轨哈希不同步；小口径偏差若干。详见 review-{28,28-fix,29,30,30-fix,31,31-fix,32}-*.md。

## 遗留（backlog，待用户裁定立票）

1. 运行侧探活（report-32 §2 清单 28 项，需用户真实桌面执行）；
2. 票 26/29/31 报告哈希与行数口径勘误（.scratch/docs 侧）；
3. CONTEXT.md 术语补全（Composition Root / WindowPollingHostedService / CI Gate 词条）；
4. Ursa Toast 反馈补缺（票 26 停手移交项）；
5. 静态单例逐票收敛（App.RuntimeOptions / UiDiagnosticLog / MainWindow 16 处 Instance 直引）；
6. 全量 DI 迁移（TrayHost/AuditTailService/ConfigFileWatcher/ServiceModeController 仍窗口手写 new）；
7. Smoke 测试 DLL-first 探测仅查 Debug 产物（CI 排除后不影响门禁）；
8. i18n 下拉文案刷新 hack（SelectionBoxItem null→restore）VM 选项集合化。
