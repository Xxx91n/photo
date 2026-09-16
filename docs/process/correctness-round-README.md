# correctness-round（锐评处置轮）— 波次表与状态

> 本工作区承接锐评处置轮（第四轮架构恢复后重启的最新轮次）。**第五~七轮历史状态表已让位**：归档见 `.scratch/_archive/architecture-recovery-roundN-*` 与 `docs/process/roundN-*.md`；WORKFLOW.md（§4.2/§4.3/§4.4/§7）继续现役。
> 决策账本双册：D 系（用户裁定）= `.scratch/correctness-round/decision-ledger.md`（D-001~D-008）；A 系（摩擦点对账闸）= `.scratch/architecture-recovery/decision-ledger.md`（A-001~A-009，原文引自 `.codex-tmp/锐评.txt`）。spec = `.scratch/architecture-recovery/spec.md`。

## 波次表（从 issue Blocked by 推导，六票全串行；顺序=优先级）

| 波次 | 票 | 标题 | 覆盖 A-xxx | Blocked by | 启动器 | 状态 |
|---|---|---|---|---|---|---|
| W1 | 01 | P0 数据面：格式黑洞挂死链修复 + 宣称收敛 + 对账断言 | A-001、A-009 | None（前置闸：工作区重锚定，等用户明令） | prompts/01-wipe-unknown-format-and-claims.md | done（ACCEPT 2026-09-16：返工 8698b5c 后 CI run 34996459375 绿；D:hot 测试保真度缺陷已登记并入票 02） |
| W2 | 02 | 配置面：空配置兜底 + 排除清单真通配 | A-002、A-003 | 01 | prompts/02-config-fallback-and-exclude-glob.md | done（ACCEPT 2026-09-16：返工 4044585 后 CI run 35003183333 绿；A-002/A-003 结算 implemented） |
| W3 | 03 | 备份面：镜像相对路径布局 + 内容分歧旁路版本化 | A-006 | 02 | prompts/03-backup-mirror-layout.md | done（ACCEPT 2026-09-16：并集树 run 35010033448 第三轮绿（前两轮红=预存 flaky 测试，非本票缺陷）；A-006 结算 implemented） |
| W4 | 04 | IPC 面：认证分层 + 承诺改写 + 部署配套 | A-004 | 03 | prompts/04-ipc-auth-layered.md | done（ACCEPT 2026-09-16：四票并集树 run 35055374835 绿；A-004 结算 implemented；Windows service 跨用户后续票待用户裁定） |
| W5 | 05 | 驻守稳定性：健康检查探针 + 撤 sync-over-async + 缓冲读评估 | A-005 | 04 | prompts/05-resident-stability.md | done（ACCEPT 2026-09-16：计费解除后 CI run 35061555907 绿（重审核子代理实物核验 Core 244 + Integration 366 = 610 零失败）；A-005 结算 implemented） |
| W6 | 06 | 收口：黑名单点状升级 + 对账收口 + 整轮审计 | A-007、A-008、A-009 | 05 | prompts/06-closeout-guards-reconciliation.md | done（窗口交付 2026-09-16，待首脑复核+CI 终判：报告 reports/06-report.md；A 系九条已全部结算 implemented；T1 承接已修；UI.csproj/ADR 0035 矛盾已收口；待大脑推 ci-verify 后登记 CI） |

## 范围外（本轮不做，见 spec Out of Scope）

- 高置信格式扩容批（收口后按批立票）；- 握手令牌纵深；- source-lint 全局收缩/AST 整改；- backlog 清账（实机目检/15 项裁定/票 08 追认/g0）——锐评轮收口后承接；- 栈操作（等用户明令）。

## 首脑复核登记

（待各票报告落盘后由大脑复核登记；协议沿用 ui-craft2：逐条实物验证/声明-证据-结论对照表/账本核对/违规呈报/本表登记+重算 frontier。）

### 票 01 首脑复核（2026-09-15，进行中）

- 静态复核已完成：14 文件触面逐字一致；产品四层落地实物命中（ExifToolBridge L133 早退/L217-228 再解析、CommandBuilder L58-65 fail-closed、FileTaskPipeline L163-167 审计分支）；fake 零残留（rg exit 1）；sample=34 无重复；README L28=34；Fact 计数 2+5；与 ui-craft2 27 提交无文件交集。
- 过程违规呈报（不替追认）：①工作区重锚定前置闸未执行即开工（提交 8d48981 落在旧基 6bf244e 上，origin/main 已前移 27 提交，票面无文件交集故无冲突，合并拓扑受影响）；②CI 云端凭证缺失即静态口径自记 ✅（报告 §8 已自呈报）。
- 大脑已按 CI-only 政策推送验证分支 ci-verify/correctness-round-t01 触发 CI（run 34992398006）。
- **CI 终判：红**（test job，error CS0103 effectiveTarget ×2 @ ExifToolBridge.cs L219/224——跨层变量名混淆，node 沙箱复演测不出 Roslyn 错，本轮盲区第二次实爆）。静态其余全过。
- **裁定：返工**——修复启动器 prompts/01-fix-roslyn-cs0103.md 已发（修复范围仅 2 处标识符替换；重跑同一套验收：守卫+行为测试+CI 绿为唯一凭证）；frontier 不前移，W2–W6 维持 blocked
- 修复窗口交付（2026-09-16 核验）：commit 8698b5c 单文件 2 行（effectiveTarget→targetPath），rg 于 ExifTool/ 零命中；报告追加「返工轮次」节并如实自曝二次缺陷（双 BOM 字节级自纠，教训=BOM 校验须核计数非存在性）。
- **首脑新发现（修复窗口扫描盲区）**：票 01 原提交含吞反斜杠 ×2——RuleEngineTests.cs `@"D:\hot"`→`@"D:hot"`（verbatim 路径损坏）；修复窗口 §6 扫描只覆盖符号可解析性家族，未覆盖反斜杠丢失家族。断言语义不受影响（Decide 按扩展名判定）。
- **裁定：修复并入票 02**（其触面本含 RuleEngineTests 域；issue 02 已增补承接项 + 建议 source-lint 守卫防 drive-relative verbatim）。票 01 其余项待 CI run 34996459375 终判。
- **CI 终判：绿**（run 34996459375，test job success、零失败步骤；预期测试计数 Core.Tests=195 待 CI artifact 精确核对，job 级全绿已满足验收）。
- **裁定：票 01 ACCEPT**（返工轮闭环；A-001/A-009 实现证据齐备；`RuleEngineTests` 合同变更裁定=接受——系 D-005.1 宣称收敛的直接实现非弱化；`but pull` 维持等用户明令；D:hot 缺陷并入票 02 已登记）。
- **frontier 重算：W2 解锁，票 02 可开工**（启动器 prompts/02-config-fallback-and-exclude-glob.md；含票 01 承接项）。
### 票 02 首脑复核（2026-09-16）
- 静态复核全过：8 文件触面（548+/11−）、A-002 兜底 L66–75/validator 硬拒/CollectWarnings 四类判定、A-003 两指针通配 L82–133、config_warning 双路径审计 L78/510、Fact 计数 6/9/6/3、承接项三处反斜杠修复+守卫 TestCodeVerbatimPathGuardTests 3 Fact、diff --check 干净。
- CI run 35000648685：编译过、IntegrationTests 332/332 绿、Core.Tests 233/234——唯一红 = 自增锚定测试用例 a.jpg.bak（.bak 不在允许门 → extension_not_allowed，测试误期望 eligible）。**产品 MatchesPattern 锚定经首脑手工 trace + aa.jpg 用例双证无缺陷。**
- **裁定：返工（仅测试用例）**——修复启动器 prompts/02-fix-anchor-case.md 已发（InlineData a.jpg.bak → a.jpg.jpg）；§12 五项裁定：①兜底来源=DefaultPaths 维持（与 ADR 0052 A6 差异属不同配置面，张力已登记）②告警/硬拒分级维持（与 D-004.2 逐字一致）③串行栈拓扑接受④but pull 等用户明令⑤推 CI 已由大脑执行。
- frontier：W3–W6 维持 blocked，待票 02 修复 CI 绿后前移。
- 修复窗口交付（2026-09-16 核验）：commit 4044585 单文件 1 行（锚定 Theory `a.jpg.bak`→`a.jpg.jpg`）；报告追加「返工轮次 2026-09-16」节，并如实呈报检查点偏差——「a.jpg.bak 零命中」不可字面满足（L57 BackupPath 测试合法使用同子串），窗口将零命中限定于锚定 Theory 内，处置正确（首脑检查点措辞过宽之责在首脑）。
- **CI 终判：绿**（run 35003183333，test job success）——返工轮闭环。
- **裁定：票 02 ACCEPT**（A-002/A-003 结算 implemented；两个自增测试在首两轮 CI 中先后抓出真缺陷——行为测试文化实证有效；D:hot 承接项闭环、source-lint 守卫 TestCodeVerbatimPathGuardTests 入场）。
- **frontier 重算：W3 解锁，票 03 可开工**（启动器 prompts/03-backup-mirror-layout.md）。W4–W6 维持 blocked。
### 票 03 首脑复核（2026-09-16，进行中）
- 静态复核全过：9 文件触面（+838/−24）与报告逐字一致；唯一入口实证（RuleEngine→BackupPathResolver.ResolveBackupSlot，旧 GetFileName+Suffix 构造零残留）；备份路径 overwrite:true 零残留（余下 7 处命中均为输出写/配置原子替换等非备份路径）；resolver 五成员+WatchPathFilter 公开判定在位；CONTEXT.md Backup 词条逐字符合 D-006（_Avoid_ 登记旧追认表述）；retention AllDirectories+CleanupEmptyDirectories 兑现。
- 口径偏差：报告称 9 Fact，实物 8 Fact（零 Theory）——登记不返工。
- 过程违规呈报：①**串行纪律违反**——issue 03 Blocked by 票 02，但窗口建了与票 01/02 **并行**的分支（f964066 父基=旧基 6bf244e，不含票 01/02 任何改动，已实证）；GitButler workspace commit e3b2a2a 已自动并集三票且无冲突标记。②git add -N（窗口自报，接受）。
- **CI 打点修正**：验证分支改推**并集树 e3b2a2a**（ci-verify/correctness-round-t03-union，run 35010033448）——那才是将落 main 的形态；打在 f964066 孤树上等于没测组合。
- **三数据点终判**：run 35010033448 第 1/2 轮红（ConnectionStateServiceTests.State_Should_Transition_To_Connected_When_Alive，Expected Connected/Actual null）、第 3 轮绿（332/332，test job success）。
- **根因定谳：预存 flaky 测试，非票 03 回归**。证据链：①该测试不在三票任何触面（git log --follow 最后触碰=票 20 时代）；②测试本体跨绿/红树代码路径相同（IntegrationTests 程序集票 03 零改动）；③内在竞态=Timer(dueTime:Zero) 首回调与 PropertyChanged 订阅赛跑，200ms 预算只覆盖首回调，回调先于订阅执行则 observed 恒 null——runner 空闲时必输，繁忙时延迟过订阅点反而绿（解释 t02 期绿、t03 期两红、第三轮绿的反直觉分布）。
- **裁定：票 03 ACCEPT**（A-006 结算 implemented；并集树 Core 242/242 含全部 8 个备份行为测试）。
- **登记测试质量项**：ConnectionStateServiceTests 三测（Connected/Reconnecting 断言同构竞态）加固——改轮询式断言（2000ms 内轮询 svc.State）或订阅先行重构，并入票 06 收口或独立微票，由大脑在票 04 派发时一并定。
- 首脑过程纠错自报：复核中曾误发 02-fix-anchor-case-r2 启动器（把票 03 并集树的 ConnectionState 失败误判为票 02 锚定问题复发；实为锚定修复已由 4044585 完成并 CI 绿实证）——已当场删除并在此自报。
- **frontier 重算：W4 解锁，票 04 可开工**（启动器 prompts/04-ipc-auth-layered.md；冒烟实验闸先行）。W5–W6 维持 blocked。
- 过程违规另呈报：票 03/04 连续两票并行锚定——D-008.7 串行纪律与 issue Blocked-by 字段在窗口侧已系统性失效，收口时须重审派票/锚定机制（或改用真串行单分支链）。
- §6 四项张力裁定：①多监控根前缀消歧=接受（现行配置单 HotFolder，无触发场景，_unsorted 兜底语义等价，前缀层心智位已预留）②ADR 0006 修订=接受，收口时立备份布局 ADR（含 0006 修订登记）③同秒旁路 TOCTOU=接受登记（retry 兜底，不引全局锁）④retention 排序说明=接受（无行为变更）。
### 票 04 首脑复核（2026-09-16，进行中）
- 静态复核全过：17 文件触面（+1377/−52）与报告一致；冒烟证据在盘（两变体 6 探针全 OK——CurrentUserOnly 未复现 ADR 0035 坑、重复 FirstPipeInstance 抛 UnauthorizedAccessException、300 次 create/dispose 无 254 泄漏）；测试 10+8 Fact；ADR 0067 在位；AGENTS.md L159/L163/§6/§9 四锚定改写实证；systemd 0750+组供给实证；NamedPipeServerStreamAcl API 零残留（仅注释记载）；semgrep 0 findings。
- **§6.2 P1 张力裁定（首脑）**：接受窗口建议①——Windows service 模式跨用户信道由「未定义」变为「确定拒绝」属 fail-closed 改善（③ 最小攻击面），且 ADR 0035 本就显式推迟跨用户（非本票引入的功能回退）；**后续票登记**：Windows 可配置 DACL（服务账户+交互用户/专用组）+ install-service.ps1 组供给 + 客户端 CurrentUserOnly 放宽，与 Unix 0660+组模型对齐——交用户裁定是否立票。
- §6 其余裁定：①CreateServer 命名歧义=接受为不动项 ③CI 平台不对称（Windows 断言无 CI 回归网）=接受，ADR 0067 已知限制。
- §7 过程违规裁定：①冒烟实验本机构建=**接受为票面授权例外**（issue 强制前置闸，OS 临时目录独立工程不入仓库，污染最小化）②静态口径代 CI=已披露（CI 为终审）③冒烟未覆盖跨用户=登记为实验设计缺口（若立后续票须补验）。
- **CI 打点**：与票 03 同理，票 04 分支又是并行锚定（父基=旧基），验证分支推**四票并集树 a513b76**（ci-verify/correctness-round-t04-union，run 35055374835）。
- **CI 终判：绿**（run 35055374835，test job success）——四票并集树全量编译+测试通过（含票 01/02/03 全部既有测试零回归 + 票 04 Unix 侧认证断言实跑）。
- **裁定：票 04 ACCEPT**（A-004 结算 implemented；冒烟闸留证在案；Windows service 跨用户信道按 fail-closed 关闭并登记后续票候选）。
- **frontier 重算：W5 解锁，票 05 可开工**（启动器 prompts/05-resident-stability.md）。W6 维持 blocked。
### 票 05 首脑复核（2026-09-16，静态全过 / CI 阻塞）

- **CI 门阻塞（外部，非代码）**：CI run 35061384335 / 35061555907 两次推送均 2–4 秒即败、零日志零步骤；GitHub check-run 注解原文 = 「recent account payments have failed or your spending limit needs to be increased」（Billing & plans）。**无源码问题，无返工项**——修复需用户在 GitHub 账户侧解除计费阻塞后由大脑重推 ci-verify 分支。
- V1 裁定：守卫断言合同翻转接受（旧断言锁旧合同「喂 exiftool 路径」，翻转=新合同「无文件探针」的必要表达，非守卫删除，4 行合同变更注释在案）。V2 裁定：可测性生产结构抽取（Handler/Loop 拆分）接受——零行为变更。V3 工具缺陷登记。
- V4 核验：线性化内容完整性已实证（diff 39bf2f0→1763434 = 14 文件 618+/234− 恰为票 01 改动，票 04 自身内容零改动仅重锚父链）；**「已获用户授权」发生在窗口会话，首脑无法核实——请你追认或否认**。V3 工具缺陷登记。
- §6 四张力裁定：T1（Program.cs:139 POSIX onReload sync-over-async）=并入票 06 收口；T2（ADR 0057 重估条件已达）+T3（旧测量基线存疑）=登记 backlog，票 06 收口时呈报是否开重估票；T4=仅动机引用。
- 过程违规另呈报：票 03/04/05 三票中两票并行锚定+一次栈线性化（含移动他票位置）——WORKFLOW §4.4 快照已做（20260916-115812），但栈手术频度已超出「例外」范畴，收口时须与 but pull/重锚定一并彻底解决。
- **frontier 重算：W6 维持 blocked**——票 05 ACCEPT 待 GitHub 计费恢复后 CI 终判（大脑重推 ci-verify/correctness-round-t05b 即可，提交树 38f6981 已就绪）；期间票 06 不可开工。
- **计费解除（用户操作：项目转公开）→ CI 重跑**：run 35061555907 rerun success；**重审核子代理实物核验**（general-purpose 子代理，只读）——Core.Tests **244/244** + IntegrationTests **366/366** = **610 测试零失败**（日志原文两行 Passed! 汇总，无任何 Failed! 行）；已知 flaky（ConnectionStateServiceTests）本轮通过零复发。
- **裁定：票 05 ACCEPT**（A-005 三件结算 implemented：(a) 无文件探针+持锁、(c) 零 sync-over-async/byte[1]、(b) 缓冲读不降级；V1 合同翻转/V2 结构抽取/V4 线性化均已裁定）。
- **frontier 重算：W6 解锁，票 06 可开工**（收口票：CompositionRoot 守卫点状升级+对账收口+UI.csproj/ADR 0035 核对+整轮收口审计；启动器 prompts/06-closeout-guards-reconciliation.md）。


