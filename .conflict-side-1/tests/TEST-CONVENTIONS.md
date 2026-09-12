# 测试约定（PhotoPrivacy · tests/）

> 定位：本文件是 `tests/` 的**测试约定区**——写测试（尤其 source-lint 类文本断言）前必读，撞红时按本文件处置。
> 立策：ui-craft 轮票 01（2026-09-12），依据 decision-ledger D-006「守卫哲学 = C（atomcode 调研版，撞红即治理）」与 A-005。
> 关联：ADR 0065（取舍记录）、docs/design/ui-visual-standard.md（视觉规范）、.scratch/ui-craft/spec.md「Testing Decisions」节。

## 0. 适用范围

- **source-lint 断言**：把源码 / AXAML / 文档当文本做 grep、正则、字面量存在性判断的编译时静态断言（本项目 `DesignSystemTests` / `UiLauncherSourceTests` / `MainWindowShellSourceTests` / `WorkflowSnapshotScriptGuardTests` 等属此类）。
- 行为测试（单元 / 集成）不受 R1 约束，但撞红时同样适用 R2 的分类治理。

## R1. 新断言必须写「防什么」注释 —— 写不出就不许加

**规则文本**：

> 任何**新增** source-lint 断言，必须在断言方法体紧邻位置（断言语句上一行，或 `Fact` 特性上方）写行内注释，完整交代两件事：
> ① **它防的具体 bug / 回潮形态**（写清楚「哪一处代码曾经或可能退化成什么样」）；
> ② **关联票号或 issue 号**（形如 `票 01 / ui-craft`、`issue #8163`）。
> 写不出 ① 的断言一律**拒收**（review 不通过、不合入）。

**依据**：

- D-006（atomcode 调研版）第 1 条；atomcode 调研定性「Absence Of Why」属 test smell 正式条目。
- 现状事实：本项目 `tests/` 1057 条断言中 491 条（46.5%）是文本 grep（锐评 §7.1 原子化实数）。本规则**只罚新增**，不追溯存量（与 D-006 第 4 条「覆盖率 / 新增债门禁只罚新增」同口径）。

**合格示例**：

```csharp
// 防：MainWindow.axaml 侧栏内部 Border 曾写死 Width=200（report-32 D3 观察项），
// 拖宽侧栏后内部面板视觉宽度不跟随 —— 锁死「内部定宽须由 ui-visual-standard.md §5 显式背书」。
// 票 04 / ui-craft
[Fact]
public void Sidebar_Inner_Border_Width_Must_Not_Be_Hardcoded() { ... }
```

**不合格示例（拒收）**：

```csharp
[Fact]
public void Theme_Swatch_Count_Must_Be_Five() { ... }   // 无注释：不知道防什么，无法在撞红时做杀/改造/保留判断
```

## R2. 撞红即三档分类 —— 杀 / 改造 / 保留

**规则文本**：

> 任何守卫（source-lint 断言）**撞红**时，禁止「无注释裸改断言使其变绿」。必须先对触发守卫做三档分类并留痕：

| 档 | 判定条件 | 处置动作 |
|---|---|---|
| **杀** | 断言与**措辞 / 格式 / 排版**耦合（钉字符串措辞、钉行数、钉注释文字），且与任何行为不变量无关 | 删除该断言；若确有不变量要守，另立语义机制替代，不保留原断言形态 |
| **改造** | 背后确有行为不变量，但当前表达**方式脆弱**（文本 grep 易被无关重构击穿） | 语义化替换。工具链优先级：**PublicApiAnalyzers(RS0016) / ApiCompat / ArchUnitNET 优先**（零运行时开销）；**Avalonia.Headless 仅在确需行为断言时使用**，且小步引入并**先实测 CI 时延**（与 CONTEXT.md「Source-lint Test」词条旧 Avoid「不引 Headless」存在张力，须实测背书后才可整词修订，不得先斩后奏） |
| **保留** | 确需护命、且无等价语义机制可替代 | 保留断言 + 按 R1 补「防什么」注释 + 在注释中做 **characterization 定性**（明示「这是变更探测器，不是契约」）+ **设复审期**（写明复审触发条件或日期） |

**配套要求**：

1. 三档分类的结论必须写进当票报告（声明→证据→结论对照表），并**逐票记录三档分类数**（D-006 度量要求）。
2. 文本断言占比如期**随票自然收敛**，不设一次性清理目标、不立专修票（atomcode 已否决「全量三档分类专修票」：巨型无行为 diff、无触发信号）。
3. 所有防回潮机制必须有「**失效即红**」自检——防恒绿（tokenlint 式假绿事故教训）。
4. 视觉一致性验收**永不交给纯文本断言单独背锅**：行为机制 + 人工 before/after 截图对照分工（见 ui-visual-standard.md §7）。

## 3. 继承口径（既有先例，不因本文件改变）

- **剥注释口径**：source-lint 读取源码一律走 `SourceLint.ReadStripped` 剥注释后再断言（票 30 教训——不剥会把注释里的示例字面量误判为违规）。
- **单槽串行**：测试执行单槽串行（`test.runsettings` MaxCpuCount=1），多窗口不得并发跑 test（WORKFLOW §5 / §7.2）。
- **CI-only**：本机禁跑构建 / 测试，验收交云端 CI（.github/workflows/ci.yml）；窗口侧只做静态自证。
- **XAML Thickness 字面量**：Padding/Margin 仍用字面量字符串以触发 `ThicknessTypeConverter`，source-lint 不得断言「Thickness 必须走 DynamicResource」（ADR 已约束，见 CONTEXT.md「Padding Literal Quantization」）。

## 4. 变更记录

| 日期 | 票 | 变更 |
|---|---|---|
| 2026-09-12 | 票 01 / ui-craft | 立区；写入 R1（新断言必写防什么 + 票号）与 R2（撞红三档分类 + 语义化优先级 + characterization 定性 + 复审期） |
