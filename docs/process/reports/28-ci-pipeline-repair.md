# 票 28 报告 — CI/CD 修复：push 测试门禁 + workflow 拆分（架构恢复第七轮）

- 日期：2026-09-05
- 分支：round7/28-ci-pipeline-repair（GitButler 栈，common base 0a8fd4b）
- commit：40fc946143344d2412f294eb6d02ccb2186af596（短哈希 40fc946，以 git log 实物为准）
- 状态：改动落盘 + 静态验证全绿；**云端验收待大脑推送 CI 验证分支**（CI-only 政策，本窗口不 push）

## 1. 声明 → 证据 → 结论对照

| # | 声明 | 证据 | 结论 |
|---|------|------|------|
| 1 | 幽灵 run 根因 = release.yml 两处 GitHub Actions 表达式不支持的语法（三元 `?:`） | 修复前实物：release.yml:77-78 两处 `${{ matrix.rid == ... ? '.exe' : '' }}`；spec Implementation Decisions CI 根因条（大脑调查报告 L1：表达式语言无 `?:` 运算符 → workflow 整文件解析失败 → 每次 push 产生 0-job 无日志瞬时失败 run，28/28 全红） | ✅ 根因实物确认 |
| 2 | 检查点 A：三元根除，改短路形式 | 改为 `${{ (matrix.rid == 'win-x64' \|\| matrix.rid == 'win-x86' \|\| matrix.rid == 'win-arm64') && '.exe' \|\| '' }}`；node 字节级扫描：release.yml 24 个 `${{ }}` 表达式 0 个含 `?`（ci.yml 0 表达式为正确形态） | ✅ |
| 3 | 检查点 B：测试 job 由 push/PR 触发 | 新增 `.github/workflows/ci.yml`：`on: push + pull_request`（不限分支，验证分支推送亦可触发）；test job 单槽 ubuntu-latest + setup-dotnet 10.0.201 + 过滤条件 `Category!=Smoke&Category!=ExifTool` 与原 release.yml 逐字一致 | ✅ |
| 4 | 检查点 B：构建/发布仅手动 + inputs 受触发方式守卫 | release.yml `on:` 仅 `workflow_dispatch`（无 push/PR 键）；build job `if: github.event_name == 'workflow_dispatch'`；release job `if: github.event_name == 'workflow_dispatch' && inputs.create_release` — 所有 `inputs.*` 引用所在 job 均受事件守卫 | ✅ |
| 5 | 检查点 C：两文件拆分 | ci.yml（375B，仅 test）与 release.yml（6065B，test→build→release 发布链）按触发方式分文件，与 spec 研究输入 Q3 结论一致 | ✅ |
| 6 | （顺带，同一 verify 步骤内）Verify structure 死检查转正 | 原 78 行校验 `worker/PhotoPrivacyWorker* || true`：路径为 ADR 0010 修订前的旧 `worker/` 子目录（commit 38142f9 后实为平铺，见 ADR 0039 §5），且 `|| true` 使该校验恒绿失效；现改为平铺根目录真实校验并移除 `|| true`；路径口径与 scripts/publish.sh:120-123、publish-app.ps1 实物一致 | ✅ 已核对 ADR 与脚本实物 |
| 7 | 行为保持（发布面） | 7-rid matrix、test→build→release 依赖链、dotnet 10.0.201、publish 脚本调用参数、deb×2/app bundle/upload/release 步骤逐字段保留（node 检查：7 rids、依赖链、触发键扫描 PASS） | ✅ |
| 8 | CONTEXT.md 术语一致（spec 用户故事 12） | Manual Dispatch 条目修订为「发布手动 + 测试门禁 push/PR 自动（对 ADR 0016 的部分修订）」；Release Directory 条目同步「完全平铺（UI+Worker 根目录）」口径 | ✅ |
| 9 | ADR 0016 修订说明随报告呈报 | 见 §3；本票不改 ADR 文件（spec Further Notes：修订随收口沉淀） | ✅ 已呈报 |
| 10 | 静态卫生 | `git diff --check` exit 0（空输出）；两 workflow 文件 LF、无 BOM；22 项 node 检查 ALL-PASS（勘误 2026-09-05：初版误写 23，实际 ALL-PASS 轮脚本为口径修正后 22 项检查，见 28-fix 报告）（初次跑 1 FAIL 为验证脚本自身口径误报——ci.yml 合法地零表达式——修正口径后复跑全绿，过程留痕） | ✅ |
| 11 | 版本控制纪律（WORKFLOW §4.2/§4.4） | GitButler 单票独立分支 round7/28-ci-pipeline-repair，单 commit，无裸 git 写、无 push；本票未触发任何动栈/丢弃类操作，§4.4 快照义务不触发；他人物品（codex/session 分支、round6 merged 栈）未触碰 | ✅ |
| 12 | CI 验证分支 run 实物绿（jobs 非空、有日志、test 成功） | 本窗口不推送（handoff 明定）；commit 40fc946 已就绪，移交大脑推送验证分支并复核 | ⏳ 移交大脑 |

**完成定义自查（handoff 五条）**：1✅ 2✅ 3✅ 5✅（本条报告 + docs 副本随第二 commit）；第 4 条（CI run 实物绿）为本票最终验收，由大脑执行——本窗口交付物已就绪。

## 2. ADR 0016 修订说明（随报告呈报，供收口沉淀为 ADR 修订）

- 原决策（ADR 0016）：CI/CD 全流程 workflow_dispatch 手动触发，push 触发方案被否决（理由：用户要求不自动运行）。
- 票 28 修订边界：**仅测试门禁放开**——ci.yml 的 test job 在 push/PR 自动运行；构建与发布（release.yml 的 build/release job）仍纯 workflow_dispatch 手动，发布节奏仍由用户把控。
- 理由：幽灵 run 使 CI 自 workflow 诞生起 28/28 全红 0 绿 + 本机 CI-only 政策 ⇒ 项目处于零云端测试证据状态；push 触发测试 + 手动发布为 atomcode Q3 调研结论确认的行业标准形态；spec 判定该冲突属决策问题，已由用户随第七轮派发裁定。
- 不变部分：matrix 平台布局、create_release inputs 门控、单 runner 原生 arm64 等 ADR 0016 其余决策全部保留。

## 3. 移交大脑动作清单

1. 推送 CI 验证分支（含 commit 40fc946），确认：push 后不再出现 0-job 幽灵 run；ci.yml 产生真实 test job run（有日志、绿）。
2. 可选冒烟：对验证分支手动 dispatch release.yml（create_release=false），确认 build matrix 7 rid 绿、Verify structure 步骤通过。
3. 云端结果回填本报告第 12 行，置 issue 28 全部 checkbox，ADR 0016 修订沉淀，收口第七轮 CI 部分。

## 4. 已知风险与边界

- ci.yml 未限分支（任何 push/PR 均触发）——保障验证分支可触发；若收口裁决限 main，一行改动。
- ubuntu-latest 上首次云端 `dotnet test` 可能暴露 Linux 特有测试问题（本机零证据属政策使然）；红灯按流程开返修窗。
- release.yml 保留自带 test job 作为发布前复检，与 ci.yml 在手动发布时重复运行一次，属预期成本。
- 与 main（53c669e）合入冲突面为零：git diff 0a8fd4b..53c669e 在 CONTEXT.md / .github/ 上无变更（仅 AGENTS.md 一行与本票无关）。

## 5. 写入通道说明（纪律留痕）

- 本窗口无 ctx_* 工具，按启动器授权的退回路径使用内置读工具读全必读清单。
- 文件写入用字节精确的 Write 工具（YAML 含大量 `${{}}`/引号/反斜杠，node 脚本字符串内嵌转义风险更高，Write 不经 shell 无嵌套吞字路径，满足「避免嵌套导致对话断开」的意图）。
- 字节级验证用 node.js 脚本（OS 临时目录，非 repo；验证后已删除），含 sha256/CRLF/BOM/三元扫描/结构锚点 22 项（勘误：初版误写 23）。
- 本机未运行任何 dotnet/actionlint/lint（CI-only 政策）。
