# 测试约定（PhotoPrivacy · tests/）

> 这份文件说的是 `tests/` 里写测试的规矩——尤其是那些把源码、配置、文档当文本检查的测试。动手写之前先读一遍；测试失败时按这里的办法处置。
> 由来：ui-craft 轮（2026-09-12），依据当时的调研结论——测试失败要先判断该删、该改还是该留，不许为了让它通过就顺手改断言。
> 关联：docs/design/ui-visual-standard.md（旧界面框架的历史视觉规范存档；现在的视觉标准看 #49 T4 的六个参照点和 docs/agents/ui-smoke.md）。

## 0. 适用范围

- **把源码当文本检查的断言**：编译期跑的静态检查，做法是对源码 / 配置 / 文档做 grep、正则、字面量存在性判断（旧界面框架的那批已随旧 Ui 工程退役（#61）；本项目现存 `PublishPropertiesCsprojGuardTests` / `SmokeScriptDiagnosticsTests` / `WorkerIpcSyncOverAsyncGuardTests` 等属此类）。
- 行为测试（单元 / 集成）不受 R1 约束，但测试失败时同样按 R2 的办法分类处置。

## R1. 新检查必须写清「防什么」——写不出就不许加

**规则**：

> 任何**新增**的把源码当文本检查的断言，必须在断言旁边（断言语句上一行，或 `Fact` 特性上方）写行内注释，交代清楚两件事：
> ① **它防的是哪个具体 bug / 哪种又犯老毛病的情况**（写清楚「哪一处代码曾经或可能变成什么样」）；
> ② **关联 issue 号**（形如 `issue #8163`）。
> ①写不出来的断言一律**不收**（review 不通过、不合入）。

**依据**：

- 当初调研的第 1 条结论：「不知道为什么存在的测试」算一种正式的坏味道。
- 现状事实：本项目 `tests/` 1057 条断言里 491 条（46.5%）是文本 grep。这条规矩**只管新增**，不追溯存量（和「覆盖率门禁只罚新增」是同一个思路）。

**合格的写法**（参照 `PublishPropertiesCsprojGuardTests` 里的真实注释）：

```csharp
// 防的是：发布属性（SelfContained / PublishSingleFile / PublishTrimmed 这些）被直接写回
// csproj / props / targets，绕过「发布参数只从发布脚本传」的规矩；直接定义会被自动继承，
// 还会被子目录的 props/targets 覆盖，静悄悄绕过脚本。
// 关联 issue #12。
[Fact]
public void Production_Project_Files_Should_Not_Hold_Publish_Properties() { ... }
```

**不合格的写法（不收）**：

```csharp
[Fact]
public void Theme_Swatch_Count_Must_Be_Five() { ... }   // 没注释：不知道防什么，测试失败时没法判断该删、该改还是该留
```

## R2. 测试失败先分三类——删 / 改 / 留

**规则**：

> 任何把源码当文本检查的测试**失败**时，禁止「为了让它通过就直接改断言」。先给这次失败分个类，再把分类结论记录下来：

| 类 | 怎么判断 | 怎么处理 |
|---|---|---|
| **删** | 断言检查的是**措辞 / 格式 / 排版**（钉死某句话怎么说、代码有几行、注释写了什么），和任何行为规矩无关 | 删掉这个断言；要是真有行为规矩要守，另想更结实的机制替代，不保留原断言的样子 |
| **改** | 背后确实有行为规矩要守，但现在的检查**方式太脆**（文本 grep 一遇到无关重构就误报） | 换成更语义化的做法。工具优先级：**PublicApiAnalyzers(RS0016) / ApiCompat / ArchUnitNET 优先**（运行时零开销）；确实需要行为断言时再引入语义化测试组件，小步来，**先在 CI 上实测耗时**（有实测数据才引入，不能先斩后奏） |
| **留** | 确实保命、且没有等价的语义化办法可替代 | 保留断言 + 按 R1 补「防什么」注释 + 注释里写明**「这是变更探测器，不是契约」** + **定好复审时间**（写明什么情况发生或到哪一天就重新看） |

**配套要求**：

1. 三类的结论要写进当期报告（声明→证据→结论对照表），并**逐类记数**（删了几条、改了几条、留了几条）。
2. 文本断言的占比顺其**自然下降**，不定一次性清理目标、不立专项（当初已否决「全量分类大扫除」：diff 巨大、没有触发信号）。
3. 所有防老毛病的机制都要有「**自己坏了会测试失败**」的自检——防止检查本身坏掉还一直显示通过（假通过）。
4. 视觉一致性**永远不要只靠文本断言单独背锅**：行为机制和人工目检分工（现在的做法见 docs/agents/ui-smoke.md，历史对照流程见 ui-visual-standard.md §7）。

## R3. 测试类怎么命名、怎么分类（Category trait）——统一规则

**规则**（检查：`TestNamingCategoryGuardTests`，三条硬性要求，坏一条就测试失败）：

> 1. **命名**：`tests/` 下含 `[Fact]`/`[Theory]` 的文件，其 `public` 测试类必须以 `Tests` 结尾。辅助类（fake / stub / fixture / 基类 / 静态 helper）用 `internal` 或嵌套形态，不受这条约束——xUnit 只发现 `public` 类，这是区分测试类和辅助类唯一靠得住的办法。
> 2. **取值范围**：`[Trait("Category", …)]` 只许 `Smoke`、`ExifTool` 两个值，和 ci.yml / release.yml 的 `--filter "Category!=Smoke&Category!=ExifTool"` 一一对应；**不写 trait = CI 默认跑的常规测试**（单元测试和全部把源码当文本检查的测试都是这个默认形态），不许自造第三个值。
> 3. **目录双向对齐**（限 `PhotoPrivacy.IntegrationTests`）：`Smoke\` 目录 ⇔ `Category=Smoke`，`ExifTool\` 目录 ⇔ `Category=ExifTool`；住在目录里的必须带对应 trait，带 trait 的必须住在对应目录。

**配套要求**：

1. 测试**方法**名沿用 `Method_Scenario_Expected` 约定，**不做 lint 检查**（和措辞绑定属于 R2 该删的那一类，只作为评审时的说法）。
2. 这个检查要防「自己抓自己」：trait 匹配正则和规则字面量在检查源码里分片拼出来，不出现连续的 `[Trait(` 字面量。

**依据**：#14 工程债——存量 `RealExifToolProbeTest`（单数命名、`Category=ExifTool` 却住 `Smoke\` 目录）和 `ReleaseReadinessFullCaptureGuardTests`（把源码当文本检查的测试没带 trait 却住 `Smoke\`）两处不一致，规矩没写成文字，犯了也没人管。

## 3. 沿用下来的老规矩（既有先例，本文件不改它们）

- **剥注释**：把源码当文本检查的一律先走 `SourceLint.ReadStripped` 剥掉注释再断言（吃过亏——不剥会把注释里的示例字面量当成违规）。XML 工程文件（csproj / props / targets）例外：不走 `ReadStripped`（剥 `//` 会破坏 XML 里的 URL 字面量），XML 注释解析器本来就无视，读取走 `SourceLint.Read`（#12）。
- **单槽串行**：测试单槽串行跑（`test.runsettings` MaxCpuCount=1），多窗口不许并发跑 test（WORKFLOW §5 / §7.2）。
- **CI-only（agent 窗口，dotnet 侧）**：agent 本机禁跑 dotnet 构建 / 测试，验收交给云端 CI（.github/workflows/ci.yml）；本机窗口只做静态检查。例外：`scripts/release-readiness.ps1`（内含 test+smoke+publish）、`scripts/publish-app.ps1` / `scripts/publish.sh`（含 `dotnet publish`）都是发版机专用的，只在明确发版时由人来跑，agent 不主动碰。

## 4. 变更记录

| 日期 | 轮次 | 变更 |
|---|---|---|
| 2026-09-12 | ui-craft | 立区；写入 R1（新检查必写防什么 + issue 号）和 R2（失败分三类：删/改/留 + 语义化优先 + 变更探测器定性 + 复审期） |
| 2026-10-29 | #87 | 清掉已退役旧界面框架的说法（AXAML、Avalonia.Headless、XAML Thickness），视觉规范标记为历史 |
| 2026-11-08 | #12 | §3 剥注释补 XML 工程文件豁免：解析器判注释、读取走 `SourceLint.Read` |
| 2026-11-08 | #14 | 新增 R3 命名与分类规则 + `TestNamingCategoryGuardTests` 三条硬性要求；存量对齐（`RealExifToolProbeTest` → `RealExifToolProbeTests` 并搬进 `ExifTool\`、`ReleaseReadinessFullCaptureGuardTests` 搬出 `Smoke\`） |
| 2026-11-09 | #253 | 去掉对两个已删测试的引用，R1 示例换成现役的 `PublishPropertiesCsprojGuardTests`；正文按 GLOSSARY 新词表全部改成人话 |
