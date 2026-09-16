# 报告 — 票 03 备份面：镜像相对路径布局 + 内容分歧旁路版本化（correctness-round）

> 覆盖 A-006（D-002 不变量②数据零意外丢失 + ⑤宣称-实现一致）｜ 轮次 correctness-round
> 分支：`03-backup-mirror-layout`（GitButler 虚拟分支，与票 01/02 栈并行）
> 状态：窗口执行完毕，停住等首脑复核。CI 行为测试绿为最终凭证（CI-only 纪律，推送由大脑执行）。

## 0. 开工闸（WORKFLOW/prompt 要求）

- Blocked by 票 02：已解除（`spp 票 02 配置面` + `qmq 票 02 返工（CI run 35000648685）` 在栈）。
- 必读清单九份全读：handoff / issue / spec(ID3) / WORKFLOW(§4.2/4.3/4.4) / A 系账本(A-006) / D 系账本(D-002、D-006) / ADR 0005/0006/0041/0043 / `.codex-tmp/锐评.txt`(P1 段)。开工复述已在会话正文完成。

## 1. 验收对照表（issue Acceptance criteria 逐条 → 证据 → 结论）

| # | 验收标准（issue 原文） | 证据 | 结论 |
|---|---|---|---|
| 1 | 行为测试在 CI 绿（双子目录同名/同文件重处理/内容变化三场景全覆盖） | 新增 `tests/PhotoPrivacy.Core.Tests/Pipeline/BackupMirrorLayoutTests.cs`（9 Fact，真实 `FileTaskPipeline` + 真实 `LocalFileOperations` + 磁盘临时目录，断言盘上实物非调用记录）：场景1 `HandleAsync_TwinNames_In_DifferentSubdirs_Should_Produce_Two_Distinct_Backups`（两镜像槽位都在、内容各自正确、扁平碰撞槽位不存在、恰 2 个 .bak、无旁路事件）；场景2 `HandleAsync_SameFile_Reprocessed_With_Identical_Content_Should_Skip_Without_Sidecar`（`backup_skipped:identical_content_slot_kept` 审计、无旁路、单备份内容不变）；场景3 `HandleAsync_Content_Changed_Should_Write_Sidecar_And_Keep_Original_Slot`（原槽位字节保真、旁路 `diverge.jpg.yyyyMMdd-HHmmss(-N)?.bak` 匹配、旁路内容=新源、共 2 备份）。**本机静态口径全绿；CI 云端为唯一凭证，待大脑推送 ci-verify 分支** | ✅（静态）/ ⏳CI |
| 2 | 不存在任何「不同源文件备份到同一路径」的路径构造 | `RuleEngine.ResolveBackupPath` 旧构造 `Path.GetFileName(sourcePath)+Suffix` 已删除（grep 零残留），全仓唯一槽位构造入口 = `BackupPathResolver.ResolveBackupSlot`：可归位监控子树 → 镜像相对路径（单射：不同源 → 不同相对路径 → 不同槽位）；不可归位 → `_unsorted/<文件名>.<SHA256-4字节十六进制>`（路径哈希随源路径单射）；逐组件规范化不引入路径合并（保留名 `_` 前缀、非法字符替换为单字符 `_`，不产生路径分隔符）。守卫测试 `ResolveBackupSlot_CrossSubdirSameName_Should_Never_Collide` + `ResolveBackupSlot_SiblingPrefix_Trap_Should_Fall_Back_To_Unsorted` | ✅ |
| 3 | CONTEXT.md Backup 词条与本票语义一致 | L15-17 词条改写：镜像相对路径布局 + 冲突规则（相同跳过=「同文件重处理保留最新」承接 / 不同 → `名字.时间戳.扩展名` 旁路 / 绝不静默覆盖）+ `_unsorted` 兜底 + ADR 0006 retention 递归兜底；_Avoid_ 登记「单文件覆盖模式」为旧扁平布局追认表述。`ContextMdTerminologyTests.DefaultBackupDirectory_Should_Be_Bak_Not_PpBackup` 所需 `` `<hotFolder>/bak` `` 出现于 L318 未动，守卫不红 | ✅ |
| 4 | 既有守卫不红；双轨报告 + 首脑复核可受理 | 静态复演全部既有备份相关断言：`RuleEngineTests:57` 源在根直下（`hot/a.jpg`）→ 镜像输出 `bak/a.jpg.bak` 与旧构造恒等，不红；`RuleEngineTests:140` HotFolder 空 → null 分支未动；`DefaultPathsTests` 仅锁默认目录名，未动；`FileTaskPipelineTests` 无备份内容断言（隔离子/in-flight/fixed-directory 路径不涉 WriteBackupAsync 变更语义）；`BackupRetentionService` 无任何既有测试（grep 确认），递归改动零冲突；`AppConfigLoader/RoundTrip/DryRun` 仅涉配置面字符串，未动。双轨报告 = 本文件 + `docs/process/reports/03-report.md` 受控副本随票提交 | ✅（静态）/ ⏳首脑复核 |

## 2. 实现落位（文件 → 角色）

| 文件 | 改动 |
|---|---|
| `src/PhotoPrivacy.Core/Pipeline/BackupPathResolver.cs` | 新增 `ResolveBackupSlot`（镜像布局单一入口：归位判定→镜像相对路径 / `_unsorted`+短哈希兜底→逐组件 Windows 规范化：结尾点/空格剥离、保留名 `_` 前缀、非法字符替换、`..`/`.`/根前缀剥离）；`ResolveSidecarPath`（`名字.yyyyMMdd-HHmmss[-N].扩展名` 旁路命名，同秒碰撞序号递进）；`ContentsEqual`（流式逐字节比对，异常按"不同"处理——宁可旁路不冒覆盖险）；`IsReparsePoint`（符号链接拒绝）；`IsSameOrUnderRoot`（委托 WatchPathFilter 共享判定） |
| `src/PhotoPrivacy.Core/Watcher/WatchPathFilter.cs` | 票面「防穿越复用 WatchPathFilter 边界判定」字面兑现：公开 `TryGetSubPathRelative` + `IsSameOrSubPath`（组件级 GetRelativePath + `..` 检测，非裸 startswith），resolver 委托同一实现（备份归位与监控排除共用边界判定单一来源） |
| `src/PhotoPrivacy.Core/Rules/RuleEngine.cs` | `ResolveBackupPath` 由扁平文件名构造改为 `BackupPathResolver.ResolveBackupSlot` 镜像构造（根因修复点） |
| `src/PhotoPrivacy.Core/Pipeline/FileTaskPipeline.cs` | Cleaned_Modified 两处 `AtomicCopyAsync(overwrite:true)` 收敛为私有 `WriteBackupAsync` 冲突分流：①源/槽位为 ReparsePoint → `backup_skipped:reparse_point_refused`(Warn) 拒绝；②槽位不存在 → 直写（overwrite:false）；③槽位存在+内容相同 → `backup_skipped:identical_content_slot_kept`(Info) 跳过；④内容分歧 → `backup_sidecar_versioned`(Info，Data 含 slot/sidecar) 旁路写入。绝不静默覆盖 = 全部分支均不传 overwrite:true |
| `src/PhotoPrivacy.Core/Pipeline/BackupRetentionService.cs` | `EnumerateFiles TopDirectoryOnly→AllDirectories`（旁路版本与 `_unsorted` 槽位仍以 `.bak` 结尾，glob 天然覆盖）→ ADR 0006 size/TTL 兜底兑现到镜像子树；新增 `CleanupEmptyDirectories`（retention 删空后镜像空目录自底向上回收）；枚举/清理失败静默尽力而为（不反噬主流程） |
| `tests/.../Pipeline/BackupMirrorLayoutTests.cs` | 9 Fact（三场景行为 + 路径构造不变量 + 兜底 + 保留名 + 旁路命名 + retention 递归/空目录） |
| `CONTEXT.md` | Backup 词条精确化（见 §1#3） |

## 3. 失效即红预演（新增守卫）

`03-evidence-red-forecast.log`（纯路径构造静态复算，无本机编译，CI-only 纪律）：

1. **回退槽位构造至旧 `GetFileName` 扁平式**：`hot/vacation/IMG_0001.jpg` 与 `hot/work/IMG_0001.jpg` 复算同得 `D:\hot\bak\IMG_0001.jpg.bak` → 场景1 双槽位存在断言红 + 内容各自正确断言红；`ResolveBackupSlot_CrossSubdirSameName` NotEqual 断言红。新构造复算两槽位含镜像子目录互异 → 绿。复演成立。
2. **回退写入为 `AtomicCopyAsync(overwrite:true)`**：场景2 无 `identical_content_slot_kept` 事件路径（直写替换）→ 事件断言红；场景3 原槽位被 File.Replace 覆盖 → `slotBytesV1` 保真断言红 + `backup_sidecar_versioned` 断言红。
3. **回退 retention 为 `TopDirectoryOnly`**：`EnforceMaxSize_Should_See_Mirror_Subtree_Backups` 中镜像子树 1220B > 1024B 上限而 TopDirectoryOnly 枚举到 0 文件、size 兜底不触发 → 幸存总大小断言 `<=1024` 红。

## 4. 调研与裁决表（atomcode，单发串行，10 分钟内完成）

问题：镜像相对路径布局 + 内容分歧旁路版本化的工业共识与边缘兜底。高置信（10+ 一手工具文档零反例 + 三个 2026 真实 CVE 反例佐证安全边界）：

| 裁决点 | 结论 | 置信 | 本票落位 |
|---|---|---|---|
| 布局 | 镜像相对路径（rsync -R/rclone --backup-dir/Syncthing .stversions/TM/FileHistory/rsnapshot 共同心智） | 高 | ResolveBackupSlot 镜像构造 |
| 冲突 | 相同跳过（幂等）/不同写时间戳旁路，永不静默覆盖（Syncthing Simple/FileHistory/exiftool _original） | 高 | WriteBackupAsync 四分支 |
| 穿越/链接 | 解析两侧+组件级判定（CVE-2026-70460 rsync、CVE-2026-54572 rclone、Borg 2d63253） | 高 | WatchPathFilter 共享判定 + IsReparsePoint 拒绝 |
| 无法归位 | `_unsorted`/lost+found 旁路 + 告警（Borg skip-with-warning 哲学） | 中 | FallbackDirName + 短哈希 |
| Windows 面 | 结尾点/空格规范化、保留名、大小写不敏感判重、长路径源树约束 | 高 | SanitizeComponent；OrdinalIgnoreCase |
| retention | 旁路版本有界兜底（Staggered/restic forget） | 高 | AllDirectories 递归（size+TTL 既有语义承接） |
| 写入纪律 | 临时文件+原子改名（Syncthing "never writes directly"） | 高 | AtomicCopyAsync(overwrite:false) 不变 |

与 D-006 裁定：**完全同向，零冲突，无 revised**。

## 5. 不动项核验

- `BackupOptions`/`AppConfig`/`config.sample.json`：零改动（布局是构造语义，无新配置键）。
- ADR 0041 `File.Replace`/`IFileOperations` 接口：零改动（AtomicCopyAsync 保留通用能力，仅备份热路径不再传 overwrite:true）。
- ADR 0043 默认目录统一 + `WatchPathFilter` 自动排除注入：零改动（`ShouldSkipPath` 的 IsSameOrSubPath 语义天然覆盖镜像子树，备份目录不产生自监听回环）。
- UI/IPC/Worker 服务面/ExifTool 桥：零改动。
- 票 01/02 已落库改动：未触碰其语义面（RuleEngine 仅动 ResolveBackupPath 方法体；其 MatchesPattern 票 02 段未动）。

## 6. 张力与呈报（待首脑裁定）

1. **多监控根前缀消歧无落位主体**：D-006 写「多监控根共享 backup 目录时以根目录名做一层前缀消歧（to-spec 按 config 实际结构定）」。现行配置模型 `WatchOptions` 仅单一 `HotFolder`——前缀层无触发场景。本票按 to-spec 授权处理为：单根镜像 + 根外路径 `_unsorted` 兜底（消歧语义等价成立）。若未来引入多根配置，镜像槽位需加根前缀层，已在本报告预留心智位。
2. **ADR 0006 决策文字被 D-006 部分推翻**：0006 的「保持 filename.bak 单文件覆盖模式不变」前提是同文件互覆盖无害，A-006 已证其为 bug。handoff 提示「收口时立备份布局 ADR（候选）」——建议大脑收口时立 ADR 登记 0006 修订 + 0005/0041 写入纪律延续，窗口不擅自代立。
3. **同秒旁路碰撞的 TOCTOU 微窗**：`ResolveSidecarPath` 以 `File.Exists` 探测 + `AtomicCopyAsync(overwrite:false)` 落地，两并发流同秒分歧理论上可撞名（后到者抛 IOException → pipeline 重试 → 重算新时间戳槽位）。单文件同路径有 `_inflight` 门；跨路径并发同秒同名概率极低且有失败重试兜底，不引入全局锁。登记在案。
4. **retention 的 CreationTimeUtc 排序在镜像语义下不再稳定代表"处理新旧"**：旁路版本文件名自带时间戳，而 size 兜底按创建时间最旧优先——同目录内语义仍正确（旁路比槽位新时先删旁路会损失版本序列头部？不会：先删最旧=先删被跳过的旧旁路，槽位（真原始）最新反而后删，符合"绝不丢失"方向）。无行为变更，登记说明。

## 7. 过程违规呈报

1. **`git add -N`（intent-to-add）触碰索引**：为让新文件进入 `git diff` 验证行尾（`git ls-files --eol`）使用了 `git add -N`——严格读法与 §4.2「禁裸 git 写命令」冲突。影响评估：仅索引标记，未写对象库、未动栈，`but commit` 自然覆盖；后续同类验证改用 `git ls-files --eol --cached-other` 或直接 `but diff`。主动登记。
2. 其余流程合规：动栈前 §4.4 轨 2 快照（`D:\Aworker\photo-snapshots\20260916-020535`，126 文件 manifest）；CI-only（本机零构建零测试）；一窗一票一分支；不 push 不 PR。

## 8. 提交与 CI（已完成提交）

- 提交：`but commit -b 03-backup-mirror-layout -m "票 03 备份面：镜像相对路径布局 + 内容分歧旁路版本化（覆盖 A-006）"`（哈希以 git log 实物为准，tip = git log -1 03-backup-mirror-layout 实物；首提物理哈希 7f2d6de，9 files +838/-24，diff --check CLEAN；本报告副本的哈希回填经一次 amend 并入同一提交（改后 tip 见 git log，报告文字不再内嵌终态哈希以防自指递归））。
- CI：待大脑推送 `ci-verify/correctness-round-t03` 触发 workflow；红则按纪律开返修启动器，本窗不自证。

## 9. 验证记录（本机静态口径）

- `git diff --check` CLEAN；`git ls-files --eol`：全部 i/lf w/lf（.gitattributes `* text=auto eol=lf`，`core.autocrlf=false`）；涉改 7 文件零 BOM（首三字节非 EF BB BF）。
- 词法器级括号平衡扫描：6 个涉改代码文件 braces/parens 终态 0、无提前转负（注：首轮粗扫在 BackupPathResolver 报 -2 为检查脚本对 `'"'` 字符字面量的误报，状态机复扫证伪）。
- 悬空引用检查：resolver 内复刻判定已删净（`private static bool TryGetSubPath` 零命中）、`Watcher.WatchPathFilter` 委托在场、pipeline 备份路径 `overwrite: true` 零残留、`WriteBackupAsync` 定义+调用 5 处。
- 真凭证 = CI 编译 + vstest 三场景绿（待大脑推送）。
