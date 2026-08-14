# ADR 0045: Hot Folder 空路径守卫 + 备份管道空值防御

**状态**: 已接受
**日期**: 2026-08-14

## 背景

config.sample.json 所有路径改为空字符串（889d08d），代码各处假设非空但
没加防护。系统性审计发现 8 处 Directory.CreateDirectory("") 会崩（已在
6387eb7 修复）。但更深层问题：hot_folder 为空时 Worker 静默跳过所有
处理——用户看到 Worker "运行中"但 D:\hot 的文件一个都没被处理。

RuleEngine.ResolveBackupPath 当 backup.Directory 和 hotFolder 都空时
生成 "bak" 相对路径——备份到不可预测的 CWD 子目录。

## 决策

1. AppConfigValidator 非 dry_run 模式下 hot_folder 必须为非空绝对路径，
   否则抛 AppConfigValidationException 阻止 Worker 启动
2. RuleEngine.ResolveBackupPath 当 hotFolder 为空时返回 null（不备份），
   不生成无意义的相对路径
3. UI 配置页面 hot_folder 为空时显示提示

### 考虑的替代方案

**静默跳过（拒绝）**: Worker 检测到空 hot_folder 直接跳过 FSW + EnumerateFiles。
用户误以为 Worker 在工作但实际什么都没处理——这正是"检测机制在干什么"
的根因。报错比静默好。

### 后果

- 用户必须显式配置 hot_folder 才能启动 Worker，不能依赖默认空值
- 旧 config.json 缺 hot_folder 字段时用 DefaultPaths 回退
- 现有空路径保护代码（6387eb7）与此 ADR 互补：保护防崩，守卫防静默失败