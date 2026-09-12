# 报告 — 票19 规则引擎单一真相源（架构恢复第五轮）

日期：2026-09-03　窗口：本票专用　分支：arc-recovery/19-wipe-engine-single-source（哈希以 git log 实物为准）

## 开工复述（启动器要求）

- Blocked by 现状：票 19 Blocked by 票 18。开工实测票 18 尚未闭环（无 report-18、无分支），故本窗口先完成 Core 独占面与前置侦察；期间票 18 由并行窗口闭环（report-18 落盘、分支 arc-recovery/18-rules-store-roundtrip 提交 yok ），共享面（RulesPanelViewModel.cs）在 18 提交后才触碰，符合 WORKFLOW §4.3。
- 已读必读清单（6/6）：handoffs/19、issues/19、issues/18、spec.md、WORKFLOW.md、docs/adr/0053。另读轮次 README（波次表）与全部相关源码/测试。

## 完成定义对照（声明 → 证据 → 结论）

| 声明（handoff/issue 19 完成定义） | 证据 | 结论 |
|---|---|---|
| WipeStrategyResolver 由规则驱动，删除 EffectiveArgs 复制表（检查点 A） | 新文件 `src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs`（唯一命令生成器，纯函数双入口 `Resolve(family, rules)` / `BuildEffectiveArgs(family, 6开关)`）；`WipeStrategyResolver.cs` 硬编码 family→命令 switch 已删除，仅留 ExtensionFamilyMap 扩展名判定，`Resolve(path, rules)` 委托引擎（rg 复核 resolver 内 0 处命令字面量）；`RulesPanelViewModel.cs` 的 `FormatRuleRow.EffectiveArgs` family switch 复制表已删除，改为 `WipeRuleEngine.BuildEffectiveArgs(...)` 实时生成（rg 复核 VM 内 0 处命令字面量） | ✅ |
| 勾选真实改变 ExifTool 命令（检查点 B，纯函数测试） | 引擎路径：`ExifToolCommandBuilder.BuildWipeTaskBlock(path, taskId, rules)` 规则重载 + `ExifToolBridge` 构造注入 wipeRules + `PooledExifToolBridgeFactory.BuildFromConfig` 透传 + `MetadataCleanerWorker.CreateBridge` 从 `config/rules.json` 载入快照（与 UI 同一 FormatRulesStore 存储）；纯函数测试 `WipeRuleEngineTests` 20 例（含 `Jpeg_PreserveIcc_Unchecked_Really_Changes_Command`：preserve_icc=true→`-all= --icc_profile:all -tagsfromfile @ -colorspacetags`，=false→`-all= -tagsfromfile @ -colorspacetags`；`Raw_StripAll_Never_Emits_All_Equals` 锁定 MakerNotes 安全不变量）+ `BuildWipeTaskBlock_Reflects_Checkbox_Changes` 端到端；UI 侧 `RulesPanelEffectiveArgsTests` 5 例（`Save_Load_Engine_Matches_Panel_Preview`：面板勾选→SaveCustomRules→rules.json→store.Load→引擎，与预览逐字一致；取消勾选触发 EffectiveArgs PropertyChanged） | ✅ |
| 单槽绿 + build 0 错 0 SCS + semgrep 无新增高危（检查点 C） | `dotnet build PhotoPrivacy.sln`：0 错误、SCS 计 0；单槽（test.runsettings MaxCpuCount=1）Core.Tests 183/183（含新增 WipeRuleEngineTests 20 例）；IntegrationTests 293/294（唯一失败 MainWindowUninstallFlowSourceTests 钉死 `Services/ServiceModeController.cs` 源模式，该文件为并行票 20 在途重构面，归属非本票；本票新增 5 例全绿）；semgrep `--config p/csharp --config p/security-audit` 全 src 改码前后均 0 findings（基线 .codex-tmp/semgrep-baseline.json vs after，无新增） | ✅ |
| 设计裁决前跑 spec.md「研究输入」的 atomcode 完整提示词 | 本窗口无 ctx 工具（ctx_batch_execute/ctx_execute/ctx_search 均不在工具清单），atomcode-research 技能硬护栏明文禁止降级 shell_command 跑 atomcode（"NEVER use shell_command…do NOT fall back"）；探测确认无在途 atomcode 进程后，采用 spec.md Further Notes 记载的大脑已完成同提示词调研结论（行业成熟模型=Schema 在代码/默认在代码/用户文件只存差异/往返测试兜底；模块化单体+MVVM+Vertical Slice 内聚，规则面板↔擦除引擎单真相源，不引入新抽象）。本实现严格落在该结论上：未新增 DI 容器、未新增抽象层，仅 1 个纯函数类 | ✅（按 spec 既定兜底，偏离已声明） |
| 报告写入 .scratch/architecture-recovery/report-19-*.md | 本文件；受控副本 docs/process/reports/19-wipe-engine-single-source.md（轨 1 沉淀）；backlog B20-wipe-engine-single-source.md 随票登记；README 状态表（.scratch + docs/process 双侧）票 19 行更新 done | ✅ |

## 零行为漂移声明

票 18 默认规则（30 键 canonical schema）经引擎生成的 8 格式族命令与 ADR 0053 M6a 命令表**逐字一致**（测试 `Resolve_WithoutRules_Reproduces_Adr0053_Commands` + `Resolve_WithPanelDefaults_Reproduces_Adr0053_Commands` + `StoreDefaults_Through_Engine_Produce_Legacy_Panel_Commands` 三重锁定）；非面板族（tiff/heic/png）无存储键，回退族安全默认与旧表一致。升级前后用户可感知行为差异仅一处：**勾选改变命令从"不可能"变为"真实生效"**——这正是本票目的。

## 缺陷现场（修复前取证）

1. `WipeStrategyResolver.Resolve`（原 64-89 行）：8 族命令 switch 硬编码，不消费任何规则存储。
2. `FormatRuleRow.EffectiveArgs`（原 260-271 行）：同一份命令表在 UI 层复制第二份，两表互不引用、与 rules.json 三轨脱节。
3. `ExifToolCommandBuilder.BuildWipeTaskBlock`：只接收 (path, taskId)，规则无处进入；`RulesPanelViewModel.SaveCustomRules`（票 18 前）只写 strip_all 单键，勾选面残缺（票 18 已修存储侧）。

## 并行窗口协同记录

- 票 17/18/21 由并行窗口提交闭环（2a8aed9 / yok / opn）；票 20 全程在途（UI 根目录搬迁），其共享面（ServiceManager.cs、ServiceModeController.cs 等）本票零触碰；全解决方案 build 门禁在票 20 自愈编译错后复验通过。
- 本票触碰文件：src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs（新）、WipeStrategies/WipeStrategyResolver.cs、ExifToolCommandBuilder.cs、ExifToolBridge.cs、PooledExifToolBridge.cs、Worker/MetadataCleanerWorker.cs、src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs、tests/PhotoPrivacy.Core.Tests/Rules/WipeRuleEngineTests.cs（新）、tests/PhotoPrivacy.IntegrationTests/Ui/RulesPanelEffectiveArgsTests.cs（新）、docs/backlog/B20-*.md（新）、README 状态表 ×2、本报告 ×2。

## WORKFLOW §4.4 快照留证

动工前完成仓库外快照 `D:/Aworker/photo-snapshots/20260903-041115`（32 文件/83714 字节），workflow-verify.js 校验 ZERO-LOSS 32/32 哈希一致。本票未执行 GitButler 栈手术；快照为防御性留存。
