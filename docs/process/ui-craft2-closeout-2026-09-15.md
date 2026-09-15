# ui-craft2 整轮收口结算（审计 Agent，2026-09-15）

> 数据源：`.scratch/ui-craft2/decision-ledger.md`（D-001~D-008 current + A-001~A-005 已结算 implemented）、`spec.md`、`README.md` 状态表、`reports/01~08`（含 04 返工轮）、ADR 0067、规范 v2.7。
> 轮次终态：W1~W8 + W4-fix 全部 ACCEPT。本文档是决策沉淀唯一现役出处；账本原件随 `.scratch` 归档保留。

## 1. 硬验收留证（审计本机明令执行，CI-only 例外条款）

| 门 | 命令 | 结果摘要 |
|---|---|---|
| 编译 | `dotnet build PhotoPrivacy.sln -v:m` | **0 错误**（35.96s 增量；`--no-incremental` 全量复验仍 0 错误）；1204 警告全为 CA 风格类，**SCS 安全警告 = 0**（grep 'SCS[0-9]' 计数 0） |
| 测试 | `dotnet vstest tests/…Core.Tests.dll tests/…IntegrationTests.dll --settings:test.runsettings /Platform:x64` | **集成 365/365 PASS（4m34s）+ 单测 191/191 PASS（16s），失败 0 跳过 0**——与票 08 报告自述数字一致（独立重跑证实） |
| 打包 | `powershell scripts/publish-app.ps1` | `release/PhotoPrivacy-0.1.0-preview-win-x64.zip` + `release/win-x64/`（含 worker/）生成，PUBLISH_EXIT=0 |
| 启动测活（Worker） | `powershell scripts/smoke.ps1 -HotFolder … -AuditFolder … -DryRun true` | SMOKE_EXIT=0（Hosting 正常起停，stderr 空） |
| 启动测活（UI） | `Start-Process release/win-x64/PhotoPrivacy.exe` → 10s | **UI-ALIVE pid=29416 responding=True** → Stop-Process → UI-STOPPED left=0（无进程残留；临时脚本用后即删） |

## 2. 交叉核对与三层一致性结论

- 报告×8+README 数字声明回测实物（双轨字节/Fact 计数/行数/核心交付类名/负向扫描 27 项）：**0 实质矛盾**（两条初判不符系 wc -l 与守卫 Split 口径差异，人工核定一致）。
- 三层文档一致性（CONTEXT.md 103 词条 / docs/adr 67 篇 / 代码）：0 硬问题。CONTEXT 旧槽位措辞仅存于沿革语境；ADR 0050/0067 两处“本仓不存在”引用核定为外部项目路径（Files.App GitHub URL / MangoDisk 仓库文件），非断链。
- 已闭环的本轮登记偏差：票 05「保留12 vs 13」尾数簿记、票 08 ToastService 行数混抄（167→184）、spec v2.7 记录行 API 名残留（已订正为 ThicknessTransition/DoubleTransition + 纠偏引用）、D-008 草拟值与实现偏差（账本已补落地注记）、启动器模板 undefined（8 份已修）。

## 3. D 系列决策沉淀（current 全集，一句话+落点）

| ID | 决策 | 实现落点 |
|---|---|---|
| D-001 | 痛点基准=新构建 nav 四枚反馈观感；目检为最终标准 | 全票验收基准（§7 V1/V2 目检登记待用户） |
| D-002 | nav=胶囊三态语言，4px 左缘槽位体系退役 | 票 03：AppTheme nav/nav-action 双段重写 + 守卫 F3 反向锁 |
| D-003 | active=主色实心+AA≥4.5 五主题验证 | 票 03：SemiColorPrimary 实心 + on-primary 深字，五主题 5.90–7.79 实解（nord/dracula 观察项闭环） |
| D-004 | 圆角窗口=DWM 方案 A（显式 Round，禁依赖 Default；Win10 no-op） | 票 02：MainWindow `Win32Properties.WindowCornerPreference="Round"` + 守卫锁；目检矩阵登记待用户 |
| D-005 | MangoDisk 观感级全面对标（tokens+骨架+交互范式；GPL 源码零拷贝） | 票 02-08：Radius/Elevation/Layout 族、四页骨架、toast 形态全部按源码取证落位 |
| D-006 | 全部页面一轮全对齐+拆票细化+全串行 | 票 04-07 逐页 ACCEPT |
| D-007 | 锐评六条=独立处置轮；Toast+AA 并入本轮；backlog 5 项维持 | Toast 票 08 落地；AA 票 03 闭环；锐评轮待启动 |
| D-008 | T-3 裁定=C 折中（active 实心胶囊+on-primary 深字+SemiBold，无左缘 pill）+hover 前景中性 | 票 03 落地；用户原话存账本 |

## 4. A 系列结算

A-001~A-005 全部 **implemented**（详见账本状态行注记，2026-09-15 结算）：页面清单实物收敛 / nav 双落点职责边界 / Toast 从零新建 / 守卫措辞钉撞红三档 / koy 悬空依赖随票 01 首件消解。无 deferred / stale 项。

## 5. 遗留与裁定积压（backlog 呈报见 README 收口行）

目检项（V1/V2 各票登记）／全局裁定积压（T-4/T-5/票 05a·b/票 06b·e·f·g/票 07c·d/T-6/票 08a·b·e/R3/section-header 退役）／流程遗留（01 分支 conflicted、票 08 CI-only 违规追认+产物处置、g0 栈）／锐评六条处置轮。
