# 报告 — 票18 规则存储完整往返（架构恢复第五轮）

日期：2026-09-03　窗口：本票专用　分支：arc-recovery/18-rules-store-roundtrip（哈希以 git log 实物为准）

## 完成定义对照（声明 → 证据 → 结论）

| 声明（handoff 完成定义） | 证据 | 结论 |
|---|---|---|
| SaveCustomRules 持久化全部逐组开关（StripAll/StripExif/StripXmp/StripIptc/StripTime/PreserveIcc） | RulesPanelViewModel.SaveCustomRules 重写：每行写 6 键，键名 FormatRulesStore.Key(row.FamilyKey, group)，group 覆盖 strip_all/preserve_icc/strip_exif/strip_xmp/strip_iptc/strip_time；FamilyKey 取枚举名小写，杜绝旧实现 FamilyName.ToLowerInvariant() 产生的 eps/ps_strip_all 斜杠键；测试 SaveCustomRules_PersistsEveryGroupToggleForEveryRow 断言 5 行 × 6 组 = 30 键逐值一致（修复前红灯 Actual 5 键） | ✅ |
| LoadRules 修复键名错配（raw_strip_exif_xmp_iptc）并用 defaults 正确回填行值 | 错配行删除：Video 行 IsEnabled 曾误读 RAW 族键（脚本核验修复后源内 0 处代码引用）；新增 BackfillFromStore(stored)：每行 6 组开关按自有族键覆盖，GetValueOrDefault(key, 行编码默认) 缺键回落；测试 LoadRules_BackfillsRowValuesFromStoredFile_WithoutCrossFamilyLeak（种子 video_strip_all=false + jpeg_strip_xmp=true，断言回填命中且无跨族泄漏，修复前红灯）与 SaveThenNewViewModel_RestoresEveryToggle（新实例模拟重启，30 开关逐值一致，修复前红灯） | ✅ |
| Save→Load 往返忠实（写读一致，无字段丢失） | FormatRulesStore 规范 schema：FamilyDefaults 5 族 × 6 组 = 30 键权威默认（镜像面板行编码默认）+ Key(familyKey, group) 统一拼装；测试 RulesStore_RoundTrip_FullSchemaSaveLoadIdentical（30 键异构值 Save→Load 字典全等）、LoadDefaults_CoversEveryPanelFamilyAndGroup（30 键覆盖 + 无斜杠键，修复前红灯 missing jpeg_strip_exif）；既有 RulesStore_RoundTrip_SaveLoadReset、RulesStore_LoadingMissingFile_ReturnsDefaults 回归绿 | ✅ |
| 单槽相关测试绿 + build 0 错 0 SCS | dotnet build PhotoPrivacy.sln：0 错误、grep SCS 计 0（CA 类既有警告与本票无关）；dotnet vstest 单槽（test.runsettings MaxCpuCount=1）RulesPanelViewModelTests 10/10；全量回归 Core.Tests 151/151 + IntegrationTests 289/289；semgrep scan --config p/csharp 两份被改源文件 0 findings | ✅ |
| 完成后写报告（声明→证据→结论对照） | 本文件；受控副本 docs/process/reports/18-rules-store-roundtrip.md | ✅ |

## 缺陷现场（修复前取证）

1. Save 只存 strip_all（RulesPanelViewModel 原 114-123 行）：SaveCustomRules 仅写 {FamilyName.ToLowerInvariant()}_strip_all，六组开关丢五组；"EPS/PS" 生成 eps/ps_strip_all 斜杠键。
2. Load 键名错配（原 83 行）：Video 行 IsEnabled = defaults.GetValueOrDefault("raw_strip_exif_xmp_iptc")——读 RAW 族键；IsEnabled 本非持久化字段。
3. defaults 无回填（原 67-98 行）：行逐组开关全部硬编码，_store.Load() 结果除错配行外未用于任何行值；FormatRulesStore.LoadDefaults 仅 6 键且含旧命名（raw_strip_exif_xmp_iptc / video_strip_all_time / pdf_requires_warning），与面板六组 schema 不对齐。

## 修复设计（expand——为票19 规则引擎单一真相源铺路）

- schema：{family}_{group} 布尔键；family ∈ jpeg/raw/video/pdf/eps（面板 5 行枚举族名小写）；group ∈ strip_all/preserve_icc/strip_exif/strip_xmp/strip_iptc/strip_time；共 30 键。
- 默认值：FormatRulesStore.FamilyDefaults 按面板行编码默认镜像（JPEG strip_all+preserve_icc；RAW strip_exif+strip_xmp+strip_iptc；Video strip_all+strip_time；PDF/EPS strip_all）。
- 回填方向：LoadRules 先按行编码默认构造行，再 BackfillFromStore 用存储值覆盖（GetValueOrDefault(key, 行默认)），缺键/损坏文件天然回落默认。
- 不持久化字段：IsEnabled / IsDangerous / RequiresUserWarning（危险族折叠进 expert gate；除解除错配外无行为变化）。
- 旧键一次性弃用：raw_strip_exif_xmp_iptc、video_strip_all_time、pdf_requires_warning 三个旧命名键移除，无迁移逻辑（rg 全仓 0 消费方；rules.json 用户本地可再生成，重存/Reset 即收敛新 schema）；jpeg_strip_all / jpeg_preserve_icc / pdf_strip_all 键名原样保留，旧存量文件对这些键的取值仍被读取。

## 红绿对照（TDD 留证）

- 先红：10 测试中 4 红 6 绿——SaveThenNewViewModel_RestoresEveryToggle（Expected False / Actual True）、SaveCustomRules_PersistsEveryGroupToggleForEveryRow（Expected 30 / Actual 5）、LoadDefaults_CoversEveryPanelFamilyAndGroup（missing default key jpeg_strip_exif）、LoadRules_BackfillsRowValuesFromStoredFile_WithoutCrossFamilyLeak（Assert.False 失败）；4 红灯全部命中上述缺陷现场，非编译错误。
- 后绿：同一测试类 10/10 通过（562 ms）。

## 门禁留证

| 门禁项 | 命令 | 结果 |
|---|---|---|
| build | dotnet build PhotoPrivacy.sln | 0 错误 |
| SCS | 同上输出 grep SCS | 0 |
| 相关测试（单槽） | dotnet vstest IntegrationTests.dll --settings:test.runsettings --TestCaseFilter RulesPanelViewModelTests | 10/10 |
| 全量回归（单槽） | dotnet vstest Core.Tests.dll + IntegrationTests.dll --settings:test.runsettings | 151/151 + 289/289 |
| semgrep | semgrep scan --config p/csharp --json 两份被改源文件 | 0 findings |
| EOL/编码 | node 落盘校验 | 三份改动文件 UTF-8 无 BOM、LF、特征片段齐全 |

## 偏差与披露

- 本窗口无 ctx 工具：按启动器降级条款用内置读取完成必读清单，目标不变。
- 文件写入按 handoff 硬约束全部经 node.js 脚本（.codex-tmp/t18-*.js，gitignored，收口清理）；首条 heredoc 嵌套踩壳层截断坑（脚本 8090/11800 字节截断），按 WORKFLOW §7-1 教训立即改为 Write 工具落盘脚本，产物经字节数/特征片段/BOM/CRLF 四重校验。
- handoff 建议技能 $implement / $code-review 不在本窗口技能目录，按其精神手动执行：先红后绿 TDD + 收尾逐条对照完成定义留证。
- IsEnabled 行为披露：修复前 Video 行 IsEnabled 实由 RAW 键驱动（错配缺陷，且 Save 从不写该键，实际旧文件下恒为禁用）；修复后固定默认启用。此为缺陷修复的必然结果，非隐藏行为变更。
- 测试侧拼键独立于被测实现（FamKey 从枚举推导），防同源盲区。
- 动栈声明：本票仅 but commit 新建独立分支（GitButler 正常提交流程），未执行 move/undo/squash/discard/uncommit/branch delete/pull 等栈手术，按 WORKFLOW §4.4 触发清单无需轨 2 快照。
- 并行窗口披露：工作区存在票17 在途未提交改动（AppConfigJson.cs / AppConfigLoader.cs / config.sample.json audit 段已对齐 diagnostic_mode），与本票文件面零重叠，本票提交严格只选票18 文件。

## 提交物清单

- src/PhotoPrivacy.Core/Configuration/FormatRulesStore.cs — 30 键规范 schema + Key() + FamilyDefaults
- src/PhotoPrivacy.Ui/ViewModels/RulesPanelViewModel.cs — SaveCustomRules 全 6 组持久化 + BackfillFromStore 回填 + 键名错配修复 + FormatRuleRow.FamilyKey
- tests/PhotoPrivacy.IntegrationTests/Ui/RulesPanelViewModelTests.cs — +4 往返测试（先红后绿）
- docs/backlog/B19-rules-store-roundtrip.md — B19 立票（票17 先占 B18，顺延）+ 完成记录
- docs/backlog/README.md — 追加第五轮 B18–B22 登记段
- docs/process/round5-spec.md / docs/process/round5-README.md — 第五轮 spec/README 受控副本
- docs/process/README.md — 沉淀索引追加 round5 条目
- docs/process/reports/18-rules-store-roundtrip.md — 本报告受控副本
- .scratch 侧（不入 git）：issues/18 勾选置 done、README 状态表行 18 置 done、本报告工作版
