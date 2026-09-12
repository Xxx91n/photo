# 跨平台发布目录结构统一到 release/<rid>/

弃用 publish/ 目录，所有编译输出统一到 release/<rid>/ 下。每个 RID 目录内为平铺结构：UI 可执行文件及所有依赖在根目录，Worker 二进制也在根目录（平铺，无 worker/ 子目录）。用户下载 ZIP/安装包后直接得到直观的平铺结构。

> **修订 (ADR 0039 §5)**: 实际实现已改为完全平铺结构——`PhotoPrivacy.exe` 和 `PhotoPrivacyWorker.exe` 均直接在 `release/<rid>/` 根目录，无 `worker/` 子目录。commit `38142f9` 的修复将 Worker 从 `worker/` 子目录提升到根目录，ADR 0039 §5 记录了实测落盘结构。

## Considered Options

- **方案 A（采纳，后修订）**: release/<rid>/ 完全平铺（UI + Worker 均在根目录）。原设计为 worker/ 子目录，后经 commit `38142f9` 修订为平铺（见 ADR 0039 §5）
- **方案 B（否决）**: 沿用 publish/app/<version>/<rid>/ 层级结构——版本嵌套增加路径深度，不直观
