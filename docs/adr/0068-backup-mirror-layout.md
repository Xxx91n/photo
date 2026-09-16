# 备份布局：镜像相对路径 + 内容分歧旁路版本化（替代扁平文件名覆盖）

日期：2026-09-16 ｜ 状态：已实施（correctness-round 票 03 落地，票 06 收口补立本条并登记 ADR 0006 修订）

> **修订登记（ADR 0006）**：0006 的「filename.bak 单文件覆盖模式」已被本条取代——扁平文件名布局在跨子目录同名文件下静默互覆盖（correctness-round A-006 锐评事实核验）。0006 的总量限制（MaxSizeMb retention 兜底）保留继续生效，镜像槽位与旁路版本同受约束。

## 背景

correctness-round 锐评（A-006）：备份根下按「文件名+后缀」扁平落盘，`hot/vacation/IMG_0001.jpg` 与 `hot/work/IMG_0001.jpg` 命中同一槽位，后写静默覆盖前写，无提示无审计。对隐私清理工具而言，静默丢失「清理前的原始」属不可接受的用户数据损失（D-002 不变量②）。原方案的「同文件只保留最新备份」语义本身是合法的（重处理幂等），需要保留的是语义、消灭的是跨文件静默互覆盖。

## 决策

- **槽位 = 镜像相对路径**：备份根下按源文件相对其监控根的子路径镜像目录结构，结构性消除跨子目录同名碰撞；多监控根共享备份目录时以根目录名做前缀消歧。
- **兜底 = `_unsorted/`**：源路径无法归位监控子树时回退 `_unsorted/<文件名>.<路径短哈希>`（SHA256 截断），绝不与他文件碰槽。
- **冲突三分流（绝不静默覆盖，FileTaskPipeline.WriteBackupAsync）**：
  1. 源或槽位为符号链接/ReparsePoint → 拒绝写入，落 `backup_skipped`/`reparse_point_refused` 审计（fail-closed）；
  2. 槽位不存在 → `AtomicCopyAsync(overwrite: false)` 原子写入；
  3. 槽位存在且逐字节内容相同 → 跳过，落 `backup_skipped`/`identical_content_slot_kept`（幂等，承接「同文件重处理保留最新」语义）；
  4. 槽位存在且内容分歧 → 写 `名字.时间戳.扩展名` 旁路版本，落 `backup_sidecar_versioned`/`content_diverged_sidecar_written`。
- **比对失败按内容不同处理**（宁可落旁路版本，不冒静默覆盖险）。
- **工程防护**：相对路径逐组件规范化（剥离 `.`/`..`/空组件/根前缀）、Windows 保留名与结尾点空格处理、穿越防护复用 `IsSameOrUnderRoot` 边界判定。

## Considered Options

- **A（采纳）镜像相对路径**：如上。结构自解释、无外部清单依赖、跨子目录结构性消歧。
- **B（否决）扁平哈希主布局**：无清单数据库下槽位不可读，与「恢复原图」心智脱节（Windows File History `$OF` 教训）。
- **C（否决）纯时间戳树主布局**：无界增长、重处理不幂等，「同文件最新」语义无处承接。

## 实现落点

`src/PhotoPrivacy.Core/Pipeline/BackupPathResolver.cs`（`ResolveBackupSlot` / `ResolveSidecarPath` / `ContentsEqual` / `IsReparsePoint` / `IsSameOrUnderRoot` / `FallbackDirName="_unsorted"` / `ShortPathHash`=SHA256 截断）；调用侧 `FileTaskPipeline.WriteBackupAsync`；行为测试 `tests/PhotoPrivacy.Core.Tests/Pipeline/BackupMirrorLayoutTests.cs`（双子目录同名两备份都在且内容正确、同文件重处理幂等保留）。
