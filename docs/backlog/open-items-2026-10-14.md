# 开放事项索引（2026-10-14 治理清理转移）

> 本文件是全仓「仍开放」问题的唯一索引——条目原为各轮治理文档中的移交/延期/裁定待决项，其载体文件已于 2026-10-14 治理清理出仓（历史见 git 历史 docs/process/ 与 .scratch 存档）。性质分四类：产品（需立票施工）/ 工程债（择机消化）/ 用户侧（用户环境动作）/ 流程（待裁定或已失效的记录）。
> atomcode 行业口径复核（2026-10-14）：多平台 CI 矩阵与 Avalonia.Headless 属「必须/建议尽快还」；CI 视觉回归、IPC 复用、VM 拆分可长期挂起；NativeAOT 择机。

| # | 条目 | 性质 | 出处 |
|---|---|---|---|
| 1 | Linux/macOS 实机 ExifTool+UDS 全链路集成验证 | 产品 | backlog/B10 |
| 2 | 服务模式功能测试（Windows Service 真实安装+生命周期冒烟） | 产品 | backlog/B13 |
| 3 | 服务模式提权路径：install 脚本 GUI 入口提权流（pkexec/osascript）未实机测 | 产品 | backlog/B13 |
| 4 | Daemon 模式（Worker 独立无 UI/服务模式）功能验证 | 产品 | backlog/B13 |
| 5 | CLI 模式功能验证 | 产品 | backlog/B13 |
| 6 | 服务模式跨用户 IPC（CurrentUserOnly 边界）支持与否的产品决策 | 产品 | ADR 0067 |
| 7 | 防 Hijack 加固：namedpipe 命名熵/公网路径随机化（当前防同机低权限，未防同机同用户抢先建管道） | 产品（低优先） | ADR 0067 |
| 8 | 未知格式处理扩展：候选 jxl/mp4/mov/m4v/3gp/psd/gif/webp 等（需 ExifTool 验证矩阵后逐个决策） | 产品 | ADR 0068 |
| 9 | **CI 多平台测试矩阵**（现仅 ubuntu-latest 单 job，Win/macOS 未跑 CI）| 工程债·atomcode=尽快还 | ci.yml |
| 10 | CI 端到端构建验证（当前只跑测试不跑 publish） | 工程债 | ci.yml |
| 11 | **Avalonia.Headless 引入**（UI 测试基础设施缺口）| 工程债·atomcode=建议尽快还 | TEST-CONVENTIONS |
| 12 | CI 视觉回归/像素 diff（审查负担+环境漂移，不引入） | 工程债·atomcode=可挂起 | ui-visual-standard |
| 13 | 原生 pubilsh 配置（PublishReadyToRun 等）与 self-contained 打包链路（发布测试通过后固化） | 工程债 | publish scripts/ADR 0035 |
| 14 | scripts/benchmark.ps1 vs release-readiness.ps1 重叠整合 | 工程债 | scripts/ |
| 15 | 测试命名/分类（Category trait）统一治理 | 工程债 | TEST-CONVENTIONS |
| 16 | 守卫测试族治理（46+ 文件散置，缺统一发现机制） | 工程债 | TEST-CONVENTIONS |
| 17 | appsettings.json 嵌套结构残留兼容代码（drift 观察） | 工程债 | round 报告 |
| 18 | 命名漂移：AuditTailService/FormatRuleRow/DefaultPaths 等未按 convention 归位 | 工程债 | round 报告 |
| 19 | Program.cs:108 LaunchWorkerAsync 参数挤 | 工程债 | round 报告 |
| 20 | RealExifToolProbeTest 硬编码 `D:\hot\1.png` 环境依赖 | 工程债 | tests/ |
| 21 | MemoryPackable/Record/JsonDerivedType 覆盖契约审计（IPC DTO 序列化一致性） | 工程债 | round 报告 |
| 22 | Config JsonPropertyName 命名风格统一 | 工程债 | round 报告 |
| 23 | ISafeFileOperations.Mock 与真实实现的一致性保证 | 工程债 | round 报告 |
| 24 | 视图缝收敛：MainWindow/Program.cs 手写 new（CompositionRoot 白名单登记的 6+1 缝）工厂化重构 | 工程债 | CompositionRootSourceTests |
| 25 | ADR 0057（CI Windows 认证缺失对策）重估：实测反馈后以证据更新 | 工程债 | ADR 0057 |
| 26 | ConnectionStateServiceTests flaky（UI 线程调度依赖） | 工程债 | round 报告 |
| 27 | 手动打包链路文档化（发布顺序、校验步骤的 runbook 化） | 工程债 | publish/release |
| 28 | DesignTokens axaml token 引用一致性全域扫描 | 工程债 | ui-visual-standard |
| 29 | audit 路径开关（dev 默认 ./logs vs service 默认 /var/log）的环境判定来源需查证 | 工程债 | DefaultPaths |
| 30 | **手动冒烟 28 项+服务模式 8 项实机目检**（轮内未执行） | 用户侧 | report-32 清单 |
| 31 | Windows 服务安装实机验证（含提权 UAC 流） | 用户侧 | backlog/B13 |
| 32 | Linux systemd 安装/卸载/日志实机 | 用户侧 | backlog/B13 |
| 33 | macOS launchd 安装/卸载/日志实机 | 用户侧 | backlog/B13 |
| 34 | ExifTool 官方版下载链验证（ExifToolPathOverride 走通） | 用户侧 | README |
| 35 | GC Datatype/serilog 等 .NET 10 升级的兼容性复测 | 用户侧 | plan-06（git 历史） |
| 36 | 深色/浅色双主题的对比度 A/AA 验证（五主题色板全测） | 用户侧 | ui-visual-standard |
| 37 | B22 登记缺位：docs/backlog 连续编号在 B21 后无 B22（若有新票应续号） | 流程 | docs/backlog |
| 38 | ADR 0012 与 0035+票 06 矛盾（已在本轮勘误横幅解决） | 流程·已处置 | ADR 0012 |
| 39 | ADR 0049 状态「计划中」与实物不符（已在本轮勘误解决） | 流程·已处置 | ADR 0049 |
| 40 | CONTEXT.md Temp File Route 词条滞后（cleanup-temp 与 PP_ 前缀口径） | 流程·已随本轮瘦身复核 | CONTEXT.md |
| 41 | CONTEXT.md Button Transition Animation 与 ADR 0054/实物矛盾（已在本轮瘦身修正） | 流程·已处置 | CONTEXT.md |
| 42 | CONTEXT.md 缺 CI Gate 词条（已在本轮补齐） | 流程·已处置 | CONTEXT.md |
| 43 | CONTEXT.md audit 事件名核对新增（backup_skipped 等 3 个新事件名已入词条） | 流程·已处置 | CONTEXT.md |
| 44 | 04-fix 票：源码审查点名未实施项（异常吞掉、ProcessDisposed、fsync 时机），无 issue 卡 | 流程 | round4-issues |
| 45 | 探活清单人工执行（28 项 UI 冒烟+8 项服务模式） | 流程·同 #30 | report-32 |
| 46 | ui-craft2 T-4（nav-item 高 32/36 双值）至 T-6 等设计残留张力 | 产品·低优先 | design 附录 A.2 |
| 47 | ExifTool stay_open 模式下 stdin 缓冲语义假设（多并发写"无边界问题"推论缺实测） | 工程债 | ADR 0001 族 |
| 48 | CI jobs.build 与 release.yml 测试重复执行（push 触发两次测试） | 工程债·低 | ci/release |
| 49 | 审计日志 rotation 与清理策略（quota 到上限后行为） | 工程债 | ADR 0006 |
| 50 | 会话/启动顺序边界：UI 先于 Worker 启动的版本协商语义 | 工程债 | ADR 0030 |
| 51 | doc/design/screenshots 基线资产手工刷新规程（4 页×4 态） | 流程 | ui-visual-standard §7 |
| 52 | ci-verify/* 远端分支 6 枚清理（用户侧 GitButler/远端操作） | 用户侧 | GitButler 栈残留 |

**处置口径**：产品项立票后逐条消化；工程债择机；用户侧项在具备实机条件时执行；标「已处置」的流程项仅为历史登记、无需再动。
