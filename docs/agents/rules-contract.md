# rules.json 契约（新 UI 与 Worker 共用）

> 定位：规则页读写的 `rules.json` 速查，供新 UI（`ui/`）与任何规则存储改动前查阅。
> 事实源分三处：`FormatRulesStore.cs`（键名与编码默认值）、
> `RulesPanelViewModel.BackfillFromStore` 与 `WipeRuleEngine.cs`（缺键逐项回退、消费侧）。本文件是缓存。
> 注：文中「旧 UI」语义出处已随旧 Ui 工程退役（#61），仅作历史参照；现役实现为新 UI（`ui/crates/photoprivacy-rules`）与 Worker。

## 位置

UI 与 Worker 都按各自 `AppContext.BaseDirectory` / 可执行文件目录解析：`<app dir>/config/rules.json`。
发布布局里两个进程同目录，因此写同一份文件；UI 直接写文件，不经过 IPC。

## Schema

- JSON 对象，键 `{family}_{group}`，值为布尔；共 5 族 × 6 组 = 30 个键。
- 族前缀**不含斜杠**：`jpeg`、`raw`、`video`、`pdf`、`eps`（历史 `eps/ps` 键属错配，禁止回潮）。
- 组名：`strip_all`、`preserve_icc`、`strip_exif`、`strip_xmp`、`strip_iptc`、`strip_time`。
- 字典键按字面匹配：`JsonSerializerDefaults.Web` 只改属性名策略，不改 `Dictionary<string,bool>` 的键，
  `jpegStripAll` 之类读不到。

编码默认值（`FormatRulesStore.FamilyDefaults`）：

| 族 | strip_all | preserve_icc | strip_exif | strip_xmp | strip_iptc | strip_time |
| --- | --- | --- | --- | --- | --- | --- |
| jpeg | true | true | false | false | false | false |
| raw | false | false | true | true | true | false |
| video | true | false | false | false | false | true |
| pdf | true | false | false | false | false | false |
| eps | true | false | false | false | false | false |

## 读写语义

- 文件缺失、读取失败、JSON 非法 → 全部回退编码默认值（旧 UI `Load()` 语义）；新 UI 额外提示「文件在但读不了」。
- 缺键逐项回退该族默认值（旧 UI `RulesPanelViewModel.BackfillFromStore` / Worker `WipeRuleEngine`），
  所以旧版只写 `strip_all` 的文件仍可读。
- 规则页只覆盖 jpeg/raw/video/pdf/eps 五族；Worker 的 `WipeRuleEngine.FamilyKey` 还会读
  `tiff_*`/`heic_*`/`png_*` 等键。规则页保存只写 30 个面板键，手写的这些键会被丢弃、对应族回退默认；
  旧 UI 的 Save 同样如此。需要保留这些族时不要经由规则页保存。
- 规则页自身忽略未知键；保存时只写 30 个规范键（旧 UI 同样会丢弃未知键）。
- 写盘为原子替换：同目录临时文件 + rename，避免并发读端读到半截 JSON。新 UI 的临时名带 pid + 序号
  （`rules.json.<pid>.<seq>.tmp`），避免迁移期新旧 UI 同时保存时互抢同一临时文件；旧 UI 仍是
  `rules.json.tmp`，但两者都不属于文件契约。

## 改动规则

schema 变更必须同步五处：C# `FormatRulesStore`（键名与编码默认值）、
C# `WipeRuleEngine.FamilyFallback`（缺键回退默认值）、Rust `ui/crates/photoprivacy-rules`、
共享 fixture `ui/crates/photoprivacy-rules/testdata/rules.golden.json`、
.NET 互读测试 `tests/PhotoPrivacy.Core.Tests/Configuration/FormatRulesStoreTests.cs`。
旧 UI 的 `RulesPanelViewModel.LoadRules` 也编码了五族默认值，但旧 UI 已冻结：schema 变更只在新栈与 Worker
侧落地，互读测试只保证既有 30 键。
规则不走 IPC，不新增 method（`Design(#49)`：IPC 协议只以 method 名扩展，且规则本就属文件契约）。
