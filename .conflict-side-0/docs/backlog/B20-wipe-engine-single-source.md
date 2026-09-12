# B20 — 规则引擎单一真相源（票19，架构恢复第五轮）

- **优先级**: 高　**来源**: 第五轮宏观评估候选①规则面板（spec.md / issues/19）

## 问题与验收

擦除引擎与规则面板双轨孤岛：`WipeStrategyResolver.Resolve` 内嵌 per-family 硬编码命令 switch，`FormatRuleRow.EffectiveArgs` 维护第二份复制表，两者互不消费 rules.json——专业模式勾选（保留 ICC / 删 MakerNotes 等）对 ExifTool 实际命令零影响。验收（issue 19 五项）：

1. WipeStrategyResolver 由规则集合生成命令，硬编码 per-family 表收敛为单一真相源
2. 删除 FormatRuleRow.EffectiveArgs 的 family→command 复制表
3. 用户勾选（如保留 ICC / 删 MakerNotes）真实反映到生成的 ExifTool 命令
4. 纯函数测试：给定规则集合 → 生成命令与勾选一致
5. 单槽相关测试绿；build 0 错 0 SCS；触及 ExifTool 参数面跑 semgrep

## 完成记录（2026-09-03，架构恢复第五轮票19）

- 落地：新增 `PhotoPrivacy.Core/Rules/WipeRuleEngine.cs`——唯一命令生成器（纯函数），两条入口：`Resolve(family, rules)`（bridge 路径，键名与 FormatRulesStore.Key schema 一致，缺键回退族安全默认）与 `BuildEffectiveArgs(family, 6 开关)`（面板行路径）；`WipeStrategyResolver.Resolve(path, rules)` 委托引擎，`ResolveFamily` 只留扩展名→族判定；`ExifToolCommandBuilder.BuildWipeTaskBlock(path, taskId, rules)` 规则重载（旧签名透传 null 保持兼容）；`ExifToolBridge` 构造注入 `wipeRules` 快照，`PooledExifToolBridgeFactory.BuildFromConfig` 透传；`MetadataCleanerWorker.CreateBridge` 从 `config/rules.json`（与 UI 同一 FormatRulesStore 存储）载入规则快照，bridge 重建（初启/ApplyConfig）时刷新。
- 安全不变量保留：RAW 族永不 `-all=`（MakerNotes 毁渲染，strip_all 展开为全部安全组）；JPEG `-all=` 后 ICC 保护与 `-tagsfromfile @ -colorspacetags` 配色恢复段受 preserve_icc 控制；Video 族 `-All=`/`-Time:All=` 大写语义与无 ICC 剥离；已知族全组不勾 → `no_rules` 跳过（空参数不空跑重写文件）；PDF/EPS 警告位不变。票 18 默认规则（30 键）下 8 族命令与 ADR 0053 M6a 表逐字一致（零行为漂移，测试锁定）。
- UI 侧：`FormatRuleRow.EffectiveArgs` 复制表（family switch）删除，改为 `WipeRuleEngine.BuildEffectiveArgs` 实时生成；6 个开关 setter 触发 `EffectiveArgs` PropertyChanged，DataGrid 预览列与实际命令同源联动。
- 测试：新增 `WipeRuleEngineTests`（Core.Tests，20 例：8 族默认/票 18 默认逐字复现、preserve_icc 勾选翻转、RAW 安全不变量、逐组组合、缺键回退、no_rules/unknown 跳过、BuildWipeTaskBlock 端到端）；`RulesPanelEffectiveArgsTests`（IntegrationTests，5 例：行预览==引擎输出、默认复现 ADR 0053、取消勾选真实改变命令+INPC 通知、Save→Load→引擎==面板预览端到端、逐组开关实时变化）。
- 门禁：全解决方案 build 0 错误 0 SCS；单槽 Core.Tests 183/183（含新增 20 例）；IntegrationTests 293/294（唯一失败 `MainWindowUninstallFlowSourceTests` 钉死 `Services/ServiceModeController.cs` 源模式，该文件为并行票 20 在途重构面，归属非本票；本票新增 5 例全绿）；semgrep `p/csharp` + `p/security-audit` 全 src 0 findings（与基线一致，无新增）。
- 设计裁决依据：spec.md「研究输入」atomcode 调研既有结论（大脑已跑）——Schema 在代码、默认在代码、用户文件只存差异、往返靠测试兜底；模块化单体 + Vertical Slice 内聚（规则面板↔擦除引擎单真相源），未引入新抽象。
- 报告：`.scratch/architecture-recovery/report-19-wipe-engine-single-source.md`（受控副本 `docs/process/reports/19-wipe-engine-single-source.md`）。
