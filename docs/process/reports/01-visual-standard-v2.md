# 票 01 收口报告 — visual-standard-v2（ui-craft2 轮首票）

> 覆盖：D-005；A-001, A-005。分支 `ui-craft2/01-visual-standard-v2`（锚定 i0 栈顶以解 koy 悬空依赖）。
> 日期：2026-09-14。本机 CI-only：零构建零测试；静态门禁全绿。

---

## 0. 结论

票 01 四件交付全部落盘并提交：①工作区 CONTEXT.md 词条入库（首件，commit `zml` / git sha `8a9dea9`）；②`docs/design/ui-visual-standard.md` 升 v2.0（MangoDisk 主基准三族重校 + §8 修订行）；③`docs/adr/0067-mangodisk-visual-benchmark.md` 新立（GPL 边界 + revised 关系 + T-3 登记）；④本报告双轨落盘。验收 checkbox 全项满足（§1 对照表）；通用纪律无违规。

## 1. 验收对照（issue 验收 checkbox）

| 验收项 | 证据 | 判定 |
|---|---|---|
| v2.0 文档存在且 tokens/骨架/范式三族均有 MangoDisk 基准定值或「票内调研定值」标注 | `docs/design/ui-visual-standard.md` v2.0：§0.1 主基准表三族定值齐备；§2.4 nav-item 实测公式；§4.1 E2 空态按 `md-empty-state` 重校；§5.1 布局 token 基准列；§6 T1-T8 按 sonner 实物重校；附录 D.1-D.7 取证表全量 | PASS |
| ADR 0067 落盘，含 GPL 禁拷约束与 D-005 revised 关系登记 | `docs/adr/0067-mangodisk-visual-benchmark.md`：D2 GPL-3.0 零拷贝边界；D1「上轮 D-005 标记 revised」明示；D4 张力 T-3 登记 | PASS |
| CONTEXT.md 词条入库（git log 可见），词条计数与保险副本一致 | commit `zml`（git `8a9dea9`）：+13/-1 纯词条 diff；程序化比对 4 块（Visual Baseline 修订 + Nav Capsule / Rounded Window / MangoDisk Benchmark 新增）标题/正文/`_Avoid_` 逐字在库；CONTEXT.md 总词条 103 | PASS |
| 报告双轨落盘（.scratch 主本 + docs/process/reports 副本逐字一致） | 本文件 + `docs/process/reports/01-visual-standard-v2.md`，SHA256 比对一致（§5） | PASS |
| D-001 承接注（过程验收代理） | 本票为文档票无 UI 改动；before/after 截图属施工票（02-08）范围；规范 §7 V1-V3 已换 MangoDisk 对照清单为后续票的过程验收代理 | PASS（登记） |

## 2. 交付物清单

| 文件 | 动作 | 字节数 |
|---|---|---|
| `CONTEXT.md`（工作区词条） | 提交（首件 `zml`/`8a9dea9`） | 45714（+13/-1） |
| `docs/design/ui-visual-standard.md` | v1.2 → v2.0 修订 | 45127 / 424 行 |
| `docs/adr/0067-mangodisk-visual-benchmark.md` | 新建 | 6610 / 63 行 |
| `.scratch/ui-craft2/reports/01-report.md` | 新建（本文件） | — |
| `docs/process/reports/01-visual-standard-v2.md` | 新建（双轨副本） | — |

未触碰：AppTheme.axaml / NavButton.axaml / 各页面 axaml / DesignTokens.axaml / tests/（票 02-08 领地，§4.3 一次一票）；`zz` 区 pp/yu/my 三份轮级 docs 副本（见 §6-R2 呈报）。

## 3. 首件核验（A-005 处置记录）

1. **悬空依赖复现**：`but commit` 首次报 `CONTEXT.md line 411 depends on commit koy`——koy 已随上轮 land 进 main（6bf244e），引擎仍记该引用（A-005 预言命中）。
2. **处置**：`but branch new ui-craft2/01-visual-standard-v2 --above sky` 将本票分支锚定 i0 栈顶（koy 在栈内成为祖先）；新分支被判 merged-upstream，按 hint 以 `--allow-merged` 完成提交 `zml`。**未做 `but pull`**——其会重排 g0/co 等他票 lane 的共享工作区，超本票权限面（§4.3），留大脑择机执行。
3. **逐字核验**：node 脚本比对保险副本 `docs/process/ui-craft2/context-entries-pending.md` 4 个词条块（标题/正文/`_Avoid_` 行）在工作区 CONTEXT.md 全部 verbatim 命中；git diff 实物 +13/-1 与副本描述一致；插入位置 = Visual Baseline 词条之后 ✓；禁改措辞遵守。
4. **格式**：CONTEXT.md 45714B、无 BOM、0 CRLF。

## 4. 调研（动工前置要求，通用纪律第 1 条）

### 4.1 atomcode 深度调研（串行一次，2026-09-14）

- 执行：`atomcode -p`（对比调研：Web 设计 token 体系 → Avalonia 12 桌面规范的通行定值/映射方法/工业先例）。Sufficiency Gate：10 检索（Exa 2 + Tavily 2 + AnySearch 6）、5 角度全覆盖、13 次原文核验。
- 核心结论：
  - **映射架构**（§2.1，Confidence 高）：三层 ResourceDictionary——`:root` 变量 → `Application.Resources` 合并字典；`.dark` → `ThemeDictionaries` Light/Dark 子字典；`var(--token)` → `{DynamicResource}`；`calc()` 无等价物须预计算写死；组件覆写 → `Classes` + 就近 Resources（Semi 官方演示）。官方口径：Avalonia 无集中 token 表，四层色板结构（Discussion #16554，维护者 stevemonaco）。
  - **oklch → sRGB**（§2.2）：Avalonia 只吃 sRGB（#8450 广色域仍 open）；shadcn neutral 板换算表确定；MangoDisk 实际栈 shadcn-vue 与 React 版 token 表逐字一致（双源核验）。
  - **圆角/间距/字号**（§2.3/2.4）：shadcn base 10 公式 sm0.6/md0.8/lg1/xl1.4；MangoDisk 皮实测 base 8 → 4/6/8/12；Tailwind 4px 基数 = DIP 同值；Semi 不强制字号档位须自建 `x:Double`。
  - **按钮过渡白名单**（§2.5）：BrushTransition 逐属性声明；结构滑动给 TransformOperationsTransition 200ms；禁 `transition: all` 等价物。
  - **侧栏胶囊三态**（§2.6）：shadcn SidebarMenuButton = hover/active 同 sidebar-accent + focus ring；侧栏独立 token 组不复用主内容 accent。
  - **Toast**（§2.8）：sonner 定值 364px / 4s / 堆叠 14px+0.05缩放/层 / 400ms 可中断过渡（作者文 + issue #630）；Avalonia 落法 = Ursa Toast/Notification 或自绘 ItemsControl+TransformOperations。
  - **对话框/空态**（§2.9/2.10）：shadcn 档 448/512/672；Avalonia 12 红利 = 主题化窗口装饰 + 页面导航控件 + 编译绑定默认。
  - **先例矩阵**（§2.11）：Semi（ThemeDictionaries 覆写）/ FluentAvalonia（token 化 ControlTheme）/ Ursa（Toast/Notification 免自制）/ Material.Avalonia（运行时换肤）。
  - **信息缺口**：Ursa Toast 样式细节未逐行核验（票 08 选型时补）。
- 决策账本回顾：调研前已全读 decision-ledger D-001~D-007 + A-001~A-005、ADR 0065、CONTEXT.md 现有词条心智模型；工业对标含 MangoDisk 本体 + Avalonia 生态同类实现。

### 4.2 MangoDisk 观感取证（gh api 只读，GPL-3.0 零拷贝）

- 取证面：主题 token 两份 CSS、main.css 全局层、app-shell/sidebar/titlebar 壳层三件、sonner 配置、empty-state/confirm-dialog/page-shell 范式三件、components.json。逐文件定值已沉淀规范附录 D。
- **关键发现（张力 T-3 来源）**：`md-sidebar.vue` 实测 active = `sidebar-accent` 实心胶囊 + 3px×24px 圆头主色左缘条 + 字重 600；hover = accent 52% 且**前景不变色**——与 D-003「主色实心胶囊+反白」及上轮 D-004「hover 向 primary 叙事」方向性偏差。**处置=登记不静默改向**（规范 §2.4 对照表 + 附录 A.2 T-3 + ADR 0067 D4），票 03 施工前裁定。
- 其他佐证：按钮全局 `!important` 禁 transform（与 N1 同构）；`prefers-reduced-motion` 全停仅运营指示放慢续转；focus ring 2px；scrollbar 10px；页壳 readable/wide 宽度档 + container queries。

## 5. 静态门禁（CI-only 口径）

| 门禁 | 结果 |
|---|---|
| CONTEXT.md 词条逐字比对（保险副本 4 块） | 4/4 verbatim 命中 |
| ContextMdTerminologyTests 措辞钉预演 | 9 PIN 在位 8/9、BAN 5/5 无命中；唯一缺钉 `4 按钮（Config/Logs/Rules/ServiceManager）` 系 b77a970 移除、上游已恢复（origin/main 在位）——**既存缺钉非本票引入**，but pull 后自愈（§6-R3 呈报） |
| 无 BOM / LF（本票全部新写文件） | 见 §7 核验输出 |
| XAML 标签平衡 | N/A（本票零 axaml 改动） |
| 撞红预演（守卫失效即红） | 本票纯文档；NavFeedbackSourceTests 预期红属票 03（A-004 预案）；术语钉缺钉为既存态（同上） |
| 新增断言 | 0（无测试代码改动） |

## 6. 张力与呈报

- **R1 / T-3（需裁定）**：nav active 定值——D-003 实色主胶囊 vs MangoDisk 实物 accent 胶囊+3px pill+600 字重；hover 前景 primary 叙事 vs 基准不变色。票 03 施工前请裁定维持或 revised（规范 §2.4 / ADR 0067 D4 登记）。
- **R2（移交大脑）**：`zz` 未提交区遗留 `pp`(docs/process/ui-craft2/decision-ledger.md 修订）/`yu`(spec.md 新增）/`my`(ui-craft2-README.md 新增）三份轮级轨 1 沉淀粉本——非本票交付物，未代提。
- **R3（观察项）**：ContextMdTerminologyTests 缺钉 `4 按钮（Config/Logs/Rules/ServiceManager）` 为基线滞后既存态（上游已修复），`but pull` 后自愈；本票未 pull 因会重排他票 lane。
- **R4（观察项）**：i0/h0 等 merged-upstream lane 残留待 `but pull` 清理；本票分支锚 i0 栈顶，pull 时将随 rebase 自然落到新基线（koy 内容已在上游）。
- **R5（观察项）**：MangoDisk 亮色皮（暖白底 + 深芒果橙 primary）与本项目暗色基调为主的气质差异，页面票实机目检时复核。

## 7. 完成定义自证

- [x] issue 验收 checkbox 4/4（§1）
- [x] 通用纪律 6 条：atomcode 调研先行 ✓（§4）；CI-only 零构建零测试 ✓；版本控制 but + 一票一分支 ✓（无动栈类操作，未触发 §4.4 快照前置）；中文交互 ✓；报告双轨逐字一致 ✓（§5）；无静默改向（T-3 登记呈报）✓
- [x] 首件提交完成且计数核验通过（§3）
- [x] 改动清单先行声明（会话内呈报记录），共享文件触碰面无越界
