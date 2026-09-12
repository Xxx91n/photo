# ADR 0060 — 架构恢复第五轮收口（第四轮收口后宏观评估落地）

- 日期：2026-09-03
- 状态：已合入 main（55c3ed9），push 待执行
- 上游：ADR 0059（round 3+4 收口）/ 2026-09-03 宏观架构调查 / atomcode 三引擎联网

## 1. 来源与决策链

第五轮由 2026-09-03 宏观架构调查触发，结论判定「基本清晰但局部残留」，定位四处明确残留：(1) 权威配置 `ui.sidebar_width` FULLY-DEAD + `backup.retain_days` SAVE-DEAD + sample/默认发散；(2) 规则面板与真实擦除引擎双轨孤岛；(3) UI 根目录 23 个 .cs 未层化 + 死代码；(4) CONTEXT.md 九处漂移/内部矛盾。

调研落地为五张垂直切片票，对应"to-spec / to-tickets"流程：票17（配置权威止血）、票18/19（规则面板 expand-contract）、票20（UI 层化）、票21（CONTEXT.md 收口）。

## 2. 五票结果一览

| 票 | 实物 commit | 分支 | Blocked by | 状态 |
|---|---|---|---|---|
| 17 配置 round-trip 对称性止血 | `2a8aed9` | ticket-17-config-roundtrip-stopbleed | None | ✅ done |
| 18 规则存储完整往返 | `288a1c1` | arc-recovery/18-rules-store-roundtrip | None | ✅ done |
| 19 规则引擎单一真相源 | `2e7f2af` | arc-recovery/19-wipe-engine-single-source | 18 | ✅ done |
| 20 UI 根目录层化 + 死代码清理 | `686fab7` | ticket-20-ui-root-layering-deadcode | None | ✅ done |
| 21 CONTEXT.md 三层一致性收口 | `3ad7134` | arc-recovery/21-context-consistency-closeout | 17, 19 | ✅ done |

## 3. 合入 main 拓扑（base→tip）

- `e1ddf70 merge: round5/17-config-roundtrip-stopbleed`
- `62757bb merge: round5/18-rules-store-roundtrip`
- `ed92583 merge: round5/19-wipe-engine-single-source`
- `e225a28 merge: round5/20-ui-root-layering-deadcode`
- `55c3ed9 merge: round5/21-context-consistency-closeout`

五票拓扑：票17→票18→票19 线性依赖链 + 票20/票21 独立基线（commit parent 显示票18 parent=票17、票19 parent=票18、票20/票21 parent=c80ae91）。合并态零冲突。

## 4. 终门禁（合并态 main @ 55c3ed9）

- `dotnet build PhotoPrivacy.sln --no-incremental`：0 错 / 1054 个既有 CA 警告（非新增）/ 0 SCS
- 首脑复核通过五个只读子代理程序化核验：5 票核心声明全 ✅（仅票18 测试数量"+4"实为 +5、票19 "20 例"实为 18 方法/32 有效用例的口径偏差）

## 5. 过程违规（首脑复核发现，单独呈报，不替追认）

| 票 | 违规 | 详情 |
|---|---|---|
| 21 | **wave-ordering 违规 + 未自报** | issue `Blocked by: 17, 19`（wave 3），实物 commit 时间 04:20:39 早于票18 (04:27:35) 约 7 分钟、早于票19 (10:57:52) 约 6.5 小时。报告「共池工作区并发」措辞与 git log 时间线矛盾。内容上不依赖票19（CONTEXT.md 全文 grep `WipeRuleEngine` 0 命中），技术上先提交合理；但 wave 措辞与实物矛盾且未自报违规。 |
| 21 | **报告 hash 披露错误** | 报告原文「票17（已 commit `7f1c28e`）」——`7f1c28e` 经 git cat-file 解析为 GitButler Workspace Commit `e96da529`，**非票17 实物**。实物为 `2a8aed93...`（短 `2a8aed9`）。票19/票20 提交消息内文均正确引用 `2a8aed9`，仅票21 报告披露错误。 |
| 17、20 | **分支命名混搭** | 票17/票20 用 `ticket-*` 前缀；票18/19/21 用 `arc-recovery/NN-...`。WORKFLOW.md 无强制规范，不算违规条款；但本轮内部不统一，第四轮约定 `arc-recovery/<NN>-<slug>`。建议未来窗口统一前缀。 |
| 18 | **测试数量口径偏差** | 报告「+4 新测试」实为 +5（`RulesStore_RoundTrip_FullSchemaSaveLoadIdentical` 也新增）。 |
| 19 | **测试数量口径偏差** | 报告「WipeRuleEngineTests 20 例」实为 18 方法 / 32 有效用例。 |
| 18 | **注释保留旧键名** | `RulesPanelViewModel.cs:85` 注释保留 `raw_strip_exif_xmp_iptc`（bug 考古，非消费）。 |
| workspace | **.gitignore 未提交** | 工作树发现新增 `results/` 行未 commit（票20 之前已观察到；本 ADR 同时提交修复）。 |

## 6. 重要事故与处置（供后鉴）

无新增事故。沿用第四轮 ADR 0058 教训 1「.scratch 全树蒸发」——本轮收口期 `D:/Aworker/photo-snapshots/20260903-040518/`（票17 稳妥原则快照，fileCount=32，totalBytes=83714，SHA256 完整）和 `20260903-110806/`（票20 提交前快照）作为防御性留存。本轮无 GitButler 栈手术（票20 amend 按 §4.4 字面条款合规）。

## 7. 决策

- 本轮所有"未自报"违规（票21 wave-ordering、票21 hash 披露）已显式登记在第 5 节，由用户在收到本 ADR 后裁定是否追认/补披露。
- 第五轮 5 票均以 --no-ff merge 合入 main（与第四轮 ADR 0059 拓扑一致），未执行 GitButler push、PR、merge squash 等动作。
- **backlog 正式立票**：docs/backlog/B18（票17）→ docs/backlog/B22（票21）共 5 张，已随各票 commit 提交进 git。

## 8. 下一动作（待用户指令）

- 紧随本 ADR 提交后：`git push origin main`（使用 gh Xxx91n 账户）；合入态含 .gitignore 新增 `results/` 忽略行（兜底本轮 he 人物品）。
- **不要立即 push 至 PR**：push 仅推 main，不开 PR。
- frontier = ∅，无下一波可开工票号。
- 后续 backlog（double-DTO 架构重构、BackgroundUiOptions 之外的 DI 容器引入等中远期项）已记 ADR 0060 不在此轮。
