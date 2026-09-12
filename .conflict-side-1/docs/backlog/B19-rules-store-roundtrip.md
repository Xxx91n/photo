# B19 — 规则存储完整往返（票18，架构恢复第五轮）

- **优先级**: 高　**来源**: 第五轮宏观评估候选①规则面板（spec.md / issues/18）

## 问题与验收

FormatRulesStore 的 Save/Load 与规则面板逐组开关双轨脱节：SaveCustomRules 仅持久化每行一个 strip_all 键（且 "EPS/PS".ToLowerInvariant() 生成含斜杠的 eps/ps_strip_all 键），LoadRules 不用存储值回填行开关、Video 行 IsEnabled 误读 RAW 族键 raw_strip_exif_xmp_iptc，rules.json 不构成可信规则存储。验收（issue 18 四项）：

1. SaveCustomRules 持久化全部逐组开关（StripAll/StripExif/StripXmp/StripIptc/StripTime/PreserveIcc）
2. LoadRules 修复键名错配（raw_strip_exif_xmp_iptc）并用 defaults 正确回填行值
3. Save→Load 往返忠实：写读一致，无字段丢失
4. 单槽相关测试绿；build 0 错 0 SCS

## 完成记录（2026-09-03，架构恢复第五轮票18）

- 落地：FormatRulesStore 规范 schema——{family}_{group} 布尔键，5 族（jpeg/raw/video/pdf/eps）× 6 组（strip_all/preserve_icc/strip_exif/strip_xmp/strip_iptc/strip_time）= 30 键权威默认（FamilyDefaults 镜像面板行编码默认），Key(familyKey, group) 统一键拼装；RulesPanelViewModel：SaveCustomRules 全 6 组持久化（FamilyKey 取枚举名小写，杜绝斜杠键）、新增 BackfillFromStore 按行回填（GetValueOrDefault(key, 行编码默认)，缺键/损坏文件天然回落）、移除 Video 行跨族键读取（IsEnabled 非持久化字段，恢复默认启用，无其他行为变化）。
- 测试：RulesPanelViewModelTests 新增 4 例，先红后绿——修复前 4 红 6 绿（红灯：仅存 5 键非 30 / defaults 缺 jpeg_strip_exif / 重启往返丢值 / 种子回填失效）；修复后 10/10 绿。测试侧独立拼键（FamKey 从枚举推导），不与实现共享 Key() 拼装。
- 门禁：build 0 错误 0 SCS；RulesPanelViewModelTests 单槽 10/10；全量单槽回归 Core.Tests 151/151 + IntegrationTests 289/289；semgrep p/csharp 两份被改源文件 0 findings。
- 旧键弃用披露：旧 LoadDefaults 共 6 键，其中 raw_strip_exif_xmp_iptc、video_strip_all_time、pdf_requires_warning 三个旧命名键移除（rg 全仓 0 消费方；rules.json 用户本地可再生成，重存/Reset 即收敛新 schema，不做迁移逻辑）；jpeg_strip_all、jpeg_preserve_icc、pdf_strip_all 键名在新 schema 下原样保留。
- 报告：.scratch/architecture-recovery/report-18-rules-store-roundtrip.md（受控副本 docs/process/reports/18-rules-store-roundtrip.md）。
- 编号备注：spec 拟票18=B18，但票17 窗口先行提交占用 B18（B18-config-roundtrip-stopbleed.md），按 B17 先例顺延取 B19；B20–B22 留给票19/20/21 窗口。
