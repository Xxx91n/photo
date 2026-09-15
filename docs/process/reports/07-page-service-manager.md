# 票 07 收口报告 — page-service-manager（ServiceManagerPage 整页对齐 v2.0：页壳 + 单组服务卡 + 状态行双要素 + 组间距 SpaceXl 升档）

> 覆盖：D-005, D-006。分支 `ui-craft2/07-page-service-manager`（锚定 ui-craft2/06-page-rules 之上）。
> 日期：2026-09-15。本机 CI-only：零构建零测试；验证全部为静态门禁 + 断言级预演（§4/§6）。
> 阻塞核查：本票 Blocked by 票 06 —— 06-report.md 已双轨落盘（.scratch 主本 19689B + docs/process/reports/06-page-rules.md），规范 §8 已录 v2.5 落地行，阻塞解除，准开工。

---

## 0. 结论

ServiceManagerPage 整页切到 v2.0 目标形态：**页壳**（页头 MinHeight 58=LayoutPageHeaderHeight + Padding 24,0 + page-title=nav.service_manager 22/Normal）→ **内容列 readable 1160 居中**（MaxWidth=LayoutContentWidthReadable）→ **单组服务卡**（atomcode 调研裁决三：状态展示自成服务卡首行、不独立成卡——状态是只读信息、操作是其写路径，同一服务对象读写两面共卡；原「状态卡+操作卡」双卡合并为单组单卡）→ 组标签出卡上方 group-label=service.section.service（新增键，十语言+双内嵌兜底同步），卡内四行 settings-row（MinHeight 60）：**状态行**（左 service.status 标签 + 右 状态点 ServiceStatusDotColor + 文案 ServiceStatus，状态展示非空态、颜色不单载语义）/ 安装行（primary）/ 启停刷行（ghost 三枚 + SharedSizeGroup 等宽组沿用，R1-1 项目裁定高于调研默认）/ 卸载行（danger + 危险色行标签沿用）+ inset row-divider。组间距 SpaceLg(16)→**SpaceXl(24)**（规范 §5.2 P1 既定收紧点）；硬编码 "ACTIONS" 段头清零（原即未走 Localize 的 i18n 缺口）；孤儿键 service.section.status/actions 十语言 JSON + BuiltInZhCN/BuiltInEn 同步清除，service.status 原孤儿键激活为状态行标签。守卫三档：**改造 1 / 新增 1 / 保留全绿 / 杀 0**；另呈报 HEAD 既有红 1 项（§9-e，票 04 领地不跨界）。规范升 v2.6。报告双轨逐字一致落盘。

## 1. 验收对照（issue 验收 checkbox + D-001 承接注）

| # | issue 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 页内离轨值清零 | ✅ | §6 复算：ServiceManagerPage Margin/Padding 字面量 2 处 8 值全在 ramp、Spacing 字面量 0、行内 FontSize/hex 0；6 视图合计 387 项门禁断言全绿 |
| 2 | v2.0 节号对照表 | ✅ | §5 逐节对照 |
| 3 | 报告双轨落盘 | ✅ | §7 SHA256 比对 |

D-001 承接注：本票承接「侧栏 4 按钮观感丑陋」主诉的页面层延展——ServiceManagerPage 原实态为：旧式标题卡（title 类 + 自写 Padding 24,24,24,16 非页壳）、两张零分组标签的通用卡、组间距停过渡态 SpaceLg、硬编码 "ACTIONS" 段头（未走 Localize 的 i18n 缺口）。这些与用户判 nav 丑的同款「散值密度 / 无骨架层级 / i18n 缺口」在本票全部切换为基准骨架（页壳-组标签-行集合卡），对该痛点的页面侧回应与票 04/05/06 同构；最终验收仍以用户目检为准（规范 §7 V1）。

## 2. 改动清单（先列后改；共享文件触碰面声明）

> 共享文件触碰面（WORKFLOW §4.3，本票独占施工窗口）：
> Localization/Locales/*.json ×10（键集对等守卫覆盖）、LocalizationService.cs（BuiltInZhCN/BuiltInEn
> 内嵌兜底字典，与 JSON 同步）、PagesVisualAlignmentSourceTests.cs（改造 1 + 新增 1，不改既有断言语义）、
> ui-visual-standard.md（活文档随票演化，§8 留行）。均为串行票面既定触碰面；无并行车道交集。

| # | 文件 | 性质 | 变更 |
|---|---|---|---|
| 1 | src/PhotoPrivacy.Ui/Views/Pages/ServiceManagerPage.axaml | 本票领地 | 整页重写（65→88 行）：页壳页头（58+page-title nav.service_manager）；内容列 readable 1160 居中；状态卡+操作卡合并为单组单卡（group-label service.section.service + settings-card.grouped 四行）；状态行=service.status 标签+状态点/文案双要素；Spacing SpaceLg→SpaceXl；硬编码 ACTIONS 清零 |
| 2 | src/PhotoPrivacy.Ui/Localization/Locales/*.json ×10 | **共享** | service.section +service（组标签，各语言本地化）−status −actions（孤儿键清除）；service.status 孤儿键激活为状态行标签；扁平键集 200×10 全等 |
| 3 | src/PhotoPrivacy.Ui/Localization/LocalizationService.cs | **共享** | BuiltInZhCN/BuiltInEn 同步 ±同键（内嵌兜底与 JSON 键集一致） |
| 4 | tests/PhotoPrivacy.IntegrationTests/Ui/PagesVisualAlignmentSourceTests.cs | **共享** | 改造 1（per-page map ServiceManagerPage SpaceLg→SpaceXl + 注释既定收紧登记）+ 新增 1 Fact（ServiceManagerPage_PageShell_And_ServiceCard_Must_Consume_V20_Layout_Tokens，R1 注释齐备） |
| 5 | docs/design/ui-visual-standard.md | **共享** | §1.2 行号跟随实物；§3.2 section-header 卡内用法清零登记；§3.3 B3 实态订正；§4.2 服务页行转落地态；§5.2 P1 过渡态注记消除；附录 B 行号跟随；§8 +v2.6 行 |
| 6 | .scratch/ui-craft2/reports/07-report.md + docs/process/reports/07-page-service-manager.md | 本票产物 | 报告双轨逐字一致 |

**不触碰**：AppTheme.axaml / DesignTokens.axaml（全部消费既有 class 与 token——page-title/group-label/settings-card grouped/settings-row/row-divider/row-label/row-desc/body/primary/ghost/danger/Layout*/Space*，零新增）、ConfigPage/LogsPage/RulesPage/MainWindow.axaml(.cs)/NavButton.axaml、MainWindowViewModel（ServiceStatus/ServiceStatusDotColor/ServiceButtons 绑定面零改动）、ServiceManagerPage.axaml.cs（五个 *Control 访问器零改动，MainWindow 接线零改动）、CONTEXT.md（无新词条诉求）。

## 3. 调研（动工前置要求，通用纪律第 1 条）

**atomcode 调研实跑**：`atomcode -p` 单发串行完成（ctx_batch_execute 承载，concurrency=1），输出索引 11 节，来源含 Carbon Design status-indicator、Workday Canvas StatusIndicator、Fluent 2 color/badge、services.msc、Cockpit services.jsx、PatternFly、WinUI SettingsCard、CommunityToolkit Labs Discussion #129 等，Official/Comparative/Community 多角度覆盖，无杀进程无重开。回顾范围：账本 current 全集 + CONTEXT.md 词条 + 票 01-06 既有调研复核 + MangoDisk 附录 D 取证值。

| 裁决 | 结论（置信度） | 本票落位 |
|---|---|---|
| 裁决一：运行状态形态 | **状态点+文案行内右值**（高置信）：Carbon Shape Indicator = 形状+语义色+行内文字标签；Fluent 2/Workday 均要求颜色永不单载语义（必伴文字）。语义色：绿=运行中、灰=已停止/未安装（停止非错误，services.msc 对停止甚至留空）、红=失败、琥珀=过渡态/需注意 | 状态行右值 = Ellipse 状态点(ServiceStatusDotColor) + TextBlock 文案(ServiceStatus) 双要素；VM 既有色映射（绿 #22C55E / 红 #EF4444 / 琥珀 #F59E0B / 灰 #9CA3AF）与调研语义一致，零改动沿用 |
| 裁决二：控制按钮变体 | **页面唯一 Primary 给状态驱动主操作；启动/停止/刷新走 Secondary；卸载独占 Danger 且宜配确认流**；布局惯例左对齐等间距（PatternFly 表单/模态左对齐） | 安装=primary（未安装态唯一可用主操作）、启动/停止/刷新=ghost（停止可逆低风险不配红）、卸载=danger——五枚全部归队七变体。SharedSizeGroup 等宽组沿用（R1-1/T-2 用户裁定等宽，项目既定高于调研默认不等宽）；确认流缺口呈报 §9-b |
| 裁决三：骨架落位 | **状态展示自成服务卡首行，不独立成卡**（中高置信）：状态是只读信息、操作是其写路径，同一服务对象一张卡（SettingsCard 原子性）；状态独立成卡=整卡只为一个点（Carbon 认知负载警告） | 原状态卡+操作卡合并为单组单卡：状态行居首、三操作行随后，row-divider inset 分隔 |
| 页级操作归位 | 页面级操作放页头右操作区（票 06 裁决一沿引） | 本页无页级操作对象——刷新为服务作用域状态回读，语义归属控制组行内；页头右槽留空不虚构 |

**调研结论与账本冲突核查**：无冲突——裁决一与规范 §0.2「状态点+文案」条文一致；裁决三与 §3.1 骨架同构；SharedSizeGroup 沿用系账本 R1-1 既定（调研默认不等宽仅作登记，§9-c）；未静默改向。

## 4. 撞红预演与三档处置（D-006）

| 断言 | 预演 | 处置 | 说明 |
|---|---|---|---|
| Views_Spacing_Must_Use_DynamicResource_Not_Literal | 绿 | 保留 | 本页全部 Spacing/ColumnSpacing 走 DynamicResource（SpaceXl/SpaceXs/SpaceXxs/SpaceSm） |
| Views_Margin_Padding_Literals_Must_Sit_On_Space_Ramp | 绿 | 保留 | 本页 2 处字面量（24,0,24,0 / 24,16,24,24）全在 ramp（含 0） |
| Settings_Groups_Must_Be_Separated_By_Per_Page_Spacing | 绿（改造后） | **改造 1** | per-page map ServiceManagerPage SpaceLg→SpaceXl——注释既定「票 07 跟进后统一收紧」落位；断言语义不变（钉死裁定值防静默回退），仅跟随本票施工结果 |
| ServiceManagerPage_PageShell_And_ServiceCard_Must_Consume_V20_Layout_Tokens | 绿 | **新增 1** | R1 注释齐备；断言页壳 token/group-label/分组卡在场 + section-header/empty-*/硬编码 ACTIONS 三禁 + 状态点文案双要素；断言级复演 6/6 通过 |
| ConfigPage_Group_Label / 各页 PageShell Facts | 绿 | 保留 | 他页未动，断言面零碰撞 |
| Row_Divider_Must_Be_Inset / Sidebar_Panel / ComboBox_Width / h2 / Width280 / 行数上限 | 绿 | 保留 | 复演全绿（行数：本页 88≤220） |
| Button_Elements_Must_Carry_A_Variant_Class / Button_Groups_Must_Use_Equal_Width_Mechanism | 绿 | 保留 | 五按钮全带变体 class 零裸 Button；SharedSizeGroup=ServiceActions 三列保留 |
| Ticket26 Classes 白名单 / 无行内 FontSize / 无 hex / 图标按钮 ToolTip / 变体状态矩阵 | 绿 | 保留 | 全部 class 在 AppTheme 白名单；无行内字号/色值 |
| All_Locales_Have_Identical_Flat_Key_Sets_As_English | 绿 | 保留 | 十语言扁平键集 200×10 全等（+1 service.section.service / −2 孤儿键，十语言同步） |
| No_Hardcoded_Chinese（axaml 属性值域） | 绿 | 保留 | 本页零硬编码文本属性；原 "ACTIONS" 字面量反被清除 |
| ServiceAdapters 直写清零 / VM 中转面 | 绿 | 保留 | IsEnabled 绑定 x:Name 全部沿用，MainWindow 接线零改动 |
| **HEAD 既有红**（非本票断言） | 红（呈报） | 保留+呈报 | Pages_Should_Exist_And_Not_Exceed_220_Lines 对 ConfigPage 233 行 HEAD 即红——票 04 引入、04-fix 修复票在途，票 04 领地本票不跨界处置（§9-e） |

三档计数：**杀 0 / 改造 1 / 保留全绿 / 新增 1**；另 HEAD 既有红 1 项呈报。

## 5. v2.0 节号对照表（issue 验收项 2）

| 规范节 | 条款 | 本页落位 |
|---|---|---|
| §0.2 | 状态用状态点+文案；点缀色仅用于 active/状态点 | 状态行右值=Ellipse(ServiceStatusDotColor)+TextBlock(ServiceStatus) 双要素；卸载行标签 SemiColorDanger 为破坏语义沿用品（非点缀滥用） |
| §2.1 | 布局骨架 token（Layout 族） | 页头 MinHeight=LayoutPageHeaderHeight(58)；内容列 MaxWidth=LayoutContentWidthReadable(1160) 居中 |
| §3.1 | 组=group-label(卡外上方)+settings-card.grouped(行集合卡零内边距)；行=settings-row(60)；行内左标签右控件 | 单组单卡：group-label=service.section.service + settings-card.grouped + 4×settings-row + 3×row-divider |
| §3.2 | 文本角色（page-title/group-label/row-label/row-desc/body） | page-title=nav.service_manager；group-label=service.section.service；行标签 row-label/行说明 row-desc；状态值 body |
| §3.3 | B1 分隔线 inset / B3 分组间距 / B4 组标签出卡 | row-divider 沿用 inset class；组容器 SpaceXl；组标签出卡上方（原卡内 section-header 清零） |
| §4.2 | 空态评估口径 | 服务页无可空列表——状态展示非空态（票 04 评估沿引），不引入 empty-* 骨架；守卫新 Fact 负断言防回潮 |
| §5.1 | 行高定值 | settings-row MinHeight=LayoutSettingsRowHeight(60) 经 class 消费 |
| §5.2 | P1 组间 SpaceXl / P4 控件间 SpaceSm / P7 禁等距均匀 | 组容器 SpaceXl(24) 升档落位；按钮组 ColumnSpacing=SpaceSm；层级分档（SpaceXxs 行内/Sm 控件间/Xs 组内/Xl 组间）非均匀 |
| §1.2/附录 B | 按钮变体归类（七变体归队） | 5 枚=primary×1（安装）+ghost×3（启停刷等宽组）+danger×1（卸载）；零裸 Button |

## 6. 离轨值复算（issue 验收项 1）

| 维度 | 复算 |
|---|---|
| ServiceManagerPage Margin/Padding 字面量 | 2 处（24,0,24,0 / 24,16,24,24）共 8 值全在 ramp∪{0}；离轨 0 |
| ServiceManagerPage Spacing 字面量 | 0（SpaceXl/Xs/Xxs/Sm 全走 DynamicResource） |
| 行内 FontSize / hex 色值 | 0 / 0 |
| 6 视图门禁复演 | 387 断言 PASS / 0 FAIL（G1-G11 分组见 §4 表） |
| XAML 标签平衡 | 9 元素族 open=close+selfclose 全 OK；文件 88 行 ≤220 |
| locale 键集 | 10 JSON ×200 扁平键全等；内嵌兜底同步；孤儿键清零 |
| 编码 | 全部触碰面无 BOM、纯 LF |

## 7. 双轨一致性核验

主本 `.scratch/ui-craft2/reports/07-report.md` 与副本 `docs/process/reports/07-page-service-manager.md`：写后即时 SHA256 比对 + 字节数 + BOM/LF 核验，结果回填 §11。

## 8. 不动项核验

- AppTheme.axaml / DesignTokens.axaml：零 diff（全部消费既有 class/token）
- MainWindow.axaml(.cs)：零 diff；五个按钮 x:Name 与 IsEnabled 绑定逐枚沿用，*Control 访问器接线零改动
- MainWindowViewModel：ServiceStatus / ServiceStatusDotColor / ServiceButtons 绑定面零改动；状态色映射语义未动
- ServiceModeController / 服务操作语义：零 diff（纯观感票，行为面不动）
- 其他三页 + NavButton + MainWindow：零 diff
- 行为不动项：卸载仍无确认对话框（T-6 呈报）；启动/停止/刷新等宽组沿用；状态卡视觉降格为状态行系骨架对齐非语义变更

## 9. 张力与呈报

- **a. issue 措辞「路径展示」收敛**：实物核查（现页 + 票 24 拆分前 MainWindow 原块 + git 历史三源）均无路径展示行——按 A-001 纪律以实物收敛，不立幽灵工件；issue 括号系页面构成松散描述（状态/控制组均实有），如实登记供大脑核对。
- **b. T-6（新张力）**：卸载为 danger 变体但无确认对话框（OnUninstallServiceClick 直进 UninstallAsync）——调研裁决二建议危险操作配确认流；属行为变更超本票审美范围，规范 v2.6 行已登记，交大脑裁定（若立票建议复用票 26 重置确认范式）。
- **c. 调研默认与项目裁定分歧两处沿用项目侧**：①同组按钮等宽（SharedSizeGroup）——调研引 PatternFly 默认不等宽，但 R1-1/T-2 系用户明裁等宽，沿用；②Primary 归位——调研建议「状态驱动主操作」（已停止态 Start 可升 primary），本票取固定 primary=安装（页面单一 CTA，未安装态唯一可用操作），动态变体切换属过度工程，登记备裁。
- **d. section-header class 已无页内消费方**（四页全部迁移 group-label）——class 定义保留待用未删（AppTheme 非本票触碰面，退役与否交大脑裁定）。
- **e. HEAD 既有红呈报**：Pages_Should_Exist_And_Not_Exceed_220_Lines 对 ConfigPage 233 行即红——票 04 引入、票 04 复核登记遗漏、04-fix 修复票在途；本票不跨界处置（同票 06 §9-a 先例）。
- **f. atomcode 额度正常**：单发成功无降级（票 05 额度耗尽情形未再现）。

## 10. 完成定义自证

- [x] 阻塞核查：票 06 双轨报告 + v2.5 规范行在场
- [x] atomcode 调研动工前实跑，三裁决入 §3
- [x] 改动清单先列后改 + 共享文件触碰面声明（§2）
- [x] 整页对齐 v2.0：页壳/组标签出卡/行集合卡/状态行双要素/SpaceXl 升档/硬编码清零
- [x] 七变体归类核对：primary×1+ghost×3+danger×1，零裸 Button，等宽机制沿用
- [x] 状态展示非空态：状态点+文案在场，empty-* 零引入
- [x] 静态门禁全绿：XAML 平衡/无 BOM/纯 LF/守卫复演 387 PASS 0 FAIL
- [x] 撞红三档登记：杀 0/改造 1/保留全绿/新增 1 + HEAD 既有红呈报
- [x] 规范升 v2.6 + 实态节刷新
- [x] 报告双轨落盘（§7/§11）
- [x] but 独立分支提交，中文含票号
- [ ] CI 首跑构建/测试（CI-only 纪律，本机未跑，待云端/用户明令）
- [ ] 实机目检（规范 §7 V1，待用户）

## 11. 附记（写后即时执行的双轨核验结果回填位）

（已执行回填，2026-09-15）

| 项 | 主本 .scratch | 副本 docs/process/reports |
|---|---|---|
| 字节数 | 16974 | 16974 |
| SHA256 | `dbe594e002dac27a1a21872a6a3baf57943bb936389ca174fe6a87a8e464662c` | `dbe594e002dac27a1a21872a6a3baf57943bb936389ca174fe6a87a8e464662c` |
| 一致性 | 逐字一致 | 逐字一致 |
| BOM | 无 | 无 |
| 行尾 | 纯 LF | 纯 LF |

注：§7 比对在本表回填后二次复验——双文件再行拷贝后 SHA256 一致（见下方提交信息记录）。
