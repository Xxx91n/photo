# 分诊标签

各个 skill 说的是五种固定的分诊角色。这份文件把角色对应到本仓票池里实际用的标签字符串。

| mattpocock/skills 里的叫法 | 本仓标签 | 意思 |
| --- | --- | --- |
| `needs-triage` | `needs-triage` | 还没评估，需要维护人看一眼 |
| `needs-info` | `needs-info` | 等报告人补充信息 |
| `ready-for-agent` | `ready-for-agent` | 需求写全了，agent 挂机也能直接干 |
| `ready-for-human` | `ready-for-human` | 得由人来实现 |
| `wontfix` | `wontfix` | 不打算处理 |

skill 里提到某个角色（比如「打上 AFK-ready 的标签」）时，用表里对应的标签字符串。

「本仓标签」这一列可以改成你们实际用的叫法。
