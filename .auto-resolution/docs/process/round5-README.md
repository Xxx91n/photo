# 架构恢复 — 第五轮（第四轮收口后宏观评估落地）

轮次来源：2026-09-03 宏观架构调查（codegraph + 三个只读子代理 + atomcode 三引擎联网）产出的《架构恢复评估》，判定「基本清晰但局部残留」，落成五票。

## 状态表

| 票 | 标题 | 来源 | Blocked by | 波次 | 状态 |
|---|---|---|---|---|---|
| 17 | 配置 round-trip 对称性止血 | 候选②配置权威 | None | 1 | ready-for-agent |
| 18 | 规则存储完整往返 | 候选①规则面板 | None | 1 | done（report-18-rules-store-roundtrip.md） |
| 19 | 规则引擎单一真相源 | 候选①规则面板 | 18 | 2 | done（report-19-wipe-engine-single-source.md） |
| 20 | UI 根目录层化 + 死代码清理 | 候选③UI层化 | None | 1 | ready-for-agent |
| 21 | CONTEXT.md 三层一致性收口 | 三层一致性 | 17, 19 | 3 | ready-for-agent |

## 波次推导（仅由 Blocked by 字段推导）

- 波 1：Blocked by None → {17, 18, 20}，frontier = {17, 18, 20}，可并行。
- 波 2：Blocked by 18 → {19}。
- 波 3：Blocked by 17、19 → {21}。
- 文件面：17 动 Configuration/AppConfig* + config.sample.json + Configuration 测试；18 动 Configuration/FormatRulesStore + 其测试；20 动 Ui 根 .cs + 删死代码（InFlightRegistry/IWipeStrategy）——同目录不同文件，无两票同文件。

## 备注

- 版本控制遵循 WORKFLOW §4.2；动栈前遵循 WORKFLOW §4.4 快照。
- backlog 正式立票（docs/backlog/B18–B22）建议随各票提交一并完成。
- atomcode 调研提示词完整版见 spec.md「研究输入」；大脑已跑一次，结论见 spec.md Further Notes。

## 自检基线（生成时点）

违禁词扫描、必读路径存在性、≤60 行限——见大脑自检输出。
